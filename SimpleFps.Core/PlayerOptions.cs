using System.Text.Json;

namespace SimpleFps.Core;

/// <summary>
/// Choices that belong to each player, not to the server, so they stay out of
/// <see cref="Movement.MoveSettings"/>. Invert Y and mouse speed join these when the options menu
/// arrives.
/// </summary>
public sealed class PlayerOptions
{
    /// <summary>Draw the high-res pack's textures and skies where it has them, or only the maps' own.</summary>
    public bool HighResTextures { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Reads saved options, falling back to the defaults if they're unreadable.</summary>
    public static PlayerOptions FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PlayerOptions>(json) ?? new PlayerOptions();
        }
        catch (JsonException)
        {
            return new PlayerOptions();
        }
    }
}
