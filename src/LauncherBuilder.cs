using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LaunchPad;

/// <summary>Builds tiny standalone launcher .exe files with Windows' own C# compiler.</summary>
static class LauncherBuilder
{
    public class Data
    {
        public string[] items { get; set; } = Array.Empty<string>();
        public string icon { get; set; }
        public bool custom { get; set; }
    }

    static string Q(string s) => "@\"" + s.Replace("\"", "\"\"") + "\"";

    /// <summary>Returns null on success, otherwise an error message.</summary>
    public static string Build(string exe, IList<string> items, IList<string> names, string iconLoc, bool custom)
    {
        var tmp = Path.Combine(Paths.Build, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var title = string.Join(", ", names);
            if (title.Length > 250) title = title[..247] + "...";
            // the list lives in the normal file info (Comments) so LaunchPad can open it again later
            var data = JsonSerializer.Serialize(new Data { items = items.ToArray(), icon = iconLoc, custom = custom });
            var src = $$"""
                using System; using System.Diagnostics; using System.Reflection; using System.Windows.Forms;
                [assembly: AssemblyTitle({{Q("Opens: " + title)}})]
                [assembly: AssemblyDescription({{Q(data)}})]
                [assembly: AssemblyProduct("LaunchPad launcher")]
                [assembly: AssemblyVersion("1.0.0.0")]
                static class P {
                    [STAThread] static void Main() {
                        string[] items = { {{string.Join(", ", items.Select(Q))}} };
                        string bad = "";
                        foreach (string i in items) {
                            try { Process.Start(new ProcessStartInfo(i) { UseShellExecute = true }); } catch { bad += "\n" + i; }
                        }
                        if (bad != "") MessageBox.Show("Couldn't open:" + bad, "LaunchPad");
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tmp, "l.cs"), src, Encoding.UTF8);
            var args = new List<string> { "/nologo", "/target:winexe", "/optimize+", "/out:" + Path.Combine(tmp, "l.exe") };
            if (Icons.Export(iconLoc, Path.Combine(tmp, "l.ico"))) args.Add("/win32icon:" + Path.Combine(tmp, "l.ico"));
            args.Add(Path.Combine(tmp, "l.cs"));
            var psi = new ProcessStartInfo(Paths.Csc) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            var log = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (!File.Exists(Path.Combine(tmp, "l.exe"))) return "Build failed:\n" + log;
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            File.Copy(Path.Combine(tmp, "l.exe"), exe, true);
            return null;
        }
        catch (Exception e) { return "Couldn't save: " + e.Message; }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    /// <summary>
    /// A dropped shortcut (.lnk/.url) is copied into LaunchPad's own Shortcuts folder and that copy is used,
    /// so deleting or moving the Desktop one doesn't break anything.
    /// </summary>
    public static string KeepShortcut(string p)
    {
        try
        {
            var ext = Path.GetExtension(p).ToLowerInvariant();
            if ((ext != ".lnk" && ext != ".url") || !File.Exists(p)) return p;
            if (p.StartsWith(Paths.Shortcuts, StringComparison.OrdinalIgnoreCase)) return p;
            Directory.CreateDirectory(Paths.Shortcuts);
            var name = Path.GetFileNameWithoutExtension(p);
            var dest = Path.Combine(Paths.Shortcuts, name + ext);
            for (int i = 2; File.Exists(dest) && !SameFile(dest, p); i++) dest = Path.Combine(Paths.Shortcuts, $"{name} ({i}){ext}");
            if (!File.Exists(dest)) File.Copy(p, dest);
            return dest;
        }
        catch { return p; }
    }

    static bool SameFile(string a, string b) => File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    /// <summary>Find a shortcut with this name somewhere else (Start menu, Desktops) when the original is gone.</summary>
    static string FindAgain(string missing)
    {
        var file = Path.GetFileName(missing);
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                                    Paths.Desktop, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
            try
            {
                var hit = Directory.EnumerateFiles(dir, file, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).FirstOrDefault();
                if (hit != null) return hit;
            }
            catch { }
        return null;
    }

    /// <summary>Rebuild every launcher so it uses LaunchPad's own shortcut copies. Returns a report.</summary>
    public static string RepairAll()
    {
        var sb = new StringBuilder();
        if (!Directory.Exists(Paths.Launchers)) return "no launchers";
        foreach (var exe in Directory.GetFiles(Paths.Launchers, "*.exe"))
        {
            var d = Read(exe);
            if (d == null) continue;
            var items = new List<string>();
            foreach (var it in d.items)
            {
                var p = it;
                if ((p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) && !File.Exists(p))
                {
                    var again = FindAgain(p);
                    sb.AppendLine(again != null ? $"  {Path.GetFileName(p)}: was missing, using {again}" : $"  {Path.GetFileName(p)}: MISSING, nothing found");
                    p = again ?? p;
                }
                items.Add(KeepShortcut(p));
            }
            var names = items.Select(i => Icons.IsUrl(i) ? i : Path.GetFileNameWithoutExtension(i)).ToList();
            var err = Build(exe, items, names, d.icon ?? Icons.Fallback, d.custom);
            sb.AppendLine($"{Path.GetFileName(exe)}: {(err ?? "rebuilt")} -> " + string.Join(" | ", items));
        }
        return sb.ToString();
    }

    public static Data Read(string exe)
    {
        try
        {
            var c = FileVersionInfo.GetVersionInfo(exe).Comments;
            if (!string.IsNullOrEmpty(c) && c.StartsWith("{")) return JsonSerializer.Deserialize<Data>(c);
            // very first launchers kept the list at the end of the file
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(exe));
            const string marker = "\n#LAUNCHPAD-DATA#\n";
            int k = text.LastIndexOf(marker, StringComparison.Ordinal);
            if (k >= 0) return JsonSerializer.Deserialize<Data>(text[(k + marker.Length)..]);
        }
        catch { }
        return null;
    }
}
