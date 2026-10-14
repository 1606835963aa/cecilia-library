using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LightReader.Core;

namespace LightReader.App;

public partial class MainWindow
{
    private async Task RunReadingStabilityChecksAsync(string root,List<object> results)
    {
        async Task Check(string name,Func<Task<bool>> run)
        {
            try { results.Add(new{file="reading:"+name,status=await run()?"PASS":"FAIL"}); }
            catch(Exception error){results.Add(new{file="reading:"+name,status="FAIL",error=error.ToString()});}
        }
        string Fixture(string name)=>Path.Combine(root,"tests","fixtures",name);
        bool Detached()=>browser==null&&nativePdf==null&&Viewer.Content==null&&ViewerTools.Content==null&&textBox==null&&imageScroll==null&&frames.Count==0&&current==null;
        async Task<bool> WaitUntil(Func<bool> condition)
        {for(var i=0;i<80;i++){if(condition())return true;await Task.Delay(50);}return false;}
        foreach(var file in new[]{"阅读样本.docx","数据样本.xlsx","分页样本.pdf","多页样本.tif","文本样本.txt"})
        {
            await Check("home releases "+file,async()=>
            {await AddFileAsync(Fixture(file));ShowHome();await Task.Delay(200);return Home.Visibility==Visibility.Visible&&Detached();});
        }
        foreach(var file in new[]{"阅读样本.docx","数据样本.xlsx"})
        {
            await Check("home releases browser source file "+file,async()=>
            {
                await AddFileAsync(Fixture(file));ShowHome();
                using var exclusive=new FileStream(Fixture(file),FileMode.Open,FileAccess.Read,FileShare.None);
                return Detached()&&exclusive.Length>0;
            });
        }
        await Check("components releases reading view",async()=>
        {await AddFileAsync(Fixture("数据样本.xlsx"));ShowComponents(this,new RoutedEventArgs());return ComponentPanel.Visibility==Visibility.Visible&&Detached();});
        await Check("CSV editor releases browser",async()=>
        {await AddFileAsync(Fixture("数据样本.csv"));if(current!.Editing){current.Editing=false;await DisplayAsync(current);}await ToggleEditAsync();return browser==null&&textBox is{IsReadOnly:false}&&current?.Text!=null;});
        foreach(var extension in new[]{"txt","csv"})
        {
            await Check("dirty "+extension+" survives home and components",async()=>
            {
                var path=Fixture(extension=="txt"?"文本样本.txt":"数据样本.csv");var before=SHA256.HashData(await File.ReadAllBytesAsync(path));
                await AddFileAsync(path);if(!current!.Editing)await ToggleEditAsync();
                var tab=current!;var original=textBox!.Text;textBox.Text+="\r\n临时缓冲核对";var edited=textBox.Text;
                ShowHome();var home=Detached()&&tab.Dirty&&tab.EditBuffer==edited;
                await AddFileAsync(path);var reopened=textBox?.Text==edited&&current?.Dirty==true;
                ShowComponents(this,new RoutedEventArgs());await AddFileAsync(path);
                var component=textBox?.Text==edited&&tab.Dirty;
                textBox!.Text=original;tab.Dirty=false;tab.EditBuffer=null;tab.Editing=false;
                ShowHome();var after=SHA256.HashData(await File.ReadAllBytesAsync(path));return home&&reopened&&component&&before.SequenceEqual(after);
            });
        }
        await Check("clean text baseline released",async()=>
        {await AddFileAsync(Fixture("文本样本.txt"));var tab=current!;var good=tab.Text==null;ShowHome();return good&&tab.Text==null&&tab.EditBuffer==null;});
        await Check("save snapshot survives home reopen and new edits",async()=>
        {
            var folder=Path.Combine(root,"tmp","reading-save-races",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            foreach(var overwriteSource in new[]{false,true})
            {
                var path=Path.Combine(folder,overwriteSource?"overwrite-source.txt":"copy-source.txt");
                var destination=overwriteSource?path:Path.Combine(folder,"saved-copy.txt");
                await File.WriteAllTextAsync(path,"保存竞态原文\r\n");var before=await File.ReadAllBytesAsync(path);
                await AddFileAsync(path);await ToggleEditAsync();
                var tab=current!;var oldEditor=textBox!;var originalBaseline=tab.Text!;
                oldEditor.Text+="保存时的快照\r\n";var snapshot=oldEditor.Text;
                var written=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var saving=SaveCurrentAsAsync(destination,async(document,buffer)=>
                {
                    try {await document.SaveAsAsync(destination,buffer,overwriteSource);written.TrySetResult();await finish.Task;}
                    catch(Exception error){written.TrySetException(error);throw;}
                });
                try
                {
                    await written.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    ShowHome();await AddFileAsync(path);var reopened=textBox!;
                    reopened.Text+="重开之后的新修改\r\n";var latest=reopened.Text;
                    finish.TrySetResult();await saving;
                    var stored=await TextDocument.ReadAsync(destination);
                    var kept=ReferenceEquals(current,tab)&&ReferenceEquals(textBox,reopened)&&!ReferenceEquals(oldEditor,reopened)
                        &&tab.Editing&&tab.Dirty&&reopened.Text==latest&&tab.EditBuffer==latest&&tab.Text!=null&&stored.Text==snapshot;
                    if(overwriteSource)kept&=tab.Text!.Text==snapshot&&tab.Text.SourceHash.SequenceEqual(stored.SourceHash);
                    else {var sourceAfter=await File.ReadAllBytesAsync(path);kept&=before.SequenceEqual(sourceAfter)&&ReferenceEquals(tab.Text,originalBaseline);}
                    if(!kept)return false;
                }
                finally
                {
                    finish.TrySetResult();try{await saving;}catch{}
                    tab.Dirty=false;tab.EditBuffer=null;tab.Editing=false;ShowHome();CloseTab(new Button{DataContext=tab},new RoutedEventArgs());
                }
            }
            return true;
        });
        await Check("save completion keeps subsequently selected document",async()=>
        {
            var folder=Path.Combine(root,"tmp","reading-save-races",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            var path=Path.Combine(folder,"switch-source.txt");var destination=Path.Combine(folder,"switch-saved.txt");
            await File.WriteAllTextAsync(path,"切换保存原文\r\n");var before=await File.ReadAllBytesAsync(path);
            await AddFileAsync(path);await ToggleEditAsync();var tab=current!;textBox!.Text+="等待保存的内容\r\n";var snapshot=textBox.Text;
            var written=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var saving=SaveCurrentAsAsync(destination,async(document,buffer)=>
            {
                try{await document.SaveAsAsync(destination,buffer,false);written.TrySetResult();await finish.Task;}
                catch(Exception error){written.TrySetException(error);throw;}
            });
            try
            {
                await written.Task.WaitAsync(TimeSpan.FromSeconds(10));await AddFileAsync(Fixture("文本样本.txt"));
                var active=current;var view=Viewer.Content;var generation=displayGeneration;
                finish.TrySetResult();await saving;
                var stored=await TextDocument.ReadAsync(destination);
                var sourceAfter=await File.ReadAllBytesAsync(path);
                return ReferenceEquals(current,active)&&ReferenceEquals(Viewer.Content,view)&&displayGeneration==generation
                    &&current?.Name=="文本样本.txt"&&Viewer.Visibility==Visibility.Visible&&stored.Text==snapshot
                    &&before.SequenceEqual(sourceAfter)&&!tab.Dirty;
            }
            finally
            {
                finish.TrySetResult();try{await saving;}catch{}
                tab.Dirty=false;tab.EditBuffer=null;tab.Editing=false;CloseTab(new Button{DataContext=tab},new RoutedEventArgs());
            }
        });
        await Check("PDF page and zoom survive home",async()=>
        {
            await AddFileAsync(Fixture("分页样本.pdf"));await nativePdf!.ShowPageAsync(1);SetZoom(1.4);await Task.Delay(400);
            ShowHome();await AddFileAsync(Fixture("分页样本.pdf"));
            return nativePdf is{HasRenderedPage:true,DisplayedPage:1}&&Math.Abs(zoom-1.4)<0.01;
        });
        await Check("PDF flip during zoom stays on requested page",async()=>
        {
            var reader=nativePdf!;var page=reader.ShowPageAsync(0);var next=reader.ShowPageAsync(1);var resize=reader.SetZoomAsync(1.7);
            await Task.WhenAll(page,next,resize);await reader.RestorePositionAsync(1,0,CancellationToken.None);
            return reader.HasRenderedPage&&reader.DisplayedPage==1;
        });
        await Check("PDF unchanged page restore reuses bitmap",async()=>
        {
            await Task.Delay(300);var reader=nativePdf!;var before=reader.RenderCount;var page=reader.DisplayedPage;
            await reader.ShowPageAsync(page);await reader.RestorePositionAsync(page,reader.VerticalOffset,CancellationToken.None);
            return reader.HasRenderedPage&&reader.DisplayedPage==page&&reader.RenderCount==before;
        });
        await Check("PDF disposal releases source file",async()=>
        {
            var path=Fixture("分页样本.pdf");var reader=nativePdf!;ShowHome();
            if(!await WaitUntil(()=>!reader.HasOpenSource))return false;
            using var exclusive=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);
            return Detached()&&exclusive.Length>0;
        });
        await Check("PDF cancelled load cleans stream",async()=>
        {
            using var token=new CancellationTokenSource();var reader=new NativePdfReader();
            var loading=reader.LoadAsync(Fixture("分页样本.pdf"),token.Token);token.Cancel();reader.Dispose();
            try{await loading;}catch(OperationCanceledException){}
            return await WaitUntil(()=>!reader.HasOpenSource);
        });
        await Check("image page and zoom survive home",async()=>
        {await AddFileAsync(Fixture("多页样本.tif"));NextImage(this,new RoutedEventArgs());SetZoom(1.6);ShowHome();await AddFileAsync(Fixture("多页样本.tif"));return frameIndex==1&&Math.Abs(zoom-1.6)<0.01;});
        await Check("Word scroll and zoom survive home",async()=>
        {
            await AddFileAsync(Fixture("阅读样本.docx"));SetZoom(1.2);
            await browser!.EvaluateAsync("window.scrollTo(0,document.querySelectorAll('section.docx')[1].offsetTop)");await Task.Delay(300);
            var position=current!.BrowserPosition;ShowHome();await AddFileAsync(Fixture("阅读样本.docx"));
            var scroll=await browser!.EvaluateAsync("window.scrollY>100");return scroll=="true"&&position!=null&&Math.Abs(zoom-1.2)<0.01;
        });
        await Check("sheet row and column page survive home",async()=>
        {
            await AddFileAsync(Fixture("数据样本.xlsx"));browser!.Find("右侧列测试");
            if(!await WaitUntil(()=>current?.BrowserPosition?.Contains("\"firstColumn\":50")==true))return false;
            ShowHome();await AddFileAsync(Fixture("数据样本.xlsx"));
            return await browser!.EvaluateAsync("document.querySelector('select')?.value==='2' && document.body.innerText.includes('右侧列测试')")=="true";
        });
        await Check("closing background tab keeps active view",async()=>
        {
            await AddFileAsync(Fixture("阅读样本.docx"));var background=current!;await AddFileAsync(Fixture("文本样本.txt"));var active=current;
            CloseTab(new Button{DataContext=background},new RoutedEventArgs());return ReferenceEquals(current,active)&&Viewer.Visibility==Visibility.Visible&&!documents.Contains(background);
        });
        await Check("last tab closes to detached home",async()=>
        {
            ShowHome();foreach(var tab in documents.ToArray())CloseTab(new Button{DataContext=tab},new RoutedEventArgs());
            await AddFileAsync(Fixture("文本样本.txt"));CloseTab(new Button{DataContext=current},new RoutedEventArgs());await Task.Delay(100);
            return documents.Count==0&&Home.Visibility==Visibility.Visible&&Detached();
        });
        await Check("initializing Word cancelled to home",async()=>
        {
            var loading=AddFileAsync(Fixture("阅读样本.docx"));await Task.Delay(10);ShowHome();await loading;await Task.Delay(100);
            return Home.Visibility==Visibility.Visible&&Detached();
        });
        await Check("rapid mixed format switch ends at text",async()=>
        {
            for(var i=0;i<5;i++)await Task.WhenAll(AddFileAsync(Fixture("阅读样本.docx")),AddFileAsync(Fixture("数据样本.xlsx")),AddFileAsync(Fixture("分页样本.pdf")),AddFileAsync(Fixture("文本样本.txt")));
            return current?.Name=="文本样本.txt"&&textBox?.Text.Contains("小熊读书")==true&&browser==null&&nativePdf==null;
        });
        await Check("missing file releases preceding view",async()=>
        {await AddFileAsync(Fixture("阅读样本.docx"));await AddFileAsync(Path.Combine(root,"tmp","missing-reading-test.docx"));return MessagePanel.Visibility==Visibility.Visible&&Detached();});
        ShowHome();
    }
}
