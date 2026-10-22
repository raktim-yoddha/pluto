
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HiveoryPet;

/// <summary>
/// The pet's few settings, in <c>%LOCALAPPDATA%\HiveoryPet\settings.json</c>. Setters, not init: the JSON source
/// generator sets every init property, so a key missing from the file would lose its default.
/// </summary>
public sealed record PetSettings
{
    public Dock Dock { get; set; } = Dock.Notch;

    /// <summary>Where along the top edge the notch is centred (physical pixels); null for the middle of the screen.</summary>
    public int? NotchX { get; set; }

    /// <summary>Where a floating pet was left (physical pixels); null until it first floats.</summary>
    public int? FloatX { get; set; }

    public int? FloatY { get; set; }

    public bool Sounds { get; set; } = true;

    /// <summary>Follow Hiveory's agents (the user clicked Connect).</summary>
    public bool Hiveory { get; set; }

    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HiveoryPet");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static PetSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.PetSettings) ?? new();
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
