using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LightReader.Core;

public sealed class OfficeConverter(string dataRoot)
{
    public async Task<string> ConvertAsync(string path, string engineFolder, CancellationToken token)
    {
        var executable = Path.Combine(engineFolder, "program", "soffice.com");
        if (!File.Exists(executable)) throw new FileNotFoundException("办公兼容组件不完整");
        var originalHash = await HashAsync(path, token);
        var version = FileVersionInfo.GetVersionInfo(executable).FileVersion ?? "unknown";
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalHash + version + "pdf-v1")));
        var cache = Path.Combine(dataRoot, "cache", cacheKey);
        var complete = Path.Combine(cache, "document.pdf");
        if (File.Exists(complete)) return complete;
        var work = Path.Combine(dataRoot, "work", Guid.NewGuid().ToString("N"));
        var input = Path.Combine(work, "input"); var output = Path.Combine(work, "output");
        var profile = Path.Combine(dataRoot,"office-profile",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version)))[..12]);
        Directory.CreateDirectory(input); Directory.CreateDirectory(output); Directory.CreateDirectory(Path.Combine(profile, "user"));
        // Always convert a snapshot, in an isolated profile with macros and link updates disabled.
        var snapshot = Path.Combine(input, "source" + Path.GetExtension(path));
        try
        {
            await using var profileLock=await LockProfileAsync(profile+".lock",token);
            File.Copy(path, snapshot);
            if (await HashAsync(snapshot, token) != originalHash) throw new IOException("文件正在修改，请稍后重试");
            ConfigureProfile(Path.Combine(profile,"user","registrymodifications.xcu"));
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = work };
            foreach (var argument in new[] { "-env:UserInstallation=" + new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri, "--headless", "--nologo", "--nodefault", "--norestore", "--convert-to", "pdf", "--outdir", output, snapshot }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("无法启动办公组件");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!token.IsCancellationRequested) throw new TimeoutException("文档处理超时"); throw; }
            await Task.WhenAll(stdout, stderr);
            var result = Path.Combine(output, "source.pdf");
            if (process.ExitCode != 0 || !File.Exists(result) || new FileInfo(result).Length < 5) throw new InvalidDataException("办公组件未能读取这份文件，可能已加密或已损坏");
            if (await HashAsync(path, token) != originalHash) throw new IOException("原文件已变化，请重新打开");
            Directory.CreateDirectory(cache);
            File.Move(result, complete, true);
            return complete;
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } }
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    { await using var input = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(input, token)); }
    private static async Task<FileStream> LockProfileAsync(string path,CancellationToken token)
    {
        var watch=Stopwatch.StartNew();
        while(true)
        {
            token.ThrowIfCancellationRequested();
            try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException){if(watch.Elapsed>TimeSpan.FromMinutes(2))throw new TimeoutException("其他文档处理任务尚未结束");await Task.Delay(100,token);}
        }
    }
    private static void ConfigureProfile(string path)
    {
        XNamespace registry="http://openoffice.org/2001/registry";
        XDocument configuration;
        if(File.Exists(path))
        {using var input=XmlReader.Create(path,new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});configuration=XDocument.Load(input);}
        else configuration=new(new XElement(registry+"items",new XAttribute(XNamespace.Xmlns+"oor",registry)));
        var root=configuration.Root!;
        foreach(var setting in new[]{("/org.openoffice.Office.Common/Security/Scripting","MacroSecurityLevel","3"),("/org.openoffice.Office.Common/Security/Scripting","DisableMacrosExecution","true"),("/org.openoffice.Office.Writer/Content/Update","Link","2"),("/org.openoffice.Office.Calc/Content/Update","Link","2")})
        {
            var item=root.Elements("item").FirstOrDefault(e=>(string?)e.Attribute(registry+"path")==setting.Item1);
            if(item==null){item=new("item",new XAttribute(registry+"path",setting.Item1));root.Add(item);}
            var property=item.Elements("prop").FirstOrDefault(e=>(string?)e.Attribute(registry+"name")==setting.Item2);
            if(property==null){property=new("prop",new XAttribute(registry+"name",setting.Item2));item.Add(property);}
            property.SetAttributeValue(registry+"op","fuse");property.ReplaceNodes(new XElement("value",setting.Item3));
        }
        configuration.Save(path);
    }
}
