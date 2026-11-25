using SimpleFps.Core.Bsp;
using SimpleFps.Core.Movement;

namespace SimpleFps.Core.Objects;

/// <summary>
/// One live entity from the map. Every entity that exists in deathmatch gets one, even classes
/// with nothing to do yet, so names, targets and killtargets always find what they name.
/// </summary>
public abstract class MapObject
{
    private protected MapObject(int entity, BspEntity fields)
    {
        Entity = entity;
        Fields = fields;
        Delay = fields.GetFloat("delay") ?? 0f;
    }

    /// <summary>Where it sits in the map's entity list.</summary>
    public int Entity { get; }

    public BspEntity Fields { get; }

    public string ClassName => Fields.ClassName;

    public string? TargetName => Fields["targetname"];

    /// <summary>What it sets off when it fires.</summary>
    public string? Target => Fields["target"];

    /// <summary>What it takes out of the map when it fires.</summary>
    public string? KillTarget => Fields["killtarget"];

    /// <summary>Shown to the player who set it off, when it fires.</summary>
    public string? Message => Fields["message"];

    /// <summary>Seconds between firing and its targets going off.</summary>
    public float Delay { get; private protected set; }

    /// <summary>Taken out by a killtarget, or a trigger that only works once has gone.</summary>
    public bool Removed { get; internal set; }

    /// <summary>False for classes not built yet, which are listed once when the map loads.</summary>
    public virtual bool Handled => true;

    /// <summary>Set off by name, from another object's target.</summary>
    /// <param name="activator">The player who started the chain, or -1 for none.</param>
    internal virtual void Use(MapObjects map, int activator)
    {
    }
}

/// <summary>A map object with a volume that players set off by being in it.</summary>
public abstract class TouchObject : MapObject
{
    private protected TouchObject(int entity, BspEntity fields, Box volume)
        : base(entity, fields)
    {
        Volume = volume;
    }

    public Box Volume { get; }

    /// <summary>False while it's switched off. Removed ones are never touched either.</summary>
    internal virtual bool Touchable => true;

    /// <summary>Called on each tick a player ends inside the volume, after they've moved.</summary>
    internal abstract void Touch(MapObjects map, int player, ref PlayerState s, in MoveCommand cmd, MoveSettings settings);
}

/// <summary>An entity with nothing to do: a light, a spawn spot, or a class not built yet.</summary>
public sealed class Placeholder : MapObject
{
    private readonly bool _known;

    internal Placeholder(int entity, BspEntity fields, bool known)
        : base(entity, fields)
    {
        _known = known;
    }

    public override bool Handled => _known;

    internal override void Use(MapObjects map, int activator) =>
        map.Report(new MapEvent(MapEventKind.Used, Entity, activator));
}
