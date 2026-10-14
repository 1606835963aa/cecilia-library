namespace LightReader.Core;

public readonly record struct PdfRenderSize(uint Width,uint Height);

public static class PdfRenderBudget
{
    public const long MaximumPixels=16*1024*1024;
    public const uint MaximumDimension=16384;

    // Specify one edge so Windows keeps the page's crop/rotation aspect ratio.
    public static PdfRenderSize ForPage(double pageWidth,double pageHeight,uint requestedWidth)
    {
        if(!double.IsFinite(pageWidth)||!double.IsFinite(pageHeight)||pageWidth<=0||pageHeight<=0||requestedWidth==0)
            throw new InvalidDataException("PDF 页面尺寸无效");
        var ratio=pageHeight/pageWidth;
        if(!double.IsFinite(ratio)||ratio<=0)throw new InvalidDataException("PDF 页面比例无效");
        var width=Math.Min(requestedWidth,Math.Min(MaximumDimension,Math.Min(Math.Sqrt(MaximumPixels/ratio),MaximumDimension/ratio)));
        width=Math.Floor(width);
        if(width*Math.Ceiling(width*ratio)>MaximumPixels)width--;
        if(width<1)return new(0,MaximumDimension);
        return new((uint)width,0);
    }
}
