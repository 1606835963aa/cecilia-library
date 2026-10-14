using System.Buffers.Binary;
using System.Text.Json;

namespace LightReader.Core;

public sealed record PdfWorkerCommand(string Operation,string? Path=null,uint Page=0,uint Width=0);
public sealed record PdfWorkerReply(bool Ok,uint PageCount=0,double PageWidth=0,double PageHeight=0,uint RenderWidth=0,uint RenderHeight=0,int ImageLength=0,string? Error=null);
public sealed record PdfWorkerFrame(PdfWorkerReply Reply,byte[] Image);

public static class PdfWorkerProtocol
{
    public const int MaximumMetadataBytes=16384;
    public const int MaximumImageBytes=(int)(PdfRenderBudget.MaximumPixels*4)+1048576;
    public static async Task WriteAsync(Stream output,PdfWorkerReply reply,ReadOnlyMemory<byte> image,CancellationToken token)
    {
        if(image.Length>MaximumImageBytes||reply.ImageLength!=image.Length)throw new InvalidDataException("PDF 图片长度无效");
        var json=JsonSerializer.SerializeToUtf8Bytes(reply);
        if(json.Length>MaximumMetadataBytes)throw new InvalidDataException("PDF 响应过长");
        var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,json.Length);
        await output.WriteAsync(header,token).ConfigureAwait(false);
        await output.WriteAsync(json,token).ConfigureAwait(false);
        await output.WriteAsync(image,token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);
    }
    public static async Task<PdfWorkerFrame> ReadAsync(Stream input,CancellationToken token)
    {
        var header=new byte[4];await input.ReadExactlyAsync(header,token).ConfigureAwait(false);
        var length=BinaryPrimitives.ReadInt32LittleEndian(header);
        if(length is <=0 or >MaximumMetadataBytes)throw new InvalidDataException("PDF 响应长度无效");
        var json=new byte[length];await input.ReadExactlyAsync(json,token).ConfigureAwait(false);
        var reply=JsonSerializer.Deserialize<PdfWorkerReply>(json)??throw new InvalidDataException("PDF 响应无效");
        if(reply.ImageLength is <0 or >MaximumImageBytes)throw new InvalidDataException("PDF 图片长度无效");
        var image=new byte[reply.ImageLength];await input.ReadExactlyAsync(image,token).ConfigureAwait(false);
        return new(reply,image);
    }
}
