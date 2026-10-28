using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pluto;

/// <summary>The bit of Win32 the pet needs that the Windows App SDK doesn't cover.</summary>
internal static partial class Native
{
    private const int ExStyle = -20;
    private const long ToolWindow = 0x80; // not in Alt+Tab or the taskbar
    private const long NoActivate = 0x08000000; // clicking the pet never takes focus from what you type in
    private const int NcRendering = 2; // DWMWA_NCRENDERING_POLICY
    private const int CornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
    private const int DoNotRound = 1;
    private const int BorderColor = 34; // DWMWA_BORDER_COLOR
    private const int Restore = 9;
    private const nint TopMost = -1; // HWND_TOPMOST
    private const uint RaiseFlags = 0x0013; // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
    private const uint ForegroundChanged = 3; // EVENT_SYSTEM_FOREGROUND

    private static readonly HashSet<nint> Unframed = [];
    private static readonly List<nint> OnTop = [];
    private static nint s_foregroundHook;
    private static nint s_mouseHook;
    private static Action<int, int>? s_pressed;

    /// <summary>
    /// No focus stealing, no taskbar button, and no frame, corners, outline or shadow from Windows: Pluto draws its own
    /// shape on a see-through window. A window with a border keeps invisible resize margins; answering WM_NCCALCSIZE
    /// with "no frame" makes the client area the whole window.
    /// </summary>
    public static unsafe void MakePetWindow(nint hwnd)
    {
        if (Unframed.Add(hwnd))
        {
            SetWindowSubclass(hwnd, &NoFrame, 1, 0);
        }

        SetWindowLongPtrW(hwnd, ExStyle, (nint)((GetWindowLongPtrW(hwnd, ExStyle) | ToolWindow | NoActivate) & ~0x100L /* WS_EX_WINDOWEDGE */));
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0027); // SWP_FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER
        var disabled = 1; // DWMNCRP_DISABLED: Windows draws no frame, caption colour or shadow into the clear parts
        DwmSetWindowAttribute(hwnd, NcRendering, ref disabled, sizeof(int));
        var square = DoNotRound;
        DwmSetWindowAttribute(hwnd, CornerPreference, ref square, sizeof(int));
        var none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, BorderColor, ref none, sizeof(int));

        SeeThrough(hwnd);
    }

    /// <summary>
    /// See-through: with "blur behind" on over an empty region (so nothing is blurred or tinted) DWM composes the window
    /// with its alpha, and a black erase counts as clear (WinUIEx's TransparentTintBackdrop does the same).
    /// </summary>
    private static void SeeThrough(nint hwnd)
    {
        var margins = default(Margins);
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var nothing = CreateRectRgn(-2, -2, -1, -1);
        var blur = new BlurBehind { Flags = 3 /* enable | region */, Enable = 1, Region = nothing };
        DwmEnableBlurBehindWindow(hwnd, ref blur);
        DeleteObject(nothing);
    }

    /// <summary>
    /// Only this part of the window (window pixels) takes the mouse; the see-through rest lets clicks reach whatever is
    /// below. It is a rectangle around the shape, so it never clips what Pluto draws.
    /// </summary>
    public static void SetHitArea(nint hwnd, int x, int y, int width, int height)
    {
        _ = SetWindowRgn(hwnd, CreateRectRgn(x, y, x + width, y + height), true); // Windows owns the region from here
    }

    /// <summary>
    /// Calls <paramref name="pressed"/> with the screen point (physical pixels) of every mouse button press anywhere,
    /// until <see cref="StopWatchingClicks"/>: how the open list knows you clicked somewhere else. Only the point is
    /// passed on, and only while the list is open.
    /// </summary>
    public static unsafe void WatchClicks(Action<int, int> pressed)
    {
        s_pressed = pressed;
        if (s_mouseHook == 0)
        {
            s_mouseHook = SetWindowsHookExW(14 /* WH_MOUSE_LL */, &OnMouse, GetModuleHandleW(0), 0);
        }
    }

    public static void StopWatchingClicks()
    {
        if (s_mouseHook != 0)
        {
            UnhookWindowsHookEx(s_mouseHook);
            s_mouseHook = 0;
        }

        s_pressed = null;
    }

    [UnmanagedCallersOnly]
    private static unsafe nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && message is 0x0201 or 0x0204 or 0x0207) // left, right or middle button down
        {
            var point = *(Point*)data;
            s_pressed?.Invoke(point.X, point.Y);
        }

        return CallNextHookEx(0, code, message, data);
    }

    /// <summary>
    /// Keeps <paramref name="hwnd"/> above everything, full-screen apps included: whenever another window comes to the
    /// front (a game, a window that made itself topmost), it goes back on top without taking focus. Windows kept later
    /// (Settings) go above earlier ones (the pet). A game in exclusive full screen draws past every window, so nothing can
    /// show over it.
    /// </summary>
    public static unsafe void KeepOnTop(nint hwnd)
    {
        OnTop.Remove(hwnd);
        OnTop.Add(hwnd);
        if (s_foregroundHook == 0)
        {
            s_foregroundHook = SetWinEventHook(ForegroundChanged, ForegroundChanged, 0, &OnForeground, 0, 0, 0 /* out of context */);
        }

        Raise();
    }

    /// <summary>Windows really has the window on top (WS_EX_TOPMOST), not just asked to.</summary>
    public static bool IsTopmost(nint hwnd) => (GetWindowLongPtrW(hwnd, ExStyle) & 0x8 /* WS_EX_TOPMOST */) != 0;

    /// <summary>The program of the window in front ("Discord"), for the log.</summary>
    public static string ForegroundApp()
    {
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "?";
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    /// <summary>Stops keeping <paramref name="hwnd"/> on top (it closed).</summary>
    public static void LetGo(nint hwnd) => OnTop.Remove(hwnd);

    /// <summary>Puts the kept windows back on top (also called every second, for apps that push in without the foreground changing).</summary>
    public static void Raise()
    {
        foreach (var hwnd in OnTop.ToArray())
        {
            SetWindowPos(hwnd, TopMost, 0, 0, 0, 0, RaiseFlags);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnForeground(nint hook, uint e, nint hwnd, int obj, int child, uint thread, uint time) => Raise();

    [UnmanagedCallersOnly]
    private static nint NoFrame(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        switch (message)
        {
            case 0x0083 when wParam != 0: // WM_NCCALCSIZE: no frame
                return 0;
            case 0x0085: // WM_NCPAINT: there is no frame to paint
                return 0;
            case 0x0086: // WM_NCACTIVATE: nor a caption to repaint as active or inactive
                return 1;
            case 0x0014 when GetClientRect(hwnd, out var client): // WM_ERASEBKGND: black, which reads as clear
                FillRect(wParam, ref client, GetStockObject(4 /* BLACK_BRUSH */));
                return 1;
            default:
                return DefSubclassProc(hwnd, message, wParam, lParam);
        }
    }

    /// <summary>Lets the window take focus (to type a task) or not (the rest of the time).</summary>
    public static void SetActivatable(nint hwnd, bool can)
    {
        var style = GetWindowLongPtrW(hwnd, ExStyle);
        var next = can ? style & ~(nint)NoActivate : style | (nint)NoActivate;
        if (next != style)
        {
            SetWindowLongPtrW(hwnd, ExStyle, next);
        }
    }

    /// <summary>The primary mouse button is down right now (whichever physical button that is).</summary>
    public static bool PrimaryButtonDown() =>
        (GetAsyncKeyState(GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 0x02 : 0x01) & 0x8000) != 0;

    /// <summary>The cursor in physical screen pixels.</summary>
    public static (int X, int Y) Cursor() => GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    /// <summary>Brings the main window of <paramref name="pid"/> to the front; false when it has none.</summary>
    public static bool BringForward(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return BringForward(process.MainWindowHandle);
        }
        catch (ArgumentException)
        {
            return false; // it has exited
        }
    }

    /// <summary>Brings the first window of a process named <paramref name="name"/> to the front.</summary>
    public static bool BringForward(string name)
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                if (process.MainWindowHandle != 0)
                {
                    return BringForward(process.MainWindowHandle);
                }
            }
        }

        return false;
    }

    /// <summary>Asks Windows to give <paramref name="hwnd"/> the focus (it may say no; the window is in front anyway).</summary>
    public static void Focus(nint hwnd) => BringForward(hwnd);

    // The pet just got the user's click, so Windows lets it hand the foreground to another window.
    private static bool BringForward(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, Restore);
        }

        return SetForegroundWindow(hwnd);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public int Flags;
        public int Enable;
        public nint Region;
        public int TransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("comctl32.dll")]
    private static unsafe partial int SetWindowSubclass(nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint data);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmEnableBlurBehindWindow(nint hwnd, ref BlurBehind blur);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial int FillRect(nint hdc, ref Rect rect, nint brush);

    [LibraryImport("gdi32.dll")]
    private static partial nint GetStockObject(int index);

    [LibraryImport("user32.dll")]
    private static unsafe partial nint SetWindowsHookExW(int kind, delegate* unmanaged<int, nint, nint, nint> proc, nint module, uint thread);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(nint name);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("user32.dll")]
    private static unsafe partial nint SetWinEventHook(uint min, uint max, nint module, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> proc, uint process, uint thread, uint flags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hwnd);

    /// <summary>
    /// Pluto's logo (the exe's own icon) on the window and on its window class. The taskbar asks a new window for its
    /// icon and, when the window is too busy to answer (Settings building its first frame), takes the class icon, which
    /// WinUI leaves empty: Windows' blank app icon, kept for as long as the window was open.
    /// </summary>
    public static void UseLogo(nint hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        var big = LoadImageW(GetModuleHandleW(0), 32512 /* the app icon */, 1 /* IMAGE_ICON */, GetSystemMetricsForDpi(11 /* SM_CXICON */, dpi), GetSystemMetricsForDpi(12, dpi), 0);
        var small = LoadImageW(GetModuleHandleW(0), 32512, 1, GetSystemMetricsForDpi(49 /* SM_CXSMICON */, dpi), GetSystemMetricsForDpi(50, dpi), 0);
        SendMessageW(hwnd, 0x0080 /* WM_SETICON */, 1 /* ICON_BIG */, big);
        SendMessageW(hwnd, 0x0080, 0 /* ICON_SMALL */, small);
        SetClassLongPtrW(hwnd, -14 /* GCLP_HICON */, big);
        SetClassLongPtrW(hwnd, -34 /* GCLP_HICONSM */, small);
    }

    /// <summary>
    /// Hides a window from the screen while it still lays out and draws (or shows it again): a new WinUI window is black
    /// until its first frame, and Settings takes a moment the first time.
    /// </summary>
    public static void Cloak(nint hwnd, bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 13 /* DWMWA_CLOAK */, ref value, sizeof(int));
    }

    /// <summary>The window's DPI scale (1 at 100%), known before it is shown.</summary>
    public static double Scale(nint hwnd) => GetDpiForWindow(hwnd) / 96.0;

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial nint LoadImageW(nint module, nint name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint SetClassLongPtrW(nint hwnd, int index, nint value);

    /// <summary>A plain Windows question box, for before WinUI is running. True on OK.</summary>
    public static bool Ask(string text) => MessageBoxW(0, text, "Pluto", 0x41 /* MB_OKCANCEL | MB_ICONINFORMATION */) == 1;

    /// <summary>A plain Windows message box, for before WinUI is running.</summary>
    public static void Tell(string text) => MessageBoxW(0, text, "Pluto", 0x30 /* MB_ICONWARNING */);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint owner, string text, string caption, uint type);
}
