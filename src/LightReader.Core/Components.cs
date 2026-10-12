using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;

namespace LightReader.Core;

public sealed record ComponentAsset(string Name, string Url, string Sha256, long Bytes);
public sealed record ComponentDefinition(string Id, string Name, string Version, string License, ComponentAsset[] Assets, string? Installer = null,long? ExpectedInstalledBytes=null)
{ public long DownloadBytes => Assets.Sum(a => a.Bytes); }
public sealed record ComponentCatalog(int SchemaVersion, ComponentDefinition[] Components);
public sealed class ComponentStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly string root;
    private readonly HttpMessageHandler? handler;
    public ComponentDefinition[] Definitions { get; }
    public ComponentStore(string dataRoot, string manifest,HttpMessageHandler? downloadHandler=null)
    {
        root = Path.Combine(dataRoot, "components");
        handler=downloadHandler;
        Definitions = JsonSerializer.Deserialize<ComponentCatalog>(File.ReadAllText(manifest), Json)?.Components ?? [];
    }
    public string Folder(ComponentDefinition component) => Path.Combine(root, component.Id, component.Version);
    public string? GetInstalled(string id)
    {
        var component = Definitions.FirstOrDefault(c => c.Id == id);
        if (component == null) return null;
        var folder = Folder(component);
        if (!File.Exists(Path.Combine(folder, ".complete.json"))) return null;
        if (component.Installer == "msi") return Directory.Exists(Path.Combine(folder, "program")) ? folder : null;
        return component.Assets.All(a => File.Exists(Path.Combine(folder, a.Name))) ? folder : null;
    }
    public async Task<string> InstallAsync(string id, IProgress<(long Done, long Total)>? progress, CancellationToken token)
    {
        var definition = Definitions.Single(c => c.Id == id);
        if (GetInstalled(id) is { } installed) return installed;
        var staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var client = handler==null?new HttpClient():new HttpClient(handler,false);
            client.Timeout=TimeSpan.FromMinutes(10);
            long received = 0;
            foreach (var asset in definition.Assets)
            {
                if (Path.GetFileName(asset.Name) != asset.Name || !Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                    throw new InvalidDataException("组件清单无效");
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(token);
                var target = Path.Combine(staging, asset.Name);
                await using (var output = File.Create(target))
                {
                    var buffer = new byte[65536]; int count; long fileBytes = 0;
                    while ((count = await source.ReadAsync(buffer, token)) > 0)
                    {
                        fileBytes += count;
                        if (fileBytes > asset.Bytes) throw new InvalidDataException("组件大小不符");
                        await output.WriteAsync(buffer.AsMemory(0, count), token);
                        received += count; progress?.Report((received, definition.DownloadBytes));
                    }
                    if (fileBytes != asset.Bytes) throw new InvalidDataException("组件下载不完整");
                }
                await using var verify = File.OpenRead(target);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(verify, token));
                if (!string.Equals(hash, asset.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("组件校验失败");
            }
            var payload = staging;
            if (definition.Installer == "msi")
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("办公兼容组件需要 Windows");
                payload = Path.Combine(staging, "expanded"); Directory.CreateDirectory(payload);
                var installer = new ProcessStartInfo("msiexec.exe") { UseShellExecute=false, CreateNoWindow=true };
                foreach (var argument in new[]{"/a", Path.Combine(staging,definition.Assets.Single().Name), "/qn", "TARGETDIR="+payload, "/L*v", Path.Combine(staging,"extract.log")}) installer.ArgumentList.Add(argument);
                using var process=Process.Start(installer)??throw new IOException("无法提取兼容组件");
                // Let the Windows Installer finish its own transaction before deleting the staging directory.
                await process.WaitForExitAsync(CancellationToken.None);
                token.ThrowIfCancellationRequested();
                if(process.ExitCode!=0 || !File.Exists(Path.Combine(payload,"program","soffice.com"))) throw new IOException("兼容组件提取失败（"+process.ExitCode+"）");
                foreach(var copy in Directory.GetFiles(payload,"*.msi",SearchOption.TopDirectoryOnly))File.Delete(copy);
            }
            else if(definition.Installer!=null)throw new NotSupportedException("未知组件安装方式");
            await File.WriteAllTextAsync(Path.Combine(payload, ".complete.json"), JsonSerializer.Serialize(definition, Json), token);
            var destination = Folder(definition);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            token.ThrowIfCancellationRequested();
            if(GetInstalled(id) is { } concurrentlyInstalled)return concurrentlyInstalled;
            string? previous=null;
            if(Directory.Exists(destination)) {previous=destination+".previous-"+Guid.NewGuid().ToString("N");Directory.Move(destination,previous);}
            try{Directory.Move(payload,destination);}
            catch {if(previous!=null && !Directory.Exists(destination))Directory.Move(previous,destination);throw;}
            if(previous!=null)Directory.Delete(previous,true);
            return destination;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    public void Remove(string id)
    {
        var definition = Definitions.Single(c => c.Id == id);
        var folder = Folder(definition);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
    public long InstalledBytes(string id)
    {
        var installed = GetInstalled(id);
        return installed == null ? 0 : Directory.EnumerateFiles(installed, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
    }
}
