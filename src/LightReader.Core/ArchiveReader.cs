using MimeKit;

namespace LightReader.Core;
public sealed record ArchiveDocument(string Html, Dictionary<string,string> Assets, string? BaseUrl);
public static class ArchiveReader
{
    public static async Task<ArchiveDocument> ReadAsync(string path,CancellationToken token)
    {
        using var message=await MimeMessage.LoadAsync(path,token);
        var body=message.BodyParts.OfType<TextPart>().FirstOrDefault(p=>p.IsHtml)??throw new InvalidDataException("归档文件没有网页正文");
        var assets=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var part in message.BodyParts.OfType<MimePart>())
        {
            token.ThrowIfCancellationRequested();
            if(part==body)continue;
            if(part.ContentType.MediaType!="image" && !part.ContentType.IsMimeType("text","css"))continue;
            if(part.Content==null)continue;
            using var output=new MemoryStream();await part.Content.DecodeToAsync(output,token);
            if(output.Length>32*1024*1024)throw new InvalidDataException("归档资源过大");
            var data=part.ContentType.IsMimeType("text","css")?"css:"+System.Text.Encoding.UTF8.GetString(output.ToArray()):"data:"+part.ContentType.MimeType+";base64,"+Convert.ToBase64String(output.ToArray());
            if(!string.IsNullOrEmpty(part.ContentId))assets["cid:"+part.ContentId]=data;
            if(part.ContentLocation!=null)assets[part.ContentLocation.ToString()]=data;
        }
        return new(body.Text,assets,body.ContentLocation?.ToString());
    }
}
