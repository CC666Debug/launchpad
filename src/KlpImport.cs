namespace LaunchPad;

/// <summary>Reads hotkeys from Stardock Keyboard Launchpad's own settings file (plain .ini text).</summary>
static class KlpImport
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Stardock\Keyboard Launchpad");

    // Windows redirects old programs' saves away from Program Files, so the live copy is usually here
    public static readonly string VirtualStore = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"VirtualStore\Program Files (x86)\Stardock\Keyboard Launchpad");

    /// <summary>The newest of Keyboard Launchpad's hotkey files.</summary>
    public static string FindFile()
    {
        var all = new List<string>();
        foreach (var dir in new[] { VirtualStore, Folder })
            if (Directory.Exists(dir))
                all.AddRange(Directory.GetFiles(dir, "HotKeys (*).ini")
                    .Where(f => !f.Contains("-Generic-") && !f.Contains("Backup", StringComparison.OrdinalIgnoreCase)));
        return all.OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
    }

    static Dictionary<string, Dictionary<string, string>> ParseIni(string file)
    {
        var all = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> cur = null;
        foreach (var raw in File.ReadAllLines(file))
        {
            var l = raw.Trim();
            if (l.StartsWith("[") && l.EndsWith("]")) { cur = new(StringComparer.OrdinalIgnoreCase); all[l[1..^1]] = cur; continue; }
            int eq = l.IndexOf('=');
            if (cur != null && eq > 0) cur[l[..eq].Trim()] = l[(eq + 1)..];
        }
        return all;
    }

    public static List<Hotkey> Read(string file)
    {
        var ini = ParseIni(file);
        var list = new List<Hotkey>();
        int n = ini.TryGetValue("General", out var g) && int.TryParse(g.GetValueOrDefault("nHotKeys"), out var c) ? c : 0;
        for (int i = 1; i <= n; i++)
        {
            if (!ini.TryGetValue($"HotKey_#{i}", out var s)) continue;
            var h = new Hotkey
            {
                Name = s.GetValueOrDefault("Name", ""),
                Category = string.IsNullOrWhiteSpace(s.GetValueOrDefault("Category")) ? "General" : s["Category"],
                Description = s.GetValueOrDefault("LongDescription", ""),
                Key = uint.TryParse(s.GetValueOrDefault("vkHotkey"), out var vk) ? vk : 0,
                Mods = uint.TryParse(s.GetValueOrDefault("vkModifiers"), out var md) ? md & 0xF : 0,
            };
            int na = int.TryParse(s.GetValueOrDefault("nActions"), out var x) ? x : 0;
            for (int j = 1; j <= na; j++)
            {
                if (!ini.TryGetValue($"HotKey_#{i}-Action{j}", out var a)) continue;
                var act = Convert(a);
                if (act != null) h.Actions.Add(act);
            }
            list.Add(h);
        }
        return list;
    }

    static HotAction Convert(Dictionary<string, string> a)
    {
        switch (a.GetValueOrDefault("Action", ""))
        {
            case "Link":
                return new HotAction
                {
                    Kind = ActionKind.Open, Target = a.GetValueOrDefault("Link", ""), Arguments = a.GetValueOrDefault("Arguments", ""),
                    StartIn = a.GetValueOrDefault("StartIn", ""), BringToFront = a.GetValueOrDefault("ForegroundIfRunning", "1") != "0"
                };
            case "Clipboard":
                return new HotAction { Kind = ActionKind.PasteText, Text = a.GetValueOrDefault("ClipData", ""), PasteNow = true };
            case "Media":
                // order Keyboard Launchpad used isn't documented; keep it but ask the user to check
                return new HotAction { Kind = ActionKind.System, System = SysCmd.Unknown, Note = "music command " + a.GetValueOrDefault("MediaAction") };
            case "System":
                return new HotAction
                {
                    Kind = ActionKind.System, System = SysCmd.Unknown,
                    Note = $"system command {a.GetValueOrDefault("SystemCommand")}/{a.GetValueOrDefault("SystemCommandClass")}"
                };
            case "Plugin":
                return new HotAction { Kind = ActionKind.System, System = SysCmd.Unknown, Note = "plugin " + Path.GetFileNameWithoutExtension(a.GetValueOrDefault("Plugin", "")) };
            default:
                return null;
        }
    }
}
