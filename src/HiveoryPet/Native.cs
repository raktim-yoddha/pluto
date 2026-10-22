using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HiveoryPet;

/// <summary>The bit of Win32 the pet needs that the Windows App SDK doesn't cover.</summary>
internal static partial class Native
{
    private const int ExStyle = -20;
    private const long ToolWindow = 0x80; // not in Alt+Tab or the taskbar
    private const long NoActivate = 0x08000000; // clicking the pet never takes focus from what you type in
    private const int CornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
    private const int Round = 2;
    private const int BorderColor = 34; // DWMWA_BORDER_COLOR
    private const int Restore = 9;

    /// <summary>No focus stealing, no taskbar button, Windows 11's rounded corners and no outline.</summary>
    public static void MakePetWindow(nint hwnd)
    {
        SetWindowLongPtrW(hwnd, ExStyle, (nint)((GetWindowLongPtrW(hwnd, ExStyle) | ToolWindow | NoActivate) & ~0x100L /* WS_EX_WINDOWEDGE */));
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0027); // SWP_FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER
        var round = Round;
        DwmSetWindowAttribute(hwnd, CornerPreference, ref round, sizeof(int));
        var none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: no outline, the acrylic edge is enough
        DwmSetWindowAttribute(hwnd, BorderColor, ref none, sizeof(int));
        var dark = 1;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
    }

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

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hwnd);
}
