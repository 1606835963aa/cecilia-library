using System.IO;
using System.Windows;

namespace LightReader.App;
public partial class App : Application
{
    public const string DisplayName = "塞西莉亚的图书馆";
    public static string DataRoot { get; private set; } = "";
    public static bool IsDark { get; private set; }
    public static event Action? ThemeChanged;
    // mode：system 跟随 Windows 的应用颜色设置，light 浅色，dark 深色
    public static void ApplyTheme(string mode)
    {
        var dark = mode == "dark" || (mode != "light" && SystemPrefersDark());
        if (dark == IsDark) return;
        IsDark = dark;
        Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml") };
        ThemeChanged?.Invoke();
    }
    private static bool SystemPrefersDark()
    {
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0; }
        catch (Exception e) when (e is System.Security.SecurityException or IOException) { return false; }
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.ToList();
        string? stateFile=null;
        var stateArg=args.IndexOf("--state-file");
        if(stateArg>=0&&stateArg+1<args.Count){stateFile=Path.GetFullPath(args[stateArg+1]);args.RemoveRange(stateArg,2);}
        string? benchmarkFile = null;
        var benchmarkArg = args.IndexOf("--benchmark");
        if (benchmarkArg >= 0 && benchmarkArg + 1 < args.Count) { benchmarkFile = Path.GetFullPath(args[benchmarkArg + 1]); args.RemoveRange(benchmarkArg, 2); }
        string? verificationRoot = null;
        var verifyArg = args.IndexOf("--verify");
        if (verifyArg >= 0 && verifyArg + 1 < args.Count) { verificationRoot = Path.GetFullPath(args[verifyArg + 1]); args.RemoveRange(verifyArg, 2); }
        var profileArg = args.IndexOf("--data-dir");
        if (profileArg >= 0 && profileArg + 1 < args.Count)
        { DataRoot = Path.GetFullPath(args[profileArg + 1]); args.RemoveRange(profileArg, 2); }
        else DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightReader");
        Directory.CreateDirectory(DataRoot);
        var window = new MainWindow(args.Where(a => !a.StartsWith("--")).ToArray(), verificationRoot, benchmarkFile,stateFile);
        MainWindow = window; window.Show();
        DispatcherUnhandledException += (_, error) =>
        { MessageBox.Show(MainWindow, error.Exception.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); error.Handled = true; };
    }
}
