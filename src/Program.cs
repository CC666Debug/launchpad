using System.Diagnostics;

namespace LaunchPad;

static class Program
{
    public static bool RestartRequested;

    [STAThread]
    static void Main(string[] args)
    {
        // one LaunchPad at a time: a second start just brings the first one up
        var mutex = new Mutex(true, "LaunchPad.SingleInstance." + Environment.UserName, out bool first);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "LaunchPad.Show." + Environment.UserName);
        int wi = Array.IndexOf(args, "--workspace");
        string wsName = wi >= 0 && wi + 1 < args.Length ? args[wi + 1] : null;
        string cmdFile = Path.Combine(Paths.Root, "open-workspace.txt");
        if (!first)
        {
            // a restart may still be letting go: give it a moment before handing over
            if (!mutex.WaitOne(3000))
            {
                // already running: pass the workspace to it instead of opening the window
                if (wsName != null) { Directory.CreateDirectory(Paths.Root); File.WriteAllText(cmdFile, wsName); }
                showEvent.Set(); return;
            }
        }

        ApplicationConfiguration.Initialize();
        Theme.Load();
        if (args.Contains("--selftest")) { SelfTest(args.Contains("--merge"), args); return; }
        bool hidden = args.Contains("--tray");
        var form = new MainForm(hidden);
        if (args.Contains("--themes")) form.Shown += (s, e) => form.OpenThemes();   // for testing
        if (args.Contains("--search")) form.Shown += (s, e) => QuickSearch.Show();  // for testing
        // LaunchPad.exe --workspace "Name" opens that workspace (handy in shortcuts)
        async void OpenWs(string name)
        {
            var ws = form.Store.Workspaces.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
            if (ws != null) await Workspaces.Open(ws); else Tray.Tip("LaunchPad", $"No workspace called \"{name}\".", true);
        }
        if (wsName != null) form.Load += (s, e) => OpenWs(wsName);

        var t = new Thread(() =>
        {
            while (showEvent.WaitOne())
                try
                {
                    string pending = null;
                    try { if (File.Exists(cmdFile)) { pending = File.ReadAllText(cmdFile).Trim(); File.Delete(cmdFile); } } catch { }
                    if (pending != null) form.BeginInvoke(() => OpenWs(pending)); else form.BeginInvoke(form.ShowMe);
                }
                catch { return; }
        }) { IsBackground = true };
        t.Start();

        // first run: bring in Keyboard Launchpad's hotkeys automatically
        if (!form.Store.ImportedKlp && form.Store.Hotkeys.Count == 0 && KlpImport.FindFile() != null)
            form.Shown += (s, e) => form.Hotkeys.Import(true);

        Application.Run(form);
        mutex.ReleaseMutex(); mutex.Dispose();
        if (RestartRequested) Process.Start(Environment.ProcessPath);
    }

    /// <summary>Registers everything and writes a report (no window). --merge also merges Keyboard Launchpad's newest hotkeys.</summary>
    static void SelfTest(bool merge, string[] args2)
    {
        var store = HotkeyStore.Load();
        var sb = new System.Text.StringBuilder();
        using var mgr = new HotkeyManager();
        if (merge || !store.ImportedKlp)
        {
            var page = new HotkeysPage(store, mgr, () => store.Save());
            int n = page.Import(false);
            sb.AppendLine($"merged {n} from {KlpImport.FindFile()}");
        }
        mgr.Apply(store);
        sb.AppendLine("quick search: " + (mgr.SearchStatus == "" ? "ready on " + KeyNames.Combo(store.Search.Mods, store.Search.Key) : mgr.SearchStatus));
        try
        {
            QuickSearch.Store = store; Runner.Store = store;
            var all = QuickSearch.Everything();
            sb.AppendLine($"quick search finds {all.Count} things: " + string.Join(", ", all.GroupBy(f => f.Sub?.Split(' ')[0]).Select(g => $"{g.Key} {g.Count()}")));
            foreach (var q in new[] { "gma", "obs", "calc", "github.com" })
                sb.AppendLine($"  '{q}' -> " + string.Join(" | ", QuickSearch.Find(all, q).Take(4).Select(f => $"{f.Title} [{f.Sub}]")));
            var form = new SearchForm(); form.Popup();
            sb.AppendLine($"popup visible: {form.Visible}, at {form.Location}");
            if (args2.Contains("--shot"))
            {
                form.Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<TextBox>()).First().Text = "gma";
                Application.DoEvents();
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(Path.Combine(Paths.Root, "build", "qs.png"));
            }
            form.Close();
        }
        catch (Exception ex) { sb.AppendLine("quick search ERROR: " + ex); }
        foreach (var w in Workspaces.OpenWindows()) sb.AppendLine($"window | {Path.GetFileName(w.Exe),-28} | {w.State,-9} | {w.Rect.Left},{w.Rect.Top} {w.Rect.Right - w.Rect.Left}x{w.Rect.Bottom - w.Rect.Top}");
        foreach (var h in store.Hotkeys)
        {
            mgr.Status.TryGetValue(h.Id, out var st);
            var acts = string.Join(" ; ", h.Actions.Select(a => a.Kind == ActionKind.PasteText ? "Paste text (hidden)" : a.Summary()));
            sb.AppendLine($"{h.Category,-8} | {h.DisplayName,-22} | {h.Combo,-22} | {(string.IsNullOrEmpty(st) ? "On" : st),-22} | {acts}");
        }
        File.WriteAllText(Path.Combine(Paths.Root, "selftest.txt"), sb.ToString());
    }
}
