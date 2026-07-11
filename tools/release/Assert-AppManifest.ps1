[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ExecutablePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "发布程序不存在或不是文件：$ExecutablePath"
}

if ([IO.Path]::GetExtension($resolvedExecutable) -ne '.exe') {
    throw "Manifest 校验仅接受 Windows EXE：$ExecutablePath"
}

if (-not ('XMacroBridge.Tools.NativeResourceReader' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace XMacroBridge.Tools
{
    public static class NativeResourceReader
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(
            string lpFileName,
            IntPtr hFile,
            uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr FindResource(
            IntPtr hModule,
            IntPtr lpName,
            IntPtr lpType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadResource(
            IntPtr hModule,
            IntPtr hResInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint SizeofResource(
            IntPtr hModule,
            IntPtr hResInfo);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LockResource(IntPtr hResData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeLibrary(IntPtr hModule);
    }
}
'@
}

$loadLibraryAsDataFile = 0x00000002
$loadLibraryAsImageResource = 0x00000020
$rtManifest = [IntPtr]24
$defaultManifestId = [IntPtr]1
$module = [XMacroBridge.Tools.NativeResourceReader]::LoadLibraryEx(
    $resolvedExecutable,
    [IntPtr]::Zero,
    $loadLibraryAsDataFile -bor $loadLibraryAsImageResource)

if ($module -eq [IntPtr]::Zero) {
    throw [ComponentModel.Win32Exception]::new(
        [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
        "无法以只读资源方式打开发布程序：$([IO.Path]::GetFileName($resolvedExecutable))")
}

try {
    $resourceInfo = [XMacroBridge.Tools.NativeResourceReader]::FindResource(
        $module,
        $defaultManifestId,
        $rtManifest)
    if ($resourceInfo -eq [IntPtr]::Zero) {
        throw [ComponentModel.Win32Exception]::new(
            [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
            '发布程序未嵌入默认 RT_MANIFEST 资源。')
    }

    $resourceSize = [XMacroBridge.Tools.NativeResourceReader]::SizeofResource($module, $resourceInfo)
    if ($resourceSize -eq 0) {
        throw '发布程序中的 RT_MANIFEST 资源为空。'
    }

    $resourceHandle = [XMacroBridge.Tools.NativeResourceReader]::LoadResource($module, $resourceInfo)
    if ($resourceHandle -eq [IntPtr]::Zero) {
        throw [ComponentModel.Win32Exception]::new(
            [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
            '无法加载发布程序中的 RT_MANIFEST 资源。')
    }

    $resourcePointer = [XMacroBridge.Tools.NativeResourceReader]::LockResource($resourceHandle)
    if ($resourcePointer -eq [IntPtr]::Zero) {
        throw '无法读取发布程序中的 RT_MANIFEST 资源。'
    }

    $manifestBytes = [byte[]]::new([int]$resourceSize)
    [Runtime.InteropServices.Marshal]::Copy(
        $resourcePointer,
        $manifestBytes,
        0,
        $manifestBytes.Length)

    $manifestStream = [IO.MemoryStream]::new($manifestBytes, $false)
    try {
        $manifest = [System.Xml.XmlDocument]::new()
        $manifest.Load($manifestStream)
    }
    catch {
        throw "发布程序中的 RT_MANIFEST 不是有效 XML：$($_.Exception.Message)"
    }
    finally {
        $manifestStream.Dispose()
    }

    $dpiAwarenessElements = @($manifest.SelectNodes("//*[local-name()='dpiAwareness']"))
    if ($dpiAwarenessElements.Count -ne 1) {
        throw "发布程序必须且只能包含一个 dpiAwareness 声明，实际为 $($dpiAwarenessElements.Count) 个。"
    }

    $dpiAwareness = $dpiAwarenessElements[0].InnerText.Trim()
    if ($dpiAwareness -ne 'PerMonitorV2,PerMonitor') {
        throw "发布程序的 dpiAwareness 必须为 PerMonitorV2,PerMonitor，实际为：$dpiAwareness"
    }

    Write-Output "PASS Embedded RT_MANIFEST PerMonitorV2: $([IO.Path]::GetFileName($resolvedExecutable))"
}
finally {
    if (-not [XMacroBridge.Tools.NativeResourceReader]::FreeLibrary($module)) {
        Write-Warning '发布程序资源句柄释放失败。'
    }
}
