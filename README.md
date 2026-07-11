# XMacro Bridge

XMacro Bridge 是一款面向 Windows 10/11 x64 的离线鼠标宏转换工具，目标是在 Razer Synapse 雷云 4 与 X-Mouse Button Control（XMBC）之间安全转换键盘、鼠标和延时事件。

当前仓库处于工程治理阶段。第一版计划支持：

- 读取雷云单宏 XML 与 `.synapse4` 整包。
- 读取 XMBC 的 `XMBCSettings.xml`、`.xmbcp` 和宏文本。
- 递归展开嵌套宏，并检测缺失引用与循环引用。
- 以雷云独立宏 XML 或 XMBC 宏文本导出。
- 在时间线中预览、验证并编辑宏事件。

## 开始工作

1. 阅读 [CLAUDE.md](CLAUDE.md)。
2. 阅读 [docs/README.md](docs/README.md) 中列出的标准文件。
3. 查看 [docs/项目状态.md](docs/项目状态.md) 和最近一份开发日志。

本仓库中的 `我的宏/` 是用户数据，不属于项目源码，不得移动、改写或自动提交。
