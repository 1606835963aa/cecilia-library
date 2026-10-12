using System.Security.Cryptography;
using System.Text;

namespace LightReader.Core;

public sealed class TextDocument
{
    public string SourcePath { get; }
    public string Text { get; }
    public Encoding Encoding { get; }
    public bool HasBom { get; }
    public string NewLine { get; }
    public byte[] SourceHash { get; }
    private TextDocument(string path, string text, Encoding encoding, bool bom, string newLine, byte[] hash) =>
        (SourcePath, Text, Encoding, HasBom, NewLine, SourceHash) = (path, text, encoding, bom, newLine, hash);
    public static async Task<TextDocument> ReadAsync(string path, CancellationToken token = default)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = await File.ReadAllBytesAsync(path, token);
        Encoding encoding; int skip = 0;
        if (bytes.AsSpan().StartsWith(new byte[]{239,187,191})) { encoding = new UTF8Encoding(true, true); skip = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[]{255,254,0,0})) { encoding = new UTF32Encoding(false, true, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[]{0,0,254,255})) { encoding = new UTF32Encoding(true, true, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[]{255,254})) { encoding = new UnicodeEncoding(false, true, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[]{254,255})) { encoding = new UnicodeEncoding(true, true, true); skip = 2; }
        else
        {
            encoding = new UTF8Encoding(false, true);
            try { _ = encoding.GetString(bytes); }
            catch (DecoderFallbackException) { encoding = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        }
        var text = encoding.GetString(bytes, skip, bytes.Length - skip);
        if (text.Contains('\0')) throw new InvalidDataException("这个文件包含二进制数据，无法按文本打开");
        var newline = text.Contains("\r\n") ? "\r\n" : text.Contains('\r') ? "\r" : "\n";
        return new(path, text, encoding, skip > 0, newline, SHA256.HashData(bytes));
    }
    public async Task SaveAsAsync(string destination, string text, bool allowOverwrite, CancellationToken token = default)
    {
        destination = Path.GetFullPath(destination);
        var same = string.Equals(destination, Path.GetFullPath(SourcePath), StringComparison.OrdinalIgnoreCase);
        if (same && (!File.Exists(SourcePath) || !SHA256.HashData(await File.ReadAllBytesAsync(SourcePath, token)).SequenceEqual(SourceHash)))
            throw new IOException("原文件已经被其他程序修改，请另存为其他文件");
        if (File.Exists(destination) && !allowOverwrite) throw new IOException("目标文件已经存在");
        // A sibling is needed for an atomic filesystem rename; it belongs to the runtime save operation.
        var staging = destination + ".lightreader-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", NewLine);
            var body = Encoding.GetBytes(normalized);
            var prefix = HasBom ? Encoding.GetPreamble() : [];
            await using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await file.WriteAsync(prefix, token); await file.WriteAsync(body, token); await file.FlushAsync(token); }
            token.ThrowIfCancellationRequested();
            // Check again after writing. Never replace a changed original with an old edit buffer.
            if (same && (!File.Exists(SourcePath) || !SHA256.HashData(await File.ReadAllBytesAsync(SourcePath, token)).SequenceEqual(SourceHash)))
                throw new IOException("原文件已变化，请另存为其他文件");
            File.Move(staging, destination, allowOverwrite);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
