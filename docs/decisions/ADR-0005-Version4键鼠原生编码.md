# ADR-0005：Version 4 键鼠使用原生 VK 与零基编码

## 状态

已接受，替代 ADR-0004 中“导出使用一基鼠标编码”的结论，并替代此前“Version 4 Makecode 使用扫描码”的实现。

## 背景

早期基于单次导入界面观察，桥接器曾将 Windows VK 转为扫描码，并将左右键导出为 `1/2`。用户随后提供桥接器实际导出的 XML 和 Synapse 4 显示截图：`Makecode=18` 被显示为 Alt，`MouseButton=1` 被显示为右键。只读对照同机多份雷云原生 `Version=4` XML 后确认，原生文件直接使用 Windows VK（例如 E=`69`）并使用 `MouseButton=0/1` 表示左/右键。

## 决策

- Version 4 独立 XML 导入将 `KeyEvent/Makecode` 直接作为 Windows VK。
- Version 4 独立 XML 导入固定按 `MouseButton=0` 左键、`1` 右键解析，不再依赖事件内容猜测。
- Version 4 导出直接写入 VK，并固定写入零基鼠标编码 `0/1`。
- 缺少 Version 的旧兼容 XML 继续保留扫描码转换和基于文档内容的鼠标双编码探测，避免破坏既有样本读取。

## 后果

Version 4 的 E/W 与左右键能与雷云原生文件及实际界面一致。旧无版本文件仍可读取，但仅含鼠标代码 `1` 的旧文件继续存在无法消歧的兼容性限制。
