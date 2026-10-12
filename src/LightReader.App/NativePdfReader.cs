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
    private double zoom=1;
    private long generation;
    private bool disposed;
    // 页面两侧边距、边框和竖向滚动条占用的宽度
    private const double PageInset=66;
    private readonly System.Windows.Threading.DispatcherTimer resize=new(){Interval=TimeSpan.FromMilliseconds(220)};
    public uint PageCount=>document?.PageCount??0;
    public event Action<string>? StatusChanged;
    // 翻页控件，由主窗口放进工具栏
    public StackPanel Tools{get;}=new(){Orientation=Orientation.Horizontal};
    public NativePdfReader()
    {
        SetResourceReference(BackgroundProperty,"Canvas");
        var previous=new Button{Tag="",ToolTip="上一页 (PageUp)"};previous.SetResourceReference(StyleProperty,"IconButton");previous.Click+=async(_,_)=>await StepAsync(-1);
        var next=new Button{Tag="",ToolTip="下一页 (PageDown)"};next.SetResourceReference(StyleProperty,"IconButton");next.Click+=async(_,_)=>await StepAsync(1);
        pageInput.ToolTip="输入页码后按回车";
        System.Windows.Automation.AutomationProperties.SetName(previous,"上一页");System.Windows.Automation.AutomationProperties.SetName(next,"下一页");System.Windows.Automation.AutomationProperties.SetName(pageInput,"页码");
        pageInput.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter&&uint.TryParse(pageInput.Text,out var number)&&number>0&&number<=PageCount){await ShowPageAsync(number-1);e.Handled=true;}};
        Tools.Children.Add(previous);Tools.Children.Add(pageInput);Tools.Children.Add(count);Tools.Children.Add(next);
        var page=new Border{Child=image,Background=Brushes.White,BorderThickness=new(1),Margin=new(24),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top};
        page.SetResourceReference(Border.BorderBrushProperty,"Border");
        scroll.Content=page;Children.Add(scroll);
        // 窗口宽度变化后稍等一下再按新宽度重画，拖动窗口边缘时不会连续重画
        resize.Tick+=async(_,_)=>{resize.Stop();try{await RefitAsync();}catch(Exception error)when(error is System.Runtime.InteropServices.COMException or IOException or OperationCanceledException){StatusChanged?.Invoke(error.Message);}};
        SizeChanged+=(_,e)=>{if(e.WidthChanged&&document!=null){resize.Stop();resize.Start();}};
    }
    public async Task LoadAsync(string path,CancellationToken token)
    {
        var file=await StorageFile.GetFileFromPathAsync(path).AsTask(token);
        source=await file.OpenAsync(FileAccessMode.Read).AsTask(token);
        document=await PdfDocument.LoadFromStreamAsync(source).AsTask(token);
        token.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();
        count.Text=$"/ {PageCount} 页";
        await ShowPageAsync(0,token);
    }
    public async Task ShowPageAsync(uint index,CancellationToken token=default)
    {
        if(document==null||disposed)return;
        var serial=++generation;
        var width=(uint)Math.Clamp(Math.Max(800,ActualWidth-PageInset)*1.5*zoom,800,3200);
        var bitmap=await RenderAsync(index,width,token);
        if(serial!=generation||disposed)return;
        image.Source=bitmap;
        var available=Math.Max(300,ActualWidth>0?ActualWidth-PageInset:1000);
        image.Width=available*zoom;image.Height=image.Width*bitmap.PixelHeight/bitmap.PixelWidth;
        if(index!=pageIndex)scroll.ScrollToTop();
        pageIndex=index;pageInput.Text=(index+1).ToString();
        StatusChanged?.Invoke($"第 {index+1} / {PageCount} 页");
    }
    public Task StepAsync(int delta)
    {
        var target=pageIndex+delta;
        return target>=0&&target<PageCount?ShowPageAsync((uint)target):Task.CompletedTask;
    }
    private async Task<BitmapSource> RenderAsync(uint index,uint width,CancellationToken token)
    {
        using var page=document!.GetPage(index);using var output=new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output,new PdfPageRenderOptions{DestinationWidth=width}).AsTask(token).ConfigureAwait(false);
        output.Seek(0);using var input=output.AsStreamForRead();
        var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);var bitmap=new WriteableBitmap(decoder.Frames[0]);bitmap.Freeze();return bitmap;
    }
    public Task SetZoomAsync(double value){zoom=value;return RefitAsync();}
    // 重画当前页并保持原来看到的位置
    private async Task RefitAsync()
    {
        var position=scroll.ExtentHeight>0?scroll.VerticalOffset/scroll.ExtentHeight:0;
        await ShowPageAsync(pageIndex);
        if(disposed)return;
        scroll.UpdateLayout();scroll.ScrollToVerticalOffset(position*scroll.ExtentHeight);
    }
    public void SavePreview(string path)
    {
        if(image.Source is not BitmapSource bitmap)return;
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
    public DocumentPaginator Paginator(Size printableArea)=>new PdfPaginator(this,printableArea);
    public void Dispose(){disposed=true;resize.Stop();generation++;image.Source=null;source?.Dispose();source=null;document=null;}
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
