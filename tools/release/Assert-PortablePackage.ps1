[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [string]$ChecksumPath,

    [Parameter(Mandatory)]
    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$package = [IO.Path]::GetFullPath($PackagePath)
$checksum = [IO.Path]::GetFullPath($ChecksumPath)
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
    throw "发布 ZIP 不存在：$package"
}
if (-not (Test-Path -LiteralPath $checksum -PathType Leaf)) {
    throw "SHA-256 文件不存在：$checksum"
}

$expectedHash = (([IO.File]::ReadAllText($checksum)).Trim() -split '\s+')[0].ToUpperInvariant()
$actualHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToUpperInvariant()
if ($expectedHash -ne $actualHash) {
    throw "发布 ZIP 的 SHA-256 不匹配。expected=$expectedHash actual=$actualHash"
}

$extractRoot = Join-Path ([IO.Path]::GetTempPath()) ('XMacroBridge-package-check-' + [guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath $package -DestinationPath $extractRoot -Force

    $requiredFiles = @(
        'XMacroBridge.App.exe',
        'README.md',
        'LICENSE.txt',
        'RELEASE-NOTES.md',
        'VERSION.txt',
        '使用说明.md',
        'Data\README.txt'
    )
    foreach ($relativePath in $requiredFiles) {
        $path = Join-Path $extractRoot $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "发布包缺少文件：$relativePath"
        }
    }

    $forbiddenNames = @('src', 'tests', 'samples', 'devlogs', '我的宏')
    foreach ($name in $forbiddenNames) {
        if (Test-Path -LiteralPath (Join-Path $extractRoot $name)) {
            throw "发布包包含禁止目录：$name"
        }
    }
    if (Get-ChildItem -LiteralPath $extractRoot -Recurse -File | Where-Object Extension -EQ '.pdb') {
        throw '发布包不得包含 PDB 调试文件。'
    }

    $versionText = [IO.File]::ReadAllText((Join-Path $extractRoot 'VERSION.txt')).Trim()
    if ($versionText -ne "XMacro Bridge $ExpectedVersion") {
        throw "VERSION.txt 与预期版本不一致：$versionText"
    }
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $extractRoot 'XMacroBridge.App.exe'))
    if ($versionInfo.ProductVersion -ne $ExpectedVersion) {
        throw "EXE ProductVersion 与预期版本不一致：$($versionInfo.ProductVersion)"
    }

    & (Join-Path $PSScriptRoot 'Assert-AppManifest.ps1') -ExecutablePath (Join-Path $extractRoot 'XMacroBridge.App.exe')
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Write-Output "PASS Portable package $ExpectedVersion and SHA-256"
}
finally {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
}
