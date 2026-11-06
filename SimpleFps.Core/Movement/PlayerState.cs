using System.Numerics;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Movement;

/// <summary>What the player asked for on one tick.</summary>
/// <param name="Yaw">Degrees. 0 faces +X and 90 faces +Y.</param>
/// <param name="Pitch">Degrees. Positive looks down.</param>
/// <param name="Forward">-1 to 1.</param>
/// <param name="Side">-1 to 1, with positive to the right.</param>
public readonly record struct MoveCommand(
    float Yaw,
    float Pitch,
    float Forward,
    float Side,
    bool Jump = false,
    bool Crouch = false,
    bool Attack = false)
{
    public Vector3 FlatForward
    {
        get
        {
            float yaw = float.DegreesToRadians(Yaw);
            return new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        }
    }

    public Vector3 FlatRight
    {
        get
        {
            float yaw = float.DegreesToRadians(Yaw);
            return new Vector3(MathF.Sin(yaw), -MathF.Cos(yaw), 0);
        }
    }

    public Vector3 Look
    {
        get
        {
            float pitch = float.DegreesToRadians(Pitch);
            return (FlatForward * MathF.Cos(pitch)) - (Vector3.UnitZ * MathF.Sin(pitch));
        }
    }
}

/// <summary>Everything about a player that carries from one tick to the next.</summary>
public struct PlayerState
{
    public Vector3 Position;
    public Vector3 Velocity;
    public bool OnGround;
    public bool Crouched;
    public bool OnLadder;
    public Vector3 GroundNormal;
    public SurfaceFlags GroundSurface;

    /// <summary>Half the body's width right now. Narrower than usual while squeezing.</summary>
    public float HalfWidth;

    /// <summary>Ticks left in a hop's touch of the ground.</summary>
    public int TouchTicks;

    /// <summary>0 is dry, 1 feet, 2 waist, 3 fully under.</summary>
    public int WaterLevel;

    public Contents WaterType;

    /// <summary>Ticks left of a climb out of water, during which steering is off.</summary>
    public int WaterJumpTicks;

    /// <summary>Ticks until the test gun can fire again.</summary>
    public int AttackCooldown;

    public static PlayerState At(Vector3 position) => new()
    {
        Position = position,
        HalfWidth = PlayerHull.FullHalfWidth,
    };

    public readonly Vector3 Mins => new(-HalfWidth, -HalfWidth, PlayerHull.Bottom);

    public readonly Vector3 Maxs => new(HalfWidth, HalfWidth, Crouched ? PlayerHull.CrouchedTop : PlayerHull.StandingTop);

    public readonly float EyeHeight => Crouched ? PlayerHull.CrouchedEye : PlayerHull.StandingEye;

    public readonly float FlatSpeed => new Vector2(Velocity.X, Velocity.Y).Length();
}

/// <summary>Body size in map units, relative to the player's position.</summary>
public static class PlayerHull
{
    public const float FullHalfWidth = 16f;
    public const float Bottom = -24f;
    public const float StandingTop = 32f;
    public const float CrouchedTop = 4f;
    public const float StandingEye = 22f;
    public const float CrouchedEye = -2f;

    /// <summary>A map's spawn point marks the floor area, so bodies start this far above it.</summary>
    public const float SpawnLift = 9f;
}
