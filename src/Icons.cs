using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LaunchPad;

static class Icons
{
    public const string Fallback = @"%SystemRoot%\System32\shell32.dll,3";

    /// <summary>"calc.exe" -> full path (System32 / PATH), so it gets a real icon.</summary>
    public static string Resolve(string p)
    {
        p = Environment.ExpandEnvironmentVariables(p ?? "").Trim().Trim('"');
        if (p == "" || IsUrl(p) || Path.IsPathRooted(p) || p.StartsWith("::")) return p;
        var dirs = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var d in dirs)
        {
            try
            {
                var f = Path.Combine(d, p);
                if (File.Exists(f)) return f;
                if (!Path.HasExtension(p) && File.Exists(f + ".exe")) return f + ".exe";
            }
            catch { }
        }
        return p;
    }

    public static bool IsUrl(string p) => Regex.IsMatch(p ?? "", @"^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase);

    /// <summary>The icon Explorer shows for a file/shortcut/folder (32px); browser icon for web links.</summary>
    public static Bitmap Shell32(string p)
    {
        var info = new Native.SHFILEINFO();
        uint flags = 0x100; // SHGFI_ICON | LARGEICON
        if (IsUrl(p)) { p = ".html"; flags |= 0x10; }
        Native.SHGetFileInfo(p, 0x80, ref info, (uint)Marshal.SizeOf(info), flags);
        if (info.hIcon == IntPtr.Zero) return null;
        using var ic = Icon.FromHandle(info.hIcon);
        var bmp = ic.ToBitmap();
        Native.DestroyIcon(info.hIcon);
        return bmp;
    }

    static (string file, int idx) Split(string loc)
    {
        int k = loc.LastIndexOf(',');
        if (k < 0) return (Environment.ExpandEnvironmentVariables(loc), 0);
        int.TryParse(loc[(k + 1)..], out int idx);
        return (Environment.ExpandEnvironmentVariables(loc[..k]), idx);
    }

    /// <summary>"file,index" -> bitmap at the requested size.</summary>
    public static Bitmap FromLocation(string loc, int size)
    {
        if (string.IsNullOrEmpty(loc)) return null;
        var (file, idx) = Split(loc);
        var h = new IntPtr[1]; var ids = new int[1];
        if (Native.PrivateExtractIcons(file, idx, size, size, h, ids, 1, 0) < 1 || h[0] == IntPtr.Zero) return null;
        using var ic = Icon.FromHandle(h[0]);
        var bmp = ic.ToBitmap();
        Native.DestroyIcon(h[0]);
        return bmp;
    }

    /// <summary>Best "file,index" icon for one item, or null.</summary>
    public static string DefaultLocation(string p)
    {
        try
        {
            if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            {
                var s = Shortcut.Read(p);
                if (!string.IsNullOrEmpty(s.IconLocation) && !s.IconLocation.StartsWith(",")) return s.IconLocation;
                if (s.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return s.Target + ",0";
                return SaveJumbo(p); // Store/installer shortcuts (Firefox, Signal...) have no icon file
            }
            if (Regex.IsMatch(p, @"\.(exe|ico|dll)$", RegexOptions.IgnoreCase) && File.Exists(p)) return p + ",0";
        }
        catch { }
        return null;
    }

    /// <summary>Grab Explorer's 256px icon for p, save as .ico in the cache, return "file,0".</summary>
    public static string SaveJumbo(string p)
    {
        try
        {
            Directory.CreateDirectory(Paths.Icons);
            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(p.ToLowerInvariant()))).ToLowerInvariant();
            var outFile = Path.Combine(Paths.Icons, hash + ".ico");
            if (File.Exists(outFile)) return outFile + ",0";
            var info = new Native.SHFILEINFO();
            Native.SHGetFileInfo(p, 0, ref info, (uint)Marshal.SizeOf(info), 0x4000); // SYSICONINDEX
            var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
            if (Native.SHGetImageList(4, ref iid, out IntPtr himl) != 0) return null; // 4 = 256px
            var h = Native.ImageList_GetIcon(himl, info.iIcon, 1);
            if (h == IntPtr.Zero) return null;
            using (var ic = Icon.FromHandle(h))
            using (var bmp = ic.ToBitmap()) WriteIco(bmp, outFile);
            Native.DestroyIcon(h);
            return outFile + ",0";
        }
        catch { return null; }
    }

    /// <summary>Multi-size .ico (PNG inside) so it looks sharp at every size.</summary>
    public static void WriteIco(Bitmap src, string outFile)
    {
        int[] sizes = { 256, 64, 48, 32, 24, 16 };
        var pngs = new List<byte[]>();
        foreach (var s in sizes)
        {
            using var b = new Bitmap(s, s);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, s, s);
            }
            using var ms = new MemoryStream();
            b.Save(ms, ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }
        using var fs = File.Create(outFile);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)sizes.Length);
        int off = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte d = (byte)(sizes[i] % 256);
            bw.Write(d); bw.Write(d); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((ushort)1); bw.Write((ushort)32); bw.Write((uint)pngs[i].Length); bw.Write((uint)off);
            off += pngs[i].Length;
        }
        foreach (var p in pngs) bw.Write(p);
    }

    /// <summary>"file,index" -> sharp .ico file.</summary>
    public static bool Export(string loc, string outFile)
    {
        using var bmp = FromLocation(loc, 256);
        if (bmp == null) return false;
        WriteIco(bmp, outFile);
        return true;
    }

    public static Icon AppIcon()
    {
        using var s = typeof(Icons).Assembly.GetManifestResourceStream("LaunchPad.ico");
        return s == null ? SystemIcons.Application : new Icon(s);
    }
}
