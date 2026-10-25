
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pluto;

/// <summary>Windows: the system's own look, light or dark. Black and White: monochrome, dot-matrix type.</summary>
public enum PlutoTheme { Windows, Black, White }

/// <summary>The face's eyes: a little grid of dots, or two soft ovals.</summary>
public enum EyeStyle { Dots, Oval }

/// <summary>
/// The pet's few settings, in <c>%LOCALAPPDATA%\Pluto\settings.json</c>. Setters, not init: the JSON source
/// generator sets every init property, so a key missing from the file would lose its default.
/// </summary>
public sealed record PetSettings
{
    public Dock Dock { get; set; } = Dock.Notch;

    /// <summary>Where along the top edge the notch is centred (physical pixels); null for the middle of the screen.</summary>
    public int? NotchX { get; set; }

    /// <summary>Where a floating pet's face was left: its top-left corner (physical pixels); null until it first floats.</summary>
    public int? FloatX { get; set; }

    public int? FloatY { get; set; }

    public bool Sounds { get; set; } = true;

    /// <summary>Show Hiveory's agents (its own "Let Pluto see your agents" switch must be on too). Off until chosen.</summary>
    public bool Hiveory { get; set; }

    public PlutoTheme Theme { get; set; } = PlutoTheme.Windows;

    public NotchSize Size { get; set; } = NotchSize.Small;

    public EyeStyle Eyes { get; set; } = EyeStyle.Dots;

    /// <summary>0…100.</summary>
    public int Volume { get; set; } = 70;

    /// <summary>Each moment's sound: null for Pluto's own, empty for none, or a file the user chose (copied into <see cref="SoundFolder"/>).</summary>
    public string? StartSound { get; set; }

    public string? DoneSound { get; set; }

    public string? WaitSound { get; set; }

    public static string SoundFolder => Path.Combine(Folder, "Sounds");

    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pluto");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static PetSettings Load()
    {
        try
        {
            // Settings from before the app was called Pluto come along once.
            var path = File.Exists(FilePath) ? FilePath : Path.Combine(Path.GetDirectoryName(Folder)!, "HiveoryPet", "settings.json");
            return JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.PetSettings) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, SettingsJson.Default.PetSettings));
            File.Move(FilePath + ".tmp", FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the pet keeps working with what it has in memory.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PetSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
