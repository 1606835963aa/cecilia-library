from pathlib import Path
import json, statistics, hashlib
ROOT=Path(__file__).resolve().parent.parent
def read(name):return json.loads((ROOT/name).read_text(encoding='utf-8-sig'))
def write(name,data):(ROOT/name).write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
gui=read('reports/gui-checks.json');perf=read('reports/performance.json');core=read('reports/core-checks.json');independent=read('reports/independent-checks.json');catalog=read('data/format-targets.json')
document_memory=read('reports/document-memory.json')
assert all(result.get('status')=='PASS' for result in gui['results']), 'Window checks include failures or checks not run'
assert all(result.get('status')=='PASS' for result in core['results']), 'Core checks include failures'
assert all(result.get('status')=='PASS' for result in independent['checks']), 'Independent checks include failures'
by_ext={}
for result in gui['results']:
    if result.get('status')=='PASS' and result.get('sha256'):
        fixture=ROOT/'tests'/'fixtures'/result['file']
        assert hashlib.sha256(fixture.read_bytes()).hexdigest().upper()==result['sha256'].upper(), 'Fixture changed after validation: '+result['file']
        by_ext.setdefault(Path(result['file']).suffix.lower(),[]).append(result)
assert all(item['extension'].startswith('.') for item in catalog['formats']), 'Catalog extensions must include the leading dot'
assert set(by_ext).issubset({item['extension'] for item in catalog['formats']}), 'A passing fixture has no format target'
for item in catalog['formats']:
    evidence=by_ext.get(item['extension'],[])
    if evidence:
        item['implementationStatus']='IMPLEMENTED';item['verificationStatus']='VERIFIED_SAMPLE'
        item['evidence']=[{'fixture':result['file'],'sha256':result['sha256'],'report':'reports/gui-checks.json','scope':'synthetic fixture'} for result in evidence]
        if item['extension'] in ['.mht','.mhtml']:item['proposedModule']='core-web-archive'
    elif item['definitionStatus']=='NEEDS_DEFINITION':item['implementationStatus']='NOT_IMPLEMENTED';item['verificationStatus']='NEEDS_DEFINITION'
    else:item['implementationStatus']='ROUTED_UNVERIFIED';item['verificationStatus']='NOT_RUN'
assert sum(item['verificationStatus']=='VERIFIED_SAMPLE' for item in catalog['formats'])==len(by_ext), 'Summary differs from per-format evidence'
catalog['lastValidationAt']=gui['ranAt'];catalog['sampleVerifiedExtensionCount']=len(by_ext)
write('data/format-targets.json',catalog)
doc='# 格式覆盖目标与实测\n\n目标 47 项：WPS 公开清单 46 个后缀，加上用户确认补入的 ODT。目前 '+str(len(by_ext))+' 个后缀有合成样本显示证据。`VERIFIED_SAMPLE` 表示所列样本通过，真实文档版本和复杂排版继续扩充验收。\n\n[WPS 格式宣传页](https://www.wps.com/feature/file-opener/) · [WPS 技术规格](https://www.wps.com/tech-specs/)\n\n| 编号 | 后缀 | 类型 | 当前状态 | 样本证据 |\n|---|---|---|---|---|\n'
for item in catalog['formats']:
    status={'VERIFIED_SAMPLE':'样本显示通过','NOT_RUN':'已接入候选组件，缺真实样本','NEEDS_DEFINITION':'查看对象待定义'}[item['verificationStatus']]
    doc+=f"| {item['id']} | `{item['extension']}` | {item['label']} | {status} | "+'、'.join(entry['fixture'] for entry in item['evidence'])+' |\n'
doc+='\n`.xla`、`.xlam` 查看加载项内部的静态工作表数据，包括隐藏工作表；插件功能与宏执行不属于查看范围。含宏类型的合成样本只含静态内容，未嵌入 VBA；真实宏文件读取和宏保留保存继续扩充。`.xml` 已测通用 XML 和 SpreadsheetML，Word XML 仍待扩充。DBF 的 memo 配套文件、复杂网页归档资源、Office 嵌入对象与动画继续补样本。\n\nWPS 官网宣传数与清单差额保留为来源记录。ODT 已由用户选择，属于我们的补充范围。\n\n[逐项运行结果](../reports/gui-checks.json) · [独立内容核对](../reports/independent-checks.json) · [格式目标数据](../data/format-targets.json)\n'
(ROOT/'docs'/'格式支持清单.md').write_text(doc,encoding='utf-8')
starts=[sample['uiReadyMs']/1000 for sample in perf['samples']];memory=[sample['treeWorkingSetBytes']/1048576 for sample in perf['samples']]
memory_table='\n'.join(f"| {item['file']} | {item['renderer']} | {item['workingSetBytes']/1048576:.1f} MiB |" for item in document_memory['records'])
progress=f'''# 开发进度与验收记录

版本：0.1.0 预览版。记录日期：2026-10-12。

主程序已经能够打开文档、切换标签、缩放、搜索和打印；Word 预览有页码跳转，表格有工作表和行列分页。TXT / CSV 的编辑、切换后保留修改、另存为及保护原文件已经验证。

| 项目 | 结果 |
|---|---|
| 构建 | Release 构建成功，0 警告、0 错误 |
| 核心行为检查 | {len(core['results'])} 项通过 |
| 窗口内部集成检查 | {len(gui['results'])} 项通过，其中 {sum(1 for r in gui['results'] if r.get('sha256'))} 份文档样本 |
| 有样本显示证据的后缀 | {len(by_ext)} 项 |
| 独立读取核对 | {len(independent['checks'])} 项通过，PDF 用 pypdf、文本用 Python 解码 |
| 主程序压缩包 | {perf['zipBytes']/1048576:.2f} MiB |
| 主程序文件夹 | {perf['mainFolderBytes']/1048576:.2f} MiB |
| 空窗口启动中位数 | {statistics.median(starts):.2f} 秒，最慢 {max(starts):.2f} 秒 |
| 空窗口进程树工作集 | {min(memory):.1f}–{max(memory):.1f} MiB；15 次均无子进程 |
| Word 快速预览组件 | 下载约 213 KiB |
| 表格组件 | 下载约 941 KiB |
| 完整办公兼容组件 | 下载约 356 MiB；提取后约 1.47 GiB |

启动测量包含 5 次新配置启动及 10 次重复配置启动，每次软件均为新进程；未通过重启电脑清除系统文件缓存。本机 i5-12600KF、约 32 GiB 内存。工作集统计相加全部子进程，包含共享页的重复计数。软件与运行时版本、原始数据见报告。

体积数字不包含共享 .NET / WebView2 运行时、可选组件、文档缓存和开发 SDK。打开文档时的浏览器子进程另计，见 [文档内存记录](../reports/document-memory.json)。完整兼容引擎用于旧版 Office 和分页布局，不常驻后台。

| 独立打开样本 | 显示路线 | 全部进程工作集快照 |
|---|---|---|
{memory_table}

每份文档使用独立测试配置，确认文档实际显示后再采样。这里是工作集快照，未测完整生命周期峰值。PDF 默认使用 Windows 原生显示，文字搜索按需切换浏览器。DOCX / 表格的浏览器占用仍较高；旧 Office 新文件转换约 20–25 秒，重复内容可以复用分页缓存。这两项继续优化。

## 已完成的验证范围

- PDF / DOCX / XLSX 的实际预览截图已检查；文字、图片、页数、公式缓存结果、日期、合并区域和长编号通过指定样本核对。
- Word 与表格组件完成真实 HTTPS 下载、大小及 SHA-256 校验；完整兼容组件用官方已缓存 MSI 通过程序自身安装流程提取并启动。
- 校验失败、取消下载和安装后离线复用通过模拟网络检查；坏更新保留原有组件文件。
- 编辑结果保留原始样本，保存后的 TXT / CSV 通过独立读取核对；覆盖发生外部变化的原文件会被拒绝。

## 尚未完成

- WPS 原生 `.wps`、`.wpt`、`.et`、`.ett`、`.dps`、`.dpt` 缺少真实格式样本，保持未验证。已有兼容路径，不能据此认定全部版本可读。
- `.ts` 在 WPS 演示清单中的含义继续核实；第 47 项已按用户选择补入 ODT。
- 普通 Word / Excel 的原格式编辑保存和 PDF 批注尚未启用。
- 密码文件、复杂图表 / 嵌入对象、大文件、多版本原生 WPS、memo 配套文件及复杂网页归档资源继续扩充。
- 新电脑缺少运行时的安装、组件版本更新回退、长期压力、完整生命周期峰值和卸载检查尚未完成。
- Computer Use 窗口接口连续两次加载失败，错误为 `Trusted RPC package must resolve within a configured trusted module directory: @oai/sky/service`。本次取得的是程序内部集成检查和预览表面截图；打开对话框、拖拽和打印对话框的实际鼠标操作仍待验证。

## 下一批工作

补充 WPS 原生与复杂文件样本，核对特殊格式，继续降低表格 / Word 视图占用、缩短旧 Office 冷转换；完善运行时引导、版本更新与压力测试。只有取得相应内容与显示证据后，才扩大发布支持范围。

[格式覆盖](格式支持清单.md) · [使用说明](使用说明.md) · [核心检查](../reports/core-checks.json) · [窗口检查](../reports/gui-checks.json) · [性能原始记录](../reports/performance.json)
'''
(ROOT/'docs'/'开发进度.md').write_text(progress,encoding='utf-8')
print(json.dumps({'formats':len(by_ext),'guiChecks':len(gui['results']),'coreChecks':len(core['results'])},ensure_ascii=False))
