[CmdletBinding()]
param(
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$appProject = Join-Path $projectRoot 'src\XMacroBridge.App\XMacroBridge.App.csproj'
$projectXml = [xml][IO.File]::ReadAllText($appProject)
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'App 项目未声明 Version。'
}

$userDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $userDotnet) {
    $userDotnet
}
else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

function Assert-PathWithinArtifacts {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $artifactsRoot.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝在 artifacts 之外执行发布清理：$fullPath"
    }
}

if (-not $SkipTests) {
    & (Join-Path $projectRoot 'tools\test.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

[IO.Directory]::CreateDirectory($artifactsRoot) | Out-Null
$publishDirectory = Join-Path $artifactsRoot ('.publish-' + [guid]::NewGuid().ToString('N'))
$packageDirectory = Join-Path $artifactsRoot ('.package-' + [guid]::NewGuid().ToString('N'))
$baseName = "XMacroBridge-$version-win-x64"
$zipPath = Join-Path $artifactsRoot ($baseName + '.zip')
$checksumPath = $zipPath + '.sha256'
Assert-PathWithinArtifacts $publishDirectory
Assert-PathWithinArtifacts $packageDirectory
Assert-PathWithinArtifacts $zipPath
Assert-PathWithinArtifacts $checksumPath

try {
    Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $checksumPath -Force -ErrorAction SilentlyContinue

    & $dotnet restore $appProject -r win-x64 --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet publish $appProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        --no-restore `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & (Join-Path $PSScriptRoot 'Assert-AppManifest.ps1') `
        -ExecutablePath (Join-Path $publishDirectory 'XMacroBridge.App.exe')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    [IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $packageDirectory -Recurse -Force
    Get-ChildItem -LiteralPath $packageDirectory -Recurse -File -Filter '*.pdb' |
        Remove-Item -Force

    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\release\README.md') -Destination (Join-Path $packageDirectory 'README.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.txt') -Destination (Join-Path $packageDirectory 'LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'RELEASE-NOTES.md') -Destination (Join-Path $packageDirectory 'RELEASE-NOTES.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\11-用户使用说明.md') -Destination (Join-Path $packageDirectory '使用说明.md')
    [IO.Directory]::CreateDirectory((Join-Path $packageDirectory 'Data')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\release\Data-README.txt') -Destination (Join-Path $packageDirectory 'Data\README.txt')
    [IO.File]::WriteAllText(
        (Join-Path $packageDirectory 'VERSION.txt'),
        "XMacro Bridge $version`r`n",
        [Text.UTF8Encoding]::new($false))

    Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
    [IO.File]::WriteAllText(
        $checksumPath,
        "$hash *$([IO.Path]::GetFileName($zipPath))`r`n",
        [Text.UTF8Encoding]::new($false))

    & (Join-Path $PSScriptRoot 'Assert-PortablePackage.ps1') `
        -PackagePath $zipPath `
        -ChecksumPath $checksumPath `
        -ExpectedVersion $version
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Output "发布包：$zipPath"
    Write-Output "校验值：$checksumPath"
}
finally {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
