using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HiveoryPet;

/// <summary>The pet's few settings, in <c>%LOCALAPPDATA%\HiveoryPet\settings.json</c>.</summary>
public sealed record PetSettings
{
    public Spot Spot { get; init; } = Spot.TopCenter;

    /// <summary>Where a floating pet was left (DIPs).</summary>
    public double FloatX { get; init; } = double.NaN;

    public double FloatY { get; init; } = double.NaN;

    public bool Sounds { get; init; } = true;

    /// <summary>Follow Hiveory's agents (the user clicked Connect).</summary>
    public bool Hiveory { get; init; }

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

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(PetSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
