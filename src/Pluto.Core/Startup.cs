using Microsoft.Win32;

namespace Pluto;

/// <summary>Start with Windows: the current user's Run key, nothing else.</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Pluto";
    private const string LegacyName = "HiveoryPet";

    /// <summary>
    /// Keeps "start with Windows" pointing at the Pluto that is running: carried over from before the app was called
    /// Pluto, and moved along when Pluto runs from somewhere new (installed with Setup.exe, a new folder), so Windows
    /// never starts an old copy.
    /// </summary>
    public static void Migrate()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(LegacyName) is not null)
        {
            key.DeleteValue(LegacyName, throwOnMissingValue: false);
            Enabled = true;
        }
        else if (key?.GetValue(Name) is string current && current != Command)
        {
            Enabled = true;
        }
    }

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool Enabled
    {
        get => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(Name) is not null;
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
            {
                key.SetValue(Name, Command);
            }
            else
            {
                key.DeleteValue(Name, throwOnMissingValue: false);
            }
        }
    }
}
