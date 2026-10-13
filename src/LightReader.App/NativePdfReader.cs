using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace LightReader.App;
public sealed class NativePdfReader : Grid,IDisposable
{
    private PdfDocument? document;
    private IRandomAccessStream? source;
    private readonly Image image=new();
    private readonly TextBox pageInput=new(){Width=52,Height=28,Padding=new(4,0,4,0),Text="1",TextAlignment=TextAlignment.Center,Margin=new(6,0,0,0)};
    private readonly TextBlock count=new(){VerticalAlignment=VerticalAlignment.Center,Margin=new(8,0,8,0)};
    private readonly ScrollViewer scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=false};
    private uint pageIndex;
    private uint requestedPage;
    private double zoom=1;
    private long generation;
    private bool disposed;
    private readonly CancellationTokenSource lifetime=new();
    private readonly SemaphoreSlim renderGate=new(1,1);
    private CancellationTokenSource? rendering;
    private Task? presentation;
    // 页面两侧边距、边框和竖向滚动条占用的宽度
    private const double PageInset=66;
    private readonly System.Windows.Threading.DispatcherTimer resize=new(){Interval=TimeSpan.FromMilliseconds(220)};
    public uint PageCount=>document?.PageCount??0;
    public uint CurrentPage=>requestedPage;
    internal uint DisplayedPage=>pageIndex;
    internal bool HasRenderedPage=>image.Source!=null&&!disposed;
    internal bool HasOpenSource=>source!=null;
    public double VerticalOffset=>scroll.VerticalOffset;
    public event Action<string>? StatusChanged;
    // 翻页控件，由主窗口放进工具栏
    public StackPanel Tools{get;}=new(){Orientation=Orientation.Horizontal};
    public NativePdfReader()
    {
        SetResourceReference(BackgroundProperty,"Canvas");
        var previous=new Button{Tag="",ToolTip="上一页 (PageUp)"};previous.SetResourceReference(StyleProperty,"IconButton");previous.Click+=async(_,_)=>await HandleNavigationAsync(()=>StepAsync(-1));
        var next=new Button{Tag="",ToolTip="下一页 (PageDown)"};next.SetResourceReference(StyleProperty,"IconButton");next.Click+=async(_,_)=>await HandleNavigationAsync(()=>StepAsync(1));
        pageInput.ToolTip="输入页码后按回车";
        System.Windows.Automation.AutomationProperties.SetName(previous,"上一页");System.Windows.Automation.AutomationProperties.SetName(next,"下一页");System.Windows.Automation.AutomationProperties.SetName(pageInput,"页码");
        pageInput.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter&&uint.TryParse(pageInput.Text,out var number)&&number>0&&number<=PageCount){e.Handled=true;await HandleNavigationAsync(()=>ShowPageAsync(number-1));}};
        Tools.Children.Add(previous);Tools.Children.Add(pageInput);Tools.Children.Add(count);Tools.Children.Add(next);
        var page=new Border{Child=image,Background=Brushes.White,BorderThickness=new(1),Margin=new(24),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top};
        page.SetResourceReference(Border.BorderBrushProperty,"Border");
        scroll.Content=page;Children.Add(scroll);
        // 窗口宽度变化后稍等一下再按新宽度重画，拖动窗口边缘时不会连续重画
        resize.Tick+=async(_,_)=>{resize.Stop();await HandleNavigationAsync(RefitAsync);};
        SizeChanged+=(_,e)=>{if(e.WidthChanged&&document!=null){resize.Stop();resize.Start();}};
    }
    public async Task LoadAsync(string path,CancellationToken token)
    {
        if(disposed)throw new OperationCanceledException();
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        IRandomAccessStream? opened=null;
        try
        {
            var file=await StorageFile.GetFileFromPathAsync(path).AsTask(cancellation.Token);
            opened=await file.OpenAsync(FileAccessMode.Read).AsTask(cancellation.Token);
            var loaded=await PdfDocument.LoadFromStreamAsync(opened).AsTask(cancellation.Token);
            if(loaded.PageCount==0)throw new InvalidDataException("PDF 没有可显示的页面");
            cancellation.Token.ThrowIfCancellationRequested();
            if(disposed)throw new OperationCanceledException();
            source=opened;opened=null;document=loaded;
            count.Text=$"/ {PageCount} 页";
            await ShowPageAsync(0,cancellation.Token);
            await WaitForPresentationAsync(cancellation.Token);
        }
        catch { Dispose();throw; }
        finally { opened?.Dispose(); }
    }
    public Task ShowPageAsync(uint index,CancellationToken token=default)
    {
        if(document==null||disposed){token.ThrowIfCancellationRequested();return Task.CompletedTask;}
        if(index>=PageCount)throw new ArgumentOutOfRangeException(nameof(index));
        requestedPage=index;
        return presentation=ShowPageCoreAsync(index,token);
    }
    private async Task ShowPageCoreAsync(uint index,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(document==null||disposed)return;
        var serial=++generation;
        rendering?.Cancel();
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        rendering=cancellation;
        var width=(uint)Math.Clamp(Math.Max(800,ActualWidth-PageInset)*1.5*zoom,800,3200);
        try
        {
            var bitmap=await RenderAsync(index,width,cancellation.Token);
            if(serial!=generation||disposed)return;
            image.Source=bitmap;
            var available=Math.Max(300,ActualWidth>0?ActualWidth-PageInset:1000);
            image.Width=available*zoom;image.Height=image.Width*bitmap.PixelHeight/bitmap.PixelWidth;
            if(index!=pageIndex)scroll.ScrollToTop();
            pageIndex=index;pageInput.Text=(index+1).ToString();
            StatusChanged?.Invoke($"第 {index+1} / {PageCount} 页");
        }
        catch(OperationCanceledException)when(!token.IsCancellationRequested){}
        finally { if(ReferenceEquals(rendering,cancellation))rendering=null; }
    }
    public Task StepAsync(int delta)
    {
        var target=requestedPage+delta;
        return target>=0&&target<PageCount?ShowPageAsync((uint)target):Task.CompletedTask;
    }
    private async Task<BitmapSource> RenderAsync(uint index,uint width,CancellationToken token)
    {
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        await renderGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var loaded=document??throw new OperationCanceledException();
            using var page=loaded.GetPage(index);using var output=new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(output,new PdfPageRenderOptions{DestinationWidth=width}).AsTask(cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            output.Seek(0);using var input=output.AsStreamForRead();
            var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);var bitmap=decoder.Frames[0];bitmap.Freeze();return bitmap;
        }
        finally { renderGate.Release(); }
    }
    public Task SetZoomAsync(double value){zoom=value;return RefitAsync();}
    // 重画当前页并保持原来看到的位置
    private async Task RefitAsync()
    {
        var position=requestedPage==pageIndex&&scroll.ExtentHeight>0?scroll.VerticalOffset/scroll.ExtentHeight:0;
        var expected=generation+1;
        await ShowPageAsync(requestedPage);
        if(disposed||expected!=generation)return;
        scroll.UpdateLayout();scroll.ScrollToVerticalOffset(position*scroll.ExtentHeight);
    }
    public async Task RestorePositionAsync(uint page,double offset,CancellationToken token)
    {
        if(disposed||document==null)return;
        var target=Math.Min(page,PageCount-1);
        await ShowPageAsync(target,token);
        await WaitForPresentationAsync(token);
        token.ThrowIfCancellationRequested();
        if(disposed||requestedPage!=target||pageIndex!=target)return;
        scroll.UpdateLayout();scroll.ScrollToVerticalOffset(Math.Max(0,offset));
    }
    private async Task WaitForPresentationAsync(CancellationToken token)
    {
        Task? waited=null;
        while(presentation is { } pending&&!ReferenceEquals(pending,waited))
        { waited=pending;await pending.WaitAsync(token); }
        token.ThrowIfCancellationRequested();
        if(disposed)throw new OperationCanceledException();
        if(image.Source==null)throw new IOException("PDF 页面尚未显示");
    }
    private async Task HandleNavigationAsync(Func<Task> action)
    {
        try { await action(); }
        catch(OperationCanceledException){}
        catch(Exception error)when(error is System.Runtime.InteropServices.COMException or IOException or ObjectDisposedException or ArgumentOutOfRangeException)
        { if(!disposed)StatusChanged?.Invoke(error.Message); }
    }
    public void SavePreview(string path)
    {
        if(image.Source is not BitmapSource bitmap)return;
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap,null,null,null));using var output=File.Create(path);encoder.Save(output);
    }
    public DocumentPaginator Paginator(Size printableArea)=>new PdfPaginator(this,printableArea);
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;resize.Stop();generation++;lifetime.Cancel();rendering?.Cancel();image.Source=null;StatusChanged=null;
        _=ReleaseSourceAsync();
    }
    private async Task ReleaseSourceAsync()
    {
        await renderGate.WaitAsync().ConfigureAwait(false);
        try { document=null;source?.Dispose();source=null; }
        finally { renderGate.Release(); }
    }
    private sealed class PdfPaginator(NativePdfReader reader,Size size):DocumentPaginator
    {
        public override bool IsPageCountValid=>true;
        public override int PageCount=>(int)reader.PageCount;
        public override Size PageSize{get;set;}=size;
        public override IDocumentPaginatorSource Source=>null!;
        public override DocumentPage GetPage(int pageNumber)
        {
            var bitmap=Task.Run(()=>reader.RenderAsync((uint)pageNumber,2400,CancellationToken.None)).GetAwaiter().GetResult();
            var drawing=new DrawingVisual();var width=Math.Min(PageSize.Width,PageSize.Height*bitmap.PixelWidth/bitmap.PixelHeight);var height=width*bitmap.PixelHeight/bitmap.PixelWidth;
            using(var context=drawing.RenderOpen())context.DrawImage(bitmap,new Rect((PageSize.Width-width)/2,0,width,height));
            return new DocumentPage(drawing,PageSize,new Rect(PageSize),new Rect(PageSize));
        }
    }
}
