# .ts 格式核实

核实日期：2026-10-14。结论：`DEFINITION_UNCONFIRMED`（WPS 清单中的具体文件类型未能确认）。格式目标继续保留 47 项，当前有样本显示证据的仍为 46 项。

WPS 的[文件查看器宣传页](https://www.wps.com/feature/file-opener/)与[技术规格](https://www.wps.com/tech-specs/)都将 `.ts` 列在演示类。两页没有给出全名、文件结构、样本或导入方式，不能由这个后缀推导出具体解析器。

进一步交叉核对了两个官方开发资料：

| 资料 | 实际列出的内容 | 能说明什么 |
|---|---|---|
| [WebOffice 格式说明](https://open.wps.cn/documents/app-integration-dev/docs-center/online-preview-edit/format) | 11 种演示后缀及名称，包括 DPS / DPT，没有 TS | 在线服务的清单没有定义 TS；它的范围不能直接代表桌面版 |
| [桌面演示保存类型](https://open.wps.cn/documents/app-integration-dev/wps365/client/wpsoffice/jsapi/wpp/enum/PpSaveAsFileType) | 演示、模板、图片、MP4、WMV、XPS 等保存常量，没有 TS 对应项 | 没有找到用于创建可验证 TS 演示样本的官方保存类型；枚举的缺项也不足以证明不能导入 |

同后缀至少有以下明确但不同的含义：

- [TypeScript 官方入门](https://www.typescriptlang.org/docs/handbook/typescript-tooling-in-5-minutes.html)使用 `.ts` 保存程序源码。
- [Qt Linguist 文件规范](https://doc.qt.io/qt-6/linguist-ts-file-format.html)定义 XML 翻译资源，根元素为 `TS`。
- [微软 PowerPoint 媒体格式说明](https://support.microsoft.com/en-us/powerpoint/video-and-audio-file-formats-supported-in-powerpoint)将 `.ts` 定义为 MPEG-2 Transport Stream（视频传输流）。演示中能插入媒体，不等同于能把该媒体作为独立演示文档打开。

现有证据无法判断 WPS 宣传页指的是视频、另一种旧格式，还是清单错误。上述三类均不拿来冒充第 47 项。没有更改后缀、上传文档、替换目标项，也没有为此安装 WPS 或视频组件。

后续完成这一项所需的证据是：WPS 对该条目的正式格式定义，或桌面 WPS 能创建/独立打开的可追溯原始样本，并能确认它与宣传页指向相同类型。取得其中之一后再决定接入方式。当前程序对未识别的 TS 保留待核实提示。

[机器可读记录](../reports/ts-format-research.json) · [格式目标](格式支持清单.md)
