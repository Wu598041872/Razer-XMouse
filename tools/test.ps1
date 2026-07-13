[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$userDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $userDotnet) {
    $userDotnet
}
else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$appSourceRoot = Join-Path $projectRoot 'src\XMacroBridge.App'
$hardCodedViewColors = Get-ChildItem -LiteralPath $appSourceRoot -Recurse -File |
    Where-Object {
        $_.Extension -in '.xaml', '.cs' -and
        $_.FullName -ne (Join-Path $appSourceRoot 'Themes\RazerDark.xaml') -and
        $_.FullName -notlike (Join-Path $appSourceRoot 'Assets\*')
    } |
    Select-String -Pattern '#[0-9A-Fa-f]{6,8}'
if ($hardCodedViewColors) {
    $details = ($hardCodedViewColors | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }) -join [Environment]::NewLine
    throw "应用视图包含未经主题资源管理的硬编码颜色：$([Environment]::NewLine)$details"
}
Write-Output 'PASS WPF view semantic color token check'

& $dotnet restore (Join-Path $projectRoot 'XMacroBridge.sln') --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build (Join-Path $projectRoot 'XMacroBridge.sln') -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet run --project (Join-Path $projectRoot 'tests\XMacroBridge.Core.Tests\XMacroBridge.Core.Tests.csproj') -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$appProject = Join-Path $projectRoot 'src\XMacroBridge.App\XMacroBridge.App.csproj'
$publishDirectory = Join-Path ([IO.Path]::GetTempPath()) ('XMacroBridge-publish-' + [guid]::NewGuid().ToString('N'))
try {
    & $dotnet restore $appProject -r win-x64 --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet publish $appProject -c $Configuration -r win-x64 --self-contained true --no-restore -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & (Join-Path $projectRoot 'tools\release\Assert-AppManifest.ps1') `
        -ExecutablePath (Join-Path $publishDirectory 'XMacroBridge.App.exe')
}
finally {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

$smokeTestProject = Join-Path $projectRoot 'tests\XMacroBridge.App.SmokeTests\XMacroBridge.App.SmokeTests.csproj'
$smokeFixturePaths = @(
    (Join-Path $projectRoot 'samples\razer\basic-key-delay.xml'),
    (Join-Path $projectRoot 'samples\razer\nested-macros.synapse4'),
    (Join-Path $projectRoot 'samples\xmbc\basic-key-delay.txt'),
    (Join-Path $projectRoot 'samples\xmbc\settings-action28.xml')
)
$previousTestMode = $env:XMACROBRIDGE_TEST_MODE
try {
    $env:XMACROBRIDGE_TEST_MODE = '1'
    & $dotnet run --project $smokeTestProject -c $Configuration --no-build -- @smokeFixturePaths
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    if ($null -eq $previousTestMode) {
        Remove-Item Env:XMACROBRIDGE_TEST_MODE -ErrorAction SilentlyContinue
    }
    else {
        $env:XMACROBRIDGE_TEST_MODE = $previousTestMode
    }
}

$devLogDate = [datetime]'2099-01-01'
$devLogPath = Join-Path $projectRoot 'devlogs\2099\2099-01-01.md'
try {
    & (Join-Path $projectRoot 'tools\devlog\Update-DevLog.ps1') -Mode DailySummary -Date $devLogDate | Out-Null
    & (Join-Path $projectRoot 'tools\devlog\Update-DevLog.ps1') -Mode DailySummary -Date $devLogDate | Out-Null
    $content = Get-Content -Raw -Encoding UTF8 -LiteralPath $devLogPath
    if ($content.Contains('Git 不可用或当前目录尚未初始化为仓库。')) {
        throw '日报将干净 Git 仓库误判为不可用。'
    }
    if (([regex]::Matches($content, '<!-- AUTO-SUMMARY:START -->')).Count -ne 1 -or
        ([regex]::Matches($content, '<!-- AUTO-SUMMARY:END -->')).Count -ne 1) {
        throw '日报自动汇总标记不是单例。'
    }
    Write-Output 'PASS Daily log clean-repository regression'
}
finally {
    Remove-Item -LiteralPath $devLogPath -Force -ErrorAction SilentlyContinue
    $yearDirectory = Split-Path -Parent $devLogPath
    if ((Test-Path -LiteralPath $yearDirectory) -and -not (Get-ChildItem -Force -LiteralPath $yearDirectory)) {
        Remove-Item -LiteralPath $yearDirectory -Force
    }
}

exit 0
