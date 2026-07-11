# 开发日志

开发日志按 `YYYY/YYYY-MM-DD.md` 保存，用于记录当天目标、完成事项、验证、决策、风险和下一步。

## 使用方法

开发会话结束时：

```powershell
./tools/devlog/Update-DevLog.ps1 -Mode Session `
  -Completed "完成事项" `
  -Tests "测试结果" `
  -Todo "下一步"
```

每日自动汇总：

```powershell
./tools/devlog/Update-DevLog.ps1 -Mode DailySummary
```

注册或移除每天 23:50 的任务计划：

```powershell
./tools/devlog/Register-DailyLogTask.ps1
./tools/devlog/Unregister-DailyLogTask.ps1
```

自动汇总区由脚本维护；人工内容写在其他章节。脚本不会自动提交或推送 Git。
