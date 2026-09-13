using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Iris.Interop;

/// <summary>
/// The few Win32 calls needed to make a plain WPF window look native on Windows 11
/// and to go genuinely fullscreen (covering the taskbar) on the right monitor.
/// </summary>
internal static class Native
{
    // --- DWM window attributes ---
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwaWindowCornerPreference = 33;

    private const int CornerPreferenceRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MonitorInfo info);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    private const uint MonitorDefaultToNearest = 2;

    /// <summary>
    /// Paints the caption, border and title text to match the app background so the
    /// window reads as one dark surface instead of a light title bar over black video.
    /// </summary>
    public static void ApplyDarkChrome(Window window, System.Windows.Media.Color background)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var on = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));

        // DWM takes colours as 0x00BBGGRR.
        var colorRef = background.R | (background.G << 8) | (background.B << 16);
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref colorRef, sizeof(int));
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref colorRef, sizeof(int));

        var text = 0xF4F3F3;
        DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));

        var corner = CornerPreferenceRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
    }

    /// <summary>
    /// Full pixel bounds (not the work area) of the monitor the window currently sits on,
    /// converted to WPF device-independent units.
    /// </summary>
    public static Rect2 GetMonitorBounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info))
        {
            return new Rect2(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        var source = PresentationSource.FromVisual(window);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        if (scaleX <= 0) scaleX = 1.0;
        if (scaleY <= 0) scaleY = 1.0;

        var r = info.rcMonitor;
        return new Rect2(
            r.Left / scaleX,
            r.Top / scaleY,
            (r.Right - r.Left) / scaleX,
            (r.Bottom - r.Top) / scaleY);
    }

    // --- Client-area background ---
    private const int GclpHbrBackground = -10;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int colorRef);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetClassLongW")]
    private static extern int SetClassLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr param);

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr param);

    private static IntPtr _backgroundBrush;

    /// <summary>
    /// Replaces the window class's background brush so Windows erases to Iris's dark
    /// ground instead of white. Without this the client area is painted white for the
    /// ~100ms between the window being shown and anything rendering into it — a visible
    /// flash on every launch, worst on a maximised window.
    /// </summary>
    public static void SetClientBackground(IntPtr hwnd, System.Windows.Media.Color color, bool includeChildren)
    {
        if (hwnd == IntPtr.Zero) return;

        if (_backgroundBrush == IntPtr.Zero)
        {
            // 0x00BBGGRR, and kept alive for the process lifetime: it stays referenced
            // by the window class, so it must not be deleted.
            _backgroundBrush = CreateSolidBrush(color.R | (color.G << 8) | (color.B << 16));
        }

        Apply(hwnd);

        if (includeChildren)
        {
            EnumChildWindows(hwnd, (child, _) => { Apply(child); return true; }, IntPtr.Zero);
        }

        static void Apply(IntPtr target)
        {
            if (IntPtr.Size == 8) SetClassLongPtr64(target, GclpHbrBackground, _backgroundBrush);
            else SetClassLong32(target, GclpHbrBackground, _backgroundBrush.ToInt32());
        }
    }

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;

    /// <summary>
    /// Darkens the windows libvlc creates for its video output, and repaints them.
    ///
    /// A 16:9 video in a wider window is pillarboxed, and libvlc draws the picture into
    /// a child window inset inside a larger one — measured on a maximised window, a
    /// 1793px video inside a 1920px parent, leaving 63px bars down each side. Those bars
    /// belong to the parent, and the parent is created by libvlc when playback starts,
    /// long after <see cref="SetClientBackground"/> walked the tree. So it kept the
    /// default class brush and erased them white, which showed as a white band down the
    /// left edge on roughly one launch in ten.
    ///
    /// Re-walking the tree here catches those windows. The brush is a property of the
    /// window class rather than the window, so this only has to win once: every vout
    /// window libvlc creates afterwards is already dark.
    /// </summary>
    public static void DarkenVideoSurface(IntPtr hwnd, System.Windows.Media.Color color)
    {
        if (hwnd == IntPtr.Zero) return;

        SetClientBackground(hwnd, color, includeChildren: true);

        // Setting the brush does not repaint what is already white, so force the erase.
        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
            RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow);
    }

    // --- Fullscreen sizing ---
    public const int WmGetMinMaxInfo = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct PointI
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointI Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    /// <summary>
    /// Answers WM_GETMINMAXINFO with the full monitor rectangle instead of the work area.
    ///
    /// This is what lets fullscreen be reached by simply maximising a borderless window,
    /// rather than un-maximising and then resizing to the monitor. That two-step dance
    /// briefly leaves the screen uncovered, which reads as a flicker; keeping the window
    /// maximised throughout means the transition is a frame change and nothing moves.
    /// </summary>
    public static void ApplyFullscreenBounds(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) return;

        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var r = info.rcMonitor;

        // Relative to the monitor the window is maximising onto.
        mmi.MaxPosition = new PointI { X = 0, Y = 0 };
        mmi.MaxSize = new PointI { X = r.Right - r.Left, Y = r.Bottom - r.Top };
        mmi.MaxTrackSize = mmi.MaxSize;

        Marshal.StructureToPtr(mmi, lParam, false);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
        int x, int y, int cx, int cy, uint flags);

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    /// <summary>A window rectangle in physical pixels.</summary>
    public readonly record struct RectPx(int Left, int Top, int Width, int Height)
    {
        public bool IsEmpty => Width <= 0 || Height <= 0;
    }

    public static RectPx GetWindowRectPx(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return default;
        return new RectPx(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>
    /// Moves and sizes a window in one call, leaving z-order, activation and the
    /// maximised flag alone.
    ///
    /// Used for both directions of the fullscreen switch on an already-maximised window.
    /// Windows will not recompute a maximised window's size in place — neither
    /// SWP_FRAMECHANGED nor re-applying SW_SHOWMAXIMIZED does anything — and the
    /// alternative, dropping out of the maximised state and back in, uncovers the screen
    /// for a few frames. Setting the rectangle directly keeps WS_MAXIMIZE intact, so the
    /// maximise button and the restore-down rectangle both still behave.
    /// </summary>
    public static void SetWindowRectPx(IntPtr hwnd, RectPx rect)
    {
        if (hwnd == IntPtr.Zero || rect.IsEmpty) return;
        SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            SwpNoZOrder | SwpNoOwnerZOrder | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>Full pixel bounds of the monitor the window currently sits on.</summary>
    public static RectPx GetMonitorRectPx(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info)) return default;

        var r = info.rcMonitor;
        return new RectPx(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    // --- Hold the window invisible until it has something to show ---
    private const int DwmwaCloak = 13;

    /// <summary>
    /// Hides the window from the compositor entirely before it is ever shown. Call from
    /// SourceInitialized: the HWND exists but nothing is on screen yet, so every step
    /// that follows — first paint, placement, maximising — happens invisibly.
    ///
    /// This uses DWM cloaking rather than WS_EX_LAYERED on purpose. WPF caches the
    /// window's extended style and rewrites it during Show(), which silently drops a
    /// layered bit set here and lets the unpainted white client area through. Cloaking
    /// is a compositor attribute, so WPF's style bookkeeping cannot clobber it.
    /// </summary>
    public static void HideUntilPainted(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var cloak = 1;
        DwmSetWindowAttribute(hwnd, DwmwaCloak, ref cloak, sizeof(int));
    }

    /// <summary>Uncloaks a window hidden by <see cref="HideUntilPainted"/>.</summary>
    public static void Reveal(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var cloak = 0;
        DwmSetWindowAttribute(hwnd, DwmwaCloak, ref cloak, sizeof(int));
    }

    // --- Activation ---
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Any process may steal the foreground from this one.</summary>
    private const int AsfwAny = -1;

    /// <summary>
    /// Called by a second instance before it hands its file over. Windows normally only
    /// lets the process that owns the foreground give it away, so without this the
    /// running window would take the file and stay behind whatever is in front of it.
    /// </summary>
    public static void GrantForegroundRights() => AllowSetForegroundWindow(AsfwAny);

    /// <summary>Brings a window to the front, once the sender has granted the right.</summary>
    public static void BringToFront(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
    }

    // --- Sleep suppression ---
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    /// <summary>Keeps the display awake while video is playing.</summary>
    public static void SetKeepAwake(bool keepAwake)
    {
        SetThreadExecutionState(keepAwake
            ? EsContinuous | EsSystemRequired | EsDisplayRequired
            : EsContinuous);
    }

    public readonly record struct Rect2(double Left, double Top, double Width, double Height);
}
