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
}
