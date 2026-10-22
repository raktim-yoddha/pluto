using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HiveoryPet;

/// <summary>The bit of Win32 the pet needs: a window that never steals focus, the cursor, and bringing a window forward.</summary>
internal static partial class Native
{
    private const int ExStyle = -20;
    private const long ToolWindow = 0x80; // not in Alt+Tab or the taskbar
    private const long NoActivate = 0x08000000; // clicking the pet never takes focus from what you type in
    private const int Restore = 9;

    public static void MakePetWindow(nint hwnd) =>
        SetWindowLongPtrW(hwnd, ExStyle, (nint)(GetWindowLongPtrW(hwnd, ExStyle) | ToolWindow | NoActivate));

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
