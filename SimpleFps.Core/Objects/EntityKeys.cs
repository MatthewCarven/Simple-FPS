using System.Globalization;
using System.Numerics;
using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Objects;

/// <summary>Reading the keys map objects share, the way the original game read them.</summary>
internal static class EntityKeys
{
    /// <summary>Entities with this flag don't exist in deathmatch, which is the only game we have.</summary>
    public const int NotInDeathmatch = 2048;

    /// <summary>Which skills and game modes an entity appears in. Bits from here up mean nothing else.</summary>
    private const int ModeFlags = 256 | 512 | 1024 | NotInDeathmatch | 4096;

    /// <summary>The flags that change how an entity behaves, with the skill and mode bits taken off.</summary>
    public static int Flags(BspEntity e) => AllFlags(e) & ~ModeFlags;

    public static int AllFlags(BspEntity e)
    {
        string? text = e["spawnflags"];
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags))
        {
            return flags;
        }

        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? (int)value : 0;
    }

    /// <summary>
    /// A vector key, taking the first three numbers. A few maps write four ("-74 0 0 0"), which the
    /// original read happily.
    /// </summary>
    public static Vector3? Vector(BspEntity e, string key)
    {
        string[]? parts = e[key]?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is null || parts.Length < 3)
        {
            return null;
        }

        Span<float> values = stackalloc float[3];
        for (int i = 0; i < 3; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    public static Vector3 Origin(BspEntity e) => Vector(e, "origin") ?? Vector3.Zero;

    /// <summary>Pitch, yaw and roll in degrees, from <c>angles</c>, or from <c>angle</c> as a yaw alone.</summary>
    public static Vector3 Angles(BspEntity e) =>
        Vector(e, "angles") ?? (e.GetFloat("angle") is { } yaw ? new Vector3(0, yaw, 0) : Vector3.Zero);

    /// <summary>
    /// Which way a push zone pushes, or a trigger must be faced. A yaw of -1 means straight up and -2
    /// straight down, since a yaw alone can't say either. Otherwise pitch tips it, with positive
    /// pitch pointing down.
    /// </summary>
    public static Vector3 Direction(Vector3 angles)
    {
        if (angles == new Vector3(0, -1, 0))
        {
            return Vector3.UnitZ;
        }

        if (angles == new Vector3(0, -2, 0))
        {
            return -Vector3.UnitZ;
        }

        float pitch = float.DegreesToRadians(angles.X);
        float yaw = float.DegreesToRadians(angles.Y);
        return new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
    }

    /// <summary>Seconds to whole ticks, never less than one when the time isn't zero.</summary>
    public static int Ticks(float seconds)
    {
        if (seconds <= 0)
        {
            return 0;
        }

        return Math.Max(1, (int)MathF.Round(seconds * GameInfo.TickRate));
    }
}
