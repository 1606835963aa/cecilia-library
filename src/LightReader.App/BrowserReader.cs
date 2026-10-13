using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using LightReader.Core;

namespace LightReader.App;
public sealed class BrowserReader : IDisposable
{
    public WebView2 View { get; } = new();
    private readonly ComponentStore components;
    private TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? showing;
    private Task? initialization;
    private TaskCompletionSource? render;
    private string currentId = "";
    private string? document;
    internal string? SourcePath=>document;
    private bool disposed;
    private bool coreConfigured;
    private long documentGeneration;
    private bool dark;
    private bool sheetDocument;
    private string findTerm = "";
    public event Action<string>? StatusChanged;
    public event Action<string>? PositionChanged;
    public event Action<string>? ReadingFailed;
    // 当前第几个、共几个；没有匹配时都是 0
    public event Action<int,int>? FindChanged;
    public BrowserReader(ComponentStore store) => components = store;
    public void SetTheme(bool useDark)
    {
        if (disposed) return;
        dark = useDark;
        View.DefaultBackgroundColor = dark ? System.Drawing.Color.FromArgb(0x14, 0x13, 0x12) : System.Drawing.Color.FromArgb(0xE9, 0xE6, 0xE0);
        if (View.CoreWebView2 != null) View.CoreWebView2.Profile.PreferredColorScheme = dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }
    public async Task InitializeAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (initialization == null || initialization.IsFaulted || initialization.IsCanceled)
        {
            ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            initialization = InitializeCoreAsync();
        }
        await initialization.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    private async Task InitializeCoreAsync()
    {
        var pageReady = ready;
        try
        {
        var options = new CoreWebView2EnvironmentOptions("--disable-gpu --disable-background-networking --disable-component-update --disable-features=msEdgeSidebarV2,msEdgeWallet");
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(App.DataRoot, "webview"), options);
        lifetime.Token.ThrowIfCancellationRequested();
        await View.EnsureCoreWebView2Async(environment);
        lifetime.Token.ThrowIfCancellationRequested();
        var core = View.CoreWebView2;
        SetTheme(dark);
        if (!coreConfigured)
        {
        try
        {
            core.Find.MatchCountChanged += (_, _) => ReportFind();
            core.Find.ActiveMatchIndexChanged += (_, _) => ReportFind();
        }
        catch (Exception e) when (e is NotImplementedException or InvalidCastException or System.Runtime.InteropServices.COMException) { }
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
        core.ProcessFailed += (_, e) =>
        {
            if (disposed) return;
            var error = new IOException("阅读组件已停止，请重新打开文档");
            ready.TrySetException(error); render?.TrySetException(error);
            ReadingFailed?.Invoke(error.Message);
        };
        core.WebMessageReceived += (_, e) =>
        {
            if (disposed || e.Source != "https://reader.local/index.html") return;
            try
            {
                using var data = JsonDocument.Parse(e.WebMessageAsJson);
                var type = data.RootElement.GetProperty("type").GetString();
                if (type == "ready") { ready.TrySetResult(); return; }
                if (data.RootElement.GetProperty("id").GetString() != currentId) return;
                if (type == "rendered") render?.TrySetResult();
                if (type == "error") render?.TrySetException(new InvalidDataException(data.RootElement.GetProperty("message").GetString()));
                if (type == "status") StatusChanged?.Invoke(data.RootElement.GetProperty("message").GetString() ?? "");
                if (type == "position")
                {
                    var position = data.RootElement.GetProperty("message").GetString();
                    if (position?.Length is > 0 and <= 8192)
                    {
                        using var state = JsonDocument.Parse(position);
                        if (state.RootElement.ValueKind == JsonValueKind.Object) PositionChanged?.Invoke(position);
                    }
                }
                if (type == "find")
                {
                    var parts = (data.RootElement.GetProperty("message").GetString() ?? "").Split('/');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var index) && int.TryParse(parts[1], out var total)) FindChanged?.Invoke(index, total);
                }
            }
            catch (JsonException) { }
            catch (KeyNotFoundException) { }
        };
        coreConfigured = true;
        }
        core.Navigate("https://reader.local/index.html");
        await pageReady.Task.WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
        }
        catch (Exception error)
        {
            pageReady.TrySetException(error);
            throw;
        }
    }
    private void ResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (disposed) return;
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
        try { e.Response = core.Environment.CreateWebResourceResponse(File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), 200, "OK", headers); }
        catch (IOException) { e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(), 404, "Unavailable", "Content-Type: text/plain"); }
        catch (UnauthorizedAccessException) { e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(), 403, "Blocked", "Content-Type: text/plain"); }
    }
    private async Task NavigateAsync(string uri, CancellationToken token)
    {
        var core = View.CoreWebView2;
        var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? navigationId = null;
        void Starting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        { if (e.Uri == uri) navigationId = e.NavigationId; }
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.NavigationId != navigationId) return;
            if (e.IsSuccess) navigation.TrySetResult(); else navigation.TrySetException(new IOException("阅读页面加载失败"));
        }
        core.NavigationStarting += Starting; core.NavigationCompleted += Completed;
        try { core.Navigate(uri); await navigation.Task.WaitAsync(TimeSpan.FromSeconds(30), token); }
        finally
        {
            if (!disposed) { core.NavigationStarting -= Starting; core.NavigationCompleted -= Completed; }
        }
    }
    public async Task ShowAsync(DocumentInfo info, string source, CancellationToken token, string? positionJson = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        showing?.Cancel(); render?.TrySetCanceled(); StopFind();
        using var active = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        showing = active;
        var cancellation = active.Token;
        var generation = ++documentGeneration;
        var id = currentId = Guid.NewGuid().ToString("N");
        document = source; sheetDocument = info.Kind == DocumentKind.Spreadsheet;
        void CheckCurrent()
        { cancellation.ThrowIfCancellationRequested(); if (disposed || generation != documentGeneration) throw new OperationCanceledException(cancellation); }
        try
        {
            await InitializeAsync(cancellation); CheckCurrent();
            if (info.Kind == DocumentKind.Pdf)
            { await NavigateAsync("https://reader.local/document/" + id + "/source", cancellation); CheckCurrent(); return; }
            if (View.CoreWebView2.Source != "https://reader.local/index.html")
            { await NavigateAsync("https://reader.local/index.html", cancellation); CheckCurrent(); }
            var rendered = render = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var archive = info.Kind == DocumentKind.HtmlArchive ? await ArchiveReader.ReadAsync(source, cancellation) : null;
            var text = archive?.Html ?? (info.Kind is DocumentKind.Html or DocumentKind.Xml ? (await TextDocument.ReadAsync(source, cancellation)).Text : null);
            CheckCurrent();
            JsonElement? position = null;
            if (positionJson?.Length is > 0 and <= 8192)
            {
                try { using var state = JsonDocument.Parse(positionJson); if (state.RootElement.ValueKind == JsonValueKind.Object) position = state.RootElement.Clone(); }
                catch (JsonException) { }
            }
            View.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "open", id, kind = archive == null ? info.Kind.ToString().ToLowerInvariant() : "html", url = "/document/" + id + "/source", text, extension = info.Extension, assets = archive?.Assets, baseUrl = archive?.BaseUrl, position }));
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(90), cancellation); CheckCurrent();
        }
        finally { if (ReferenceEquals(showing, active)) showing = null; }
    }
    public void Find(string query, bool backward = false) => _ = FindAsync(query, backward);
    private async Task FindAsync(string query, bool backward)
    {
        if (disposed) return;
        var generation = documentGeneration;
        var core = View.CoreWebView2;
        if (core == null || !core.Source.StartsWith("https://reader.local/index.html", StringComparison.Ordinal)) return;
        // 表格的数据不全在页面上，由表格组件自己跨工作表查找
        if (!sheetDocument)
        {
            try
            {
                var find = core.Find;
                if (query == findTerm) { if (backward) find.FindPrevious(); else find.FindNext(); return; }
                var options = core.Environment.CreateFindOptions();
                options.FindTerm = query; options.SuppressDefaultFindDialog = true; options.ShouldHighlightAllMatches = true;
                findTerm = query;
                await find.StartAsync(options); if (!disposed && generation == documentGeneration) ReportFind(); return;
            }
            catch (Exception e) when (e is NotImplementedException or InvalidCastException or System.Runtime.InteropServices.COMException or InvalidOperationException or ObjectDisposedException) { if (generation == documentGeneration) findTerm = ""; }
        }
        if (!disposed && generation == documentGeneration) core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "find", id = currentId, query, backward }));
    }
    private void ReportFind()
    {
        if (disposed || findTerm.Length == 0) return;
        try { var find = View.CoreWebView2.Find; FindChanged?.Invoke(Math.Max(0, find.ActiveMatchIndex), find.MatchCount); }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or ObjectDisposedException) { }
    }
    public void StopFind()
    {
        if (findTerm.Length == 0) return;
        findTerm = "";
        try { View.CoreWebView2?.Find.Stop(); }
        catch (Exception e) when (e is NotImplementedException or InvalidCastException or System.Runtime.InteropServices.COMException or InvalidOperationException or ObjectDisposedException) { }
    }
    public Task<string> EvaluateAsync(string expression)
    { ObjectDisposedException.ThrowIf(disposed, this); return View.CoreWebView2.ExecuteScriptAsync(expression); }
    public void Dispose()
    {
        if (disposed) return;
        StopFind(); disposed = true; documentGeneration++;
        showing?.Cancel(); lifetime.Cancel(); ready.TrySetCanceled(); render?.TrySetCanceled();
        document = null; currentId = ""; View.Dispose();
        StatusChanged = null; FindChanged = null; PositionChanged = null; ReadingFailed = null;
    }
}
