using System.Diagnostics;
using System.Text;

namespace LaunchPad;

/// <summary>Does what a hotkey says.</summary>
static class Runner
{
    public static Form Owner;   // main window, used for monitor-off / screensaver messages and dialogs
    public static HotkeyStore Store;

    public static async Task Run(Hotkey hk)
    {
        foreach (var a in hk.Actions)
        {
            try { await Do(a); }
            catch (Exception e) { Tray.Tip("LaunchPad", $"{hk.DisplayName}: couldn't {a.Summary()}\n{e.Message}", true); }
        }
    }

    public static async Task Do(HotAction a)
    {
        switch (a.Kind)
        {
            case ActionKind.Open: Open(a); break;
            case ActionKind.PasteText: await Paste(a); break;
            case ActionKind.System: Sys(a.System); break;
            case ActionKind.Media: Media(a.Media); break;
            case ActionKind.MouseClick: await Click(a); break;
            case ActionKind.Wait: await Task.Delay(Math.Clamp(a.Ms, 0, 60000)); break;
            case ActionKind.Workspace:
                var ws = Store?.Workspaces.FirstOrDefault(w => w.Id == a.WorkspaceId);
                if (ws != null) await Workspaces.Open(ws);
                break;
        }
    }

    // ---------- open ----------
    static void Open(HotAction a)
    {
        var target = Environment.ExpandEnvironmentVariables(a.Target ?? "").Trim().Trim('"');
        if (target == "") return;
        if (Path.IsPathRooted(target) && !File.Exists(target) && !Directory.Exists(target))
            throw new FileNotFoundException("Can't find " + target + ". Edit the hotkey and point it at the right place.");
        // with arguments (Chrome web apps, steam://...) the program being open doesn't mean *this* thing is open
        if (a.BringToFront && string.IsNullOrWhiteSpace(a.Arguments) && TryFront(target)) return;
        Native.AllowSetForegroundWindow(-1);
        ProcessStartInfo psi;
        if (target.StartsWith("::")) psi = new ProcessStartInfo("explorer.exe", "shell:" + target);
        else psi = new ProcessStartInfo(target) { UseShellExecute = true, Arguments = a.Arguments ?? "" };
        if (!string.IsNullOrWhiteSpace(a.StartIn)) psi.WorkingDirectory = Environment.ExpandEnvironmentVariables(a.StartIn);
        else if (Path.IsPathRooted(target) && File.Exists(target)) psi.WorkingDirectory = Path.GetDirectoryName(target);
        Process.Start(psi);
    }

    /// <summary>If the program is already running, bring its window to the front.</summary>
    static bool TryFront(string target)
    {
        string exe = target;
        if (exe.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) exe = Shortcut.Read(exe).Target;
        if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Path.IsPathRooted(exe)) return false;   // e.g. calc.exe: just start it
        var name = Path.GetFileNameWithoutExtension(exe);
        var pids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName(name))
        {
            if (string.Equals(ImagePath(p.Id), exe, StringComparison.OrdinalIgnoreCase)) pids.Add(p.Id);
            p.Dispose();
        }
        if (pids.Count == 0) return false;
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((h, l) =>
        {
            if (!Native.IsWindowVisible(h) || Native.GetWindow(h, 4) != IntPtr.Zero || Native.GetWindowTextLength(h) == 0) return true; // 4 = GW_OWNER
            Native.GetWindowThreadProcessId(h, out int pid);
            if (pids.Contains(pid)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) return false;
        if (Native.IsIconic(found)) Native.ShowWindow(found, 9); // SW_RESTORE
        Native.SetForegroundWindow(found);
        return true;
    }

    static string ImagePath(int pid)
    {
        var h = Native.OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024); int size = sb.Capacity;
            return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { Native.CloseHandle(h); }
    }

    // ---------- paste ----------
    static async Task WaitForKeysUp()
    {
        int[] mods = { 0x10, 0x11, 0x12, 0x5B, 0x5C };
        for (int i = 0; i < 100; i++)
        {
            if (mods.All(k => (Native.GetAsyncKeyState(k) & 0x8000) == 0)) return;
            await Task.Delay(20);
        }
    }

    static async Task Paste(HotAction a)
    {
        string old = null;
        try { if (Clipboard.ContainsText()) old = Clipboard.GetText(); } catch { }
        Clipboard.SetText(a.Text ?? "");
        if (!a.PasteNow) { Tray.Tip("LaunchPad", "Copied — ready to paste.", false); return; }
        await WaitForKeysUp();
        Native.keybd_event(0x11, 0, 0, UIntPtr.Zero);            // Ctrl down
        Native.Tap(0x56);                                          // V
        Native.keybd_event(0x11, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        await Task.Delay(400);
        try { if (old != null) Clipboard.SetText(old); } catch { }
    }

    // ---------- system ----------
    static bool Confirm(string what) =>
        MessageBox.Show(what + " now?", "LaunchPad", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    static void Shell(string exe, string args) =>
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });

    public static void Sys(SysCmd c)
    {
        var fg = Native.GetForegroundWindow();
        switch (c)
        {
            case SysCmd.LockPC: Native.LockWorkStation(); break;
            case SysCmd.Sleep: Native.SetSuspendState(false, false, false); break;
            case SysCmd.Hibernate: Native.SetSuspendState(true, false, false); break;
            case SysCmd.ShutDown: if (Confirm("Shut down")) Shell("shutdown.exe", "/s /t 0"); break;
            case SysCmd.Restart: if (Confirm("Restart")) Shell("shutdown.exe", "/r /t 0"); break;
            case SysCmd.SignOut: if (Confirm("Sign out")) Shell("shutdown.exe", "/l"); break;
            case SysCmd.ShowDesktop: ShellApp(s => s.ToggleDesktop()); break;
            case SysCmd.RunBox: ShellApp(s => s.FileRun()); break;
            case SysCmd.CloseWindow: Native.PostMessage(fg, 0x0010, IntPtr.Zero, IntPtr.Zero); break; // WM_CLOSE
            case SysCmd.MinimizeWindow: Native.ShowWindow(fg, 6); break;
            case SysCmd.MaximizeWindow: Native.ShowWindow(fg, Native.IsZoomed(fg) ? 9 : 3); break;
            case SysCmd.AlwaysOnTop:
                bool top = (Native.GetWindowLong(fg, -20) & 0x8) != 0; // WS_EX_TOPMOST
                Native.SetWindowPos(fg, new IntPtr(top ? -2 : -1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                break;
            case SysCmd.MonitorOff: Native.PostMessage(Owner.Handle, 0x0112, new IntPtr(0xF170), new IntPtr(2)); break;
            case SysCmd.ScreenSaver: Native.PostMessage(Owner.Handle, 0x0112, new IntPtr(0xF140), IntPtr.Zero); break;
            case SysCmd.EmptyRecycleBin: if (Confirm("Empty the Recycle Bin")) Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x4); break;
            case SysCmd.TaskManager: Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); break;
            case SysCmd.ToggleHiddenFiles: ToggleHidden(); break;
            case SysCmd.FindFiles: Process.Start(new ProcessStartInfo("explorer.exe", "search-ms:") { UseShellExecute = true }); break;
            case SysCmd.NewFolder:   // Explorer's own "new folder" keys, sent to the folder window in front
                Native.SetForegroundWindow(fg);
                Native.SendKeys(new[] { Native.Key(0x11, false), Native.Key(0x10, false), Native.Key(0x4E, false), Native.Key(0x4E, true), Native.Key(0x10, true), Native.Key(0x11, true) });
                break;
            case SysCmd.EjectDrive: Native.mciSendString("set cdaudio door open wait", null, 0, IntPtr.Zero); break;
            case SysCmd.CloseDrive: Native.mciSendString("set cdaudio door closed wait", null, 0, IntPtr.Zero); break;
            case SysCmd.QuickSearch: QuickSearch.Show(); break;
            case SysCmd.Unknown: Tray.Tip("LaunchPad", "This hotkey came from Keyboard Launchpad and still needs setting up.", true); break;
        }
    }

    /// <summary>Flip Explorer's "show hidden files and folders" and refresh open folder windows.</summary>
    static void ToggleHidden()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        bool showing = (k.GetValue("Hidden") as int?) == 1;
        k.SetValue("Hidden", showing ? 2 : 1, Microsoft.Win32.RegistryValueKind.DWord);
        Native.SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);  // tell Explorer settings changed
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            foreach (dynamic w in shell.Windows()) { try { w.Refresh(); } catch { } }
        }
        catch { }
        Tray.Tip("LaunchPad", showing ? "Hidden files are now hidden." : "Hidden files are now showing.", false);
    }

    static void ShellApp(Action<dynamic> act)
    {
        dynamic s = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
        act(s);
    }

    // ---------- media ----------
    public static void Media(MediaCmd m)
    {
        byte vk = m switch
        {
            MediaCmd.PlayPause => 0xB3, MediaCmd.Next => 0xB0, MediaCmd.Previous => 0xB1, MediaCmd.Stop => 0xB2,
            MediaCmd.VolumeUp => 0xAF, MediaCmd.VolumeDown => 0xAE, _ => 0xAD
        };
        Native.Tap(vk);
    }

    // ---------- mouse ----------
    static async Task Click(HotAction a)
    {
        await WaitForKeysUp();
        var back = Cursor.Position;
        Native.SetCursorPos(a.X, a.Y);
        await Task.Delay(30);
        (uint down, uint up) = a.Button switch { "Right" => (0x0008u, 0x0010u), "Middle" => (0x0020u, 0x0040u), _ => (0x0002u, 0x0004u) };
        int times = a.Button == "Double" ? 2 : 1;
        for (int i = 0; i < times; i++) { Native.mouse_event(down, 0, 0, 0, UIntPtr.Zero); Native.mouse_event(up, 0, 0, 0, UIntPtr.Zero); await Task.Delay(40); }
        Native.SetCursorPos(back.X, back.Y);
    }
}
