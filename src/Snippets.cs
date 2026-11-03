using System.Runtime.InteropServices;
using System.Text;

namespace LaunchPad;

/// <summary>
/// Watches what you type (only the last few letters, kept in memory, never saved) and swaps a
/// snippet's short code for its text the moment the code is complete.
/// </summary>
sealed class SnippetEngine : IDisposable
{
    readonly HotkeyStore _store;
    readonly Native.LowLevelProc _proc;   // kept alive so Windows can call it
    IntPtr _hook;
    readonly StringBuilder _buf = new();
    IntPtr _lastWindow;
    readonly Control _ui;

    public SnippetEngine(HotkeyStore store, Control ui)
    {
        _store = store; _ui = ui;
        _proc = Hook;
        Refresh();
    }

    /// <summary>Hook the keyboard only while snippets are on and there are some.</summary>
    public void Refresh()
    {
        bool want = _store.SnippetsOn && _store.Snippets.Any(s => s.Enabled && s.Abbrev.Length > 0);
        if (want && _hook == IntPtr.Zero) _hook = Native.SetWindowsHookEx(13, _proc, Native.GetModuleHandle(null), 0); // WH_KEYBOARD_LL
        else if (!want && _hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _buf.Clear();
    }

    static bool Down(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    IntPtr Hook(int n, IntPtr w, IntPtr l)
    {
        if (n >= 0 && (w == 0x100 || w == 0x104))   // key down
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(l);
            if (k.dwExtraInfo != Native.OurMark) OnKey(k);
        }
        return Native.CallNextHookEx(_hook, n, w, l);
    }

    void OnKey(Native.KBDLLHOOKSTRUCT k)
    {
        var fg = Native.GetForegroundWindow();
        if (fg != _lastWindow) { _buf.Clear(); _lastWindow = fg; }
        uint vk = k.vkCode;
        if (vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x14 or 0x5B or 0x5C) return; // modifiers alone
        if (vk == 0x08) { if (_buf.Length > 0) _buf.Length--; return; }                                          // Backspace
        bool ctrl = Down(0x11), alt = Down(0x12);
        if ((ctrl && !alt) || Down(0x5B) || Down(0x5C)) { _buf.Clear(); return; }                                // shortcuts
        // what character does this key make (respecting Shift, Caps Lock, AltGr, keyboard layout)?
        var state = new byte[256];
        if (Down(0x10)) state[0x10] = 0x80;
        if (ctrl) state[0x11] = 0x80;
        if (alt) state[0x12] = 0x80;
        if ((Native.GetKeyState(0x14) & 1) != 0) state[0x14] = 0x01;
        Native.GetWindowThreadProcessId(fg, out _);
        var layout = Native.GetKeyboardLayout(GetThread(fg));
        var sb = new StringBuilder(8);
        int r = Native.ToUnicodeEx(vk, k.scanCode, state, sb, sb.Capacity, 0x4, layout);   // 0x4 = don't disturb dead keys
        if (r <= 0) { _buf.Clear(); return; }   // arrows, Enter, Esc, F-keys...
        var ch = sb.ToString(0, r);
        if (ch.Any(char.IsControl)) { _buf.Clear(); return; }
        _buf.Append(ch);
        if (_buf.Length > 64) _buf.Remove(0, _buf.Length - 64);
        var typed = _buf.ToString();
        var hit = _store.Snippets.Where(s => s.Enabled && s.Abbrev.Length > 0 && typed.EndsWith(s.Abbrev, StringComparison.Ordinal))
                                 .OrderByDescending(s => s.Abbrev.Length).FirstOrDefault();
        if (hit == null) return;
        _buf.Clear();
        // do it after this key has finished going through
        _ui.BeginInvoke(async () => { await Task.Delay(30); await Expand(hit.Abbrev.Length, hit.Text); });
    }

    static uint GetThread(IntPtr h) => GetWindowThreadProcessIdRaw(h, IntPtr.Zero);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] static extern uint GetWindowThreadProcessIdRaw(IntPtr h, IntPtr pid);

    public static string Fill(string text)
    {
        var now = DateTime.Now;
        string clip = "";
        if (text.Contains("{clipboard}")) try { clip = Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { }
        return text.Replace("{date}", now.ToString("d")).Replace("{time}", now.ToString("t"))
                   .Replace("{longdate}", now.ToString("D")).Replace("{clipboard}", clip);
    }

    /// <summary>Erase the code, type the text. Long text goes in through the clipboard instead.</summary>
    public static async Task Expand(int erase, string text)
    {
        text = Fill(text ?? "");
        var keys = new List<Native.INPUT>();
        for (int i = 0; i < erase; i++) { keys.Add(Native.Key(0x08, false)); keys.Add(Native.Key(0x08, true)); }
        Native.SendKeys(keys);
        await Task.Delay(20);
        if (text.Length > 300)
        {
            string old = null;
            try { if (Clipboard.ContainsText()) old = Clipboard.GetText(); } catch { }
            Clipboard.SetText(text);
            Native.SendKeys(new[] { Native.Key(0x11, false), Native.Key(0x56, false), Native.Key(0x56, true), Native.Key(0x11, true) });
            await Task.Delay(400);
            try { if (old != null) Clipboard.SetText(old); } catch { }
            return;
        }
        keys.Clear();
        foreach (var c in text.Replace("\r\n", "\n"))
        {
            if (c == '\n') { keys.Add(Native.Key(0x0D, false)); keys.Add(Native.Key(0x0D, true)); }
            else if (c == '\t') { keys.Add(Native.Key(0x09, false)); keys.Add(Native.Key(0x09, true)); }
            else { keys.Add(Native.Uni(c, false)); keys.Add(Native.Uni(c, true)); }
        }
        Native.SendKeys(keys);
    }

    public void Dispose() { if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
}

class SnippetsPage : UserControl
{
    readonly HotkeyStore _store;
    readonly Action _changed;
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true };
    readonly CheckBox _on = new() { Text = "Snippets on", AutoSize = true, Margin = new Padding(12, 12, 3, 3) };
    readonly TextBox _search = new() { Width = 170, PlaceholderText = "Search", Margin = new Padding(3, 8, 3, 3) };

    public SnippetsPage(HotkeyStore store, Action changed)
    {
        _store = store; _changed = changed; Dock = DockStyle.Fill;
        _list.Columns.Add("Type this", 140); _list.Columns.Add("Name", 200); _list.Columns.Add("Turns into", 500);
        Theme.HookListView(_list);
        _list.ClientSizeChanged += (s, e) => _list.Columns[2].Width = Math.Max(200, _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[1].Width);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        var add = Theme.MakeButton("Add", 60); var edit = Theme.MakeButton("Edit", 60); var del = Theme.MakeButton("Delete", 70);
        var fromHk = Theme.MakeButton("From paste hotkeys...", 170);
        bar.Controls.AddRange(new Control[] { _search, add, edit, del, fromHk, _on });
        var hint = new Label { Text = "Type the short code in any program and it turns into the text. Start codes with ; or // so normal typing never triggers them.", Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(8, 4, 8, 0), Tag = "dim" };
        Controls.Add(_list); Controls.Add(bar); Controls.Add(hint);
        new ToolTip().SetToolTip(fromHk, "Make snippets out of your \"Paste saved text\" hotkeys");

        _on.Checked = store.SnippetsOn;
        _on.CheckedChanged += (s, e) => { _store.SnippetsOn = _on.Checked; _changed(); };
        add.Click += (s, e) => { var sn = new Snippet(); if (Edit(sn)) { _store.Snippets.Add(sn); _changed(); Fill(); } };
        edit.Click += (s, e) => EditSel();
        _list.DoubleClick += (s, e) => EditSel();
        del.Click += (s, e) => DeleteSel();
        _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteSel(); };
        _search.TextChanged += (s, e) => Fill();
        fromHk.Click += (s, e) => FromHotkeys();
        Fill();
    }

    public void Fill()
    {
        _list.BeginUpdate(); _list.Items.Clear();
        var q = _search.Text.Trim();
        foreach (var s in _store.Snippets)
        {
            if (q != "" && !(s.Abbrev + " " + s.Name + " " + s.Text).Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var it = new ListViewItem(s.Abbrev) { Tag = s.Id, ForeColor = s.Enabled ? Theme.Fore : Theme.Dim };
            it.SubItems.Add(s.Name);
            var one = s.Text.Replace("\r", "").Replace("\n", " ⏎ ");
            it.SubItems.Add(one.Length > 120 ? one[..120] + "…" : one);
            _list.Items.Add(it);
        }
        _list.EndUpdate();
    }

    bool Edit(Snippet s) { using var d = new SnippetDialog(s, _store); return d.ShowDialog(this) == DialogResult.OK; }

    void EditSel()
    {
        if (_list.SelectedItems.Count == 0) return;
        var s = _store.Snippets.First(x => x.Id == (string)_list.SelectedItems[0].Tag);
        var copy = System.Text.Json.JsonSerializer.Deserialize<Snippet>(System.Text.Json.JsonSerializer.Serialize(s));
        if (Edit(copy)) { _store.Snippets[_store.Snippets.IndexOf(s)] = copy; _changed(); Fill(); }
    }

    void DeleteSel()
    {
        var ids = _list.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList();
        if (ids.Count == 0) return;
        if (MessageBox.Show(this, ids.Count == 1 ? "Delete this snippet?" : $"Delete these {ids.Count} snippets?", "LaunchPad",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _store.Snippets.RemoveAll(s => ids.Contains(s.Id)); _changed(); Fill();
    }

    void FromHotkeys()
    {
        var paste = _store.Hotkeys.Where(h => h.Actions.Count == 1 && h.Actions[0].Kind == ActionKind.PasteText).ToList();
        var fresh = paste.Where(h => !_store.Snippets.Any(s => s.Text == h.Actions[0].Text)).ToList();
        if (fresh.Count == 0) { MessageBox.Show(this, "All your paste-text hotkeys already have snippets.", "LaunchPad"); return; }
        if (MessageBox.Show(this, $"Make {fresh.Count} snippets from your paste-text hotkeys?\n\nEach gets a code like ;name (for \"@obi\" that's ;obi). The hotkeys stay too. You can change the codes after.",
                "LaunchPad", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        foreach (var h in fresh)
        {
            var code = ";" + new string(h.DisplayName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (code == ";") code = ";snip" + (_store.Snippets.Count + 1);
            while (_store.Snippets.Any(s => s.Abbrev == code)) code += "1";
            _store.Snippets.Add(new Snippet { Abbrev = code, Name = h.DisplayName, Text = h.Actions[0].Text });
        }
        _changed(); Fill();
    }
}

class SnippetDialog : ThemedDialog
{
    readonly Snippet _s;
    readonly HotkeyStore _store;
    readonly TextBox _abbr = new() { Width = 180 }, _name = new() { Dock = DockStyle.Fill };
    readonly TextBox _text = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical };
    readonly CheckBox _on = new() { Text = "On", AutoSize = true, Checked = true, Margin = new Padding(16, 7, 3, 3) };

    public SnippetDialog(Snippet s, HotkeyStore store) : base(s.Abbrev == "" ? "New snippet" : "Edit snippet", 600, 440)
    {
        _s = s; _store = store;
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(_abbr); row.Controls.Add(_on);
        t.Controls.Add(L("Type this"), 0, 0); t.Controls.Add(row, 1, 0);
        t.Controls.Add(L("Name"), 0, 1); t.Controls.Add(_name, 1, 1);
        t.Controls.Add(new Label(), 0, 2); t.Controls.Add(new Label { Text = "Turns into:", AutoSize = true, Tag = "dim", Margin = new Padding(3, 8, 3, 2) }, 1, 2);
        t.Controls.Add(_text, 0, 3); t.SetColumnSpan(_text, 2);
        t.Controls.Add(new Label { Text = "You can use {date}, {time}, {longdate} and {clipboard} in the text.", AutoSize = true, Tag = "dim", Margin = new Padding(3, 6, 3, 3) }, 0, 4);
        t.SetColumnSpan(t.GetControlFromPosition(0, 4), 2);
        Controls.Add(t); t.BringToFront();
        _abbr.Text = s.Abbrev == "" ? ";" : s.Abbrev; _name.Text = s.Name; _text.Text = (s.Text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"); _on.Checked = s.Enabled;
        Shown += (o, e) => { _abbr.Focus(); _abbr.SelectionStart = _abbr.Text.Length; };
    }

    protected override bool Check()
    {
        var a = _abbr.Text.Trim();
        if (a.Length < 2) { MessageBox.Show(this, "Make the code at least 2 characters, like ;sig", "LaunchPad"); return false; }
        if (a.Any(char.IsWhiteSpace)) { MessageBox.Show(this, "No spaces in the code.", "LaunchPad"); return false; }
        if (_store.Snippets.Any(x => x.Id != _s.Id && x.Abbrev == a)) { MessageBox.Show(this, "Another snippet already uses that code.", "LaunchPad"); return false; }
        if (_text.Text == "") { MessageBox.Show(this, "Type what it should turn into.", "LaunchPad"); return false; }
        _s.Abbrev = a; _s.Name = _name.Text.Trim(); _s.Text = _text.Text.Replace("\r\n", "\n"); _s.Enabled = _on.Checked;
        return true;
    }
}
