using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using LightReader.Core;

namespace LightReader.App;
public sealed class DocumentTab(string path)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Badge => FileBadge.For(Path).Text;
    public Brush BadgeBrush => FileBadge.For(Path).Brush;
    public TextDocument? Text { get; set; }
    public string? EditBuffer { get; set; }
    public bool Editing { get; set; }
    public bool Dirty { get; set; }
    public double Zoom { get; set; } = 1;
    public bool Precise { get; set; }
    public bool PdfTextMode { get; set; }
    public uint PdfPage { get; set; }
    public double PdfOffset { get; set; }
    public int ImagePage { get; set; }
    public double ImageHorizontalOffset { get; set; }
    public double ImageVerticalOffset { get; set; }
    public double RtfOffset { get; set; }
    public string? BrowserPosition { get; set; }
}
public partial class MainWindow : Window
{
    private readonly ObservableCollection<DocumentTab> documents = [];
    private readonly ReaderSettings settings = ReaderSettings.Load();
    private readonly ComponentStore components = new(App.DataRoot, Path.Combine(AppContext.BaseDirectory, "component-manifest.json"));
    private readonly OfficeConverter converter = new(App.DataRoot);
    private BrowserReader? browser;
    private NativePdfReader? nativePdf;
    private string? pdfSource;
    private CancellationTokenSource? operation;
    private DocumentTab? current;
    private DocumentInfo? info;
    private TextBox? textBox;
    private ScrollViewer? imageScroll;
    private List<BitmapSource> frames = [];
    private int frameIndex;
    private double zoom = 1;
    private Func<Task>? action;
    private bool suppressTabChange;
    private long displayGeneration;
    private string findQuery = "";
    private int findIndex = -1;
    private bool pdfModeChanging;
    public MainWindow(string[] paths, string? verificationRoot = null, string? benchmarkFile = null,string? stateFile=null)
    {
        InitializeComponent(); Tabs.ItemsSource = documents;
        documents.CollectionChanged += (_, _) => AppTitle.Visibility = documents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        App.ThemeChanged += OnThemeChanged; App.ApplyTheme(settings.Theme); UpdateThemeButton();
        if (settings.WindowWidth >= MinWidth && settings.WindowHeight >= MinHeight)
        { Width = Math.Min(settings.WindowWidth, SystemParameters.WorkArea.Width); Height = Math.Min(settings.WindowHeight, SystemParameters.WorkArea.Height); }
        if (settings.Maximized) WindowState = WindowState.Maximized;
        OnStateChanged(this, EventArgs.Empty);
        SourceInitialized += (_, _) => { ((System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this)).AddHook(WindowMessage); ApplyWindowTheme(); };
        ShowHome();
        Loaded += async (_, _) => { if (benchmarkFile != null) await RunBenchmarkAsync(benchmarkFile); else if (verificationRoot != null) await RunVerificationAsync(verificationRoot); else {foreach (var path in paths) await AddFileAsync(path);if(stateFile!=null)await File.WriteAllTextAsync(stateFile,System.Text.Json.JsonSerializer.Serialize(new{shown=Viewer.Visibility==Visibility.Visible,status=Status.Text,renderer=nativePdf!=null?"Windows.Data.Pdf":browser!=null?"WebView2":"WPF"}));} };
    }
    private async void OpenDialog(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "打开文档", Multiselect = true, Filter = "文档|*.doc;*.docx;*.docm;*.dot;*.dotx;*.dotm;*.wps;*.wpt;*.odt;*.xls;*.xlsx;*.xlsm;*.xlt;*.xltx;*.xltm;*.et;*.ett;*.csv;*.dbf;*.prn;*.dif;*.ppt;*.pptx;*.pptm;*.pot;*.potx;*.potm;*.pps;*.ppsx;*.ppsm;*.dps;*.dpt;*.pdf;*.txt;*.rtf;*.htm;*.html;*.mht;*.mhtml;*.xml;*.jpg;*.png;*.tif;*.bmp;*.xla;*.xlam;*.ts|所有文件|*.*" };
        if (dialog.ShowDialog(this) == true) foreach (var path in dialog.FileNames) await AddFileAsync(path);
    }
    internal async Task AddFileAsync(string path)
    {
        try { path = System.IO.Path.GetFullPath(path); if (!File.Exists(path)) throw new FileNotFoundException("文件不存在"); }
        catch (Exception e) { ReleaseReadingView(); SetMessage("无法打开文件", e.Message, false); Status.Text = "打开失败"; return; }
        var tab = documents.FirstOrDefault(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (tab == null) { tab = new(path); documents.Add(tab); }
        suppressTabChange = true; Tabs.SelectedItem = tab; suppressTabChange = false;
        await DisplayAsync(tab);
    }
    private void RememberBuffer()
    {
        if (current == null) return;
        current.Zoom = zoom;
        if (textBox != null)
        { if (current.Editing && current.Dirty) current.EditBuffer = textBox.Text; settings.ScrollPositions[current.Path] = textBox.VerticalOffset; }
        if (nativePdf != null) { current.PdfPage = nativePdf.CurrentPage; current.PdfOffset = nativePdf.VerticalOffset; }
        if (imageScroll != null)
        { current.ImagePage = frameIndex; current.ImageHorizontalOffset = imageScroll.HorizontalOffset; current.ImageVerticalOffset = imageScroll.VerticalOffset; }
        if (Viewer.Content is FlowDocumentScrollViewer rich && FindDescendant<ScrollViewer>(rich) is { } scroll) current.RtfOffset = scroll.VerticalOffset;
    }
    // 标签只留下小的阅读状态；未保存的文本才保留原文基线和编辑缓冲。
    private void ReleaseReadingView(bool remember = true)
    {
        if (remember) RememberBuffer();
        displayGeneration++;
        operation?.Cancel(); operation?.Dispose(); operation = null;
        if (current is { Dirty: false } clean) { clean.Text = null; clean.EditBuffer = null; }
        if (Keyboard.FocusedElement is Visual focus && Viewer.IsAncestorOf(focus))
        { Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, null); }
        Viewer.Content = null; ViewerTools.Content = null;
        textBox = null;
        if (imageScroll?.Content is Image image) image.Source = null;
        if (imageScroll != null) imageScroll.Content = null;
        imageScroll = null; frames.Clear();
        nativePdf?.Dispose(); nativePdf = null; pdfSource = null;
        browser?.Dispose(); browser = null;
        current = null; info = null; pdfModeChanging = false;
        suppressTabChange = true; Tabs.SelectedItem = null; suppressTabChange = false;
        Toolbar.Visibility = Visibility.Collapsed; FormatLabel.Text = ""; Title = App.DisplayName;
        pdfMode.Visibility = ImagePages.Visibility = Visibility.Collapsed;
        pdfMode.IsEnabled = true;
    }
    private bool IsCurrentDisplay(DocumentTab tab, long generation, CancellationToken token) =>
        generation == displayGeneration && ReferenceEquals(current, tab) && !token.IsCancellationRequested;
    private void CheckDisplay(DocumentTab tab, long generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsCurrentDisplay(tab, generation, token)) throw new OperationCanceledException(token);
    }
    private async Task DisplayAsync(DocumentTab tab, bool precise = false)
    {
        ReleaseReadingView(); operation = new();
        var cancellation = operation.Token;
        var generation = displayGeneration;
        tab.Precise |= precise; precise = tab.Precise;
        current = tab; zoom = tab.Zoom; ZoomLabel.Text = $"{zoom:P0}";
        HidePanels(); Toolbar.Visibility = Visibility.Visible; Viewer.Content = null;
        if (Tabs.SelectedItem != tab) { suppressTabChange = true; Tabs.SelectedItem = tab; suppressTabChange = false; }
        EditButton.Visibility = SaveButton.Visibility = PreciseButton.Visibility = ImagePages.Visibility = Visibility.Collapsed;
        SearchButton.IsEnabled = true; PrintButton.IsEnabled = true;
        FormatLabel.Text = System.IO.Path.GetExtension(tab.Path).TrimStart('.').ToUpperInvariant();
        Title = tab.Name + " · " + App.DisplayName;
        var timer = Stopwatch.StartNew();
        try
        {
            SetMessage("正在打开", tab.Name, true);
            var inspected = await Task.Run(() => FileInspector.Inspect(tab.Path), cancellation);
            CheckDisplay(tab, generation, cancellation);
            info = inspected;
            if (info.Extension is "xla" or "xlam") FormatLabel.Text += " · 加载项数据";
            if (info.Kind == DocumentKind.Undefined) { SetMessage("查看范围待核实", info.Problem ?? "", false); return; }
            if (new FileInfo(tab.Path).Length > 200 * 1024 * 1024 && info.Kind is not DocumentKind.Pdf) throw new InvalidDataException("这份文件超过当前预览版本的处理范围（200 MiB）");
            var component = precise ? "office" : info.Component;
            if (component != null && components.GetInstalled(component) == null)
            {
                var definition = components.Definitions.FirstOrDefault(c => c.Id == component);
                SetMessage("需要格式组件", definition == null ? "兼容组件仍在验证" : $"{definition.Name} · 下载 {Size(definition.DownloadBytes)}"+(definition.ExpectedInstalledBytes is { } bytes ? $" · 安装约 {Size(bytes)}":""), false);
                if (definition != null)
                {
                    action = async () =>
                    {
                        SetMessage("正在安装组件", definition.Name, true);
                        try
                        {
                            await components.InstallAsync(component, InstallProgress(), cancellation);
                             if (IsCurrentDisplay(tab, generation, cancellation)) await DisplayAsync(tab, precise);
                        }
                         catch (OperationCanceledException) { if (generation == displayGeneration) SetMessage("安装已取消", definition.Name, false); }
                         catch (Exception error) { if (IsCurrentDisplay(tab, generation, cancellation)) { SetMessage("组件安装未完成", error.Message, false); Status.Text = "可以重新安装"; } }
                    };
                    ActionButton.Visibility = Visibility.Visible;
                }
                return;
            }
            HidePanels(); Viewer.Visibility = Visibility.Visible;
            if (precise || info.Kind == DocumentKind.Office)
            {
                SetMessage("正在生成分页预览", tab.Name, true);
                var output = await converter.ConvertAsync(tab.Path, components.GetInstalled("office")!, cancellation);
                CheckDisplay(tab, generation, cancellation); HidePanels(); Viewer.Visibility = Visibility.Visible;
                await ShowPdfAsync(output,cancellation);
                SearchButton.IsEnabled = false;
            }
            else if (info.Kind == DocumentKind.Text || (info.Extension == "csv" && tab.Editing))
            {
                     var loaded = tab.Dirty && tab.Text != null ? tab.Text : await TextDocument.ReadAsync(tab.Path, cancellation);
                     CheckDisplay(tab, generation, cancellation);
                     tab.Text = tab.Editing ? loaded : null;
                     var editor = new TextBox { Text = tab.Dirty ? tab.EditBuffer ?? loaded.Text : loaded.Text, IsReadOnly = !tab.Editing, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new(0), Padding = new(44,32,44,32), FontSize = 17 * zoom, FontFamily = new("Cascadia Mono, Microsoft YaHei UI") };
                     textBox = editor; StyleTextView(editor);
                     editor.TextChanged += (_, _) => { if (IsCurrentDisplay(tab, generation, cancellation) && ReferenceEquals(textBox, editor) && tab.Editing) { tab.Dirty = editor.Text != tab.Text?.Text; Status.Text = tab.Dirty ? "有未保存的修改" : "编辑中"; } };
                     Viewer.Content = editor;
                     if (settings.ScrollPositions.TryGetValue(tab.Path, out var offset))
                     { editor.Loaded += (_, _) => { if (IsCurrentDisplay(tab, generation, cancellation)) editor.ScrollToVerticalOffset(offset); }; editor.UpdateLayout(); editor.ScrollToVerticalOffset(offset); }
                    EditButton.Visibility = Visibility.Visible; EditButton.Content = tab.Editing ? "返回阅读" : "编辑"; SaveButton.Visibility = tab.Editing ? Visibility.Visible : Visibility.Collapsed;
            }
            else switch (info.Kind)
            {
                case DocumentKind.Rtf:
                    var flow = new FlowDocument { PagePadding = new(40), FontFamily = new("Microsoft YaHei UI") };
                    using (var source = File.OpenRead(tab.Path)) new TextRange(flow.ContentStart, flow.ContentEnd).Load(source, DataFormats.Rtf);
                    var rich = new FlowDocumentScrollViewer { Document = flow, Zoom = zoom * 100, IsToolBarVisible = false, IsInactiveSelectionHighlightEnabled = true, Background = Brushes.White };
                    rich.Loaded += (_, _) => { if (IsCurrentDisplay(tab, generation, cancellation)) FindDescendant<ScrollViewer>(rich)?.ScrollToVerticalOffset(tab.RtfOffset); };
                    Viewer.Content = rich; break;
                case DocumentKind.Image:
                     var decoded = await Task.Run(() => DecodeImages(tab.Path), cancellation); CheckDisplay(tab, generation, cancellation); frames=decoded;
                    imageScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
                     Viewer.Content = imageScroll; frameIndex = Math.Clamp(tab.ImagePage, 0, frames.Count - 1); DisplayImage(); SearchButton.IsEnabled = false;
                     imageScroll.UpdateLayout(); imageScroll.ScrollToHorizontalOffset(tab.ImageHorizontalOffset); imageScroll.ScrollToVerticalOffset(tab.ImageVerticalOffset);
                    ImagePages.Visibility = frames.Count > 1 ? Visibility.Visible : Visibility.Collapsed; break;
                case DocumentKind.Pdf:
                    await ShowPdfAsync(tab.Path,cancellation);SearchButton.IsEnabled=false;break;
                default:
                    await ShowBrowserAsync(info, tab.Path, cancellation);
                    if (info.Kind == DocumentKind.Pdf) SearchButton.IsEnabled = false;
                    if (info.Kind is DocumentKind.Word or DocumentKind.Spreadsheet)
                    { PreciseButton.Visibility = Visibility.Visible; if (info.Extension == "csv") EditButton.Visibility = Visibility.Visible; }
                    break;
            }
            CheckDisplay(tab, generation, cancellation);
            settings.Add(tab.Path); Status.Text = $"已打开 · {timer.Elapsed.TotalSeconds:F2} 秒";
        }
        catch (OperationCanceledException) { if (generation == displayGeneration) { ReleaseReadingView(); SetMessage("已取消", tab.Name, false); Status.Text = "已取消"; } }
        catch (Exception error) { if (generation == displayGeneration && !cancellation.IsCancellationRequested) { ReleaseReadingView(); SetMessage("无法显示文档", error.Message, false); Status.Text = "打开失败"; } }
    }
    private async Task ShowBrowserAsync(DocumentInfo documentInfo, string path, CancellationToken token)
    {
        var tab = current ?? throw new OperationCanceledException(token);
        var generation = displayGeneration;
        CheckDisplay(tab, generation, token);
        if (browser == null)
        {
            var created = browser = new(components); created.SetTheme(App.IsDark);
            created.StatusChanged += status => { if (IsCurrentDisplay(tab, generation, token) && ReferenceEquals(browser, created)) BrowserStatus(status); };
            created.FindChanged += (index, total) => { if (IsCurrentDisplay(tab, generation, token) && ReferenceEquals(browser, created)) ShowFind(index, total); };
            created.PositionChanged += position => { if (IsCurrentDisplay(tab, generation, token) && ReferenceEquals(browser, created)) tab.BrowserPosition = position; };
            created.ReadingFailed += detail =>
            {
                if (!IsCurrentDisplay(tab, generation, token) || !ReferenceEquals(browser, created)) return;
                ReleaseReadingView(); SetMessage("阅读组件已停止", detail, false); Status.Text = "可以重新打开";
            };
            // 在文档里按住 Ctrl 滚动滚轮时由浏览器自己缩放，这里把比例同步到工具栏
            var view = browser.View; view.ZoomFactorChanged += (_, _) => { if (Viewer.Content == view) { zoom = view.ZoomFactor; ZoomLabel.Text = $"{zoom:P0}"; } };
        }
        var reader = browser;
        Viewer.Content = reader.View; reader.View.ZoomFactor = zoom;
        await reader.ShowAsync(documentInfo, path, token, documentInfo.Kind == DocumentKind.Pdf ? null : tab.BrowserPosition);
        CheckDisplay(tab, generation, token);
    }
    private async Task ShowPdfAsync(string path,CancellationToken token)
    {
        var tab = current ?? throw new OperationCanceledException(token);
        var generation = displayGeneration;
        var documentInfo = info ?? throw new OperationCanceledException(token);
        CheckDisplay(tab, generation, token);
        pdfSource = path; pdfMode.Visibility = Visibility.Visible;
        if (tab.PdfTextMode)
        { await ShowBrowserAsync(documentInfo with { Kind = DocumentKind.Pdf }, path, token); CheckDisplay(tab, generation, token); pdfMode.Content = "页面阅读"; return; }
        browser?.Dispose();browser=null;nativePdf?.Dispose();
        var reader=new NativePdfReader();nativePdf=reader;pdfSource=path;Viewer.Content=reader;ViewerTools.Content=reader.Tools;
        reader.StatusChanged += status => { if (IsCurrentDisplay(tab, generation, token) && ReferenceEquals(nativePdf, reader)) BrowserStatus(status); };
        reader.ReadingFailed += detail =>
        {
            if(!IsCurrentDisplay(tab,generation,token)||!ReferenceEquals(nativePdf,reader))return;
            ReleaseReadingView();SetMessage("PDF 阅读组件已停止",detail,false);Status.Text="可以重新打开";
        };
        pdfMode.Content="文字与搜索";
        try
        {
            await reader.LoadAsync(path,token); CheckDisplay(tab, generation, token);
            if (zoom != 1) { await reader.SetZoomAsync(zoom); CheckDisplay(tab, generation, token); }
            if (tab.PdfPage != 0 || tab.PdfOffset != 0)
            { await reader.RestorePositionAsync(tab.PdfPage, tab.PdfOffset, token); CheckDisplay(tab, generation, token); }
        }
        catch(Exception error)when(error is System.Runtime.InteropServices.COMException or IOException)
        {
            CheckDisplay(tab, generation, token); reader.Dispose();nativePdf=null;ViewerTools.Content=null;
            await ShowBrowserAsync(documentInfo with{Kind=DocumentKind.Pdf},path,token); CheckDisplay(tab, generation, token); tab.PdfTextMode = true; pdfMode.Content="页面阅读";
        }
    }
    private async void PdfMode(object sender,RoutedEventArgs e)=>await SwitchPdfModeAsync();
    private async Task SwitchPdfModeAsync()
    {
        if(pdfModeChanging || pdfSource==null || info==null || current==null)return;
        var tab = current; var path = pdfSource; var documentInfo = info;
        var textMode = nativePdf != null;
        ReleaseReadingView(); operation = new();
        var token = operation.Token; var generation = displayGeneration;
        current = tab; info = documentInfo; zoom = tab.Zoom; pdfSource = path;
        Toolbar.Visibility = Visibility.Visible; FormatLabel.Text = documentInfo.Extension.ToUpperInvariant(); Title = tab.Name + " · " + App.DisplayName;
        suppressTabChange = true; Tabs.SelectedItem = tab; suppressTabChange = false;
        tab.PdfTextMode = textMode;
        pdfModeChanging = true; pdfMode.IsEnabled = false;
        HidePanels(); Viewer.Visibility = Visibility.Visible; pdfMode.Visibility = Visibility.Visible;
        try
        {
            await ShowPdfAsync(path,token); CheckDisplay(tab, generation, token);
            SearchButton.IsEnabled = false; Status.Text = textMode ? "文字与搜索" : "页面阅读";
        }
        catch(OperationCanceledException){ if (generation == displayGeneration) { ReleaseReadingView(); SetMessage("已取消", tab.Name, false); Status.Text = "已取消"; } }
        catch(Exception error){ if (generation == displayGeneration) { ReleaseReadingView(); SetMessage("无法显示文档", error.Message, false); Status.Text=error.Message; } }
        finally { if (generation == displayGeneration) { pdfModeChanging = false; pdfMode.IsEnabled = true; } }
    }
    private void BrowserStatus(string status) => Status.Text = status;
    private static List<BitmapSource> DecodeImages(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var result = new List<BitmapSource>();
        foreach (var frame in decoder.Frames) { if ((long)frame.PixelWidth * frame.PixelHeight > 60000000) throw new InvalidDataException("图片像素过大"); frame.Freeze(); result.Add(frame); }
        return result;
    }
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T found) return found;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private void DisplayImage()
    {
        if (imageScroll == null || frames.Count == 0) return;
        var frame = frames[frameIndex];
        var available = Math.Max(200, Viewer.ActualWidth - 48); var scale = Math.Min(1,Math.Min(available/frame.PixelWidth,Math.Max(200,Viewer.ActualHeight-48)/frame.PixelHeight));
        imageScroll.Content = new Image { Source = frame, Width = frame.PixelWidth * scale * zoom, Height = frame.PixelHeight * scale * zoom, Margin = new(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        ImagePageLabel.Text = $"{frameIndex + 1} / {frames.Count}";
    }
    private void HidePanels()
    { Home.Visibility = ComponentPanel.Visibility = MessagePanel.Visibility = Viewer.Visibility = SearchPanel.Visibility = Visibility.Collapsed; ActionButton.Visibility = Visibility.Collapsed; action = null; browser?.StopFind(); findQuery = ""; SearchCount.Text = ""; }
    private void SetMessage(string title, string detail, bool busy)
    { HidePanels(); MessagePanel.Visibility = Visibility.Visible; MessageTitle.Text = title; MessageDetail.Text = detail; Progress.IsIndeterminate = true; Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; }
    private async void TabChanged(object sender, SelectionChangedEventArgs e) { if (!suppressTabChange && Tabs.SelectedItem is DocumentTab tab) await DisplayAsync(tab); }
    private async void OnDrop(object sender, DragEventArgs e) { DropHint.Visibility = Visibility.Collapsed; if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) foreach (var path in paths) await AddFileAsync(path); }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        var files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None; DropHint.Visibility = files ? Visibility.Visible : Visibility.Collapsed; e.Handled = true;
    }
    private void OnDragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;
    private void OnWheel(object sender, MouseWheelEventArgs e)
    { if (Keyboard.Modifiers == ModifierKeys.Control && current != null && Viewer.Visibility == Visibility.Visible) { SetZoom(zoom + (e.Delta > 0 ? .1 : -.1)); e.Handled = true; } }
    private void TabMouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Middle) { CloseTab(sender, e); e.Handled = true; } }
    private Progress<(long Done, long Total)> InstallProgress()
    {
        var generation = displayGeneration;
        var token = operation?.Token ?? CancellationToken.None;
        return new(p =>
        {
            if (generation != displayGeneration || token.IsCancellationRequested || MessagePanel.Visibility != Visibility.Visible) return;
            MessageDetail.Text = $"{Size(p.Done)} / {Size(p.Total)}";
            if (p.Total > 0) { Progress.IsIndeterminate = false; Progress.Value = 100d * p.Done / p.Total; }
        });
    }
    private void StyleTextView(TextBox box)
    {
        box.BorderThickness = new(0); box.VerticalContentAlignment = VerticalAlignment.Top; box.IsInactiveSelectionHighlightEnabled = true;
        // 窗口很宽时把自动换行的文字收在中间一栏，方便阅读
        if (box.TextWrapping == TextWrapping.Wrap) box.SizeChanged += (_, e) => { var side = Math.Max(44, (e.NewSize.Width - 980) / 2); box.Padding = new(side, 32, side, 32); };
    }
    private async void RunAction(object sender, RoutedEventArgs e) { var next = action; if (next != null) await next(); }
    private void CancelWork(object sender, RoutedEventArgs e)
    {
        var installing = MessageTitle.Text.Contains("安装");
        ReleaseReadingView();
        SetMessage(installing ? "安装已取消" : "已取消", "", false);
        Status.Text = "已取消";
    }
    private async void PrecisePreview(object sender, RoutedEventArgs e) { if (current != null) await DisplayAsync(current, true); }
    private void PreviousImage(object sender, RoutedEventArgs e) { if (frameIndex > 0) { frameIndex--; DisplayImage(); } }
    private void NextImage(object sender, RoutedEventArgs e) { if (frameIndex + 1 < frames.Count) { frameIndex++; DisplayImage(); } }
    private void SetZoom(double value)
    {
        zoom = Math.Clamp(value, 0.25, 3); ZoomLabel.Text = $"{zoom:P0}";
        if(nativePdf!=null){_ = ZoomPdfAsync(zoom);}
        else if (Viewer.Content == browser?.View && browser != null) browser.View.ZoomFactor = zoom;
        else if (textBox != null) textBox.FontSize = 17 * zoom;
        else if (Viewer.Content is FlowDocumentScrollViewer flow) flow.Zoom = zoom * 100;
        else DisplayImage();
    }
    private void ZoomIn(object sender, RoutedEventArgs e) => SetZoom(zoom + .1);
    private async Task ZoomPdfAsync(double value)
    {
        var reader = nativePdf; var generation = displayGeneration;
        try { if(reader!=null)await reader.SetZoomAsync(value); }
        catch(Exception error) { if (generation == displayGeneration && ReferenceEquals(nativePdf, reader)) Status.Text=error.Message; }
    }
    private void ZoomOut(object sender, RoutedEventArgs e) => SetZoom(zoom - .1);
    private async void FitView(object sender, RoutedEventArgs e)
    {
        SetZoom(1);
        if(Viewer.Content==browser?.View && info?.Kind==DocumentKind.Word && browser!=null)
        {
            var reader = browser; var generation = displayGeneration;
            try
            {
                var widthText=await reader.EvaluateAsync("document.querySelector('section.docx')?.getBoundingClientRect().width || 0");
                if(generation == displayGeneration && ReferenceEquals(browser, reader) && double.TryParse(widthText,System.Globalization.CultureInfo.InvariantCulture,out var width)&&width>0)SetZoom(Math.Min(1,Math.Max(200,Viewer.ActualWidth-56)/width));
            }
            catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException or OperationCanceledException or System.Runtime.InteropServices.COMException) { }
        }
    }
    private void ToggleSearch(object sender, RoutedEventArgs e)
    {
        var show = SearchPanel.Visibility != Visibility.Visible;
        SearchPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) { SearchInput.Focus(); SearchInput.SelectAll(); }
        else { browser?.StopFind(); findQuery = ""; SearchCount.Text = ""; }
    }
    private void SearchKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Find(Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true; } }
    private void SearchTextChanged(object sender, TextChangedEventArgs e) { findQuery = ""; SearchCount.Text = ""; }
    private void FindNext(object sender, RoutedEventArgs e) => Find(false);
    private void FindPrevious(object sender, RoutedEventArgs e) => Find(true);
    private void ShowFind(int index, int total) => SearchCount.Text = total == 0 ? "没有找到" : index == 0 ? $"共 {total} 处" : $"{index} / {total}";
    private void Find(bool backward)
    {
        var query = SearchInput.Text; if (query.Length == 0) return;
        var fresh = query != findQuery; findQuery = query;
        if (textBox != null)
        {
            var text = textBox.Text; var matches = new List<int>();
            for (var i = text.IndexOf(query, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(query, i + query.Length, StringComparison.OrdinalIgnoreCase)) matches.Add(i);
            if (matches.Count == 0) { ShowFind(0, 0); return; }
            findIndex = fresh ? Math.Max(0, matches.FindIndex(m => m >= textBox.SelectionStart)) : ((findIndex + (backward ? -1 : 1)) % matches.Count + matches.Count) % matches.Count;
            // 文本框获得过一次焦点后，才会在焦点回到搜索框时继续显示选中的高亮
            textBox.Focus(); textBox.Select(matches[findIndex], query.Length); SearchInput.Focus(); textBox.ScrollToLine(textBox.GetLineIndexFromCharacterIndex(matches[findIndex])); ShowFind(findIndex + 1, matches.Count);
        }
        else if (Viewer.Content is FlowDocumentScrollViewer rtf)
        {
            var matches = new List<TextRange>();
            for (var pointer = rtf.Document.ContentStart; pointer != null; pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
            {
                if (pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
                var run = pointer.GetTextInRun(LogicalDirection.Forward);
                for (var i = run.IndexOf(query, StringComparison.OrdinalIgnoreCase); i >= 0; i = run.IndexOf(query, i + query.Length, StringComparison.OrdinalIgnoreCase))
                    matches.Add(new(pointer.GetPositionAtOffset(i)!, pointer.GetPositionAtOffset(i + query.Length)!));
            }
            if (matches.Count == 0) { ShowFind(0, 0); return; }
            findIndex = fresh ? 0 : ((findIndex + (backward ? -1 : 1)) % matches.Count + matches.Count) % matches.Count;
            rtf.Focus(); rtf.Selection?.Select(matches[findIndex].Start, matches[findIndex].End); SearchInput.Focus(); matches[findIndex].Start.Paragraph?.BringIntoView(); ShowFind(findIndex + 1, matches.Count);
        }
        else browser?.Find(query, backward);
    }
    private async void Edit(object sender, RoutedEventArgs e)=>await ToggleEditAsync();
    private async Task ToggleEditAsync()
    {
        if (current == null) return;
        var tab = current;
        if (tab.Editing && tab.Dirty) { Status.Text = "先另存为或关闭文件后再返回阅读"; return; }
        tab.Editing = !tab.Editing;
        await DisplayAsync(tab);
        if (ReferenceEquals(current, tab) && Viewer.Visibility == Visibility.Visible)
            Status.Text = tab.Editing ? "编辑中" : "阅读中";
    }
    private async void SaveAs(object sender, RoutedEventArgs e)
    {
        if (current?.Text == null || textBox == null) return;
        var dialog = new SaveFileDialog { FileName = current.Name, DefaultExt = System.IO.Path.GetExtension(current.Path), Filter = "原格式|*" + System.IO.Path.GetExtension(current.Path), OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await SaveCurrentAsAsync(dialog.FileName,File.Exists(dialog.FileName));
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "保存未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private Task SaveCurrentAsAsync(string destination,bool overwrite) =>
        SaveCurrentAsAsync(destination, (document, snapshot) => document.SaveAsAsync(destination, snapshot, overwrite));
    private async Task SaveCurrentAsAsync(string destination,Func<TextDocument,string,Task> persist)
    {
        var tab=current??throw new InvalidOperationException("没有打开文本");
        var editor=textBox??throw new InvalidOperationException("当前文档不在编辑模式");
        var text = tab.Text ?? throw new InvalidOperationException("当前文档不在编辑模式");
        var saved = editor.Text;
        var generation = displayGeneration;
        if(!System.IO.Path.GetExtension(destination).Equals(System.IO.Path.GetExtension(tab.Path),StringComparison.OrdinalIgnoreCase))throw new IOException("请保留原文件格式");
        string LatestText() => ReferenceEquals(current, tab) && tab.Editing && textBox != null
            ? textBox.Text : tab.EditBuffer ?? editor.Text;
        await persist(text, saved);
        var sourceUpdated = System.IO.Path.GetFullPath(destination).Equals(tab.Path, StringComparison.OrdinalIgnoreCase);
        // 覆盖原路径后刷新已保存基线；等待读取时仍可能继续输入，所以随后重新取最新文本。
        var baseline = sourceUpdated ? await TextDocument.ReadAsync(destination) : text;
        var latest = LatestText();
        var sameView = generation == displayGeneration && ReferenceEquals(current, tab) && ReferenceEquals(textBox, editor);
        if (latest != saved)
        {
            // 视图重建后使用新编辑器或离开时记下的缓冲，不能用旧编辑器清掉后来的输入。
            tab.Text = baseline; tab.Dirty = true; tab.EditBuffer = latest;
            if (ReferenceEquals(current, tab) && tab.Editing) Status.Text = "已保存 · 仍有新的修改";
            return;
        }
        if (!sameView)
        {
            tab.Dirty = false; tab.EditBuffer = null;
            // 当前标签已经重建编辑器时继续保留编辑模式和基线；完成旧保存不切换视图。
            if (ReferenceEquals(current, tab) && tab.Editing && textBox != null)
            { tab.Text = baseline; Status.Text = "已保存"; }
            else { tab.Editing = false; tab.Text = null; }
            return;
        }
        tab.Dirty=false;tab.EditBuffer=null;tab.Editing=false;tab.Text=null;
        await AddFileAsync(destination);
        if (current?.Path.Equals(System.IO.Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase) == true) Status.Text="已保存 · "+System.IO.Path.GetFileName(destination);
    }
    private async void Print(object sender, RoutedEventArgs e)
    {
        if (Viewer.Content == browser?.View && browser != null) { browser.View.CoreWebView2.ShowPrintUI(); return; }
        var dialog = new PrintDialog(); if (dialog.ShowDialog() != true) return;
        if(nativePdf!=null)dialog.PrintDocument(nativePdf.Paginator(new(dialog.PrintableAreaWidth,dialog.PrintableAreaHeight)),current?.Name??"PDF");
        else if (textBox != null)
        { var flow = new FlowDocument(new Paragraph(new Run(textBox.Text))) { PagePadding = new(40), PageWidth = dialog.PrintableAreaWidth, FontSize = 12 }; dialog.PrintDocument(((IDocumentPaginatorSource)flow).DocumentPaginator, current?.Name ?? "文档"); }
        else if (Viewer.Content is FlowDocumentScrollViewer rtf) dialog.PrintDocument(((IDocumentPaginatorSource)rtf.Document).DocumentPaginator, current?.Name ?? "文档");
        else if (imageScroll?.Content is Image image) dialog.PrintVisual(image, current?.Name ?? "图片");
        await Task.CompletedTask;
    }
    private void ShowRecent(object sender, RoutedEventArgs e) => ShowHome();
    // 离开正在看的文档，回到首页或组件页；点标签可以回到文档
    private void LeaveDocument()
    {
        ReleaseReadingView(); HidePanels();
    }
    private void ShowHome()
    {
        LeaveDocument(); Home.Visibility = Visibility.Visible;
        var recent = settings.Recent.Select(file => new RecentItem(file)).ToArray();
        RecentList.ItemsSource = recent; RecentHeader.Visibility = recent.Length > 0 ? Visibility.Visible : Visibility.Collapsed; Status.Text = "就绪";
    }
    private async void OpenRecent(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is RecentItem item) await AddFileAsync(item.Path); }
    private void ClearRecent(object sender, RoutedEventArgs e) { settings.Recent.Clear(); settings.ScrollPositions.Clear(); settings.Save(); ShowHome(); }
    private void ShowComponents(object sender, RoutedEventArgs e)
    {
        LeaveDocument(); ComponentPanel.Visibility = Visibility.Visible; ComponentList.Children.Clear();
        foreach (var component in components.Definitions)
        {
            var row = new Border { CornerRadius = new(10), BorderThickness = new(1), Padding = new(20,16,16,16), Margin = new(0,0,0,12) };
            row.SetResourceReference(Border.BackgroundProperty, "Surface"); row.SetResourceReference(Border.BorderBrushProperty, "Border");
            var installed = components.GetInstalled(component.Id) != null;
            var panel = new DockPanel(); var button = new Button { Content = installed ? "移除" : "安装" };
            if (!installed) button.SetResourceReference(StyleProperty, "PrimaryButton");
            button.Click += async (_, _) =>
            {
                if (ComponentPanel.Visibility != Visibility.Visible) return;
                operation?.Cancel(); operation?.Dispose(); operation = new();
                var token = operation.Token; var generation = ++displayGeneration;
                try
                {
                    if (components.GetInstalled(component.Id) != null) components.Remove(component.Id);
                    else
                    {
                        SetMessage("正在安装组件", component.Name, true);
                        await components.InstallAsync(component.Id, InstallProgress(), token);
                    }
                    if (generation == displayGeneration && !token.IsCancellationRequested) ShowComponents(sender, e);
                }
                catch (OperationCanceledException) { if (generation == displayGeneration) { ShowComponents(sender,e); Status.Text = "安装已取消"; } }
                catch (Exception error) { if (generation == displayGeneration && !token.IsCancellationRequested) SetMessage("组件操作未完成", error.Message, false); }
            };
            DockPanel.SetDock(button, Dock.Right); panel.Children.Add(button);
            var description = new StackPanel(); description.Children.Add(new TextBlock { Text = component.Name, FontSize = 16, FontWeight = FontWeights.SemiBold });
            var installedSize = components.InstalledBytes(component.Id);
            var detail = new TextBlock { Text = installedSize > 0 ? $"已安装 · {Size(installedSize)} · {component.Version}" : $"下载 {Size(component.DownloadBytes)} · {component.Version}", Margin = new(0,7,0,0) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted"); description.Children.Add(detail);
            panel.Children.Add(description); row.Child = panel; ComponentList.Children.Add(row);
        }
        Status.Text = "格式组件";
    }
    private void ClearCache(object sender, RoutedEventArgs e)
    {
        ShowHome();
        var cache = System.IO.Path.Combine(App.DataRoot, "cache");
        try { if (Directory.Exists(cache)) Directory.Delete(cache, true); Status.Text = "预览缓存已清理"; }
        catch (IOException error) { Status.Text = error.Message; }
    }
    private bool ConfirmClose(DocumentTab tab) => !tab.Dirty || MessageBox.Show(this, "有未保存的修改。关闭这份文件？", tab.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private async void CloseTab(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DocumentTab tab || !ConfirmClose(tab)) return;
        var wasCurrent = tab == current; suppressTabChange = true; documents.Remove(tab); suppressTabChange = false;
        if (!wasCurrent) return;
        if (documents.Count > 0) { suppressTabChange = true; Tabs.SelectedItem = documents[^1]; suppressTabChange = false; await DisplayAsync(documents[^1]); }
        else ShowHome();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        RememberBuffer(); if (documents.Any(t => !ConfirmClose(t))) { e.Cancel = true; return; }
        if (WindowState == WindowState.Normal) { settings.WindowWidth = Width; settings.WindowHeight = Height; }
        settings.Maximized = WindowState == WindowState.Maximized;
        ReleaseReadingView(false); App.ThemeChanged -= OnThemeChanged; settings.Save();
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var viewing = current != null && Toolbar.Visibility == Visibility.Visible;
        var searching = SearchPanel.Visibility == Visibility.Visible;
        if (Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            if (e.Key == Key.F3 && searching) { Find(Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Shift) return;
            if (e.Key == Key.Escape && searching) { ToggleSearch(sender, e); e.Handled = true; }
            if (e.Key is Key.PageDown or Key.PageUp && viewing && nativePdf != null && Keyboard.FocusedElement is not TextBox) { _ = nativePdf.StepAsync(e.Key == Key.PageDown ? 1 : -1); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.O) { OpenDialog(sender,e); e.Handled=true; }
        if (e.Key == Key.F && SearchButton.IsEnabled && viewing) { if (searching) { SearchInput.Focus(); SearchInput.SelectAll(); } else ToggleSearch(sender,e); e.Handled=true; }
        if (e.Key == Key.S && SaveButton.Visibility == Visibility.Visible && viewing) { SaveAs(sender,e); e.Handled=true; }
        if (e.Key == Key.P && PrintButton.IsEnabled && viewing) { Print(sender,e); e.Handled=true; }
        if (e.Key == Key.W && current != null) { var dummy = new Button { DataContext = current }; CloseTab(dummy,e); e.Handled=true; }
        if (e.Key is Key.OemPlus or Key.Add && viewing) { SetZoom(zoom + .1); e.Handled=true; }
        if (e.Key is Key.OemMinus or Key.Subtract && viewing) { SetZoom(zoom - .1); e.Handled=true; }
        if (e.Key is Key.D0 or Key.NumPad0 && viewing) { SetZoom(1); e.Handled=true; }
        if (e.Key == Key.Tab && documents.Count > 1) { Tabs.SelectedIndex = (Tabs.SelectedIndex + 1) % documents.Count; e.Handled=true; }
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void MaximizeWindow(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OnStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        // 最大化时窗口边缘会伸出屏幕一圈，这里把内容收回来
        var edge = SystemParameters.WindowResizeBorderThickness;
        Root.Padding = maximized ? new(edge.Left + 4, edge.Top + 4, edge.Right + 4, edge.Bottom + 4) : new(0);
        MaximizeButton.Tag = maximized ? "" : ""; MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
    }
    private void CycleTheme(object sender, RoutedEventArgs e)
    {
        settings.Theme = settings.Theme switch { "system" => "light", "light" => "dark", _ => "system" };
        settings.Save(); App.ApplyTheme(settings.Theme); UpdateThemeButton();
    }
    private void UpdateThemeButton() => (ThemeButton.Tag, ThemeButton.ToolTip) = settings.Theme switch
    {
        "light" => ("", "外观：浅色（点击切换）"),
        "dark" => ("", "外观：深色（点击切换）"),
        _ => ("", "外观：跟随系统（点击切换）")
    };
    private void OnThemeChanged() { browser?.SetTheme(App.IsDark); ApplyWindowTheme(); }
    private void ApplyWindowTheme()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        var dark = App.IsDark ? 1 : 0; _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Windows 的深浅色设置变化时会广播这条消息
        if (message == 0x001A && settings.Theme == "system" && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet") App.ApplyTheme("system");
        return IntPtr.Zero;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private static string Size(long bytes) => bytes < 1024*1024 ? $"{bytes/1024d:F0} KiB" : $"{bytes/(1024d*1024):F1} MiB";
}
