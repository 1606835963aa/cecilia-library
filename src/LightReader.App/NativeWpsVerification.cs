using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using LightReader.Core;
using Microsoft.Web.WebView2.Core;

namespace LightReader.App;

public partial class MainWindow
{
    // Uses only explicitly catalogued public test files, never recent user files.
    internal async Task RunNativeWpsChecksAsync(string root)
    {
        var results = new List<object>();
        var report = Path.Combine(root,"reports","native-wps-display.json");
        var capture = Path.Combine(root,"reports","private","native-wps");
        Directory.CreateDirectory(capture);
        try
        {
            using var catalog = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"data","native-wps-fixtures.json")));
            foreach(var fixture in catalog.RootElement.GetProperty("fixtures").EnumerateArray())
            {
                var id=fixture.GetProperty("id").GetString()!;
                var file=fixture.GetProperty("file").GetString()!;
                if(Path.GetFileName(file)!=file)throw new InvalidDataException("Invalid fixture filename");
                var path=Path.Combine(root,"tests","external-fixtures","native-wps",file);
                var expectedHash=fixture.GetProperty("sha256").GetString();
                if(!File.Exists(path)||expectedHash==null)
                {results.Add(new{id,file,status="NOT_RUN",reason="Verified source file unavailable"});continue;}
                var watch=Stopwatch.StartNew();
                try
                {
                    var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
                    if(!hash.Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Fixture hash mismatch");
                    var kind=FileInspector.Inspect(path).Kind.ToString();
                    await AddFileAsync(path);await Task.Delay(400);
                    var shown=Viewer.Visibility==Visibility.Visible&&MessagePanel.Visibility!=Visibility.Visible;
                    var pages=new List<object>();
                    var textChecks=new List<object>();
                    if(shown&&browser!=null)
                    {
                        foreach(var text in fixture.GetProperty("expectedText").EnumerateArray())
                        {
                            var expected=text.GetString()!;
                            var found=await browser.EvaluateAsync("document.body.innerText.includes("+JsonSerializer.Serialize(expected)+")")=="true";
                            shown&=found;textChecks.Add(new{expected,found});
                        }
                        if(fixture.TryGetProperty("sheetCount",out var count))
                            shown&=await browser.EvaluateAsync("document.querySelector('select').options.length === "+count.GetInt32())=="true";
                        var output=Path.Combine(capture,id+"-quick.png");
                        await using(var image=File.Create(output))await browser.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
                        if(fixture.TryGetProperty("expectedPages",out _))
                        {await DisplayAsync(current!,true);shown&=nativePdf!=null&&Viewer.Visibility==Visibility.Visible&&MessagePanel.Visibility!=Visibility.Visible;}
                    }
                    string? referencePdf=null;
                    if(shown&&nativePdf!=null)
                    {
                        shown=nativePdf.PageCount==fixture.GetProperty("expectedPages").GetUInt32();
                        for(uint page=0;page<nativePdf.PageCount;page++)
                        {
                            await nativePdf.ShowPageAsync(page);
                            var rendered=nativePdf.HasRenderedPage&&nativePdf.DisplayedPage==page;
                            shown&=rendered;
                            var output=Path.Combine(capture,id+"-page-"+(page+1)+".png");
                            nativePdf.SavePreview(output);
                            pages.Add(new{page=page+1,rendered,preview=Path.GetRelativePath(root,output),sha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(output)))});
                        }
                        referencePdf=Path.Combine(capture,id+".pdf");File.Copy(pdfSource!,referencePdf,true);
                    }
                    else if(browser==null)shown=false;
                    var smoke=fixture.GetProperty("scope").GetString()=="blank-smoke";
                    var renderer=nativePdf!=null?"Windows.Data.Pdf":browser!=null?"WebView2":"none";
                    var errorMessage=shown?null:MessageDetail.Text;
                    ShowHome();
                    bool sourceUnchanged;
                    using(var exclusive=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))
                        sourceUnchanged=exclusive.Length>0&&hash==Convert.ToHexString(SHA256.HashData(exclusive));
                    shown&=sourceUnchanged;
                    results.Add(new{id,file,status=shown?(smoke?"SMOKE_ONLY":"PASS"):"FAIL",sha256=hash,kind,renderer,milliseconds=watch.ElapsedMilliseconds,pages,textChecks,
                        referencePdf=referencePdf==null?null:Path.GetRelativePath(root,referencePdf),error=errorMessage,sourceUnchanged});
                }
                catch(Exception error){results.Add(new{id,file,status="FAIL",error=error.Message});ShowHome();}
            }
        }
        catch(Exception error){results.Add(new{id="runner",status="FAIL",error=error.ToString()});}
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{ranAt=DateTimeOffset.Now,applicationSha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(typeof(App).Assembly.Location))),
            kind="Application-owned display checks; public source files remain local; PDF text and visual checks recorded separately",results,unhandledErrors=App.UnhandledErrors.ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        Close();
    }
}
