using System.IO.Compression;

namespace LaunchPad;

/// <summary>One .lpbackup file (a zip) with hotkeys, settings and every launcher.</summary>
static class Backup
{
    public const string Filter = "LaunchPad backup (*.lpbackup)|*.lpbackup";

    static readonly string[] Files = { "hotkeys.json", "theme.txt", "tabs.txt", "settings.json" };

    public static void Export(string path)
    {
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var f in Files)
            {
                var full = Path.Combine(Paths.Root, f);
                if (File.Exists(full)) zip.CreateEntryFromFile(full, f);
            }
            if (Directory.Exists(Paths.Shortcuts))
                foreach (var f in Directory.GetFiles(Paths.Shortcuts))
                    zip.CreateEntryFromFile(f, "Shortcuts/" + Path.GetFileName(f));
            if (Directory.Exists(Paths.Launchers))
                foreach (var exe in Directory.GetFiles(Paths.Launchers, "*.exe"))
                    zip.CreateEntryFromFile(exe, "Launchers/" + Path.GetFileName(exe));
            // Desktop shortcuts that point at launchers, so a restore can put them back
            foreach (var lnk in Directory.GetFiles(Paths.Desktop, "*.lnk"))
                if (Shortcut.Read(lnk).Target.StartsWith(Paths.Launchers, StringComparison.OrdinalIgnoreCase))
                    zip.CreateEntryFromFile(lnk, "Desktop/" + Path.GetFileName(lnk));
        }
        File.Move(tmp, path, true);
    }

    /// <summary>Puts a backup back. Saves what's here now first, just in case.</summary>
    public static string Restore(string path)
    {
        var safety = Path.Combine(Paths.Root, "backups", $"before restore {DateTime.Now:yyyy-MM-dd HHmmss}.lpbackup");
        Directory.CreateDirectory(Path.GetDirectoryName(safety));
        Export(safety);
        using var zip = ZipFile.OpenRead(path);
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith("/")) continue;
            string dest;
            if (e.FullName.StartsWith("Launchers/")) dest = Path.Combine(Paths.Launchers, Path.GetFileName(e.FullName));
            else if (e.FullName.StartsWith("Shortcuts/")) dest = Path.Combine(Paths.Shortcuts, Path.GetFileName(e.FullName));
            else if (e.FullName.StartsWith("Desktop/")) dest = Path.Combine(Paths.Desktop, Path.GetFileName(e.FullName));
            else if (Files.Contains(e.FullName)) dest = Path.Combine(Paths.Root, e.FullName);
            else continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            e.ExtractToFile(dest, true);
        }
        return safety;
    }
}
