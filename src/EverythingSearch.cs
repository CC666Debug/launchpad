using System.Runtime.InteropServices;

namespace LaunchPad;

/// <summary>
/// Asks the Everything app (voidtools, free) for files, using the message channel Everything itself offers
/// to other programs (WM_COPYDATA "IPC"). Nothing extra to install. Works with Everything 1.4 and 1.5.
/// </summary>
sealed class EverythingSearch : NativeWindow
{
    const int WM_COPYDATA = 0x004A;
    const int QueryW = 2;          // EVERYTHING_IPC_COPYDATAQUERYW
    const int ReplyId = 0x4C50;    // our tag on the answer
    TaskCompletionSource<List<(string path, bool folder)>> _wait;

    [StructLayout(LayoutKind.Sequential)] struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref COPYDATASTRUCT l);

    static EverythingSearch _one;
    public static EverythingSearch Instance => _one ??= new EverythingSearch();
    EverythingSearch() { CreateHandle(new CreateParams()); }

    static IntPtr Target()
    {
        var h = FindWindow("EVERYTHING_TASKBAR_NOTIFICATION", null);
        if (h == IntPtr.Zero) h = FindWindow("EVERYTHING_TASKBAR_NOTIFICATION_(1.5a)", null);
        return h;
    }

    public static bool Running => Target() != IntPtr.Zero;

    /// <summary>Up to max results for the text, or empty if Everything isn't running / doesn't answer quickly.</summary>
    public async Task<List<(string path, bool folder)>> Find(string text, int max)
    {
        var ev = Target();
        if (ev == IntPtr.Zero || string.IsNullOrWhiteSpace(text)) return new();
        _wait?.TrySetResult(new());   // an older question that never got answered
        _wait = new TaskCompletionSource<List<(string, bool)>>();
        var mine = _wait;

        // EVERYTHING_IPC_QUERYW: reply_hwnd, reply_copydata_message, search_flags, offset, max_results, search_string\0
        var chars = (text + "\0").ToCharArray();
        int size = 5 * 4 + chars.Length * 2;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(buf, 0, (int)(long)Handle);
            Marshal.WriteInt32(buf, 4, ReplyId);
            Marshal.WriteInt32(buf, 8, 0);
            Marshal.WriteInt32(buf, 12, 0);
            Marshal.WriteInt32(buf, 16, max);
            Marshal.Copy(chars, 0, buf + 20, chars.Length);
            var cds = new COPYDATASTRUCT { dwData = new IntPtr(QueryW), cbData = size, lpData = buf };
            if (SendMessage(ev, WM_COPYDATA, Handle, ref cds) == IntPtr.Zero) return new();
        }
        finally { Marshal.FreeHGlobal(buf); }
        var done = await Task.WhenAny(mine.Task, Task.Delay(800));
        return done == mine.Task ? mine.Task.Result : new();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_COPYDATA)
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(m.LParam);
            if ((long)cds.dwData == ReplyId)
            {
                // EVERYTHING_IPC_LISTW: 7 DWORDs (totfolders, totfiles, totitems, numfolders, numfiles, numitems, offset), then items
                var list = new List<(string, bool)>();
                var p = cds.lpData;
                int n = Marshal.ReadInt32(p, 20);
                for (int i = 0; i < n; i++)
                {
                    int at = 28 + i * 12;   // EVERYTHING_IPC_ITEMW: flags, filename_offset, path_offset
                    int flags = Marshal.ReadInt32(p, at);
                    var name = Marshal.PtrToStringUni(p + Marshal.ReadInt32(p, at + 4));
                    var dir = Marshal.PtrToStringUni(p + Marshal.ReadInt32(p, at + 8));
                    bool folder = (flags & 1) != 0;
                    list.Add((string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name), folder || (flags & 2) != 0));
                }
                _wait?.TrySetResult(list);
                m.Result = new IntPtr(1);
                return;
            }
        }
        base.WndProc(ref m);
    }
}
