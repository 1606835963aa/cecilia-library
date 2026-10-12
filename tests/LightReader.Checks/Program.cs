using System.Text;
using LightReader.Core;

var projectRoot = Path.GetFullPath(args[0]);
var root = Path.Combine(projectRoot, "tmp", "checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var results = new List<object>();
async Task Check(string name, Func<Task> action)
{ try { await action(); results.Add(new { name, status = "PASS" }); Console.WriteLine("PASS " + name); } catch(Exception e) { results.Add(new {name,status="FAIL",error=e.Message});Console.WriteLine("FAIL " + name + " " + e.Message); Environment.ExitCode=1; } }
void Assert(bool condition, string message) { if(!condition)throw new Exception(message); }
try
{
    foreach(var item in new (string,Encoding)[]{(".txt",new UTF8Encoding(false)),(".txt",new UTF8Encoding(true)),(".txt",new UnicodeEncoding(false,true)),(".txt",new UnicodeEncoding(true,true))})
    {
        await Check("Text encoding " + item.Item2.WebName + " BOM=" + (item.Item2.GetPreamble().Length>0),async()=>{
            var input=Path.Combine(root,Guid.NewGuid()+item.Item1);var output=input+".saved";
            await File.WriteAllTextAsync(input,"中文编号 001234\r\n第二行",item.Item2);var document=await TextDocument.ReadAsync(input);
            await document.SaveAsAsync(output,document.Text,false);
            var before=await File.ReadAllBytesAsync(input);var after=await File.ReadAllBytesAsync(output);
            Assert(before.SequenceEqual(after),"Unchanged round trip changed bytes");
            await document.SaveAsAsync(output,"中文编号 001234\n修改行",true);
            var reopened=await TextDocument.ReadAsync(output);Assert(reopened.Text.Contains("修改行")&&reopened.Text.Contains("\r\n"),"Edit or newline lost");
        });
    }
    await Check("GBK preserves Chinese",async()=>{
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);var file=Path.Combine(root,"gbk.txt");await File.WriteAllTextAsync(file,"中文字符测试\n下一行",Encoding.GetEncoding(936));var d=await TextDocument.ReadAsync(file);Assert(d.Text.Contains("中文字符"),"GBK lost");await d.SaveAsAsync(file+".saved",d.Text,false);Assert(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(file+".saved")),"GBK changed");
    });
    await Check("Changed original cannot be overwritten",async()=>{
        var file=Path.Combine(root,"modified.txt");await File.WriteAllTextAsync(file,"before");var d=await TextDocument.ReadAsync(file);await File.WriteAllTextAsync(file,"external");
        try{await d.SaveAsAsync(file,"edit",true);throw new Exception("Changed source overwritten");}catch(IOException){Assert(await File.ReadAllTextAsync(file)=="external","Original damaged");}
    });
    await Check("Existing destination protected",async()=>{
        var source=Path.Combine(root,"source.txt");var target=Path.Combine(root,"target.txt");await File.WriteAllTextAsync(source,"source");await File.WriteAllTextAsync(target,"keep");var d=await TextDocument.ReadAsync(source);
        try{await d.SaveAsAsync(target,"edit",false);throw new Exception("Target overwritten");}catch(IOException){Assert(await File.ReadAllTextAsync(target)=="keep","Target damaged");}
    });
    await Check("Cancelled save retains original and removes staging",async()=>{
        var file=Path.Combine(root,"cancel.txt");await File.WriteAllTextAsync(file,"original");var d=await TextDocument.ReadAsync(file);using var cancel=new CancellationTokenSource();cancel.Cancel();
        try{await d.SaveAsAsync(file,"changed",true,cancel.Token);throw new Exception("Cancelled save completed");}catch(OperationCanceledException){Assert(await File.ReadAllTextAsync(file)=="original","Original damaged");Assert(!Directory.GetFiles(root,"*.tmp").Any(),"Staging left");}
    });
    await Check("PDF recognized by contents",async()=>{var p=Path.Combine(root,"wrong.txt");await File.WriteAllTextAsync(p,"%PDF-1.7\n");Assert(FileInspector.Inspect(p).Kind==DocumentKind.Pdf,"Signature ignored");});
    await Check("Renamed DOCX rejected",async()=>{var p=Path.Combine(root,"fake.docx");await File.WriteAllTextAsync(p,"not a document");try{FileInspector.Inspect(p);throw new Exception("Accepted fake");}catch(InvalidDataException){}});
    await Check("XML external entities rejected",async()=>{var p=Path.Combine(root,"external.xml");await File.WriteAllTextAsync(p,"<!DOCTYPE x [<!ENTITY secret SYSTEM 'file:///C:/Windows/win.ini'>]><x>&secret;</x>");try{FileInspector.Inspect(p);throw new Exception("DTD accepted");}catch(System.Xml.XmlException){}});
    await Check("TS remains undefined",async()=>{var p=Path.Combine(root,"special.ts");await File.WriteAllTextAsync(p,"sample");Assert(FileInspector.Inspect(p).Kind==DocumentKind.Undefined,"TS guessed");});
    await Check("Excel add-in data routes without executing the add-in",()=>{foreach(var ext in new[]{"xla","xlam"}){var p=Path.Combine(projectRoot,"tests","fixtures","加载项数据."+ext);Assert(FileInspector.Inspect(p).Kind==DocumentKind.Spreadsheet,"Add-in not recognized");}return Task.CompletedTask;});
    await Check("Invalid add-in containers rejected",async()=>{foreach(var ext in new[]{"xla","xlam"}){var p=Path.Combine(root,"fake."+ext);await File.WriteAllTextAsync(p,"not an add-in");try{FileInspector.Inspect(p);throw new Exception("Fake add-in accepted");}catch(InvalidDataException){}}});
    await Check("ODT recognized by its package type",async()=>{Assert(FileInspector.Inspect(Path.Combine(projectRoot,"tests","fixtures","开放文档.odt")).Kind==DocumentKind.Office,"ODT not recognized");var fake=Path.Combine(root,"fake.odt");File.Copy(Path.Combine(projectRoot,"tests","fixtures","数据样本.xlsx"),fake);try{FileInspector.Inspect(fake);throw new Exception("XLSX accepted as ODT");}catch(InvalidDataException){}});
    await Check("Broken component not installed",()=>{var manifest=Path.Combine(projectRoot,"data","component-manifest.json");var store=new ComponentStore(root,manifest);var d=store.Definitions.First();Directory.CreateDirectory(store.Folder(d));File.WriteAllText(Path.Combine(store.Folder(d),".complete.json"),"{}");Assert(store.GetInstalled(d.Id)==null,"Incomplete accepted");return Task.CompletedTask;});
    await Check("Installed fixture signatures",()=>{var expected=new Dictionary<string,DocumentKind>{{"阅读样本.docx",DocumentKind.Word},{"数据样本.xlsx",DocumentKind.Spreadsheet},{"分页样本.pdf",DocumentKind.Pdf},{"多页样本.tif",DocumentKind.Image},{"办公表格.xml",DocumentKind.Office}};foreach(var item in expected)Assert(FileInspector.Inspect(Path.Combine(projectRoot,"tests","fixtures",item.Key)).Kind==item.Value,"Wrong kind "+item.Key);return Task.CompletedTask;});
    await Check("MHTML decodes Chinese MIME part",async()=>{var archive=await ArchiveReader.ReadAsync(Path.Combine(projectRoot,"tests","fixtures","归档样本.mht"),CancellationToken.None);Assert(archive.Html.Contains("网页归档测试"),"MIME text missing");});
    async Task<string> TestManifest(string version,string hash)
    {var manifest=Path.Combine(root,"test-manifest-"+version+".json");await File.WriteAllTextAsync(manifest,System.Text.Json.JsonSerializer.Serialize(new{schemaVersion=1,components=new[]{new{id="simulated",name="test",version,license="test",assets=new[]{new{name="component.js",url="https://fixture.invalid/component.js",sha256=hash,bytes=3}}}}}));return manifest;}
    await Check("Component checksum failure is atomic",async()=>{
        var manifest=await TestManifest("bad",new string('0',64));using var network=new FixtureNetwork("abc"u8.ToArray());var store=new ComponentStore(root,manifest,network);
        try{await store.InstallAsync("simulated",null,CancellationToken.None);throw new Exception("Corrupt component accepted");}catch(InvalidDataException){Assert(store.GetInstalled("simulated")==null,"Broken installation visible");Assert(!Directory.GetDirectories(Path.Combine(root,"components"),".staging-*").Any(),"Staging left");}
    });
    await Check("Installed component works without network",async()=>{
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("abc"u8));var manifest=await TestManifest("good",hash);using var network=new FixtureNetwork("abc"u8.ToArray());var store=new ComponentStore(root,manifest,network);
        var folder=await store.InstallAsync("simulated",null,CancellationToken.None);network.Offline=true;Assert(await store.InstallAsync("simulated",null,CancellationToken.None)==folder,"Installed offline reuse failed");Assert(network.Requests==1,"Offline requested network");
        var badManifest=await TestManifest("update",new string('0',64));using var updateNetwork=new FixtureNetwork("abc"u8.ToArray());var update=new ComponentStore(root,badManifest,updateNetwork);
        try{await update.InstallAsync("simulated",null,CancellationToken.None);throw new Exception("Bad update accepted");}catch(InvalidDataException){Assert(File.ReadAllText(Path.Combine(folder,"component.js"))=="abc","Working version lost");}
    });
    await Check("Cancelled component download leaves no installation",async()=>{
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("abc"u8));var manifest=await TestManifest("cancel",hash);using var network=new FixtureNetwork("abc"u8.ToArray());var store=new ComponentStore(root,manifest,network);using var cancel=new CancellationTokenSource();cancel.Cancel();
        try{await store.InstallAsync("simulated",null,cancel.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Assert(store.GetInstalled("simulated")==null,"Cancelled install visible");}
    });
    await Check("Cancelled Office profile-lock wait cleans work directory",async()=>{
        var engine=Path.Combine(projectRoot,"components","office","26.2.6");var version=System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(engine,"program","soffice.com")).FileVersion??"unknown";
        var data=Path.Combine(root,"office-lock-case");var profile=Path.Combine(data,"office-profile",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(version)))[..12]);Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        using var blocker=new FileStream(profile+".lock",FileMode.Create,FileAccess.ReadWrite,FileShare.None);using var cancellation=new CancellationTokenSource();
        var conversion=new OfficeConverter(data).ConvertAsync(Path.Combine(projectRoot,"tests","fixtures","旧文字样本.doc"),engine,cancellation.Token);await Task.Delay(300);cancellation.Cancel();
        try{await conversion;throw new Exception("Cancelled lock wait completed");}catch(OperationCanceledException){Assert(!Directory.GetDirectories(Path.Combine(data,"work")).Any(),"Cancelled work retained");}
    });
    if(args.Contains("--office-installer"))await Check("Office MSI component installs from verified cached official archive",async()=>{
        var manifest=Path.Combine(projectRoot,"data","component-manifest.json");using var network=new ArchiveNetwork(Path.Combine(projectRoot,"tmp","libreoffice.msi"));var store=new ComponentStore(root,manifest,network);
        var folder=await store.InstallAsync("office",null,CancellationToken.None);Assert(store.GetInstalled("office")==folder,"Installed engine not registered");
        var processInfo=new System.Diagnostics.ProcessStartInfo(Path.Combine(folder,"program","soffice.com")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};processInfo.ArgumentList.Add("--version");
        using var process=System.Diagnostics.Process.Start(processInfo)!;var version=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();Assert(process.ExitCode==0&&version.Contains("26.2.6"),"Extracted engine not runnable");
    });
    Directory.CreateDirectory(Path.Combine(projectRoot,"reports"));await File.WriteAllTextAsync(Path.Combine(projectRoot,"reports","core-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{ranAt=DateTimeOffset.Now,results},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
}
finally { Directory.Delete(root,true); }

sealed class FixtureNetwork(byte[] payload):HttpMessageHandler
{
    public bool Offline{get;set;} public int Requests{get;private set;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {token.ThrowIfCancellationRequested();Requests++;if(Offline)throw new HttpRequestException("Simulated offline");return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(payload)});}
}
sealed class ArchiveNetwork(string path):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {token.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StreamContent(File.OpenRead(path))});}
}
