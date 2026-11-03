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
        if (!first)
        {
            // a restart may still be letting go: give it a moment before handing over
            if (!mutex.WaitOne(3000)) { showEvent.Set(); return; }
        }

        ApplicationConfiguration.Initialize();
        Theme.Load();
        if (args.Contains("--selftest")) { SelfTest(args.Contains("--merge")); return; }
        bool hidden = args.Contains("--tray");
        var form = new MainForm(hidden);
        if (args.Contains("--themes")) form.Shown += (s, e) => form.OpenThemes();   // for testing

        var t = new Thread(() =>
        {
            while (showEvent.WaitOne())
                try { form.BeginInvoke(form.ShowMe); } catch { return; }
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
    static void SelfTest(bool merge)
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
        foreach (var h in store.Hotkeys)
        {
            mgr.Status.TryGetValue(h.Id, out var st);
            var acts = string.Join(" ; ", h.Actions.Select(a => a.Kind == ActionKind.PasteText ? "Paste text (hidden)" : a.Summary()));
            sb.AppendLine($"{h.Category,-8} | {h.DisplayName,-22} | {h.Combo,-22} | {(string.IsNullOrEmpty(st) ? "On" : st),-22} | {acts}");
        }
        File.WriteAllText(Path.Combine(Paths.Root, "selftest.txt"), sb.ToString());
    }
}
