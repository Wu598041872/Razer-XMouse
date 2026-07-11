[CmdletBinding()]
param(
    [ValidateSet('Session', 'DailySummary')]
    [string]$Mode = 'DailySummary',

    [string[]]$Completed = @(),
    [string[]]$Tests = @(),
    [string[]]$Todo = @(),
    [string[]]$Decisions = @(),
    [string[]]$Risks = @(),
    [datetime]$Date = (Get-Date)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ScriptVersion = '1.0.0'
$script:ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$script:DevLogRoot = [IO.Path]::GetFullPath((Join-Path $script:ProjectRoot 'devlogs'))
$script:LockPath = Join-Path $script:DevLogRoot '.update.lock'
$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)

function Assert-PathWithinDevLogs {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $script:DevLogRoot.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝写入 devlogs 目录之外的路径：$fullPath"
    }
}

function Convert-ToBulletList {
    param([string[]]$Items, [string]$EmptyText = '无。')

    $validItems = @($Items | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($validItems.Count -eq 0) {
        return "- $EmptyText"
    }

    return (($validItems | ForEach-Object { '- ' + $_.Trim() }) -join [Environment]::NewLine)
}

function Get-DefaultLogContent {
    param([string]$DateText)

    $templatePath = Join-Path $script:DevLogRoot 'templates\daily-template.md'
    if (Test-Path -LiteralPath $templatePath) {
        return ([IO.File]::ReadAllText($templatePath, $script:Utf8NoBom)).Replace('{{DATE}}', $DateText)
    }

    return @"
# 开发日志：$DateText

## 当日目标

- 待填写。

## 已完成事项

- 待填写。

## 测试及验证

- 待填写。

## 技术决策

- 无。

## 风险、问题和阻塞

- 无。

## 未完成事项

- 待填写。

## 下一开发日待办

- 待填写。

<!-- AUTO-SUMMARY:START -->
## 自动汇总

尚未生成。
<!-- AUTO-SUMMARY:END -->
"@
}

function Write-AtomicUtf8File {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    Assert-PathWithinDevLogs -Path $Path
    $directory = Split-Path -Parent $Path
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $tempPath = Join-Path $directory ('.' + [IO.Path]::GetFileName($Path) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    Assert-PathWithinDevLogs -Path $tempPath

    try {
        [IO.File]::WriteAllText($tempPath, $Content, $script:Utf8NoBom)
        if (Test-Path -LiteralPath $Path) {
            $backupPath = $Path + '.bak'
            Assert-PathWithinDevLogs -Path $backupPath
            try {
                [IO.File]::Replace($tempPath, $Path, $backupPath, $true)
                Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
            }
            catch {
                Copy-Item -LiteralPath $Path -Destination $backupPath -Force
                Move-Item -LiteralPath $tempPath -Destination $Path -Force
                Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
            }
        }
        else {
            Move-Item -LiteralPath $tempPath -Destination $Path
        }
    }
    finally {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-GitRead {
    param([string[]]$Arguments)

    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git -or -not (Test-Path -LiteralPath (Join-Path $script:ProjectRoot '.git'))) {
        return [pscustomobject]@{ Success = $false; Lines = @() }
    }

    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & $git.Source -C $script:ProjectRoot @Arguments 2>$null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }

    if ($exitCode -ne 0) {
        return [pscustomobject]@{ Success = $false; Lines = @() }
    }

    return [pscustomobject]@{ Success = $true; Lines = @($output) }
}

function Get-ProjectTodo {
    $statusPath = Join-Path $script:ProjectRoot 'docs\项目状态.md'
    if (-not (Test-Path -LiteralPath $statusPath)) {
        return @('未找到 docs/项目状态.md。')
    }

    $lines = [IO.File]::ReadAllLines($statusPath, $script:Utf8NoBom)
    $todos = @($lines | Where-Object { $_ -match '^\s*- \[ \] ' } | ForEach-Object { $_ -replace '^\s*- \[ \] ', '' })
    if ($todos.Count -eq 0) {
        return @('项目状态中没有未完成任务。')
    }

    return $todos
}

[IO.Directory]::CreateDirectory($script:DevLogRoot) | Out-Null
$lockStream = $null
try {
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            $lockStream = [IO.File]::Open($script:LockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            break
        }
        catch [IO.IOException] {
            if ($attempt -eq 20) {
                throw '无法取得开发日志写入锁，可能有另一个日志任务仍在运行。'
            }
            Start-Sleep -Milliseconds 100
        }
    }

    $dateOnly = $Date.Date
    $dateText = $dateOnly.ToString('yyyy-MM-dd')
    $yearDirectory = Join-Path $script:DevLogRoot $dateOnly.ToString('yyyy')
    $logPath = Join-Path $yearDirectory ($dateText + '.md')
    Assert-PathWithinDevLogs -Path $logPath

    if (Test-Path -LiteralPath $logPath) {
        $content = [IO.File]::ReadAllText($logPath, $script:Utf8NoBom)
    }
    else {
        $content = Get-DefaultLogContent -DateText $dateText
    }

    if ($Mode -eq 'Session') {
        $sessionBlock = @"

## 开发会话 $((Get-Date).ToString('HH:mm:ss'))

### 完成事项
$(Convert-ToBulletList -Items $Completed)

### 测试及验证
$(Convert-ToBulletList -Items $Tests -EmptyText '未执行验证。')

### 技术决策
$(Convert-ToBulletList -Items $Decisions)

### 风险或阻塞
$(Convert-ToBulletList -Items $Risks)

### 后续待办
$(Convert-ToBulletList -Items $Todo -EmptyText '暂无。')
"@
        $content = $content.TrimEnd() + [Environment]::NewLine + $sessionBlock.TrimStart() + [Environment]::NewLine
    }
    else {
        $nextDate = $dateOnly.AddDays(1)
        $repository = Invoke-GitRead -Arguments @('rev-parse', '--is-inside-work-tree')
        $head = Invoke-GitRead -Arguments @('rev-parse', '--verify', 'HEAD')
        if (-not $head.Success) {
            $commits = @()
        }
        else {
            $commitResult = Invoke-GitRead -Arguments @(
                'log',
                ('--since=' + $dateOnly.ToString('yyyy-MM-ddT00:00:00')),
                ('--until=' + $nextDate.ToString('yyyy-MM-ddT00:00:00')),
                '--pretty=format:%h %s'
            )
            $commits = $commitResult.Lines
        }
        $changeResult = Invoke-GitRead -Arguments @('status', '--short', '--untracked-files=all')
        $changes = $changeResult.Lines
        $gitAvailable = ($repository.Success -and $changeResult.Success)

        if (-not $gitAvailable) {
            $commitText = '- Git 不可用或当前目录尚未初始化为仓库。'
            $changeText = '- 无法取得工作区状态。'
        }
        else {
            $commitText = Convert-ToBulletList -Items $commits -EmptyText '当日没有 Git 提交。'
            $changeText = Convert-ToBulletList -Items $changes -EmptyText '当日无代码或文档变更。'
        }

        $todoText = Convert-ToBulletList -Items (Get-ProjectTodo)
        $autoBlock = @"
<!-- AUTO-SUMMARY:START -->
## 自动汇总

- 汇总时间：$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'))
- 脚本版本：$script:ScriptVersion

### 当日 Git 提交

$commitText

### 当前工作区变更

$changeText

### 项目待办快照

$todoText
<!-- AUTO-SUMMARY:END -->
"@
        $pattern = '(?s)<!-- AUTO-SUMMARY:START -->.*?<!-- AUTO-SUMMARY:END -->'
        if ([Text.RegularExpressions.Regex]::IsMatch($content, $pattern)) {
            $content = [Text.RegularExpressions.Regex]::Replace($content, $pattern, [Text.RegularExpressions.MatchEvaluator]{ param($match) $autoBlock }, 1)
        }
        else {
            $content = $content.TrimEnd() + [Environment]::NewLine + [Environment]::NewLine + $autoBlock + [Environment]::NewLine
        }
    }

    Write-AtomicUtf8File -Path $logPath -Content $content
    Write-Output "开发日志已更新：$logPath"
}
finally {
    if ($null -ne $lockStream) {
        $lockStream.Dispose()
    }
    Remove-Item -LiteralPath $script:LockPath -Force -ErrorAction SilentlyContinue
}
