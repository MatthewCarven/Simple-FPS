namespace SimpleFps.Core.Sounds;

/// <summary>
/// How loud a sound is at a distance, worked out as the original game did. Every sound is at
/// full volume within 80 units, then fades in a straight line to silence. How fast it fades is
/// its attenuation: 0 never fades, 1 (normal) is silent at 2,080 units, 2 (idle) at 1,080,
/// 3 (static) at about 413. Looping sounds always fade like static ones, whatever the map says
/// (Matthew, 2026-11-25, keeping what the maps' authors heard).
/// </summary>
public static class SoundFalloff
{
    public const float None = 0f;
    public const float Normal = 1f;
    public const float Idle = 2f;
    public const float Static = 3f;

    /// <summary>Closer than this, a sound is at full volume.</summary>
    public const float FullVolumeDistance = 80f;

    /// <summary>The original's fade for looping sounds, the same as static.</summary>
    private const float LoopFade = 0.003f;

    /// <summary>
    /// The attenuation a map's <c>attenuation</c> key means: missing or 0 is normal, and -1 is
    /// none, heard everywhere, since 0 was taken. Anything else is used as it is, never below 0.
    /// </summary>
    public static float FromKey(float? key) => key switch
    {
        null or 0f => Normal,
        -1f => None,
        _ => MathF.Max(0f, key.Value),
    };

    /// <summary>The volume a map's <c>volume</c> key means: missing or 0 is full, and it never goes past full.</summary>
    public static float VolumeFromKey(float? key) => key is null or 0f ? 1f : Math.Clamp(key.Value, 0f, 1f);

    /// <summary>How much of a sound's volume is left at a distance, from 1 down to 0.</summary>
    public static float Gain(float distance, float attenuation)
    {
        // The original's quirk: static's fade is three thousandths a unit, the others half a thousandth for each step.
        float fade = attenuation == Static ? attenuation * 0.001f : attenuation * 0.0005f;
        return Fade(distance, fade);
    }

    /// <summary>How much of a looping sound's volume is left at a distance.</summary>
    public static float LoopGain(float distance) => Fade(distance, LoopFade);

    private static float Fade(float distance, float fade) =>
        MathF.Max(0f, 1f - (MathF.Max(0f, distance - FullVolumeDistance) * fade));
}
