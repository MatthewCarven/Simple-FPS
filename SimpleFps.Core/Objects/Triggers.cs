using System.Numerics;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Movement;

namespace SimpleFps.Core.Objects;

/// <summary>
/// <c>trigger_multiple</c> and <c>trigger_once</c>: a volume that fires its targets when a player
/// walks into it. A multiple waits <c>wait</c> seconds (0.2 if unset) before it can fire again;
/// a once, or a multiple with a negative wait, goes after firing. Either can also be set off by name.
/// </summary>
public sealed class TouchTrigger : TouchObject
{
    private const float DefaultWait = 0.2f;

    /// <summary>Players can't set it off, only other objects by name.</summary>
    private const int NotPlayer = 2;

    /// <summary>Starts switched off. The first use by name switches it on without firing it.</summary>
    private const int StartsOff = 4;

    private readonly int _flags;
    private readonly Vector3 _facing;
    private long _readyAt;

    internal TouchTrigger(int entity, BspEntity fields, Box volume, bool once)
        : base(entity, fields, volume)
    {
        _flags = EntityKeys.Flags(fields);
        float wait = fields.GetFloat("wait") ?? 0f;
        Once = once || wait < 0;
        WaitTicks = EntityKeys.Ticks(wait == 0 ? DefaultWait : wait);
        IsOn = (_flags & StartsOff) == 0;

        // A trigger given an angle only fires for someone facing within 90 degrees of it.
        Vector3 angles = EntityKeys.Angles(fields);
        _facing = angles == Vector3.Zero ? Vector3.Zero : EntityKeys.Direction(angles);
    }

    public bool Once { get; }

    public int WaitTicks { get; }

    public bool IsOn { get; private set; }

    internal override bool Touchable => IsOn;

    internal override void Touch(MapObjects map, int player, ref PlayerState s, in PlayerState before, in MoveCommand cmd, MoveSettings settings)
    {
        if ((_flags & NotPlayer) != 0 || (_facing != Vector3.Zero && Vector3.Dot(cmd.Look, _facing) < 0))
        {
            return;
        }

        Fire(map, player);
    }

    internal override void Use(MapObjects map, int activator)
    {
        if (!IsOn)
        {
            IsOn = true;
            return;
        }

        Fire(map, activator);
    }

    private void Fire(MapObjects map, int activator)
    {
        if (map.Ticks < _readyAt)
        {
            return;
        }

        _readyAt = map.Ticks + WaitTicks;
        map.UseTargets(this, activator);
        if (Once)
        {
            map.Remove(this);
        }
    }
}

/// <summary><c>trigger_relay</c>: passes a use on to its targets, usually to add a delay or a message.</summary>
public sealed class Relay : MapObject
{
    internal Relay(int entity, BspEntity fields)
        : base(entity, fields)
    {
    }

    internal override void Use(MapObjects map, int activator) => map.UseTargets(this, activator);
}

/// <summary>
/// <c>trigger_counter</c>: fires its targets on its <c>count</c>th use (2 if unset), then goes.
/// Each use before that tells the player how many are left, unless flag 1 hushes it.
/// </summary>
public sealed class Counter : MapObject
{
    private const int DefaultCount = 2;
    private const int Quiet = 1;

    private readonly bool _quiet;

    internal Counter(int entity, BspEntity fields)
        : base(entity, fields)
    {
        _quiet = (EntityKeys.Flags(fields) & Quiet) != 0;
        int count = (int)(fields.GetFloat("count") ?? 0f);
        Left = count > 0 ? count : DefaultCount;
    }

    /// <summary>Uses still needed before it fires.</summary>
    public int Left { get; private set; }

    internal override void Use(MapObjects map, int activator)
    {
        if (Left == 0)
        {
            return;
        }

        Left--;
        if (Left > 0)
        {
            if (!_quiet)
            {
                map.Say(activator, $"{Left} more to go...");
            }

            return;
        }

        if (!_quiet)
        {
            map.Say(activator, "Sequence completed!");
        }

        map.UseTargets(this, activator);
        map.Remove(this);
    }
}

/// <summary><c>trigger_always</c>: fires its targets once as the map starts, never sooner than 0.2 seconds in.</summary>
public sealed class AlwaysTrigger : MapObject
{
    private const float SoonestDelay = 0.2f;

    internal AlwaysTrigger(int entity, BspEntity fields)
        : base(entity, fields)
    {
        Delay = MathF.Max(Delay, SoonestDelay);
    }

    internal void Start(MapObjects map) => map.UseTargets(this, -1);
}

/// <summary>
/// <c>func_timer</c>: fires its targets every <c>wait</c> seconds (1 if unset), give or take a
/// random amount up to <c>random</c>. Using it switches it on or off, waiting <c>delay</c> before
/// the first firing. Flag 1 starts it on, a second after the map starts plus <c>pausetime</c>.
/// </summary>
public sealed class RepeatTimer : MapObject
{
    private const float DefaultWait = 1f;
    private const float StartOnPause = 1f;
    private const int StartOn = 1;

    private readonly float _wait;
    private readonly float _random;
    private readonly float _pause;

    // Each switch bumps this, so a firing scheduled before an off and on again is dropped.
    private int _generation;
    private int _activator = -1;

    internal RepeatTimer(int entity, BspEntity fields)
        : base(entity, fields)
    {
        float wait = fields.GetFloat("wait") ?? 0f;
        _wait = wait > 0 ? wait : DefaultWait;

        // The spread must leave every gap above zero.
        _random = Math.Clamp(fields.GetFloat("random") ?? 0f, 0f, _wait - (1f / GameInfo.TickRate));
        _pause = fields.GetFloat("pausetime") ?? 0f;
        StartsOn = (EntityKeys.Flags(fields) & StartOn) != 0;
    }

    public bool StartsOn { get; }

    public bool IsOn { get; private set; }

    internal void Start(MapObjects map)
    {
        if (StartsOn)
        {
            IsOn = true;
            Schedule(map, StartOnPause + _pause + Delay + NextGap(map));
        }
    }

    internal override void Use(MapObjects map, int activator)
    {
        _generation++;
        if (IsOn)
        {
            IsOn = false;
            return;
        }

        IsOn = true;
        _activator = activator;
        if (Delay > 0)
        {
            Schedule(map, Delay);
        }
        else
        {
            Fire(map);
        }
    }

    private float NextGap(MapObjects map) => _wait + (map.Spread() * _random);

    private void Schedule(MapObjects map, float seconds)
    {
        int generation = _generation;
        map.Schedule(EntityKeys.Ticks(seconds), () =>
        {
            if (generation == _generation)
            {
                Fire(map);
            }
        });
    }

    private void Fire(MapObjects map)
    {
        if (!IsOn || Removed)
        {
            return;
        }

        // Its own delay applies here too, so a delayed timer's targets lag its firings. The original did the same.
        map.UseTargets(this, _activator);
        Schedule(map, NextGap(map));
    }
}
