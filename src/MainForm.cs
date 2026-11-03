namespace LaunchPad;

class MainForm : Form
{
    readonly HotkeyStore _store = HotkeyStore.Load();
    readonly HotkeyManager _mgr = new();
    readonly Panel _content = new() { Dock = DockStyle.Fill };
    readonly Button _tabL = new() { Text = "Launchers", Tag = "tab" }, _tabH = new() { Text = "Hotkeys", Tag = "tab" };
    readonly Button _theme = new() { Width = 44, Height = 36, Font = new Font("Segoe UI Symbol", 14), Margin = new Padding(3, 6, 8, 3) };
    readonly LaunchersPage _launchers;
    readonly HotkeysPage _hotkeys;
    readonly NotifyIcon _tray = new();
    readonly ToolStripMenuItem _miPause = new("Pause all hotkeys"), _miStartup = new("Start with Windows");
    readonly System.Windows.Forms.Timer _retry = new() { Interval = 10000 };
    bool _quitting, _toldTray;

    public MainForm(bool startHidden)
    {
        Text = "LaunchPad"; Icon = Icons.AppIcon(); Font = new Font("Segoe UI", 10);
        ClientSize = new Size(980, 600); MinimumSize = new Size(760, 420); StartPosition = FormStartPosition.CenterScreen;
        Runner.Owner = this;

        _mgr.Apply(_store);
        _launchers = new LaunchersPage();
        _hotkeys = new HotkeysPage(_store, _mgr, Changed);
        _hotkeys.Restart = () => { Program.RestartRequested = true; _quitting = true; Close(); };

        // header: tabs on the left, sun/moon on the right
        var header = new Panel { Dock = DockStyle.Top, Height = 48 };
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, Padding = new Padding(6, 4, 0, 0), WrapContents = false };
        // tab order is yours: Hotkeys first unless you dragged them around
        var order = new[] { _tabH, _tabL };
        try { if (File.ReadAllText(Paths.TabsFile).Trim() == "Launchers,Hotkeys") order = new[] { _tabL, _tabH }; } catch { }
        foreach (var b in order)
        {
            b.Width = 120; b.Height = 40; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.Margin = new Padding(2, 0, 2, 0);
            b.Font = new Font("Segoe UI Semibold", 11); b.Paint += PaintTab; tabs.Controls.Add(b);
            DragTab(b, tabs);
        }
        new ToolTip().SetToolTip(_tabH, "Drag a tab left or right to reorder");
        new ToolTip().SetToolTip(_tabL, "Drag a tab left or right to reorder");
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        right.Controls.Add(_theme);
        header.Controls.Add(tabs); header.Controls.Add(right);
        Controls.Add(_content); Controls.Add(header);
        _tabL.Click += (s, e) => { if (!_dragged) ShowPage(_launchers); };
        _tabH.Click += (s, e) => { if (!_dragged) ShowPage(_hotkeys); };
        // tap: light/dark. Hold it (or right-click) for colors and accent, like Markpad
        var hold = new System.Windows.Forms.Timer { Interval = 500 };
        bool held = false;
        hold.Tick += (s, e) => { hold.Stop(); held = true; OpenThemes(); };
        _theme.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right) { held = true; OpenThemes(); return; }
            held = false; hold.Start();
        };
        _theme.MouseUp += (s, e) => hold.Stop();
        _theme.MouseLeave += (s, e) => hold.Stop();
        _theme.Click += (s, e) => { if (held) { held = false; return; } Theme.Toggle(); };
        new ToolTip().SetToolTip(_theme, "Light/dark (hold for theme colors)");
        Theme.Changed += ApplyTheme;
        ShowPage(order[0] == _tabH ? _hotkeys : _launchers);

        // tray
        _tray.Icon = Icon; _tray.Text = "LaunchPad"; _tray.Visible = true;
        Tray.Icon = _tray;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open LaunchPad", null, (s, e) => ShowMe());
        menu.Items.Add("Hotkey cheat sheet", null, (s, e) => CheatSheet.Open(_store));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miPause); menu.Items.Add(_miStartup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => { _quitting = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMe(); };
        _miPause.Click += (s, e) => { _store.Paused = !_store.Paused; Changed(); _hotkeys.Refill(); };
        _miStartup.Click += (s, e) => { Startup.Enabled = !Startup.Enabled; _miStartup.Checked = Startup.Enabled; };
        menu.Opening += (s, e) => { _miPause.Checked = _store.Paused; _miStartup.Checked = Startup.Enabled; };

        // keys another app was holding (e.g. Keyboard Launchpad): try again now and then
        _retry.Tick += (s, e) => { if (_mgr.AnyBlocked) { _mgr.Apply(_store); _hotkeys.Refill(); } };
        _retry.Start();

        if (startHidden) { WindowState = FormWindowState.Minimized; ShowInTaskbar = false; _toldTray = true; }
        Load += (s, e) => { if (startHidden) Hide(); };
        HandleCreated += (s, e) => ApplyTheme();
    }

    bool _dragged;
    ThemeDialog _themeDlg;

    public void OpenThemes()
    {
        if (_themeDlg is { IsDisposed: false }) { _themeDlg.Activate(); return; }
        _themeDlg = new ThemeDialog { StartPosition = FormStartPosition.Manual };
        // pin it just under the sun/moon button, kept on the same screen
        var btn = _theme.PointToScreen(new Point(_theme.Width, _theme.Height + 4));
        var area = Screen.FromControl(this).WorkingArea;
        int x = Math.Clamp(btn.X - _themeDlg.Width, area.Left, area.Right - _themeDlg.Width);
        int y = Math.Clamp(btn.Y, area.Top, area.Bottom - _themeDlg.Height);
        _themeDlg.Location = new Point(x, y);
        _themeDlg.Show(this);
    }

    /// <summary>Hold a tab and slide it past the other one to swap them.</summary>
    void DragTab(Button b, FlowLayoutPanel tabs)
    {
        Point start = Point.Empty; bool down = false;
        b.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { down = true; _dragged = false; start = e.Location; } };
        b.MouseMove += (s, e) =>
        {
            if (!down || Math.Abs(e.X - start.X) < 8) return;
            _dragged = true;
            var p = tabs.PointToClient(b.PointToScreen(e.Location));
            foreach (Control other in tabs.Controls)
                if (other != b && other.Bounds.Contains(new Point(p.X, other.Top + 5)))
                {
                    tabs.Controls.SetChildIndex(b, tabs.Controls.GetChildIndex(other));
                    break;
                }
        };
        b.MouseUp += (s, e) =>
        {
            down = false;
            if (!_dragged) return;
            var names = tabs.Controls.Cast<Control>().Select(c => c.Text);
            try { Directory.CreateDirectory(Paths.Root); File.WriteAllText(Paths.TabsFile, string.Join(",", names)); } catch { }
        };
    }

    void Changed()
    {
        try { _store.Save(); } catch (Exception e) { MessageBox.Show("Couldn't save hotkeys: " + e.Message, "LaunchPad"); }
        _mgr.Apply(_store);
    }

    void ShowPage(Control page)
    {
        _content.Controls.Clear();
        _content.Controls.Add(page);
        _tabL.Invalidate(); _tabH.Invalidate();
        Theme.Apply(page);
    }

    void PaintTab(object sender, PaintEventArgs e)
    {
        var b = (Button)sender;
        bool on = _content.Controls.Count > 0 && ((b == _tabL && _content.Controls[0] == _launchers) || (b == _tabH && _content.Controls[0] == _hotkeys));
        e.Graphics.Clear(Theme.Back);
        TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, on ? Theme.Fore : Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (on) using (var br = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(br, 14, b.Height - 4, b.Width - 28, 3);
    }

    void ApplyTheme()
    {
        _theme.Text = Theme.Dark ? char.ConvertFromUtf32(0x2600) : char.ConvertFromUtf32(0x1F319); // sun in dark, moon in light
        Theme.Apply(this);
        Theme.Apply(_launchers); Theme.Apply(_hotkeys);
        _hotkeys.Refill();
        _tabL.Invalidate(); _tabH.Invalidate();
    }

    public void ShowMe()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // closing the window keeps hotkeys running in the tray
        if (!_quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true; Hide();
            if (!_toldTray) { Tray.Tip("LaunchPad is still running", "Your hotkeys keep working. Right-click the tray icon to exit.", false); _toldTray = true; }
            return;
        }
        _tray.Visible = false; _tray.Dispose();
        _mgr.Dispose();
        base.OnFormClosing(e);
    }

    public HotkeysPage Hotkeys => _hotkeys;
    public HotkeyStore Store => _store;
}
