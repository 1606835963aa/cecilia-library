using System.Text.Json;

namespace LightReader.App;
public sealed record RecentFile(string Path, string Name);
public sealed class ReaderSettings
{
    public List<RecentFile> Recent { get; set; } = [];
    public Dictionary<string, double> ScrollPositions { get; set; } = [];
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
    { Recent.RemoveAll(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); Recent.Insert(0, new(path, System.IO.Path.GetFileName(path))); Recent = Recent.Take(30).ToList(); Save(); }
}
