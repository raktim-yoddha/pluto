namespace Pluto;

/// <summary>Diagnostics only, never content: <c>%LOCALAPPDATA%\Pluto\pet.log</c>, started afresh past 256 KB.</summary>
public static class Log
{
    private static readonly string FilePath = Path.Combine(PetSettings.Folder, "pet.log");
    private static readonly Lock Gate = new();

    public static void Write(string what, Exception? error = null)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(PetSettings.Folder);
                if (new FileInfo(FilePath) is { Exists: true, Length: > 256 * 1024 })
                {
                    File.Delete(FilePath);
                }

                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:u} {what}{(error is null ? string.Empty : $": {error.GetType().Name}: {error.Message}")}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // nowhere to write; carry on
            }
        }
    }
}
