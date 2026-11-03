using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchPad;

public enum ActionKind { Open, PasteText, System, Media, MouseClick, Wait, Workspace }

public enum SysCmd
{
    LockPC, Sleep, Hibernate, ShutDown, Restart, SignOut,
    ShowDesktop, CloseWindow, MinimizeWindow, MaximizeWindow, AlwaysOnTop,
    MonitorOff, ScreenSaver, EmptyRecycleBin, TaskManager, RunBox, ToggleHiddenFiles,
    FindFiles, NewFolder, EjectDrive, CloseDrive, QuickSearch, Unknown
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
    // Workspace
    public string WorkspaceId { get; set; } = "";

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
        ActionKind.Workspace => "Open workspace " + (Runner.Store?.Workspaces.FirstOrDefault(w => w.Id == WorkspaceId)?.Name ?? "(deleted)"),
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
        .Replace("Run Box", "Run box").Replace("Toggle Hidden Files", "Show / hide hidden files")
        .Replace("Find Files", "Find files (search window)").Replace("New Folder", "New folder (in the open folder window)")
        .Replace("Eject Drive", "Open the CD/DVD drive").Replace("Close Drive", "Close the CD/DVD drive").Replace("Quick Search", "Quick Search box");
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
    public List<Workspace> Workspaces { get; set; } = new();
    public List<Snippet> Snippets { get; set; } = new();
    public bool SnippetsOn { get; set; } = true;
    public SearchSettings Search { get; set; } = new();

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

/// <summary>One window in a workspace: what it is, how to open it, where it goes.</summary>
public class WinItem
{
    public string Name { get; set; } = "";          // window title when saved
    public string Exe { get; set; } = "";           // program that owns the window (used to find it)
    public string TitleContains { get; set; } = ""; // optional: only windows whose title has this
    public string Launch { get; set; } = "";        // what to start if it isn't open
    public string Args { get; set; } = "";
    public bool OpenIfClosed { get; set; } = true;
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public string State { get; set; } = "Normal";   // Normal, Maximized, Minimized
}

public class Workspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<WinItem> Items { get; set; } = new();
}

public class Snippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Abbrev { get; set; } = "";
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class SearchSettings
{
    public bool Enabled { get; set; } = true;
    public uint Mods { get; set; } = Native.MOD_WIN | Native.MOD_ALT;   // Win + Alt + Space (Ctrl+Alt+Space is often taken)
    public uint Key { get; set; } = 0x20;   // Space
    public bool Apps { get; set; } = true;
    public bool Desktop { get; set; } = true;
    public bool Hotkeys { get; set; } = true;
    public bool Launchers { get; set; } = true;
    public bool Workspaces { get; set; } = true;
    public bool Snippets { get; set; } = true;
    public bool Web { get; set; } = true;
    public string WebUrl { get; set; } = "https://www.google.com/search?q=";
    public Dictionary<string, int> Uses { get; set; } = new();   // what you pick most comes first
}
