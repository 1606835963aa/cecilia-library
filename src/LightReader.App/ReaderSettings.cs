using System.Text.Json;
using System.Windows.Media;

namespace LightReader.App;
public sealed record RecentFile(string Path, string Name)
{
    public DateTime? OpenedAt { get; init; }
}
public sealed class ReaderSettings
{
    public List<RecentFile> Recent { get; set; } = [];
    public Dictionary<string, double> ScrollPositions { get; set; } = [];
    public string Theme { get; set; } = "system";
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool Maximized { get; set; }
    public static ReaderSettings Load()
    {
        try { return JsonSerializer.Deserialize<ReaderSettings>(File.ReadAllText(Path.Combine(App.DataRoot, "settings.json"))) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    public void Save()
    {
        var file = Path.Combine(App.DataRoot, "settings.json");
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, file, true);
    }
    public void Add(string path)
    { Recent.RemoveAll(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); Recent.Insert(0, new(path, System.IO.Path.GetFileName(path)) { OpenedAt = DateTime.Now }); Recent = Recent.Take(30).ToList(); Save(); }
}
public static class FileBadge
{
    private static readonly (string Extensions, string Text, Brush Brush)[] groups =
    [
        (" doc docx docm dot dotx dotm wps wpt odt rtf ", "W", Fill(0x3B, 0x6F, 0xB6)),
        (" xls xlsx xlsm xlt xltx xltm et ett csv dbf prn dif xla xlam ", "X", Fill(0x2E, 0x8B, 0x57)),
        (" ppt pptx pptm pot potx potm pps ppsx ppsm dps dpt ", "P", Fill(0xC8, 0x63, 0x2B)),
        (" pdf ", "PDF", Fill(0xC0, 0x39, 0x2B)),
        (" jpg jpeg png tif tiff bmp ", "图", Fill(0x8E, 0x5B, 0xB5)),
        (" htm html mht mhtml xml ", "网", Fill(0x4A, 0x7C, 0x8C))
    ];
    private static readonly Brush other = Fill(0x7A, 0x74, 0x6A);
    private static Brush Fill(byte r, byte g, byte b) { var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush; }
    public static (string Text, Brush Brush) For(string path)
    {
        var extension = " " + System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant() + " ";
        foreach (var group in groups) if (group.Extensions.Contains(extension)) return (group.Text, group.Brush);
        return ("文", other);
    }
}
public sealed class RecentItem(RecentFile file)
{
    public string Path => file.Path;
    public string Name => file.Name;
    public string Folder => System.IO.Path.GetDirectoryName(file.Path) ?? "";
    public string Badge => FileBadge.For(file.Path).Text;
    public Brush BadgeBrush => FileBadge.For(file.Path).Brush;
    public string When
    {
        get
        {
            if (file.OpenedAt is not { } time) return "";
            var passed = DateTime.Now - time;
            if (passed < TimeSpan.FromMinutes(1)) return "刚刚";
            if (passed < TimeSpan.FromHours(1)) return $"{(int)passed.TotalMinutes} 分钟前";
            if (time.Date == DateTime.Today) return $"今天 {time:HH:mm}";
            if (time.Date == DateTime.Today.AddDays(-1)) return $"昨天 {time:HH:mm}";
            return time.Year == DateTime.Today.Year ? $"{time:M月d日}" : $"{time:yyyy年M月d日}";
        }
    }
}
