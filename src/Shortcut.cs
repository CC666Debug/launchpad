namespace LaunchPad;

/// <summary>Read/write Windows .lnk shortcuts through the built-in WScript.Shell COM object.</summary>
static class Shortcut
{
    static dynamic _shell;
    static dynamic Shell => _shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));

    public record Info(string Target, string Arguments, string WorkingDir, string IconLocation, string Description);

    public static Info Read(string lnk)
    {
        try
        {
            dynamic s = Shell.CreateShortcut(lnk);
            return new Info((string)s.TargetPath, (string)s.Arguments, (string)s.WorkingDirectory, (string)s.IconLocation, (string)s.Description);
        }
        catch { return new Info("", "", "", "", ""); }
    }

    public static void Write(string lnk, string target, string args, string workDir, string iconLoc, string description)
    {
        dynamic s = Shell.CreateShortcut(lnk);
        s.TargetPath = target;
        s.Arguments = args ?? "";
        s.WorkingDirectory = workDir ?? "";
        if (!string.IsNullOrEmpty(iconLoc)) s.IconLocation = iconLoc;
        s.Description = description ?? "";
        s.Save();
    }
}
