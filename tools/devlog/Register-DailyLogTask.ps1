[CmdletBinding()]
param(
    [string]$TaskName = 'XMacroBridge-DailyDevLog',
    [ValidatePattern('^(?:[01]\d|2[0-3]):[0-5]\d$')]
    [string]$Time = '23:50'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$updateScript = Join-Path $PSScriptRoot 'Update-DevLog.ps1'
if (-not (Test-Path -LiteralPath $updateScript)) {
    throw "未找到日报脚本：$updateScript"
}

$powerShellExe = (Get-Command powershell.exe -ErrorAction Stop).Source
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -Mode DailySummary' -f $updateScript
$today = Get-Date
$parts = $Time.Split(':')
$triggerAt = Get-Date -Year $today.Year -Month $today.Month -Day $today.Day -Hour ([int]$parts[0]) -Minute ([int]$parts[1]) -Second 0

$action = New-ScheduledTaskAction -Execute $powerShellExe -Argument $arguments -WorkingDirectory $projectRoot
$trigger = New-ScheduledTaskTrigger -Daily -At $triggerAt
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description '每天汇总 XMacro Bridge 的 Git 状态、完成事项和项目待办。' -Force | Out-Null
Write-Output "已注册任务计划：$TaskName（每天 $Time，错过后补运行）"
