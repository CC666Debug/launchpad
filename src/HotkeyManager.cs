namespace LaunchPad;

/// <summary>Registers every hotkey with Windows and runs it when pressed.</summary>
sealed class HotkeyManager : NativeWindow, IDisposable
{
    readonly Dictionary<int, Hotkey> _byId = new();
    public readonly Dictionary<string, string> Status = new();   // hotkey Id -> problem text, empty = working
    int _next = 1;

    public HotkeyManager() { CreateHandle(new CreateParams()); }

    public void Apply(HotkeyStore store)
    {
        foreach (var id in _byId.Keys) Native.UnregisterHotKey(Handle, id);
        _byId.Clear(); Status.Clear(); _next = 1;
        var seen = new Dictionary<(uint, uint), string>();
        foreach (var h in store.Hotkeys)
        {
            if (h.Key == 0) { Status[h.Id] = "No key"; continue; }
            if (!h.Enabled) { Status[h.Id] = "Off"; continue; }
            if (store.Paused) { Status[h.Id] = "Paused"; continue; }
            if (seen.TryGetValue((h.Mods, h.Key), out var other)) { Status[h.Id] = "Same keys as " + other; continue; }
            int id = _next++;
            if (Native.RegisterHotKey(Handle, id, h.Mods | Native.MOD_NOREPEAT, h.Key))
            {
                _byId[id] = h; seen[(h.Mods, h.Key)] = h.DisplayName;
                Status[h.Id] = h.Actions.Any(a => a.NeedsSetup) ? "Needs setup" : h.Actions.Any(Missing) ? "Can't find the file" : "";
            }
            else Status[h.Id] = "In use by another app";
        }
    }

    static bool Missing(HotAction a)
    {
        if (a.Kind != ActionKind.Open) return false;
        var t = Environment.ExpandEnvironmentVariables(a.Target ?? "").Trim().Trim('"');
        return Path.IsPathRooted(t) && !File.Exists(t) && !Directory.Exists(t);
    }

    /// <summary>Let go of every key for a moment (while editing a hotkey).</summary>
    public void Suspend()
    {
        foreach (var id in _byId.Keys) Native.UnregisterHotKey(Handle, id);
        _byId.Clear();
    }

    public bool AnyBlocked => Status.Values.Any(v => v == "In use by another app");

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _byId.TryGetValue((int)m.WParam, out var hk))
        {
            _ = Runner.Run(hk);
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _byId.Keys) Native.UnregisterHotKey(Handle, id);
        _byId.Clear();
        DestroyHandle();
    }
}
