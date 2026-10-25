using CommunityToolkit.Mvvm.ComponentModel;

namespace Pluto;

/// <summary>The moments that make a sound.</summary>
public enum SoundKind { Started, Finished, NeedsYou }

/// <summary>
/// One moment's sound in Settings: Pluto's own, none, or a file you chose. A chosen file is copied into
/// <see cref="PetSettings.SoundFolder"/>, so it keeps working if the original moves.
/// </summary>
public sealed partial class SoundSlot(SoundKind kind, Pet pet) : ObservableObject
{
    /// <summary>The sound files Windows' media player reads.</summary>
    public static readonly string[] Types = [".wav", ".mp3", ".m4a", ".wma", ".flac", ".aac"];

    public SoundKind Kind { get; } = kind;

    public string Header => Kind switch
    {
        SoundKind.Started => "An agent starts",
        SoundKind.Finished => "An agent is done",
        _ => "An agent needs you",
    };

    /// <summary>null for Pluto's own, empty for none, or the chosen file.</summary>
    public string? File => Kind switch
    {
        SoundKind.Started => pet.Settings.StartSound,
        SoundKind.Finished => pet.Settings.DoneSound,
        _ => pet.Settings.WaitSound,
    };

    /// <summary>0 = Pluto's, 1 = none, 2 = your own.</summary>
    public int Choice => File switch { null => 0, "" => 1, _ => 2 };

    public string Detail => Choice switch
    {
        0 => Kind switch
        {
            SoundKind.Started => "Pluto's soft tap",
            SoundKind.Finished => "Pluto's two-note chime",
            _ => "Pluto's gentle knock",
        },
        1 => "Silent",
        _ => Path.GetFileName(File!),
    };

    /// <summary>Back to Pluto's own sound (0) or none (1).</summary>
    public void Choose(int choice)
    {
        if (choice is 0 or 1)
        {
            Set(choice == 0 ? null : string.Empty);
        }
    }

    /// <summary>Copies <paramref name="source"/> in and uses it; the reason when it can't.</summary>
    public string? UseFile(string source)
    {
        var type = Path.GetExtension(source).ToLowerInvariant();
        if (!Types.Contains(type))
        {
            return "Pluto plays WAV, MP3, M4A, WMA, FLAC and AAC files.";
        }

        try
        {
            Directory.CreateDirectory(PetSettings.SoundFolder);
            foreach (var old in Directory.EnumerateFiles(PetSettings.SoundFolder, Kind + ".*"))
            {
                System.IO.File.Delete(old);
            }

            var copy = Path.Combine(PetSettings.SoundFolder, Kind + type);
            System.IO.File.Copy(source, copy, overwrite: true);
            Set(copy);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "Couldn't copy that file. " + e.Message;
        }
    }

    private void Set(string? file)
    {
        pet.Update(s => Kind switch
        {
            SoundKind.Started => s with { StartSound = file },
            SoundKind.Finished => s with { DoneSound = file },
            _ => s with { WaitSound = file },
        });
        OnPropertyChanged(nameof(File));
        OnPropertyChanged(nameof(Choice));
        OnPropertyChanged(nameof(Detail));
    }
}
