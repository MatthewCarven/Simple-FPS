using System.Numerics;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Movement;

namespace SimpleFps.Core.Objects;

/// <summary>
/// <c>misc_teleporter</c>, a pad on the floor, or <c>trigger_teleport</c>, a volume. Sends a player
/// to the first entity named by its <c>target</c>, usually a <c>misc_teleporter_dest</c>.
/// </summary>
/// <remarks>
/// Agreed with Matthew (2026-11-25): you keep your flat speed, pointed the way the exit faces, and
/// you face that way too, with the view levelled. Up and down speed is kept. The original stopped
/// you dead instead. Arriving on someone isn't a telefrag yet; that waits for damage in milestone 5.
/// </remarks>
public sealed class Teleporter : TouchObject
{
    /// <summary>Where the old pad's touch box sat, relative to a <c>misc_teleporter</c>'s origin.</summary>
    public static readonly Box PadTouch = new(new Vector3(-8, -8, 8), new Vector3(8, 8, 24));

    /// <summary>Players arrive this far above the exit's origin, or a little higher if that isn't clear.</summary>
    public const float ExitLift = 10f;

    internal Teleporter(int entity, BspEntity fields, Box volume, Vector3? pad)
        : base(entity, fields, volume)
    {
        Pad = pad;
    }

    /// <summary>The floor under a <c>misc_teleporter</c>, where the game draws its pad. Null for a volume.</summary>
    public Vector3? Pad { get; }

    /// <summary>Where it leads as the map starts, or null if its target names nothing.</summary>
    public MapObject? Exit { get; internal set; }

    /// <summary>
    /// The floor under the exit, where the game draws a plain disc. Null when there's no exit, or
    /// the exit is another teleporter, which has a pad of its own.
    /// </summary>
    public Vector3? ExitPad { get; internal set; }

    /// <summary>Where a player arrives and which way they face, for a given exit.</summary>
    public static (Vector3 Position, float Yaw) Arrival(MapObject exit) =>
        (EntityKeys.Origin(exit.Fields) + (Vector3.UnitZ * ExitLift), EntityKeys.Angles(exit.Fields).Y);

    internal override void Touch(MapObjects map, int player, ref PlayerState s, in MoveCommand cmd)
    {
        // Found afresh each time, as the original did, so a killtarget on the exit takes effect.
        if (map.FirstNamed(Target) is not { } exit)
        {
            return;
        }

        (Vector3 position, float yaw) = Arrival(exit);
        float radians = float.DegreesToRadians(yaw);
        var facing = new Vector3(MathF.Cos(radians), MathF.Sin(radians), 0);

        s.Position = map.Settle(position, s);
        s.Velocity = (facing * s.FlatSpeed) + (Vector3.UnitZ * s.Velocity.Z);
        s.Teleports++;
        s.TeleportYaw = yaw;

        // TODO 4c: play the teleporter's sound from 4a here, at both ends.
        map.Report(new MapEvent(MapEventKind.Teleported, Entity, player));
    }
}

/// <summary>
/// <c>trigger_push</c>: sets the velocity of any player inside it, every tick they're in it. The
/// map's <c>speed</c> is a tenth of the real speed, as in the original, and 1000 if unset. The
/// direction comes from <c>angle</c> or <c>angles</c>, where an angle of -1 is up and -2 down.
/// Flag 1 makes it work once and then go.
/// </summary>
public sealed class PushZone : TouchObject
{
    /// <summary>
    /// The fastest a push zone sends anyone. It's the fastest any map asks for by number
    /// (<c>q2e3m4</c>); the original couldn't hold a player's speed much past it anyway. Only the
    /// few zones with no speed at all, which the original took as 10,000, reach it.
    /// </summary>
    public const float TopSpeed = 4000f;

    private const float DefaultSpeed = 1000f;
    private const float SpeedScale = 10f;
    private const int OnceFlag = 1;

    internal PushZone(int entity, BspEntity fields, Box volume)
        : base(entity, fields, volume)
    {
        float speed = fields.GetFloat("speed") ?? 0f;
        Speed = Math.Clamp((speed == 0 ? DefaultSpeed : speed) * SpeedScale, -TopSpeed, TopSpeed);
        Direction = EntityKeys.Direction(EntityKeys.Angles(fields));
        Once = (EntityKeys.Flags(fields) & OnceFlag) != 0;
    }

    /// <summary>Map units a second.</summary>
    public float Speed { get; }

    public Vector3 Direction { get; }

    public bool Once { get; }

    public Vector3 Velocity => Direction * Speed;

    internal override void Touch(MapObjects map, int player, ref PlayerState s, in MoveCommand cmd)
    {
        s.Velocity = Velocity;
        map.PushedNow(Entity, player);
        if (Once)
        {
            map.Remove(this);
        }
    }
}

/// <summary>
/// <c>trigger_hurt</c>: found and kept, but it does no harm until damage arrives in milestone 5.
/// <c>dmg</c> is damage a tenth of a second (5 if unset).
/// </summary>
public sealed class HurtZone : MapObject
{
    private const int DefaultDamage = 5;
    private const int StartOff = 1;
    private const int Toggle = 2;

    private bool _canSwitch;

    internal HurtZone(int entity, BspEntity fields, Box volume)
        : base(entity, fields)
    {
        Volume = volume;
        Flags = EntityKeys.Flags(fields);
        int damage = (int)(fields.GetFloat("dmg") ?? 0f);
        Damage = damage != 0 ? damage : DefaultDamage;
        IsOn = (Flags & StartOff) == 0;
        _canSwitch = (Flags & (StartOff | Toggle)) != 0;
    }

    public Box Volume { get; }

    public int Damage { get; }

    /// <summary>
    /// The map's flags: 1 starts off, 2 can be switched on and off by name, 4 silent, 8 ignores
    /// protection, 16 hurts once a second instead of ten times.
    /// </summary>
    public int Flags { get; }

    public bool IsOn { get; private set; }

    public bool Touches(in PlayerState s) => !Removed && IsOn && Volume.Touches(s.Position + s.Mins, s.Position + s.Maxs);

    /// <summary>A zone that starts off switches on when used. Only one with flag 2 switches back and forth.</summary>
    internal override void Use(MapObjects map, int activator)
    {
        if (!_canSwitch)
        {
            return;
        }

        IsOn = !IsOn;
        _canSwitch = (Flags & Toggle) != 0;
    }
}
