namespace LaunchPad;

static class Paths
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchPad");
    public static readonly string Launchers = Path.Combine(Root, "Launchers");
    public static readonly string Icons = Path.Combine(Root, "Icons");
    public static readonly string Build = Path.Combine(Root, "build");
    public static readonly string HotkeysFile = Path.Combine(Root, "hotkeys.json");
    public static readonly string SettingsFile = Path.Combine(Root, "settings.json");
    public static readonly string ThemeFile = Path.Combine(Root, "theme.txt");
    public static readonly string TabsFile = Path.Combine(Root, "tabs.txt");
    public static readonly string WindowFile = Path.Combine(Root, "window.txt");
    public static readonly string CheatSheet = Path.Combine(Root, "Hotkey cheat sheet.html");
    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static readonly string Csc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
}
