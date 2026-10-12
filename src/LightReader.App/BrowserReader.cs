using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using LightReader.Core;

namespace LightReader.App;
public sealed class BrowserReader : IDisposable
{
    public WebView2 View { get; } = new();
    private readonly ComponentStore components;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? render;
    private string currentId = "";
    private string? document;
    internal string? SourcePath=>document;
    private bool initialized;
    public event Action<string>? StatusChanged;
    public BrowserReader(ComponentStore store) => components = store;
    public async Task InitializeAsync()
    {
        if (initialized) { await ready.Task; return; }
        initialized = true;
        var options = new CoreWebView2EnvironmentOptions("--disable-gpu --disable-background-networking --disable-component-update --disable-features=msEdgeSidebarV2,msEdgeWallet");
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(App.DataRoot, "webview"), options);
        await View.EnsureCoreWebView2Async(environment);
        var core = View.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += ResourceRequested;
        core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://reader.local/", StringComparison.Ordinal)) e.Cancel = true; };
        core.FrameNavigationStarting += (_, e) => { e.Cancel = true; };
        core.NewWindowRequested += (_, e) => { e.Handled = true; };
        core.DownloadStarting += (_, e) => { e.Cancel = true; };
        core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; };
        core.WebMessageReceived += (_, e) =>
        {
            if (!e.Source.StartsWith("https://reader.local/index.html", StringComparison.Ordinal)) return;
            try
            {
                using var data = JsonDocument.Parse(e.WebMessageAsJson);
                var type = data.RootElement.GetProperty("type").GetString();
                if (type == "ready") { ready.TrySetResult(); return; }
                if (data.RootElement.GetProperty("id").GetString() != currentId) return;
                if (type == "rendered") render?.TrySetResult();
                if (type == "error") render?.TrySetException(new InvalidDataException(data.RootElement.GetProperty("message").GetString()));
                if (type == "status") StatusChanged?.Invoke(data.RootElement.GetProperty("message").GetString() ?? "");
            }
            catch (JsonException) { }
            catch (KeyNotFoundException) { }
        };
        core.Navigate("https://reader.local/index.html");
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    private void ResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var core = View.CoreWebView2;
        var uri = new Uri(e.Request.Uri);
        if (uri.Scheme is "data" or "blob") return;
        string? file = null;
        if (uri.Host == "reader.local" && uri.Scheme == "https")
        {
            var relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            if (relative == "document/" + currentId + "/source") file = document;
            else if (relative.StartsWith("component/", StringComparison.Ordinal))
            {
                var parts = relative.Split('/');
                if (parts.Length == 3 && components.GetInstalled(parts[1]) is { } folder && Path.GetFileName(parts[2]) == parts[2])
                    file = Path.Combine(folder, parts[2]);
            }
            else if (relative is "index.html" or "reader.js" or "reader.css" or "sheet-worker.js")
                file = Path.Combine(AppContext.BaseDirectory, "www", relative);
        }
        if (file == null || !File.Exists(file))
        { e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(), 403, "Blocked", "Content-Type: text/plain"); return; }
        var contentType = Path.GetExtension(file).ToLowerInvariant() switch { ".js" => "text/javascript", ".css" => "text/css", ".html" => "text/html", ".pdf" => "application/pdf", _ => "application/octet-stream" };
        var headers = $"Content-Type: {contentType}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff";
        if (contentType == "text/html") headers += "\r\nContent-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data: blob:; connect-src 'self'; worker-src 'self' blob:; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";
        e.Response = core.Environment.CreateWebResourceResponse(File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), 200, "OK", headers);
    }
    public async Task ShowAsync(DocumentInfo info, string source, CancellationToken token)
    {
        await InitializeAsync(); token.ThrowIfCancellationRequested();
        document = source; currentId = Guid.NewGuid().ToString("N");
        if (info.Kind == DocumentKind.Pdf)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? s, CoreWebView2NavigationCompletedEventArgs e)
            { if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new IOException("PDF 显示失败")); }
            View.CoreWebView2.NavigationCompleted += Completed;
            try
            { View.CoreWebView2.Navigate("https://reader.local/document/" + currentId + "/source"); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30), token); }
            finally { View.CoreWebView2.NavigationCompleted -= Completed; }
            return;
        }
        if (!View.CoreWebView2.Source.StartsWith("https://reader.local/index.html", StringComparison.Ordinal))
        {
            var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? s, CoreWebView2NavigationCompletedEventArgs e) { if (e.IsSuccess) navigation.TrySetResult(); else navigation.TrySetException(new IOException("阅读页面加载失败")); }
            View.CoreWebView2.NavigationCompleted += Completed;
            try { View.CoreWebView2.Navigate("https://reader.local/index.html"); await navigation.Task.WaitAsync(TimeSpan.FromSeconds(30), token); }
            finally { View.CoreWebView2.NavigationCompleted -= Completed; }
        }
        render = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var archive=info.Kind==DocumentKind.HtmlArchive?await ArchiveReader.ReadAsync(source,token):null;
        var text = archive?.Html??(info.Kind is DocumentKind.Html or DocumentKind.Xml ? (await TextDocument.ReadAsync(source, token)).Text : null);
        View.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "open", id = currentId, kind = archive==null?info.Kind.ToString().ToLowerInvariant():"html", url = "/document/" + currentId + "/source", text, extension = info.Extension, assets=archive?.Assets,baseUrl=archive?.BaseUrl }));
        await render.Task.WaitAsync(TimeSpan.FromSeconds(90), token);
    }
    public void Find(string query)
    {
        if (!View.CoreWebView2.Source.StartsWith("https://reader.local/index.html", StringComparison.Ordinal)) return;
        View.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "find", query }));
    }
    public Task<string> EvaluateAsync(string expression) => View.CoreWebView2.ExecuteScriptAsync(expression);
    public void Dispose() { render?.TrySetCanceled(); View.Dispose(); }
}
