using System.Runtime.InteropServices;
using System.Windows;

namespace SimpleViewer;

/// <summary>Win32 helpers; all coordinates are physical screen pixels (the process is PerMonitorV2 aware).</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT ptMin, ptMax; public RECT rcNormal; }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);

    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

    static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    public static Point CursorPos() => GetCursorPos(out var p) ? new Point(p.X, p.Y) : default;

    static Rect MonitorRect(IntPtr mon)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(mon, ref mi) ? ToRect(mi.rcMonitor) : Rect.Empty;
    }

    /// <summary>Full rectangle (incl. taskbar) of the monitor that contains most of the window.</summary>
    public static Rect MonitorOfWindow(IntPtr hwnd) => MonitorRect(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    /// <summary>Full rectangle of the monitor that contains most of the given screen rectangle.</summary>
    public static Rect MonitorOfRect(Rect r)
    {
        var rc = new RECT { Left = (int)r.Left, Top = (int)r.Top, Right = (int)r.Right, Bottom = (int)r.Bottom };
        return MonitorRect(MonitorFromRect(ref rc, MONITOR_DEFAULTTONEAREST));
    }

    /// <summary>Bounding box of all monitors (may contain areas without a monitor).</summary>
    public static Rect VirtualScreen() =>
        new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    public static Rect WindowRect(IntPtr hwnd) => GetWindowRect(hwnd, out var r) ? ToRect(r) : Rect.Empty;

    public static void Place(IntPtr hwnd, Rect r) =>
        SetWindowPos(hwnd, IntPtr.Zero, (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

    public static WINDOWPLACEMENT GetPlacement(IntPtr hwnd)
    {
        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        GetWindowPlacement(hwnd, ref wp);
        return wp;
    }

    public static void SetPlacement(IntPtr hwnd, WINDOWPLACEMENT wp) => SetWindowPlacement(hwnd, ref wp);
}
