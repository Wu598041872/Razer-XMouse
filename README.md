# XMacro Bridge

XMacro Bridge 是一款面向 Windows 10/11 x64 的离线鼠标宏转换工具，用于在 Razer Synapse 雷云 4 与 X-Mouse Button Control（XMBC）之间转换、检查和编辑键鼠宏。

当前版本：`0.1.0-preview.4`。

## 快速使用

1. 解压绿色版 ZIP，运行 `XMacroBridge.App.exe`，无需安装或管理员权限。
2. 选择文件、文件夹，或把宏文件拖入窗口。
3. 在左侧选择宏，在时间线查看事件和兼容性诊断。
4. 按需编辑延时、键盘或鼠标事件；错误诊断消除前不会允许危险导出。
5. 选择目标格式并点击“安全导出”。

详细操作请阅读 [用户使用说明](docs/11-用户使用说明.md)。

## 当前支持

- 输入：雷云独立宏 XML、雷云 4 `.synapse4`、XMBC XML/`.xmbcp` 和 XMBC 宏文本。
- 处理：键盘、鼠标、固定/随机延时、嵌套宏展开、时间线编辑、搜索、撤销和重做。
- 输出：雷云独立宏 XML、XMBC 宏文本。
- 安全：全程离线、输入只读、输出原子写入、未知事件可见、危险状态阻断导出。

## 预览版限制

- 不生成新的 `.synapse4` 整包，也不修改雷云或 XMBC 的运行中配置。
- 雷云输出仅开放已经验证的事件编码；不兼容事件会阻止导出。
- XMBC 输出是可粘贴或保存的宏文本，不包含应用规则和层级配置。
- 真实软件兼容性仍在持续扩充，重要宏请先保留原文件并在安全环境试运行。

## 开发

开发者先阅读 [CLAUDE.md](CLAUDE.md)、[文档索引](docs/README.md)和[项目状态](docs/项目状态.md)。完整验证命令：

```powershell
./tools/test.ps1 -Configuration Release
```

生成绿色版：

```powershell
./tools/release/Build-Portable.ps1
```

`我的宏/` 是用户数据目录，不属于项目源码，不得移动、改写或自动提交。
