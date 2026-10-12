using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    public TextDocument? Text { get; set; }
    public string? EditBuffer { get; set; }
    public bool Editing { get; set; }
    public bool Dirty { get; set; }
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
    private readonly Button pdfMode=new(){Content="文字与搜索",Visibility=Visibility.Collapsed};
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
    public MainWindow(string[] paths, string? verificationRoot = null, string? benchmarkFile = null,string? stateFile=null)
    {
        InitializeComponent(); Tabs.ItemsSource = documents;
        ((StackPanel)((DockPanel)Toolbar.Child).Children[0]).Children.Add(pdfMode);pdfMode.Click+=PdfMode;
        Loaded += async (_, _) => { if (benchmarkFile != null) await RunBenchmarkAsync(benchmarkFile); else if (verificationRoot != null) await RunVerificationAsync(verificationRoot); else {foreach (var path in paths) await AddFileAsync(path);if(stateFile!=null)await File.WriteAllTextAsync(stateFile,System.Text.Json.JsonSerializer.Serialize(new{shown=Viewer.Visibility==Visibility.Visible,status=Status.Text,renderer=nativePdf!=null?"Windows.Data.Pdf":browser!=null?"WebView2":"WPF"}));} };
    }
    private async void OpenDialog(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "打开文档", Multiselect = true, Filter = "文档|*.doc;*.docx;*.docm;*.dot;*.dotx;*.dotm;*.wps;*.wpt;*.xls;*.xlsx;*.xlsm;*.xlt;*.xltx;*.xltm;*.et;*.ett;*.csv;*.dbf;*.prn;*.dif;*.ppt;*.pptx;*.pptm;*.pot;*.potx;*.potm;*.pps;*.ppsx;*.ppsm;*.dps;*.dpt;*.pdf;*.txt;*.rtf;*.htm;*.html;*.mht;*.mhtml;*.xml;*.jpg;*.png;*.tif;*.bmp;*.xla;*.xlam;*.ts|所有文件|*.*" };
        if (dialog.ShowDialog(this) == true) foreach (var path in dialog.FileNames) await AddFileAsync(path);
    }
    internal async Task AddFileAsync(string path)
    {
        try { path = System.IO.Path.GetFullPath(path); if (!File.Exists(path)) throw new FileNotFoundException("文件不存在"); }
        catch (Exception e) { SetMessage("无法打开文件", e.Message, false); return; }
        var tab = documents.FirstOrDefault(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (tab == null) { tab = new(path); documents.Add(tab); }
        suppressTabChange = true; Tabs.SelectedItem = tab; suppressTabChange = false;
        await DisplayAsync(tab);
    }
    private void RememberBuffer()
    {
        if (current != null && textBox != null)
        { if (current.Editing) current.EditBuffer = textBox.Text; settings.ScrollPositions[current.Path] = textBox.VerticalOffset; }
    }
    private async Task DisplayAsync(DocumentTab tab, bool precise = false)
    {
        RememberBuffer(); operation?.Cancel(); operation?.Dispose(); operation = new();
        var cancellation = operation.Token;
        var generation = ++displayGeneration;
        current = tab; info = null; textBox = null; frames.Clear(); imageScroll = null; zoom = 1; ZoomLabel.Text = "100%";
        nativePdf?.Dispose();nativePdf=null;pdfSource=null;pdfMode.Visibility=Visibility.Collapsed;
        HidePanels(); Toolbar.Visibility = Visibility.Visible; Viewer.Content = null;
        EditButton.Visibility = SaveButton.Visibility = PreciseButton.Visibility = ImagePages.Visibility = Visibility.Collapsed;
        SearchButton.IsEnabled = true; PrintButton.IsEnabled = true;
        FormatLabel.Text = System.IO.Path.GetExtension(tab.Path).TrimStart('.').ToUpperInvariant();
        Title = tab.Name + " · 轻阅";
        var timer = Stopwatch.StartNew();
        try
        {
            SetMessage("正在打开", tab.Name, true);
            var inspected = await Task.Run(() => FileInspector.Inspect(tab.Path), cancellation);
            cancellation.ThrowIfCancellationRequested();
            info = inspected;
            if (info.Kind == DocumentKind.Undefined) { SetMessage("查看范围待核实", info.Problem ?? "", false); return; }
            if(!precise && info.Kind is DocumentKind.Text or DocumentKind.Rtf or DocumentKind.Image){browser?.Dispose();browser=null;}
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
                            await components.InstallAsync(component, new Progress<(long Done, long Total)>(p => MessageDetail.Text = $"{Size(p.Done)} / {Size(p.Total)}"), cancellation);
                            if (current == tab && !cancellation.IsCancellationRequested) await DisplayAsync(tab, precise);
                        }
                        catch (OperationCanceledException) { if (current == tab) SetMessage("安装已取消", definition.Name, false); }
                        catch (Exception error) { if (current == tab) { SetMessage("组件安装未完成", error.Message, false); Status.Text = "可以重新安装"; } }
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
                cancellation.ThrowIfCancellationRequested(); HidePanels(); Viewer.Visibility = Visibility.Visible;
                await ShowPdfAsync(output,cancellation);
                SearchButton.IsEnabled = false;
            }
            else if (info.Kind == DocumentKind.Text || (info.Extension == "csv" && tab.Editing))
            {
                    if (tab.Text == null || !tab.Dirty) tab.Text = await TextDocument.ReadAsync(tab.Path, cancellation); cancellation.ThrowIfCancellationRequested();
                    textBox = new TextBox { Text = tab.EditBuffer ?? tab.Text.Text, IsReadOnly = !tab.Editing, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new(0), Padding = new(44,32,44,32), FontSize = 17, FontFamily = new("Cascadia Mono, Microsoft YaHei UI"), Background = Brushes.White };
                    textBox.TextChanged += (_, _) => { if (current == tab && tab.Editing) { tab.Dirty = textBox.Text != tab.Text.Text; Status.Text = tab.Dirty ? "有未保存的修改" : "编辑中"; } };
                    Viewer.Content = textBox;
                    if (settings.ScrollPositions.TryGetValue(tab.Path, out var offset)) textBox.ScrollToVerticalOffset(offset);
                    EditButton.Visibility = Visibility.Visible; EditButton.Content = tab.Editing ? "返回阅读" : "编辑"; SaveButton.Visibility = tab.Editing ? Visibility.Visible : Visibility.Collapsed;
            }
            else switch (info.Kind)
            {
                case DocumentKind.Rtf:
                    var flow = new FlowDocument { PagePadding = new(40), FontFamily = new("Microsoft YaHei UI") };
                    using (var source = File.OpenRead(tab.Path)) new TextRange(flow.ContentStart, flow.ContentEnd).Load(source, DataFormats.Rtf);
                    Viewer.Content = new FlowDocumentScrollViewer { Document = flow, IsToolBarVisible = false, Background = Brushes.White }; break;
                case DocumentKind.Image:
                    var decoded = await Task.Run(() => DecodeImages(tab.Path), cancellation); cancellation.ThrowIfCancellationRequested();frames=decoded;
                    imageScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(241,245,249)) };
                    Viewer.Content = imageScroll; frameIndex = 0; DisplayImage(); SearchButton.IsEnabled = false;
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
            cancellation.ThrowIfCancellationRequested();
            settings.Add(tab.Path); Status.Text = $"已打开 · {timer.Elapsed.TotalSeconds:F2} 秒";
        }
        catch (OperationCanceledException) { if (generation == displayGeneration) { SetMessage("已取消", tab.Name, false); Status.Text = "已取消"; } }
        catch (Exception error) { if (generation == displayGeneration && !cancellation.IsCancellationRequested) { SetMessage("无法显示文档", error.Message, false); Status.Text = "打开失败"; } }
    }
    private async Task ShowBrowserAsync(DocumentInfo documentInfo, string path, CancellationToken token)
    {
        if (browser == null) { browser = new(components); browser.StatusChanged += BrowserStatus; }
        Viewer.Content = browser.View; browser.View.ZoomFactor=1; await browser.ShowAsync(documentInfo, path, token);
    }
    private async Task ShowPdfAsync(string path,CancellationToken token)
    {
        browser?.Dispose();browser=null;nativePdf?.Dispose();
        var reader=new NativePdfReader();nativePdf=reader;pdfSource=path;Viewer.Content=reader;
        reader.StatusChanged+=BrowserStatus;pdfMode.Visibility=Visibility.Visible;pdfMode.Content="文字与搜索";
        try{await reader.LoadAsync(path,token);}
        catch(Exception error)when(error is System.Runtime.InteropServices.COMException or IOException)
        {
            token.ThrowIfCancellationRequested();reader.Dispose();nativePdf=null;
            await ShowBrowserAsync(info! with{Kind=DocumentKind.Pdf},path,token);pdfMode.Content="页面阅读";
        }
    }
    private async void PdfMode(object sender,RoutedEventArgs e)=>await SwitchPdfModeAsync();
    private async Task SwitchPdfModeAsync()
    {
        if(pdfSource==null||info==null)return;
        try
        {
            if(nativePdf!=null){nativePdf.Dispose();nativePdf=null;await ShowBrowserAsync(info with{Kind=DocumentKind.Pdf},pdfSource,operation?.Token??CancellationToken.None);pdfMode.Content="页面阅读";}
            else await ShowPdfAsync(pdfSource,operation?.Token??CancellationToken.None);
        }
        catch(OperationCanceledException){}
        catch(Exception error){Status.Text=error.Message;}
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
    private void DisplayImage()
    {
        if (imageScroll == null || frames.Count == 0) return;
        var frame = frames[frameIndex];
        var available = Math.Max(200, Viewer.ActualWidth - 48); var scale = Math.Min(1,Math.Min(available/frame.PixelWidth,Math.Max(200,Viewer.ActualHeight-48)/frame.PixelHeight));
        imageScroll.Content = new Image { Source = frame, Width = frame.PixelWidth * scale * zoom, Height = frame.PixelHeight * scale * zoom, Margin = new(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        ImagePageLabel.Text = $"{frameIndex + 1} / {frames.Count}";
    }
    private void HidePanels()
    { Welcome.Visibility = RecentPanel.Visibility = ComponentPanel.Visibility = MessagePanel.Visibility = Viewer.Visibility = SearchPanel.Visibility = Visibility.Collapsed; ActionButton.Visibility = Visibility.Collapsed; action = null; }
    private void SetMessage(string title, string detail, bool busy)
    { HidePanels(); MessagePanel.Visibility = Visibility.Visible; MessageTitle.Text = title; MessageDetail.Text = detail; Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; }
    private async void TabChanged(object sender, SelectionChangedEventArgs e) { if (!suppressTabChange && Tabs.SelectedItem is DocumentTab tab) await DisplayAsync(tab); }
    private async void OnDrop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) foreach (var path in paths) await AddFileAsync(path); }
    private void OnDragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void RunAction(object sender, RoutedEventArgs e) { var next = action; if (next != null) await next(); }
    private void CancelWork(object sender, RoutedEventArgs e) { operation?.Cancel(); browser?.Dispose(); browser = null; if(MessageTitle.Text.Contains("安装")){MessageTitle.Text="正在取消安装";MessageDetail.Text="正在清理组件";} }
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
    private async Task ZoomPdfAsync(double value){try{if(nativePdf!=null)await nativePdf.SetZoomAsync(value);}catch(Exception error){Status.Text=error.Message;}}
    private void ZoomOut(object sender, RoutedEventArgs e) => SetZoom(zoom - .1);
    private async void FitView(object sender, RoutedEventArgs e)
    {
        SetZoom(1);
        if(Viewer.Content==browser?.View && info?.Kind==DocumentKind.Word && browser!=null)
        {
            var widthText=await browser.EvaluateAsync("document.querySelector('section.docx')?.getBoundingClientRect().width || 0");
            if(double.TryParse(widthText,System.Globalization.CultureInfo.InvariantCulture,out var width)&&width>0)SetZoom(Math.Min(1,Math.Max(200,Viewer.ActualWidth-56)/width));
        }
    }
    private void ToggleSearch(object sender, RoutedEventArgs e) { SearchPanel.Visibility = SearchPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; if (SearchPanel.Visibility == Visibility.Visible) SearchInput.Focus(); }
    private void SearchKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { FindNext(sender, e); e.Handled = true; } }
    private void FindNext(object sender, RoutedEventArgs e)
    {
        var query = SearchInput.Text; if (query.Length == 0) return;
        if (textBox != null)
        {
            var start = textBox.SelectionStart + textBox.SelectionLength;
            var index = textBox.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) index = textBox.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index < 0) Status.Text = "没有找到";
            else { textBox.Focus(); textBox.Select(index, query.Length); textBox.ScrollToLine(textBox.GetLineIndexFromCharacterIndex(index)); }
        }
        else if (Viewer.Content is FlowDocumentScrollViewer rtf)
        { var text = new TextRange(rtf.Document.ContentStart, rtf.Document.ContentEnd).Text; Status.Text = text.Contains(query, StringComparison.OrdinalIgnoreCase) ? "文档包含这段文字" : "没有找到"; }
        else browser?.Find(query);
    }
    private async void Edit(object sender, RoutedEventArgs e)=>await ToggleEditAsync();
    private async Task ToggleEditAsync()
    {
        if (current == null) return;
        if (info?.Extension == "csv" && !current.Editing)
        {
            current.Text = await TextDocument.ReadAsync(current.Path); current.EditBuffer = current.Text.Text; current.Editing = true;
            textBox = new TextBox { Text = current.EditBuffer, AcceptsReturn = true, AcceptsTab = true, FontSize = 16, FontFamily = new("Cascadia Mono, Microsoft YaHei UI"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(30) };
            var editingTab = current;
            textBox.TextChanged += (_, _) => { editingTab.Dirty = textBox.Text != editingTab.Text!.Text; Status.Text = "有未保存的修改"; };
            Viewer.Content = textBox; EditButton.Content = "返回阅读"; SaveButton.Visibility = Visibility.Visible; Status.Text = "CSV 文本编辑"; return;
        }
        current.Editing = !current.Editing;
        if (!current.Editing && current.Dirty)
        { current.Editing = true; Status.Text = "先另存为或关闭文件后再返回阅读"; return; }
        if (info?.Extension == "csv") { await DisplayAsync(current); return; }
        if (textBox != null) textBox.IsReadOnly = !current.Editing;
        EditButton.Content = current.Editing ? "返回阅读" : "编辑"; SaveButton.Visibility = current.Editing ? Visibility.Visible : Visibility.Collapsed; Status.Text = current.Editing ? "编辑中" : "阅读中";
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
    private async Task SaveCurrentAsAsync(string destination,bool overwrite)
    {
        var tab=current??throw new InvalidOperationException("没有打开文本");
        var editor=textBox??throw new InvalidOperationException("当前文档不在编辑模式");
        if(!System.IO.Path.GetExtension(destination).Equals(System.IO.Path.GetExtension(tab.Path),StringComparison.OrdinalIgnoreCase))throw new IOException("请保留原文件格式");
        await tab.Text!.SaveAsAsync(destination,editor.Text,overwrite);
        tab.Dirty=false;tab.EditBuffer=null;tab.Editing=false;tab.Text=null;
        await AddFileAsync(destination);Status.Text="已保存 · "+System.IO.Path.GetFileName(destination);
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
    private void ShowRecent(object sender, RoutedEventArgs e) { RememberBuffer(); displayGeneration++; operation?.Cancel(); HidePanels(); RecentPanel.Visibility = Visibility.Visible; Toolbar.Visibility = Visibility.Collapsed; RecentList.ItemsSource = settings.Recent.ToArray(); Status.Text = "最近文件"; }
    private async void OpenRecent(object sender, MouseButtonEventArgs e) { if (RecentList.SelectedItem is RecentFile item) await AddFileAsync(item.Path); }
    private void ClearRecent(object sender, RoutedEventArgs e) { settings.Recent.Clear(); settings.ScrollPositions.Clear(); settings.Save(); RecentList.ItemsSource = settings.Recent.ToArray(); }
    private void ShowComponents(object sender, RoutedEventArgs e)
    {
        RememberBuffer(); displayGeneration++; operation?.Cancel(); HidePanels(); ComponentPanel.Visibility = Visibility.Visible; Toolbar.Visibility = Visibility.Collapsed; ComponentList.Children.Clear();
        foreach (var component in components.Definitions)
        {
            var row = new Border { Background = Brushes.White, CornerRadius = new(10), Padding = new(20), Margin = new(0,0,0,14) };
            var panel = new DockPanel(); var button = new Button { Content = components.GetInstalled(component.Id) != null ? "移除" : "安装", Tag = component };
            button.Click += async (_, _) =>
            {
                try
                {
                    browser?.Dispose(); browser = null;
                    if (components.GetInstalled(component.Id) != null) components.Remove(component.Id);
                    else
                    {
                        operation?.Dispose(); operation = new(); SetMessage("正在安装组件", component.Name, true);
                        await components.InstallAsync(component.Id, new Progress<(long Done, long Total)>(p => MessageDetail.Text = $"{Size(p.Done)} / {Size(p.Total)}"), operation.Token);
                    }
                    ShowComponents(sender, e);
                }
                catch (OperationCanceledException) { ShowComponents(sender,e); Status.Text = "安装已取消"; }
                catch (Exception error) { SetMessage("组件操作未完成", error.Message, false); }
            };
            DockPanel.SetDock(button, Dock.Right); panel.Children.Add(button);
            var description = new StackPanel(); description.Children.Add(new TextBlock { Text = component.Name, FontSize = 18, FontWeight = FontWeights.SemiBold });
            var installedSize = components.InstalledBytes(component.Id);
            description.Children.Add(new TextBlock { Text = installedSize > 0 ? $"已安装 · {Size(installedSize)} · {component.Version}" : $"下载 {Size(component.DownloadBytes)} · {component.Version}", Margin = new(0,9,0,0) });
            panel.Children.Add(description); row.Child = panel; ComponentList.Children.Add(row);
        }
        Status.Text = "组件装好后可离线使用";
    }
    private void ClearCache(object sender, RoutedEventArgs e)
    {
        operation?.Cancel(); browser?.Dispose(); browser = null; Viewer.Content = null;
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
        operation?.Cancel(); current = null;
        if (documents.Count > 0) { suppressTabChange = true; Tabs.SelectedItem = documents[^1]; suppressTabChange = false; await DisplayAsync(documents[^1]); }
        else { browser?.Dispose(); browser = null;nativePdf?.Dispose();nativePdf=null; Viewer.Content = null; textBox = null; frames.Clear(); HidePanels(); Welcome.Visibility = Visibility.Visible; Toolbar.Visibility = Visibility.Collapsed; Status.Text = "就绪"; FormatLabel.Text = ""; Title = "轻阅"; }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        RememberBuffer(); if (documents.Any(t => !ConfirmClose(t))) { e.Cancel = true; return; }
        operation?.Cancel(); browser?.Dispose();nativePdf?.Dispose(); settings.Save();
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.O) { OpenDialog(sender,e); e.Handled=true; }
        if (e.Key == Key.F && SearchButton.IsEnabled && current != null) { ToggleSearch(sender,e); e.Handled=true; }
        if (e.Key == Key.S && SaveButton.Visibility == Visibility.Visible) { SaveAs(sender,e); e.Handled=true; }
        if (e.Key == Key.W && current != null) { var dummy = new Button { DataContext = current }; CloseTab(dummy,e); e.Handled=true; }
    }
    private static string Size(long bytes) => bytes < 1024*1024 ? $"{bytes/1024d:F0} KiB" : $"{bytes/(1024d*1024):F1} MiB";
}
