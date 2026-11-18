using System.Runtime.InteropServices;

namespace LaunchPad;

/// <summary>
/// Windows' "app ID" of a window. Chrome/Brave/Edge web apps (PWAs) run inside the browser's own process,
/// so the program + its command line only say "chrome.exe"; the app ID ("Brave._crx_...") says which web app it is,
/// and shell:AppsFolder\&lt;id&gt; opens exactly that app.
/// </summary>
static class AppIds
{
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint i, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [DllImport("shell32.dll")]
    static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PROPVARIANT pv);

    static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome.exe", "brave.exe", "msedge.exe", "vivaldi.exe", "opera.exe" };

    public static bool IsBrowser(string exe) => Browsers.Contains(Path.GetFileName(exe ?? ""));

    /// <summary>The window's app ID, or "" (most normal windows don't set one).</summary>
    public static string Of(IntPtr hwnd)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store == null) return "";
            try
            {
                var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 }; // System.AppUserModel.ID
                if (store.GetValue(ref key, out var v) != 0) return "";
                try { return v.vt == 31 && v.p != IntPtr.Zero ? Marshal.PtrToStringUni(v.p) ?? "" : ""; }   // 31 = VT_LPWSTR
                finally { PropVariantClear(ref v); }
            }
            finally { Marshal.ReleaseComObject(store); }
        }
        catch { return ""; }
    }

    /// <summary>The app ID for windows whose program alone can't reopen them: browser web apps ("Brave._crx_...")
    /// and Windows Store apps (hosted by ApplicationFrameHost.exe, e.g. "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"). Else "".</summary>
    public static string Reopenable(IntPtr hwnd, string exe)
    {
        if (IsBrowser(exe))
        {
            var id = Of(hwnd);
            return id.Contains("_crx_", StringComparison.OrdinalIgnoreCase) ? id : "";
        }
        if (Path.GetFileName(exe ?? "").Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var id = Of(hwnd);
            return id.Contains('!') ? id : "";
        }
        return "";
    }
}
