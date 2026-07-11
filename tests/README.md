# 测试目录

`XMacroBridge.Core.Tests` 是零第三方依赖的离线测试可执行程序，覆盖 Core、格式适配器、应用服务与 Presentation 工作区回归。`XMacroBridge.App.SmokeTests` 真实创建 WPF 主窗口，验证三主题、DPI context、布局、虚拟化、键盘与 UI Automation 合同。

运行 `tools/test.ps1 -Configuration Release` 会依次执行恢复、0 警告构建、核心断言、Windows x64 自包含临时发布及嵌入 Manifest 检查、三主题 WPF 冒烟和日报回归。单独运行 `dotnet test` 不构成完整质量门禁。

后续在此目录继续扩充格式黄金样本、集成测试和 UI 自动化测试。未经匿名化的用户宏不得作为测试夹具提交。
