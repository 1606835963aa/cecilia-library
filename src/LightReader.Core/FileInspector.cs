using System.IO.Compression;
using System.Text;
using System.Xml;

namespace LightReader.Core;

public enum DocumentKind { Text, Html, HtmlArchive, Xml, Rtf, Pdf, Image, Word, Spreadsheet, Office, Undefined }
public sealed record DocumentInfo(string Path, string Extension, DocumentKind Kind, string? Component, string? Problem = null);

public static class FileInspector
{
    private static readonly HashSet<string> Word = ["docx", "dotx", "docm", "dotm"];
    private static readonly HashSet<string> Sheets = ["xlsx", "xlsm", "xltx", "xltm", "xls", "xlt", "et", "ett", "csv", "dbf", "prn", "dif"];
    private static readonly HashSet<string> Office = ["doc", "dot", "wps", "wpt", "ppt", "pptx", "pptm", "pot", "potx", "potm", "pps", "ppsx", "ppsm", "dps", "dpt", "mht", "mhtml"];
    public static DocumentInfo Inspect(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("文件不存在", path);
        var ext = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[16];
        var length = stream.Read(header);
        var bytes = header[..length];
        if (bytes.StartsWith("%PDF-"u8)) return new(path, ext, DocumentKind.Pdf, null);
        if (bytes.StartsWith(new byte[] {137,80,78,71}) || bytes.StartsWith(new byte[]{255,216,255}) || bytes.StartsWith("BM"u8) || bytes.StartsWith(new byte[]{73,73,42,0}) || bytes.StartsWith(new byte[]{77,77,0,42}))
            return new(path, ext, DocumentKind.Image, null);
        if (Word.Contains(ext) || new[]{"xlsx","xlsm","xltx","xltm","pptx","pptm","potx","potm","ppsx","ppsm"}.Contains(ext))
        {
            if (bytes.StartsWith(new byte[]{208,207,17,224,161,177,26,225}))
                return new(path, ext, DocumentKind.Office, "office", "文件可能已加密，需要办公兼容组件");
            if (!bytes.StartsWith("PK"u8)) throw new InvalidDataException("文件内容与格式不符，或文件已损坏");
            using var zip = ZipFile.OpenRead(path);
            if(zip.Entries.Count>20000 || zip.Entries.Sum(entry=>entry.Length)>200*1024*1024)throw new InvalidDataException("文档解压后超过当前处理范围");
            if (zip.GetEntry("word/document.xml") != null) return new(path, ext, DocumentKind.Word, "word");
            if (zip.GetEntry("xl/workbook.xml") != null) return new(path, ext, DocumentKind.Spreadsheet, "sheet");
            if (zip.GetEntry("ppt/presentation.xml") != null) return new(path, ext, DocumentKind.Office, "office");
            throw new InvalidDataException("文件缺少文档主体");
        }
        if (ext == "rtf" && bytes.StartsWith("{\\rtf"u8)) return new(path, ext, DocumentKind.Rtf, null);
        if (ext is "txt" or "csv") return new(path, ext, ext == "csv" ? DocumentKind.Spreadsheet : DocumentKind.Text, ext == "csv" ? "sheet" : null);
        if (ext is "html" or "htm") return new(path, ext, DocumentKind.Html, null);
        if (ext is "mht" or "mhtml") return new(path, ext, DocumentKind.HtmlArchive, null);
        if (ext == "xml")
        {
            using var reader = XmlReader.Create(path, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (reader.NamespaceURI is "urn:schemas-microsoft-com:office:spreadsheet" or "http://schemas.microsoft.com/office/word/2003/wordml")
                return new(path, ext, DocumentKind.Office, "office");
            return new(path, ext, DocumentKind.Xml, null);
        }
        if (ext is "xla" or "xlam" or "ts") return new(path, ext, DocumentKind.Undefined, null, "这个后缀的查看范围正在核实");
        if (Sheets.Contains(ext)) return new(path, ext, DocumentKind.Spreadsheet, "sheet");
        if (Office.Contains(ext)) return new(path, ext, DocumentKind.Office, "office");
        throw new NotSupportedException("暂未接入这个格式");
    }
}
