# WPS 原生格式验证

记录日期：2026-10-14。程序版本：0.1.2。

六种原生格式已取得样本显示证据，格式覆盖从 40 项增加至 46 项。剩余 `.ts` 的格式定义尚未核实。

| 格式 | 原始样本 | 已核对范围 |
|---|---|---|
| `.wps` | 公开预览示例 | 6 页文字、标题与代码示例 |
| `.wpt` | Kingsoft Memo 模板 | 1 页文字及模板布局 |
| `.et` | 公开预览示例 | 5 张工作表的数据视图、9 页原始打印布局 |
| `.ett` | Kingsoft Invoice 模板 | 单元格、合并区域、已保存的日期结果；1 页完整布局及标志 |
| `.dps` | 公开预览示例 | 15 页中文、图片、图形和图表 |
| `.dpt` | Books 模板 | 12 页背景图片、文字和图形 |

六份完整样本共 44 页，逐页取得程序内部预览并检查画面。额外的一份空白 `.dps` 只记录为 `SMOKE_ONLY`（能打开空白文件），不单独增加格式数量。表格同时检查快速数据视图与分页预览；`.et` 原始打印范围存在跨页列，分页结果保留其 9 页结构。

验证分为四部分：

1. 下载后核对固定 SHA-256（文件内容指纹），检查 OLE 文档结构及 `WordDocument`、`Workbook` 或 `PowerPoint Document` 主体；六份完整样本均有 `KSOProductBuildVer` 或 `Kingsoft Office` 创建程序信息。
2. 用实际发布程序打开文件，检查表格内容、工作表数量和完整页数，逐页确认页面图片已经显示。返回首页后立即检查源文件可以独占读取，且原文指纹不变。
3. 用 pypdf 独立提取分页结果中的文字，核对预期内容与页数；用 Pillow 检查页面图片及其指纹。表格数据另与原有读取方式逐单元格比较。
4. 查看全部页面的缩略图，以及两份完整的快速表格预览。这里验证的是本程序画面，没有通过外部 WPS 窗口逐像素比较。

本批验证覆盖上述样本。不同年代版本、密码文件、复杂嵌入对象和动画继续单独补测。

## 样本来源与复现

- 文字、表格和演示文稿来自 [CUBE 文件预览演示文档](https://cube.cndinfo.com/docs/ecosystem/ams/preview)列出的公开样本。下载时只调整保存文件的主文件名，保留原后缀，内容指纹与原下载完全一致。
- 模板来自 [wps-osistematico-templates](https://github.com/henriquead7/wps-osistematico-templates/tree/aa887f54b94fc0d44881ddf7689b87ab94a2be7e) 的固定提交。原始文件、下载地址和指纹见[样本清单](../data/native-wps-fixtures.json)。
- 外部样本保存在忽略的 `tests/external-fixtures/native-wps`；原始正文、页面图片和分页结果不进入 Git，也不进入程序发布包。没有读取用户的真实文件。

```powershell
.\scripts\prepare-native-wps-fixtures.ps1
node scripts\check-native-wps-structure.cjs
$testProfile = Join-Path $PWD 'tmp\native-wps-profile'
New-Item -ItemType Directory -Path $testProfile -Force
if (!(Test-Path (Join-Path $testProfile 'components'))) {
    New-Item -ItemType Junction -Path (Join-Path $testProfile 'components') -Target (Join-Path $PWD 'components')
}
Start-Process 'dist\LightReader\LightReader.exe' -ArgumentList @('--verify-native-wps',('"'+$PWD.Path+'"'),'--data-dir',('"'+$testProfile+'"')) -Wait
python scripts\check-native-wps-output.py
```

项目中须按构建说明准备表格与办公兼容组件。已有样本可以用 `-Offline` 校验，不重新下载。源站文件变化后指纹检查会拒绝替换已有文件。结束后先移除测试配置中的 `components` 联接，再清理本次的临时配置，保留项目组件目录。

[文件结构检查](../reports/native-wps-structure.json) · [实际显示检查](../reports/native-wps-display.json) · [独立内容核对](../reports/native-wps-independent.json) · [画面检查记录](../reports/native-wps-visual.json)
