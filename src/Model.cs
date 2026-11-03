using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchPad;

public enum ActionKind { Open, PasteText, System, Media, MouseClick, Wait }

public enum SysCmd
{
    LockPC, Sleep, Hibernate, ShutDown, Restart, SignOut,
    ShowDesktop, CloseWindow, MinimizeWindow, MaximizeWindow, AlwaysOnTop,
    MonitorOff, ScreenSaver, EmptyRecycleBin, TaskManager, RunBox, ToggleHiddenFiles, Unknown
}

public enum MediaCmd { PlayPause, Next, Previous, Stop, VolumeUp, VolumeDown, Mute }

public class HotAction
{
    public ActionKind Kind { get; set; }
    // Open
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string StartIn { get; set; } = "";
    public bool BringToFront { get; set; } = true;
    // PasteText
    public string Text { get; set; } = "";
    public bool PasteNow { get; set; } = true;
    // System / Media
    public SysCmd System { get; set; }
    public MediaCmd Media { get; set; }
    public string Note { get; set; } = "";   // e.g. what Keyboard Launchpad called it
    // MouseClick
    public int X { get; set; }
    public int Y { get; set; }
    public string Button { get; set; } = "Left";
    // Wait
    public int Ms { get; set; } = 500;

    public HotAction Clone() => JsonSerializer.Deserialize<HotAction>(JsonSerializer.Serialize(this));

    [JsonIgnore]
    public bool NeedsSetup => (Kind == ActionKind.System && System == SysCmd.Unknown) || (Kind == ActionKind.Open && string.IsNullOrWhiteSpace(Target));

    public string Summary() => Kind switch
    {
        ActionKind.Open when string.IsNullOrWhiteSpace(Target) => "Needs setup (nothing to open yet)",
        ActionKind.Open => "Open " + Pretty(Target) + Args(),
        ActionKind.PasteText => (PasteNow ? "Paste text" : "Copy text"),
        ActionKind.System => System == SysCmd.Unknown ? "Needs setup (" + Note + ")" : Words(System.ToString()),
        ActionKind.Media => "Music: " + Words(Media.ToString()),
        ActionKind.MouseClick => $"{Button} click at {X}, {Y}",
        ActionKind.Wait => $"Wait {Ms} ms",
        _ => Kind.ToString()
    };

    string Args()
    {
        if (string.IsNullOrWhiteSpace(Arguments)) return "";
        if (Arguments.Contains("--app-id=")) return " (web app)";
        return Arguments.Length > 40 ? " " + Arguments[..40] + "…" : " " + Arguments;
    }

    static string Pretty(string t)
    {
        if (string.IsNullOrEmpty(t)) return "(nothing)";
        if (Icons.IsUrl(t)) return t.Length > 50 ? t[..50] + "…" : t;
        var n = Path.GetFileNameWithoutExtension(t.TrimEnd('\\'));
        return string.IsNullOrEmpty(n) ? t : n;
    }

    public static string Words(string s) => global::System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ")
        .Replace("Run Box", "Run box").Replace("Toggle Hidden Files", "Show / hide hidden files");
}

public class Hotkey
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Category { get; set; } = "General";
    public string Description { get; set; } = "";
    public uint Mods { get; set; }   // MOD_ALT 1, MOD_CONTROL 2, MOD_SHIFT 4, MOD_WIN 8 (same as Keyboard Launchpad)
    public uint Key { get; set; }    // virtual key code, 0 = none
    public bool Enabled { get; set; } = true;
    public List<HotAction> Actions { get; set; } = new();

    public Hotkey Clone() => JsonSerializer.Deserialize<Hotkey>(JsonSerializer.Serialize(this));

    [JsonIgnore] public string Combo => KeyNames.Combo(Mods, Key);
    [JsonIgnore] public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : (Actions.Count > 0 ? Actions[0].Summary() : "(no name)");
}

public class HotkeyStore
{
    public List<Hotkey> Hotkeys { get; set; } = new();
    public List<string> Categories { get; set; } = new();   // keeps empty categories around
    public bool Paused { get; set; }
    public bool ImportedKlp { get; set; }

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static HotkeyStore Load()
    {
        HotkeyStore s;
        try { s = JsonSerializer.Deserialize<HotkeyStore>(File.ReadAllText(Paths.HotkeysFile), Opts) ?? new(); }
        catch { return new(); }
        s.Fixups();
        return s;
    }

    /// <summary>Tidy old Keyboard Launchpad leftovers.</summary>
    public void Fixups()
    {
        foreach (var a in Hotkeys.SelectMany(h => h.Actions).Where(a => a.Kind == ActionKind.Open))
        {
            // some Chrome app hotkeys had chrome_proxy.exe's path stuck at the front of the arguments
            a.Arguments = global::System.Text.RegularExpressions.Regex.Replace(a.Arguments ?? "", "^\\s*\"[^\"]*chrome_proxy\\.exe\"\\s*", "",
                global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Paths.Root);
        var tmp = Paths.HotkeysFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts));
        File.Move(tmp, Paths.HotkeysFile, true);
    }

    public List<string> AllCategories()
    {
        var all = new List<string>(Categories);
        foreach (var h in Hotkeys)
            if (!string.IsNullOrWhiteSpace(h.Category) && !all.Contains(h.Category, StringComparer.OrdinalIgnoreCase)) all.Add(h.Category);
        if (all.Count == 0) all.Add("General");
        return all;
    }

    public void RenameCategory(string from, string to)
    {
        foreach (var h in Hotkeys) if (string.Equals(h.Category, from, StringComparison.OrdinalIgnoreCase)) h.Category = to;
        var keep = AllCategories();
        Categories = keep.Select(c => string.Equals(c, from, StringComparison.OrdinalIgnoreCase) ? to : c)
                         .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
