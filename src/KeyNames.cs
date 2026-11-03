namespace LaunchPad;

static class KeyNames
{
    static readonly Dictionary<uint, string> Special = new()
    {
        [0xC0] = "`", [0xBD] = "-", [0xBB] = "=", [0xDB] = "[", [0xDD] = "]", [0xDC] = "\\", [0xE2] = "\\ (extra key)",
        [0xBA] = ";", [0xDE] = "'", [0xBC] = ",", [0xBE] = ".", [0xBF] = "/",
        [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
        [0x20] = "Space", [0x0D] = "Enter", [0x09] = "Tab", [0x08] = "Backspace", [0x1B] = "Esc",
        [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x13] = "Pause", [0x91] = "Scroll Lock", [0x2C] = "Print Screen",
    };

    public static string Key(uint vk)
    {
        if (vk == 0) return "";
        if (Special.TryGetValue(vk, out var s)) return s;
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60);
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
        return ((Keys)vk).ToString();
    }

    public static string Combo(uint mods, uint vk)
    {
        if (vk == 0) return "";
        var parts = new List<string>();
        if ((mods & Native.MOD_WIN) != 0) parts.Add("Win");
        if ((mods & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & Native.MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & Native.MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(Key(vk));
        return string.Join(" + ", parts);
    }

    public static bool IsModifier(Keys k) => k is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin
        or Keys.LShiftKey or Keys.RShiftKey or Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu;

    // ---------- shortcuts that are already spoken for ----------
    const uint W = Native.MOD_WIN, C = Native.MOD_CONTROL, A = Native.MOD_ALT, S = Native.MOD_SHIFT;

    /// <summary>Windows' own shortcuts (Windows 11).</summary>
    static readonly Dictionary<(uint, uint), string> Windows = new()
    {
        [(W, 'A')] = "Quick settings", [(W, 'B')] = "jump to the tray icons", [(W, 'C')] = "Copilot", [(W, 'D')] = "show the desktop",
        [(W, 'E')] = "File Explorer", [(W, 'F')] = "Feedback Hub", [(W, 'G')] = "Game Bar", [(W, 'H')] = "voice typing",
        [(W, 'I')] = "Settings", [(W, 'K')] = "Cast", [(W, 'L')] = "lock the PC", [(W, 'M')] = "minimize all windows",
        [(W, 'N')] = "notifications and calendar", [(W, 'O')] = "rotation lock", [(W, 'P')] = "display / projector mode",
        [(W, 'Q')] = "Search", [(W, 'R')] = "the Run box", [(W, 'S')] = "Search", [(W, 'T')] = "taskbar apps",
        [(W, 'U')] = "Accessibility settings", [(W, 'V')] = "clipboard history", [(W, 'W')] = "Widgets", [(W, 'X')] = "the quick link menu",
        [(W, 'Z')] = "snap layouts", [(W, 0x09)] = "Task View", [(W, 0xBE)] = "the emoji panel", [(W, 0xBA)] = "the emoji panel",
        [(W, 0xBC)] = "peek at the desktop", [(W, 0x2C)] = "save a screenshot", [(W, 0x20)] = "switch keyboard language",
        [(W, 0x26)] = "maximize window", [(W, 0x28)] = "restore / minimize window", [(W, 0x25)] = "snap window left", [(W, 0x27)] = "snap window right",
        [(W, 0x24)] = "minimize the other windows", [(W, 0x13)] = "System info", [(W, 0xBB)] = "Magnifier zoom in", [(W, 0xBD)] = "Magnifier zoom out",
        [(W, 0x6B)] = "Magnifier zoom in", [(W, 0x6D)] = "Magnifier zoom out", [(W, 0x1B)] = "close Magnifier",
        [(W | S, 'S')] = "screen snip", [(W | S, 0x25)] = "move window to the other monitor", [(W | S, 0x27)] = "move window to the other monitor",
        [(W | S, 'M')] = "restore minimized windows", [(W | S, 0x26)] = "stretch window top to bottom",
        [(W | C, 'D')] = "new virtual desktop", [(W | C, 0x25)] = "previous virtual desktop", [(W | C, 0x27)] = "next virtual desktop",
        [(W | C, 0x73)] = "close the virtual desktop", [(W | C, 0x0D)] = "Narrator", [(W | C, 'C')] = "color filters", [(W | C, 'O')] = "on-screen keyboard",
        [(W | C, 'Q')] = "Quick Assist", [(W | C, 'S')] = "speech recognition", [(W | C, 'N')] = "Narrator settings", [(W | C, 'V')] = "sound output picker",
        [(W | C | S, 'B')] = "reset the graphics driver", [(W | A, 'R')] = "Game Bar recording", [(W | A, 'G')] = "Game Bar: record last 30 s",
        [(W | A, 0x2C)] = "Game Bar screenshot", [(W | A, 'D')] = "date and time", [(W | A, 'K')] = "mute the mic in calls", [(W | A, 'B')] = "HDR on/off",
        [(W | A, 0x26)] = "snap window to the top half", [(W | A, 0x28)] = "snap window to the bottom half",
        [(C | A, 0x2E)] = "the Ctrl+Alt+Del screen", [(C | S, 0x1B)] = "Task Manager", [(A, 0x09)] = "switching windows (Alt+Tab)",
        [(A, 0x73)] = "closing windows (Alt+F4)", [(A, 0x1B)] = "cycling windows", [(C, 0x1B)] = "the Start menu", [(A, 0x20)] = "the window menu",
        [(C | A, 0x09)] = "the window switcher", [(0, 0x2C)] = "the Snipping Tool", [(A, 0x2C)] = "screenshot of the window",
    };

    /// <summary>Shortcuts nearly every program uses.</summary>
    static readonly Dictionary<(uint, uint), string> Apps = new()
    {
        [(C, 'A')] = "Select all", [(C, 'C')] = "Copy", [(C, 'V')] = "Paste", [(C, 'X')] = "Cut", [(C, 'Z')] = "Undo", [(C, 'Y')] = "Redo",
        [(C, 'S')] = "Save", [(C, 'O')] = "Open", [(C, 'N')] = "New", [(C, 'P')] = "Print", [(C, 'F')] = "Find", [(C, 'H')] = "Replace / History",
        [(C, 'W')] = "Close tab", [(C, 'T')] = "New tab", [(C, 'R')] = "Reload", [(C, 'D')] = "Bookmark (browsers)", [(C, 'L')] = "Address bar",
        [(C, 'B')] = "Bold", [(C, 'I')] = "Italic", [(C, 'U')] = "Underline", [(C, 'E')] = "Search bar / center text", [(C, 'K')] = "Insert link",
        [(C, 'J')] = "Downloads", [(C, 'G')] = "Find next", [(C, 'Q')] = "Quit (some apps)", [(C | S, 'T')] = "Reopen closed tab",
        [(C | S, 'N')] = "New private window / new folder", [(C | S, 'V')] = "Paste as plain text", [(C | S, 'Z')] = "Redo", [(C | S, 'S')] = "Save as",
        [(C | S, 'P')] = "Command palette / private window", [(C | S, 'I')] = "Developer tools",
        [(C, 0x09)] = "Next tab", [(C | S, 0x09)] = "Previous tab", [(C, 0x73)] = "Close tab", [(0, 0x70)] = "Help", [(0, 0x74)] = "Refresh",
        [(0, 0x71)] = "Rename", [(0, 0x7A)] = "Full screen", [(C, 0x6B)] = "Zoom in", [(C, 0x6D)] = "Zoom out", [(C, 0xBB)] = "Zoom in", [(C, 0xBD)] = "Zoom out",
        [(C, '0')] = "Reset zoom", [(A, 0x25)] = "Back", [(A, 0x27)] = "Forward",
    };

    /// <summary>Try to grab the keys for a moment: if Windows says no, some other program has them.</summary>
    public static bool HeldByOtherApp(IntPtr hwnd, uint mods, uint vk)
    {
        if (vk == 0) return false;
        if (!Native.RegisterHotKey(hwnd, 0x7FF0, mods | Native.MOD_NOREPEAT, vk)) return true;
        Native.UnregisterHotKey(hwnd, 0x7FF0);
        return false;
    }

    /// <summary>0 = fine, 1 = programs use it (amber), 2 = Windows uses it (red).</summary>
    public static (int level, string text) Warn(uint mods, uint vk)
    {
        if (vk == 0) return (0, "");
        if (Windows.TryGetValue((mods, vk), out var w)) return (2, $"Windows uses {Combo(mods, vk)} for {w}.");
        if (Apps.TryGetValue((mods, vk), out var a) && a != "") return (1, $"Most programs use {Combo(mods, vk)} for “{a}”. Your hotkey would take it over everywhere.");
        if (mods == 0 && !(vk >= 0x70 && vk <= 0x87) && vk != 0x13 && vk != 0x91) return (1, "A key on its own stops working for typing. Add Ctrl, Alt or Win.");
        if (mods == S && vk >= 0x20 && vk <= 0x5A) return (1, "Shift + a letter is just a capital letter. Add Ctrl, Alt or Win.");
        return (0, "");
    }
}
