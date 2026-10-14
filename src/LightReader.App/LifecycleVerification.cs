using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace LightReader.App;

public partial class MainWindow
{
    internal async Task RunLifecycleProbeAsync(string configuration)
    {
        using var config=JsonDocument.Parse(await File.ReadAllTextAsync(configuration));
        var root=config.RootElement.GetProperty("projectRoot").GetString()!;
        var output=config.RootElement.GetProperty("outputDirectory").GetString()!;
        var iterations=config.RootElement.GetProperty("iterations").GetInt32();
        var pdfOnly=config.RootElement.TryGetProperty("pdfOnly",out var pdfFlag)&&pdfFlag.GetBoolean();
        var searchOnly=config.RootElement.TryGetProperty("searchOnly",out var searchFlag)&&searchFlag.GetBoolean();
        var sequence=0;
        var watch=Stopwatch.StartNew();
        var checks=new List<object>();
        async Task Publish(string path,object value)
        {
            var staged=path+".tmp";
            await File.WriteAllTextAsync(staged,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(staged,path,true);
        }
        void Check(string name,bool condition)=>checks.Add(new{name,status=condition?"PASS":"FAIL"});
        async Task Sample(string step,int iteration=0)
        {
            var process=Process.GetCurrentProcess();process.Refresh();
            var record=new{sequence=++sequence,step,iteration,timestamp=DateTimeOffset.Now,elapsedMs=watch.ElapsedMilliseconds,
                currentTab=current?.Name,selectedTab=(Tabs.SelectedItem as DocumentTab)?.Name,tabCount=documents.Count,
                surface=Home.Visibility==Visibility.Visible?"home":ComponentPanel.Visibility==Visibility.Visible?"components":Viewer.Visibility==Visibility.Visible?"viewer":"message",
                renderer=nativePdf!=null?"Windows.Data.Pdf":browser!=null?"WebView2":"WPF",status=Status.Text,
                browserAlive=browser!=null,nativePdfAlive=nativePdf!=null,pdfBitmapPresented=nativePdf?.HasRenderedPage??false,pdfRenderCount=nativePdf?.RenderCount??0,unhandledErrorCount=App.UnhandledErrors.Count,viewerAttached=Viewer.Content!=null,toolsAttached=ViewerTools.Content!=null,
                textBoxAlive=textBox!=null,frameCount=frames.Count,retainedTextChars=documents.Sum(tab=>(tab.Text?.Text.Length??0)+(tab.EditBuffer?.Length??0)),
                workingSetBytes=process.WorkingSet64,privateBytes=process.PrivateMemorySize64,managedBytes=GC.GetTotalMemory(false),handleCount=process.HandleCount};
            await Publish(Path.Combine(output,$"stage-{sequence:D4}.json"),record);
            var ack=Path.Combine(output,$"ack-{sequence:D4}");
            var waiting=Stopwatch.StartNew();
            while(!File.Exists(ack)) { if(waiting.Elapsed>TimeSpan.FromSeconds(90))throw new TimeoutException("Lifecycle sampler did not acknowledge");await Task.Delay(30); }
        }
        bool Detached()=>browser==null&&nativePdf==null&&Viewer.Content==null&&ViewerTools.Content==null&&textBox==null&&imageScroll==null&&frames.Count==0;
        async Task HomeSamples(string prefix,int iteration=0)
        {
            ShowHome();
            var delay=Stopwatch.StartNew();
            foreach(var elapsed in new[]{500,2000,5000})
            {var remaining=elapsed-(int)delay.ElapsedMilliseconds;if(remaining>0)await Task.Delay(remaining);await Sample(prefix+"-"+elapsed+"ms",iteration);}
            Check(prefix+" view detached",Detached());
        }
        string Fixture(string name)=>Path.Combine(root,"tests","fixtures",name);
        try
        {
            await Task.Delay(1000);await Sample("empty-home");
            if(searchOnly)
            {
                await AddFileAsync(Path.Combine(root,"tmp","memory-fixtures","搜索表格样本.xlsx"));await Task.Delay(1000);await Sample("search-open");
                async Task<bool> Find(string query,int expectedCount)
                {
                    var result=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var reader=browser??throw new IOException("Search reader missing");
                    void Found(int index,int total)=>result.TrySetResult(total);
                    reader.FindChanged+=Found;
                    try{reader.Find(query);return await result.Task.WaitAsync(TimeSpan.FromSeconds(60))==expectedCount;}
                    finally{reader.FindChanged-=Found;}
                }
                for(var iteration=1;iteration<=iterations;iteration++)
                {
                    Check("all 600000 matches "+iteration,await Find("检索样本",600000));await Task.Delay(1000);await Sample("search-matches",iteration);
                    Check("clear search "+iteration,await Find("",0));await Task.Delay(1000);await Sample("search-cleared",iteration);
                }
                await HomeSamples("search-home");Check("no unhandled UI errors",App.UnhandledErrors.Count==0);
                await Publish(Path.Combine(output,"complete.json"),new{completedAt=DateTimeOffset.Now,stages=sequence,checks,unhandledErrors=App.UnhandledErrors});
                Close();return;
            }
            if(pdfOnly)
            {
                var file=Fixture("分页样本.pdf");
                for(var iteration=1;iteration<=iterations;iteration++)
                {
                    await AddFileAsync(file);await Task.Delay(1000);
                    Check("PDF bitmap "+iteration,nativePdf?.HasRenderedPage==true);
                    await Sample("pdf-cycle-open",iteration);await HomeSamples("pdf-cycle-home",iteration);
                    try { using var exclusive=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.None);Check("PDF file released "+iteration,true); }
                    catch(IOException){Check("PDF file released "+iteration,false);}
                }
                Check("no unhandled UI errors",App.UnhandledErrors.Count==0);
                await Publish(Path.Combine(output,"complete.json"),new{completedAt=DateTimeOffset.Now,stages=sequence,checks,unhandledErrors=App.UnhandledErrors});
                Close();return;
            }
            foreach(var file in new[]{"阅读样本.docx","数据样本.xlsx","分页样本.pdf","多页样本.tif","文本样本.txt"})
            {
                await AddFileAsync(Fixture(file));await Task.Delay(1000);
                Check("shown "+file,Viewer.Visibility==Visibility.Visible&&MessagePanel.Visibility!=Visibility.Visible&&(nativePdf==null||nativePdf.HasRenderedPage));
                await Sample("opened-"+file);await HomeSamples("home-after-"+file);
                await AddFileAsync(Fixture(file));await Task.Delay(500);await Sample("reopened-"+file);
            }
            ShowComponents(this,new RoutedEventArgs());await Task.Delay(2000);await Sample("components-after-read");Check("components view detached",Detached());
            foreach(var file in new[]{"内存文字样本.docx","内存表格样本.xlsx"})
            {
                await AddFileAsync(Path.Combine(root,"tmp","memory-fixtures",file));await Task.Delay(1000);
                Check("shown "+file,Viewer.Visibility==Visibility.Visible&&MessagePanel.Visibility!=Visibility.Visible);
                await Sample("opened-"+file);await HomeSamples("home-after-"+file);
            }
            for(var iteration=1;iteration<=iterations;iteration++)
            {
                await AddFileAsync(Fixture(iteration%2==0?"数据样本.xlsx":"阅读样本.docx"));await Task.Delay(400);
                Check("cycle shown "+iteration,Viewer.Visibility==Visibility.Visible&&MessagePanel.Visibility!=Visibility.Visible);
                await Sample("cycle-open",iteration);
                ShowHome();await Task.Delay(2000);await Sample("cycle-home",iteration);Check("cycle detached "+iteration,Detached());
            }
            Check("no unhandled UI errors",App.UnhandledErrors.Count==0);
            await Publish(Path.Combine(output,"complete.json"),new{completedAt=DateTimeOffset.Now,stages=sequence,checks,unhandledErrors=App.UnhandledErrors});
        }
        catch(Exception error)
        { await Publish(Path.Combine(output,"complete.json"),new{completedAt=DateTimeOffset.Now,stages=sequence,checks,error=error.ToString()}); }
        Close();
    }
}
