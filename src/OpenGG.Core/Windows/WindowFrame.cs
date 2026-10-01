using System.Runtime.InteropServices;

namespace OneRGB.Windows;

/// <summary>Use the current monitor's working area, including taskbars on any edge.</summary>
public static partial class WindowFrame
{
    public static void Round(nint window, int diameter, bool maximized)
    {
        if (maximized) { SetWindowRgn(window, 0, 1); return; }
        if (GetWindowRect(window, out var bounds) == 0) return;
        var region = CreateRoundRectRgn(0, 0, bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1, diameter, diameter);
        if (region != 0 && SetWindowRgn(window, region, 1) == 0) { DeleteObject(region); }
    }

    public static (int Left, int Top, int Right, int Bottom) WorkArea(nint window)
    {
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfoW(MonitorFromWindow(window, 2), ref monitor) == 0) return default;
        return (monitor.Work.Left, monitor.Work.Top, monitor.Work.Right, monitor.Work.Bottom);
    }

    [LibraryImport("gdi32.dll")] private static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [LibraryImport("user32.dll")] private static partial int SetWindowRgn(nint window, nint region, int redraw);
    [LibraryImport("gdi32.dll")] private static partial int DeleteObject(nint handle);
    public static int CornerHit(nint window, nint screenPoint, int reach)
    {
        if (GetWindowRect(window, out var bounds) == 0) return 0;
        var x = (short)((long)screenPoint & 0xffff); var y = (short)(((long)screenPoint >> 16) & 0xffff);
        var left = x >= bounds.Left && x < bounds.Left + reach;
        var right = x < bounds.Right && x >= bounds.Right - reach;
        var top = y >= bounds.Top && y < bounds.Top + reach;
        var bottom = y < bounds.Bottom && y >= bounds.Bottom - reach;
        return top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : 0;
    }
    [LibraryImport("user32.dll")] private static partial int GetWindowRect(nint window, out Rect bounds);

    public static unsafe void ConstrainMaximized(nint window, nint minMaxInfo)
    {
        if (minMaxInfo == 0) return;
        var monitor = new MonitorInfo { Size = sizeof(MonitorInfo) };
        if (GetMonitorInfoW(MonitorFromWindow(window, 2), ref monitor) == 0) return;
        var limits = (MinMaxInfo*)minMaxInfo;
        limits->MaxPosition = new Point { X = monitor.Work.Left - monitor.Bounds.Left, Y = monitor.Work.Top - monitor.Bounds.Top };
        limits->MaxSize = new Point { X = monitor.Work.Right - monitor.Work.Left, Y = monitor.Work.Bottom - monitor.Work.Top };
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Bounds, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetMonitorInfoW(nint monitor, ref MonitorInfo info);
}
