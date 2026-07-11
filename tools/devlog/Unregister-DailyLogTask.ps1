[CmdletBinding()]
param([string]$TaskName = 'XMacroBridge-DailyDevLog')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -eq $task) {
    Write-Output "任务计划不存在：$TaskName"
    exit 0
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
Write-Output "已移除任务计划：$TaskName"
