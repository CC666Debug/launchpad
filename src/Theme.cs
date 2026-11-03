using System.Text.Json;

namespace LaunchPad;

/// <summary>Colors: Markpad's palettes (Plain, Red, Blue, Green, Yellow) in light and dark, plus an accent.</summary>
static class Theme
{
    public static bool Dark;
    public static string Mode = "";      // "" = like Windows, "light", "dark"
    public static string Palette = "";   // "", red, blue, green, yellow
    public static string AccentHex = ""; // "" = the palette's own
    public static event Action Changed;

    // name, key, swatch color  (same as Markpad)
    public static readonly (string Name, string Key, string Swatch)[] Palettes =
        { ("Plain", "", "#8a8d93"), ("Red", "red", "#a4463f"), ("Blue", "blue", "#3d6a9e"), ("Green", "green", "#4a7a51"), ("Yellow", "yellow", "#93700f") };
    public static readonly (string Name, string Hex)[] Accents =
        { ("Theme's own", ""), ("Purple", "#8b5cf6"), ("Pink", "#e0457b"), ("Red", "#d64545"), ("Orange", "#e8730c"), ("Gold", "#c99a06"), ("Green", "#2f9e5a"), ("Teal", "#14a3a3") };

    record Pal(string Bg, string Panel, string Text, string Muted, string BorderC, string AccentC, string Hover);
    static readonly Dictionary<string, (Pal Light, Pal Dark)> Pals = new()
    {
        [""] = (new("#fbfbfa", "#f3f2ef", "#1f2328", "#6b6f76", "#d6d4ce", "#3b6fd8", "#e7e5df"),
                new("#1b1c1f", "#232428", "#e6e6e3", "#9a9ca3", "#3e4046", "#7aa2f7", "#2e3035")),
        ["red"] = (new("#fbf8f7", "#f4edeb", "#2a2020", "#76696a", "#dccdca", "#a4463f", "#ebdfdb"),
                   new("#1e1818", "#272020", "#ece2e1", "#a69796", "#4a3c3c", "#d98a83", "#342a2a")),
        ["blue"] = (new("#f7f9fb", "#edf1f5", "#1d2430", "#66707d", "#cdd5de", "#3d6a9e", "#e0e7ee"),
                    new("#171b21", "#1e232b", "#e2e7ee", "#939dab", "#3a4350", "#86a9d3", "#29303b")),
        ["green"] = (new("#f8faf7", "#eef2ec", "#1f271f", "#69736a", "#cfd8cc", "#4a7a51", "#e1e8dd"),
                     new("#181c18", "#1f251f", "#e3eae2", "#95a194", "#3b453b", "#8dba91", "#2b322b")),
        ["yellow"] = (new("#fbf9f3", "#f4f0e4", "#2a261b", "#756f60", "#dcd4bf", "#93700f", "#ebe5d3"),
                      new("#1d1b15", "#25221a", "#ece7da", "#a39d8c", "#463f2f", "#d4ae4f", "#312d23")),
    };

    static Pal P => (Pals.TryGetValue(Palette, out var p) ? p : Pals[""]) is var x && Dark ? x.Dark : x.Light;
    static Color C(string hex) => ColorTranslator.FromHtml(hex);

    public static Color Back => C(P.Panel);
    public static Color Box => C(P.Bg);
    public static Color Fore => C(P.Text);
    public static Color Dim => C(P.Muted);
    public static Color Btn => C(P.Hover);
    public static Color Border => C(P.BorderC);
    public static Color Accent => AccentHex != "" ? C(AccentHex) : C(P.AccentC);
    public static Color Bad => Dark ? Color.FromArgb(255, 110, 110) : Color.FromArgb(190, 20, 30);
    public static Color Warn => Dark ? Color.FromArgb(255, 190, 90) : Color.FromArgb(170, 100, 0);
    public static Color Good => Dark ? Color.FromArgb(110, 220, 140) : Color.FromArgb(20, 130, 60);

    static bool WindowsDark()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (k?.GetValue("AppsUseLightTheme") as int?) == 0;
        }
        catch { return false; }
    }

    class Saved { public string palette { get; set; } = ""; public string accent { get; set; } = ""; }

    public static void Load()
    {
        try { var m = File.ReadAllText(Paths.ThemeFile).Trim(); Mode = m is "light" or "dark" ? m : ""; } catch { Mode = ""; }
        try { var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(Paths.SettingsFile)); Palette = s.palette ?? ""; AccentHex = s.accent ?? ""; } catch { }
        Dark = Mode == "" ? WindowsDark() : Mode == "dark";
        // follow Windows when set to "like the system"
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (Mode != "") return;
            bool d = WindowsDark();
            if (d != Dark) { Dark = d; Changed?.Invoke(); }
        };
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(Paths.ThemeFile, Mode == "" ? "system" : Mode);
            File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(new Saved { palette = Palette, accent = AccentHex }));
        }
        catch { }
    }

    /// <summary>Tap on the sun/moon: flip light/dark.</summary>
    public static void Toggle() => SetMode(Dark ? "light" : "dark");

    public static void SetMode(string mode)
    {
        Mode = mode;
        Dark = mode == "" ? WindowsDark() : mode == "dark";
        Save(); Changed?.Invoke();
    }

    public static void SetPalette(string p) { Palette = p; AccentHex = ""; Save(); Changed?.Invoke(); }   // a new color brings its own accent
    public static void SetAccent(string hex) { AccentHex = hex ?? ""; Save(); Changed?.Invoke(); }

    /// <summary>Color a form and everything inside it.</summary>
    public static void Apply(Control root)
    {
        if (root is Form f && f.IsHandleCreated) Native.DarkTitle(f.Handle, Dark);
        Paint(root);
    }

    static void Paint(Control c)
    {
        switch (c)
        {
            case Button b:
                if (b.Tag as string == "tab") break; // tabs paint themselves
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Border; b.BackColor = Btn; b.ForeColor = Fore;
                break;
            case TextBox or ListBox or NumericUpDown:
                c.BackColor = Box; c.ForeColor = Fore;
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Box; cb.ForeColor = Fore;
                break;
            case ListView lv:
                lv.BackColor = Box; lv.ForeColor = Fore;
                if (lv.IsHandleCreated) Native.SetWindowTheme(lv.Handle, Dark ? "DarkMode_ItemsView" : "ItemsView", null);
                lv.Invalidate();
                break;
            default:
                if (c.Tag as string != "keepcolor") { c.BackColor = Back; c.ForeColor = c.Tag as string == "dim" ? Dim : Fore; }
                break;
        }
        foreach (Control k in c.Controls) Paint(k);
    }

    /// <summary>Owner-draw a ListView's column header so it follows the theme.</summary>
    public static void HookListView(ListView lv)
    {
        lv.OwnerDraw = true;
        lv.DrawColumnHeader += (s, e) =>
        {
            using var bg = new SolidBrush(Btn);
            e.Graphics.FillRectangle(bg, e.Bounds);
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), Fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        // rows: drawn by hand so selection uses the accent color with readable text
        lv.DrawItem += (s, e) => { if (lv.View != View.Details) e.DrawDefault = true; };
        lv.DrawSubItem += (s, e) =>
        {
            var g = e.Graphics;
            bool sel = e.Item.Selected;
            int col = e.ColumnIndex;
            var r = e.Bounds;
            if (col == 0 && lv.Columns.Count > 0) r.Width = lv.Columns[0].Width;
            var bg = sel ? Mix(Accent, Box, lv.Focused ? 0.45 : 0.30) : Box;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
            int x = r.X + 4;
            if (col == 0 && lv.SmallImageList != null)
            {
                var key = e.Item.ImageKey;
                int idx = !string.IsNullOrEmpty(key) ? lv.SmallImageList.Images.IndexOfKey(key) : e.Item.ImageIndex;
                var sz = lv.SmallImageList.ImageSize;
                if (idx >= 0) g.DrawImage(lv.SmallImageList.Images[idx], x, r.Y + (r.Height - sz.Height) / 2, sz.Width, sz.Height);
                x += sz.Width + 4;
            }
            var fg = sel ? Fore : (e.Item.UseItemStyleForSubItems ? e.Item.ForeColor : e.SubItem.ForeColor);
            if (sel && col == lv.Columns.Count - 1 && e.SubItem.ForeColor != Fore && !e.Item.UseItemStyleForSubItems) fg = e.SubItem.ForeColor; // keep status colors
            TextRenderer.DrawText(g, e.SubItem.Text, lv.Font, new Rectangle(x, r.Y, r.Right - x - 2, r.Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        lv.GotFocus += (s, e) => lv.Invalidate();
        lv.LostFocus += (s, e) => lv.Invalidate();
        lv.HandleCreated += (s, e) => Native.SetWindowTheme(lv.Handle, Dark ? "DarkMode_ItemsView" : "ItemsView", null);
    }

    /// <summary>Right-click menus that follow the theme (colors are read live, so switching light/dark just works).</summary>
    public static void Menu(ToolStrip m)
    {
        m.Renderer = new MenuRenderer();
        m.WhenOpening(() => { m.BackColor = Back; m.ForeColor = Fore; foreach (ToolStripItem i in m.Items) Recolor(i); });
    }

    static void WhenOpening(this ToolStrip m, Action a)
    {
        if (m is ToolStripDropDown dd) dd.Opening += (s, e) => a();
        a();
    }

    static void Recolor(ToolStripItem i)
    {
        i.ForeColor = Fore; i.BackColor = Back;
        if (i is ToolStripDropDownItem d) { d.DropDown.BackColor = Back; d.DropDown.Renderer = m_renderer; foreach (ToolStripItem k in d.DropDownItems) Recolor(k); }
    }
    static readonly MenuRenderer m_renderer = new();

    class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Accent;
        public override Color MenuItemSelected => Mix(Accent, Back, 0.35);
        public override Color MenuItemSelectedGradientBegin => Mix(Accent, Back, 0.35);
        public override Color MenuItemSelectedGradientEnd => Mix(Accent, Back, 0.35);
        public override Color MenuItemPressedGradientBegin => Mix(Accent, Back, 0.45);
        public override Color MenuItemPressedGradientEnd => Mix(Accent, Back, 0.45);
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Mix(Accent, Back, 0.30);
        public override Color CheckSelectedBackground => Mix(Accent, Back, 0.50);
        public override Color CheckPressedBackground => Mix(Accent, Back, 0.50);
    }

    class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Fore : Dim; base.OnRenderItemText(e); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Fore; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
            using (var b = new SolidBrush(Mix(Accent, Back, 0.35))) e.Graphics.FillRectangle(b, r);
            TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, r, Fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Blend a over b (amount 0..1 of a).</summary>
    public static Color Mix(Color a, Color b, double amount) => Color.FromArgb(
        (int)(a.R * amount + b.R * (1 - amount)), (int)(a.G * amount + b.G * (1 - amount)), (int)(a.B * amount + b.B * (1 - amount)));

    public static Button MakeButton(string text, int w = 80)
        => new() { Text = text, Width = w, Height = 34, Margin = new Padding(3) };
}
