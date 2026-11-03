namespace LaunchPad;

static class Tray
{
    public static NotifyIcon Icon;

    public static void Tip(string title, string text, bool warn)
    {
        if (Icon == null) return;
        Icon.BalloonTipTitle = title;
        Icon.BalloonTipText = text;
        Icon.BalloonTipIcon = warn ? ToolTipIcon.Warning : ToolTipIcon.Info;
        Icon.ShowBalloonTip(4000);
    }
}

static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("LaunchPad") != null;
        }
        set
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("LaunchPad", $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue("LaunchPad", false);
        }
    }
}
