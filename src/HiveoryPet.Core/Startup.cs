using Microsoft.Win32;

namespace HiveoryPet;

/// <summary>Start with Windows: the current user's Run key, nothing else.</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "HiveoryPet";

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
