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
    private readonly Image image=new(){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new(24)};
    private readonly TextBox pageInput=new(){Width=64,Text="1",TextAlignment=TextAlignment.Center};
    private readonly TextBlock count=new(){VerticalAlignment=VerticalAlignment.Center,Margin=new(10,0,10,0)};
    private readonly ScrollViewer scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
    private uint pageIndex;
    private double zoom=1;
    private long generation;
    private bool disposed;
    public uint PageCount=>document?.PageCount??0;
    public event Action<string>? StatusChanged;
    public NativePdfReader()
    {
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        Background=new SolidColorBrush(Color.FromRgb(241,245,249));
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Background=Brushes.White,Margin=new(0),HorizontalAlignment=HorizontalAlignment.Stretch};
        var previous=new Button{Content="上一页",Margin=new(20,8,8,8)};previous.Click+=async(_,_)=>{if(pageIndex>0)await ShowPageAsync(pageIndex-1);};bar.Children.Add(previous);bar.Children.Add(pageInput);bar.Children.Add(count);
        var next=new Button{Content="下一页",Margin=new(0,8,8,8)};next.Click+=async(_,_)=>{if(pageIndex+1<PageCount)await ShowPageAsync(pageIndex+1);};bar.Children.Add(next);
        pageInput.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter&&uint.TryParse(pageInput.Text,out var number)&&number>0&&number<=PageCount){await ShowPageAsync(number-1);e.Handled=true;}};
        Children.Add(bar);scroll.Content=image;SetRow(scroll,1);Children.Add(scroll);
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
        var width=(uint)Math.Clamp(Math.Max(800,ActualWidth-48)*1.5*zoom,800,3200);
        var bitmap=await RenderAsync(index,width,token);
        if(serial!=generation||disposed)return;
        image.Source=bitmap;
        var available=Math.Max(300,ActualWidth>0?ActualWidth-48:1000);
        image.Width=available*zoom;image.Height=image.Width*bitmap.PixelHeight/bitmap.PixelWidth;
        pageIndex=index;pageInput.Text=(index+1).ToString();scroll.ScrollToTop();
        StatusChanged?.Invoke($"第 {index+1} / {PageCount} 页");
    }
    private async Task<BitmapSource> RenderAsync(uint index,uint width,CancellationToken token)
    {
        using var page=document!.GetPage(index);using var output=new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output,new PdfPageRenderOptions{DestinationWidth=width}).AsTask(token).ConfigureAwait(false);
        output.Seek(0);using var input=output.AsStreamForRead();
        var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);var bitmap=new WriteableBitmap(decoder.Frames[0]);bitmap.Freeze();return bitmap;
    }
    public Task SetZoomAsync(double value){zoom=value;return ShowPageAsync(pageIndex);}
    public void SavePreview(string path)
    {
        if(image.Source is not BitmapSource bitmap)return;
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
    public DocumentPaginator Paginator(Size printableArea)=>new PdfPaginator(this,printableArea);
    public void Dispose(){disposed=true;generation++;image.Source=null;source?.Dispose();source=null;document=null;}
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
