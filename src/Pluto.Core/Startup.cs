using Microsoft.Win32;

namespace Pluto;

/// <summary>Start with Windows: the current user's Run key, nothing else.</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Pluto";
    private const string LegacyName = "HiveoryPet";

    /// <summary>Carries a "start with Windows" from before the app was called Pluto over to the new name.</summary>
    public static void Migrate()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(LegacyName) is not null)
        {
            key.DeleteValue(LegacyName, throwOnMissingValue: false);
            Enabled = true;
        }
    }

    public static bool Enabled
    {
        get => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(Name) is not null;
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
            {
                key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(Name, throwOnMissingValue: false);
            }
        }
    }
}
