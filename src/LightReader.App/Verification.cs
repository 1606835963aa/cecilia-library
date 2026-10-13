using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace LightReader.App;
public partial class MainWindow
{
    private async Task RunBenchmarkAsync(string output)
    {
        await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
        var process=Process.GetCurrentProcess();
        var ready=(DateTime.Now-process.StartTime).TotalMilliseconds;
        await Task.Delay(700);
        var record=new {uiReadyMs=ready,workingSetBytes=process.WorkingSet64,privateBytes=process.PrivateMemorySize64,browserInitialized=browser!=null,runtime=Environment.Version.ToString()};
        await File.WriteAllTextAsync(output,JsonSerializer.Serialize(record));await Task.Delay(800);Close();
    }
    private async Task RunVerificationAsync(string projectRoot)
    {
        var reports = Path.Combine(projectRoot, "reports"); Directory.CreateDirectory(reports);
        var capture = Path.Combine(reports,"previews"); Directory.CreateDirectory(capture);
        var results = new List<object>();
        try
        {
            foreach (var id in new[]{"word","sheet"})
            {
                var watch=Stopwatch.StartNew();
                await components.InstallAsync(id,null,CancellationToken.None);
                results.Add(new {file="component:"+id,status="PASS",milliseconds=watch.ElapsedMilliseconds,installedBytes=components.InstalledBytes(id)});
            }
            var checks=new (string File,string? Expression)[] {
                ("文本样本.txt",null), ("富文本样本.rtf",null), ("图片样本.png",null),("图片样本.jpg",null),("图片样本.bmp",null),("多页样本.tif",null),
                ("阅读样本.docx","document.body.innerText.includes('小熊读书') && document.body.innerText.includes('001234567890123456') && document.querySelectorAll('section.docx').length >= 2 && [...document.querySelectorAll('img')].some(i=>i.complete && i.naturalWidth>0)"),
                ("模板样本.dotx","document.body.innerText.includes('第二页检查') && document.querySelectorAll('section.docx').length >= 2"),
                ("静态宏类型.docm","document.body.innerText.includes('小熊读书')"),("静态宏模板.dotm","document.body.innerText.includes('小熊读书')"),
                ("数据样本.xlsx","document.body.innerText.includes('001234567890123456') && document.body.innerText.includes('1,255.25') && document.body.innerText.includes('2026-10-12') && document.querySelector('td[colspan=\"3\"]') !== null && document.querySelectorAll('option').length === 3"),
                ("表格模板.xltx","document.body.innerText.includes('001234567890123456') && document.querySelectorAll('option').length === 3"),
                ("静态宏表格.xlsm","document.body.innerText.includes('001234567890123456')"),("静态宏表格模板.xltm","document.body.innerText.includes('001234567890123456')"),
                ("数据样本.csv","document.body.innerText.includes('001234567890123456') && document.body.innerText.includes('00002') && document.body.innerText.includes('有逗号,有引号')"),
                ("旧表格样本.xls","document.body.innerText.includes('001234567890123456') && document.querySelectorAll('option').length === 3"),
                ("旧表格模板.xlt","document.body.innerText.includes('001234567890123456') && document.querySelectorAll('option').length === 3"),
                ("加载项数据.xla","document.body.innerText.includes('加载项数据查看') && document.body.innerText.includes('001234567890123456') && document.body.innerText.includes('2026-10-12') && document.body.innerText.includes('1,255.25') && document.querySelectorAll('option').length === 3"),
                ("加载项数据.xlam","document.body.innerText.includes('加载项数据查看') && document.body.innerText.includes('001234567890123456') && document.body.innerText.includes('2026-10-12') && document.body.innerText.includes('1,255.25') && document.querySelectorAll('option').length === 3"),
                ("旧表格样本.dbf","document.querySelector('table') !== null"),("旧表格样本.prn","document.querySelector('table') !== null"),("旧表格样本.dif","document.querySelector('table') !== null"),
                ("网页样本.html","document.body.innerText.includes('中文正文') && !window.badScriptRan && !document.querySelector('[onerror]') && !document.querySelector('a[href]') && document.querySelector('#document script') === null"),
                ("网页样本.htm","document.body.innerText.includes('中文正文')"),
                ("通用样本.xml","document.body.innerText.includes('中文 XML 检查')"),
                ("扫描样本.pdf",null), ("分页样本.pdf",null),
                ("旧文字样本.doc",null), ("旧文字模板.dot",null), ("演示样本.pptx",null), ("旧演示样本.ppt",null),("旧演示模板.pot",null),("旧演示播放.pps",null), ("办公表格.xml",null),
                ("开放文档.odt",null),
                ("演示变体.pptm",null),("演示变体.potx",null),("演示变体.potm",null),("演示变体.ppsx",null),("演示变体.ppsm",null),("归档样本.mht",null),("归档样本.mhtml",null)
            };
            foreach(var check in checks)
            {
                var path=Path.Combine(projectRoot,"tests","fixtures",check.File);
                if(!File.Exists(path)){results.Add(new{file=check.File,status="NOT_RUN",reason="fixture absent"});continue;}
                var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
                var watch=Stopwatch.StartNew();
                await AddFileAsync(path); await Task.Delay(700);
                bool passed=Viewer.Visibility==Visibility.Visible && MessagePanel.Visibility!=Visibility.Visible;
                string evidence="";
                if(check.Expression!=null && browser!=null)
                { evidence=await browser.EvaluateAsync(check.Expression);passed &= evidence=="true"; }
                if(check.File=="多页样本.tif")passed &= frames.Count==2;
                if(check.File=="文本样本.txt")passed &= textBox?.Text.Contains("小熊读书")==true;
                if(check.File=="富文本样本.rtf")passed &= Viewer.Content is FlowDocumentScrollViewer rich && new System.Windows.Documents.TextRange(rich.Document.ContentStart,rich.Document.ContentEnd).Text.Contains("小熊读书");
                if(Viewer.Content==browser?.View && browser!=null)
                { await using var screenshot=File.Create(Path.Combine(capture,check.File+".png"));await browser.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,screenshot); }
                if(nativePdf!=null){nativePdf.SavePreview(Path.Combine(capture,check.File+".png"));evidence=$"Windows.Data.Pdf: {nativePdf.PageCount} pages, bitmap={nativePdf.HasRenderedPage}";passed &= nativePdf.PageCount>0&&nativePdf.HasRenderedPage;}
                results.Add(new{file=check.File,status=passed?"PASS":"FAIL",milliseconds=watch.ElapsedMilliseconds,sha256=hash,evidence,previewSource=nativePdf!=null?pdfSource:Viewer.Content==browser?.View?browser?.SourcePath:null,error=MessagePanel.Visibility==Visibility.Visible?MessageDetail.Text:"",hostWorkingSet=Process.GetCurrentProcess().WorkingSet64});
                await File.WriteAllTextAsync(Path.Combine(reports,"gui-checks.json"),JsonSerializer.Serialize(new{ranAt=DateTimeOffset.Now,results},new JsonSerializerOptions{WriteIndented=true}));
            }
            await AddFileAsync(Path.Combine(projectRoot,"tests","fixtures","分页样本.pdf"));
            var nativePages=nativePdf?.PageCount==2;
            if(nativePdf!=null){await nativePdf.ShowPageAsync(1);nativePdf.SavePreview(Path.Combine(capture,"native-pdf-page2.png"));}
            await SwitchPdfModeAsync();var browserMode=Viewer.Content==browser?.View&&browser!=null;
            await SwitchPdfModeAsync();
            results.Add(new{file="flow:native PDF page2 and text-view toggle",status=nativePages&&browserMode&&nativePdf!=null&&browser==null?"PASS":"FAIL"});
            await AddFileAsync(Path.Combine(projectRoot,"tests","fixtures","数据样本.xlsx"));
            SetZoom(1.5);await AddFileAsync(Path.Combine(projectRoot,"tests","fixtures","阅读样本.docx"));
            var zoomReset=browser?.View.ZoomFactor==1;
            var navigation=await browser!.EvaluateAsync("document.querySelector('.doc-page-nav select')?.options.length === 2");
            results.Add(new{file="flow:reset zoom and Word page navigation",status=zoomReset&&navigation=="true"?"PASS":"FAIL"});
            await AddFileAsync(Path.Combine(projectRoot,"tests","fixtures","数据样本.xlsx"));
            browser!.Find("尾部搜索词");await Task.Delay(400);var found=await browser.EvaluateAsync("document.querySelector('.selected')?.textContent === '尾部搜索词'");
            results.Add(new{file="flow:search across sheet row pages",status=found=="true"?"PASS":"FAIL",evidence=found});
            browser.Find("右侧列测试");await Task.Delay(400);found=await browser.EvaluateAsync("document.querySelector('.selected')?.textContent === '右侧列测试'");
            results.Add(new{file="flow:search across column pages",status=found=="true"?"PASS":"FAIL",evidence=found});
            var counted=System.Text.RegularExpressions.Regex.IsMatch(SearchCount.Text,@"^\d+ / \d+$");
            results.Add(new{file="flow:sheet search shows position and total",status=counted?"PASS":"FAIL",evidence=SearchCount.Text});
            await AddFileAsync(Path.Combine(projectRoot,"tests","fixtures","阅读样本.docx"));
            SearchInput.Text="小熊读书";Find(false);await Task.Delay(800);var firstMatch=SearchCount.Text;Find(false);await Task.Delay(400);var secondMatch=SearchCount.Text;Find(true);await Task.Delay(400);
            var wordTotal=System.Text.RegularExpressions.Regex.Match(firstMatch,@"^1 / (\d+)$");
            results.Add(new{file="flow:Word search count, next and previous",status=wordTotal.Success&&secondMatch==(wordTotal.Groups[1].Value=="1"?"1 / 1":"2 / "+wordTotal.Groups[1].Value)&&SearchCount.Text==firstMatch?"PASS":"FAIL",evidence=$"{firstMatch} -> {secondMatch} -> {SearchCount.Text}"});
            await AddFileAsync(Path.Combine(projectRoot,"tests","fixtures","文本样本.txt"));
            SearchInput.Text="小熊读书";Find(false);
            results.Add(new{file="flow:text search count and selection",status=SearchCount.Text.StartsWith("1 / ")&&textBox?.SelectedText=="小熊读书"?"PASS":"FAIL",evidence=SearchCount.Text});
            var darkBefore=App.IsDark;App.ApplyTheme(darkBefore?"light":"dark");
            await AddFileAsync(Path.Combine(projectRoot,"tests","fixtures","数据样本.xlsx"));await Task.Delay(300);
            var scheme=await browser!.EvaluateAsync("matchMedia('(prefers-color-scheme: dark)').matches");
            var shell=((SolidColorBrush)FindResource("Bg")).Color;
            results.Add(new{file="flow:theme switch reaches shell and preview",status=App.IsDark!=darkBefore&&scheme==(App.IsDark?"true":"false")&&shell==(App.IsDark?Color.FromRgb(0x1B,0x1A,0x18):Color.FromRgb(0xF4,0xF2,0xEE))?"PASS":"FAIL",evidence=$"dark={App.IsDark}, preview={scheme}, shell={shell}"});
            App.ApplyTheme(settings.Theme);
            foreach(var extension in new[]{"txt","csv"})
            {
                var original=Path.Combine(projectRoot,"tests","fixtures",extension=="txt"?"文本样本.txt":"数据样本.csv");var before=await File.ReadAllBytesAsync(original);
                await AddFileAsync(original);await ToggleEditAsync();
                if(textBox==null)throw new Exception("Editor absent");
                textBox.Text+="\r\n编辑检查，小熊读书\r\n";var edited=textBox.Text;
                await AddFileAsync(Path.Combine(projectRoot,"tests","fixtures","图片样本.png"));await AddFileAsync(original);
                var bufferKept=textBox?.Text==edited && current?.Dirty==true;
                var saved=Path.Combine(projectRoot,"tests","saved","编辑样本."+extension);Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                await SaveCurrentAsAsync(saved,File.Exists(saved));
                var reopened=await LightReader.Core.TextDocument.ReadAsync(saved);
                results.Add(new{file="flow:edit switch and save "+extension,status=bufferKept&&reopened.Text.Contains("编辑检查")&&File.ReadAllBytes(original).SequenceEqual(before)?"PASS":"FAIL",output=saved});
            }
            await Task.WhenAll(AddFileAsync(Path.Combine(projectRoot,"tests","fixtures","阅读样本.docx")),AddFileAsync(Path.Combine(projectRoot,"tests","fixtures","文本样本.txt")));
            results.Add(new{file="flow:rapid file switch",status=current?.Name=="文本样本.txt" && textBox?.Text.Contains("小熊读书")==true && Viewer.Visibility==Visibility.Visible?"PASS":"FAIL"});
            await RunReadingStabilityChecksAsync(projectRoot,results);
            results.Add(new{file="flow:no unhandled UI errors",status=App.UnhandledErrors.Count==0?"PASS":"FAIL",errors=App.UnhandledErrors.ToArray()});
        }
        catch(Exception error){results.Add(new{file="runner",status="FAIL",error=error.ToString()});}
        await File.WriteAllTextAsync(Path.Combine(reports,"gui-checks.json"),JsonSerializer.Serialize(new{ranAt=DateTimeOffset.Now,results},new JsonSerializerOptions{WriteIndented=true}));
        Status.Text="验证完成 · 结果已写入 reports/gui-checks.json";
    }
}
