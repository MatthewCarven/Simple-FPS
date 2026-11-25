using System.Globalization;
using System.Numerics;
using SimpleFps.Core.Bake;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;
using SimpleFps.Core.Movement;

namespace SimpleFps.Core.Objects;

/// <summary>
/// A map's live objects: triggers, relays, counters, timers, teleporters, push zones and hurt
/// zones, spawned from its entities and run inside the simulation a tick at a time. Objects find
/// each other by name: firing sets off everything its <c>target</c> names, after <c>delay</c>
/// seconds, takes out everything its <c>killtarget</c> names, and shows its <c>message</c> to the
/// player who set it off.
/// </summary>
/// <remarks>
/// Written from how these entities are documented to behave, not from the original game's code.
/// Entities flagged as not in deathmatch are left out, since deathmatch is the only game there is.
/// Everything here depends only on the map and the players, and random spreads come from a fixed
/// seed, so a server and its clients will agree.
/// </remarks>
public sealed class MapObjects
{
    /// <summary>A chain of uses this deep is a loop in the map, and is cut off.</summary>
    private const int ChainLimit = 64;

    /// <summary>The old pad's base sat this far below a teleporter's origin, used when no floor is found.</summary>
    private const float PadDrop = 24f;

    private const float FloorSearch = 128f;

    /// <summary>A teleporter's origin can sit a little inside a raised step. Its floor is looked for this far up.</summary>
    private const float StepSearch = 32f;

    /// <summary>
    /// Lifts tried, smallest first, when an exit leaves a body touching or inside something. Most
    /// leave the feet exactly on the floor, which the smallest clears.
    /// </summary>
    private static readonly float[] ArrivalLifts = [ICollisionWorld.SurfaceGap, 1, 2, 4, 8, 16, 24, 32];

    private static readonly MoveSettings DefaultSettings = new();

    private const int PushReportTicks = GameInfo.TickRate * 3 / 2;

    /// <summary>Ground at least this level counts as a floor, as it does for movement.</summary>
    private const float WalkableNormalZ = 0.7f;

    /// <summary>Classes that need nothing from us, so they aren't listed as unhandled.</summary>
    private static readonly HashSet<string> NothingToDo = new(StringComparer.Ordinal)
    {
        "worldspawn", "light", "func_group", "info_null", "info_notnull", "path_corner",
        "info_player_deathmatch", "info_player_start", "info_player_coop", "info_player_intermission",
        "misc_teleporter_dest",
    };

    private readonly List<MapObject> _objects = [];
    private readonly Dictionary<string, List<MapObject>> _named = new(StringComparer.Ordinal);
    private readonly List<TouchObject> _touchables = [];
    private readonly List<TouchObject> _touching = [];
    private readonly PriorityQueue<Action, (long Due, long Order)> _scheduled = new();
    private readonly List<MapEvent> _events = [];
    private readonly List<string> _problems = [];
    private readonly HashSet<string> _problemSet = new(StringComparer.Ordinal);
    private readonly Dictionary<int, long> _pushReportAt = [];
    private readonly ICollisionWorld? _world;
    private long _order;
    private int _depth;
    private uint _random = 0x9E3779B9u;

    private MapObjects(ICollisionWorld? world)
    {
        _world = world;
    }

    /// <summary>Ticks since the map started.</summary>
    public long Ticks { get; private set; }

    /// <summary>Every live entity, in the map's order.</summary>
    public IReadOnlyList<MapObject> All => _objects;

    public IReadOnlyList<Teleporter> Teleporters { get; private set; } = [];

    public IReadOnlyList<PushZone> PushZones { get; private set; } = [];

    public IReadOnlyList<HurtZone> HurtZones { get; private set; } = [];

    /// <summary>Classes with no map object yet, and how many of each, most first.</summary>
    public IReadOnlyList<(string ClassName, int Count)> Unhandled { get; private set; } = [];

    /// <summary>Entities flagged as not in deathmatch, which were left out.</summary>
    public int LeftOut { get; private set; }

    /// <summary>Things in the map that can't work as written, each listed once.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>What happened on the last tick. Cleared as the next one starts.</summary>
    public IReadOnlyList<MapEvent> Events => _events;

    /// <summary>A map with no objects, for worlds built by hand.</summary>
    public static MapObjects Empty() => new(null);

    public static MapObjects Spawn(BakedMap map, ICollisionWorld world) =>
        Spawn(map.Entities, map.Collision.Models.Select(m => new Box(m.Mins, m.Maxs)).ToArray(), world);

    /// <param name="entities">The map's entities, with any override applied.</param>
    /// <param name="models">Each brush model's bounds, where entry 0 is the world.</param>
    /// <param name="world">
    /// Finds the floor under teleporter pads and a clear spot at each exit. Without one, pads sit
    /// where the old pad's base did and exits are taken as they are.
    /// </param>
    public static MapObjects Spawn(IReadOnlyList<BspEntity> entities, IReadOnlyList<Box> models, ICollisionWorld? world)
    {
        var map = new MapObjects(world);
        for (int i = 0; i < entities.Count; i++)
        {
            BspEntity entity = entities[i];
            if ((EntityKeys.AllFlags(entity) & EntityKeys.NotInDeathmatch) != 0)
            {
                map.LeftOut++;
                continue;
            }

            MapObject made = map.Make(i, entity, models);
            map._objects.Add(made);
            if (made is TouchObject touchable)
            {
                map._touchables.Add(touchable);
            }

            if (made.TargetName is { } name)
            {
                if (!map._named.TryGetValue(name, out List<MapObject>? list))
                {
                    map._named[name] = list = [];
                }

                list.Add(made);
            }
        }

        map.Teleporters = map._objects.OfType<Teleporter>().ToList();
        map.PushZones = map._objects.OfType<PushZone>().ToList();
        map.HurtZones = map._objects.OfType<HurtZone>().ToList();
        map.Unhandled = map._objects
            .Where(o => !o.Handled)
            .GroupBy(o => o.ClassName)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(g => g.Item2)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        foreach (Teleporter teleporter in map.Teleporters)
        {
            map.FindExit(teleporter);
        }

        foreach (PushZone push in map.PushZones)
        {
            push.Floor = map.ZoneFloor(push.Volume);
        }

        foreach (MapObject o in map._objects)
        {
            switch (o)
            {
                case AlwaysTrigger always:
                    always.Start(map);
                    break;
                case RepeatTimer timer:
                    timer.Start(map);
                    break;
            }
        }

        return map;
    }

    /// <summary>One line for the log: what spawned, what isn't handled yet, and what was left out.</summary>
    public string Summary()
    {
        string counts = string.Join(", ", _objects
            .Where(o => o is not Placeholder)
            .GroupBy(o => o.ClassName)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key} {g.Count()}"));
        string unhandled = Unhandled.Count == 0 ? "nothing" : string.Join(", ", Unhandled.Select(u => $"{u.ClassName} {u.Count}"));
        return $"map objects: {(counts.Length > 0 ? counts : "none")}; not handled yet: {unhandled}; {LeftOut} left out as not for deathmatch";
    }

    /// <summary>Sets off whatever a player ends the tick inside. Called after they've moved.</summary>
    /// <param name="settings">The server's movement settings, or the defaults if left out.</param>
    public void Touch(int player, ref PlayerState s, in MoveCommand cmd, MoveSettings? settings = null)
    {
        if (_touchables.Count == 0)
        {
            return;
        }

        // Everything touched is found first, so whatever one of them switches on waits for the next tick.
        Vector3 min = s.Position + s.Mins;
        Vector3 max = s.Position + s.Maxs;
        _touching.Clear();
        foreach (TouchObject o in _touchables)
        {
            if (!o.Removed && o.Touchable && o.Volume.Touches(min, max))
            {
                _touching.Add(o);
            }
        }

        int teleports = s.Teleports;
        PlayerState before = s;
        foreach (TouchObject o in _touching)
        {
            if (!o.Removed && o.Touchable)
            {
                o.Touch(this, player, ref s, before, cmd, settings ?? DefaultSettings);

                // The rest were found where the player was, not where they are now.
                if (s.Teleports != teleports)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Forgets the last tick's events. The simulation calls this as each tick starts.</summary>
    public void ClearEvents() => _events.Clear();

    /// <summary>Moves the clock on a tick and runs whatever was waiting for it: delays and timers.</summary>
    public void Advance()
    {
        Ticks++;
        while (_scheduled.TryPeek(out Action? action, out (long Due, long Order) when) && when.Due <= Ticks)
        {
            _scheduled.Dequeue();
            action();
        }
    }

    /// <summary>The first live object with this name, as the original game looked things up.</summary>
    public MapObject? FirstNamed(string? name) => Named(name).FirstOrDefault(o => !o.Removed);

    /// <summary>Every object with this name, in the map's order, including removed ones.</summary>
    public IReadOnlyList<MapObject> Named(string? name) =>
        name is not null && _named.TryGetValue(name, out List<MapObject>? list) ? list : [];

    /// <summary>Fires an object: its targets, killtargets and message, now or after its delay.</summary>
    internal void UseTargets(MapObject source, int activator)
    {
        string? target = source.Target;
        string? killTarget = source.KillTarget;
        string? message = source.Message;
        if (source.Delay > 0)
        {
            Schedule(EntityKeys.Ticks(source.Delay), () => Fire(source, target, killTarget, message, activator));
        }
        else
        {
            Fire(source, target, killTarget, message, activator);
        }
    }

    internal void Schedule(int ticks, Action action) =>
        _scheduled.Enqueue(action, (Ticks + Math.Max(ticks, 1), _order++));

    internal void Remove(MapObject o)
    {
        if (!o.Removed)
        {
            o.Removed = true;
            Report(new MapEvent(MapEventKind.Removed, o.Entity, -1));
        }
    }

    internal void Report(MapEvent e) => _events.Add(e);

    /// <summary>Words for a player. Nothing happens when it was the world that set things off.</summary>
    internal void Say(int player, string text)
    {
        if (player >= 0)
        {
            Report(new MapEvent(MapEventKind.Message, -1, player, text));
        }
    }

    internal void PushedNow(int entity, int player)
    {
        if (!_pushReportAt.TryGetValue(player, out long at) || Ticks >= at)
        {
            _pushReportAt[player] = Ticks + PushReportTicks;
            Report(new MapEvent(MapEventKind.Pushed, entity, player));
        }
    }

    /// <summary>A random number from -1 up to 1, the same sequence every time the map starts.</summary>
    internal float Spread()
    {
        // xorshift32: small, and the same on every machine.
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return ((_random >> 8) / (float)(1 << 23)) - 1f;
    }

    private static string Where(BspEntity e) =>
        e["model"] is { } model ? model : EntityKeys.Origin(e).ToString("F0", CultureInfo.InvariantCulture);

    /// <summary>The floor under a point, as the top of whatever is below it, or a guess without a world.</summary>
    private Vector3 Floor(Vector3 origin)
    {
        Vector3 fallback = origin - (Vector3.UnitZ * PadDrop);
        if (_world is null)
        {
            return fallback;
        }

        for (float up = 0; up <= StepSearch; up++)
        {
            Vector3 start = origin + (Vector3.UnitZ * up);
            TraceResult t = _world.Trace(start, start - (Vector3.UnitZ * (FloorSearch + up)), Vector3.Zero, Vector3.Zero, Contents.PlayerSolid);
            if (!t.StartSolid)
            {
                return t.Hit ? t.End : fallback;
            }
        }

        return fallback;
    }

    /// <summary>
    /// The highest ground under a push zone, looked for on a 5 by 5 grid across it, from a little
    /// above its top, since some are buried in the floor they belong to, down to well below its
    /// bottom, since others float over theirs. Only ground inside its outline counts, so a ledge
    /// beside it isn't its floor, while a raised platform inside it is. Null if nothing is found.
    /// </summary>
    private float? ZoneFloor(Box volume)
    {
        if (_world is null)
        {
            return null;
        }

        const int Samples = 5;
        const float Below = 256f;
        const float ScanStep = 4f;
        const float Above = 32f;
        float? highest = null;
        Vector3 size = volume.Max - volume.Min;
        for (int i = 0; i < Samples; i++)
        {
            for (int j = 0; j < Samples; j++)
            {
                float x = volume.Min.X + (size.X * (i + 0.5f) / Samples);
                float y = volume.Min.Y + (size.Y * (j + 0.5f) / Samples);

                // Start from the first clear point down, since a zone can reach into walls.
                for (float z = volume.Max.Z + Above; z >= volume.Min.Z - Below; z -= ScanStep)
                {
                    var top = new Vector3(x, y, z);
                    TraceResult t = _world.Trace(top, new Vector3(x, y, volume.Min.Z - Below), Vector3.Zero, Vector3.Zero, Contents.PlayerSolid);
                    if (t.StartSolid)
                    {
                        continue;
                    }

                    if (t.Hit && t.Normal.Z >= WalkableNormalZ)
                    {
                        highest = MathF.Max(highest ?? float.MinValue, t.End.Z);
                    }

                    break;
                }
            }
        }

        return highest;
    }

    /// <summary>
    /// Lifts a body that would arrive touching or inside something to the lowest clear spot above.
    /// If none is near, it's left for movement to push clear, as it does anyone stuck.
    /// </summary>
    internal Vector3 Settle(Vector3 position, in PlayerState s)
    {
        if (_world is null || !_world.Trace(position, position, s.Mins, s.Maxs, Contents.PlayerSolid).StartSolid)
        {
            return position;
        }

        foreach (float lift in ArrivalLifts)
        {
            Vector3 raised = position + (Vector3.UnitZ * lift);
            if (!_world.Trace(raised, raised, s.Mins, s.Maxs, Contents.PlayerSolid).StartSolid)
            {
                return raised;
            }
        }

        return position;
    }

    private void Problem(string text)
    {
        if (_problemSet.Add(text))
        {
            _problems.Add(text);
        }
    }

    private MapObject Make(int index, BspEntity e, IReadOnlyList<Box> models)
    {
        string name = e.ClassName;
        switch (name)
        {
            case "trigger_relay":
                return new Relay(index, e);
            case "trigger_counter":
                return new Counter(index, e);
            case "trigger_always":
                return new AlwaysTrigger(index, e);
            case "func_timer":
                return new RepeatTimer(index, e);
            case "misc_teleporter":
            {
                Vector3 origin = EntityKeys.Origin(e);
                return new Teleporter(index, e, Teleporter.PadTouch.Offset(origin), Floor(origin));
            }
        }

        bool brush = name is "trigger_multiple" or "trigger_once" or "trigger_push" or "trigger_hurt" or "trigger_teleport";
        if (!brush)
        {
            return new Placeholder(index, e, NothingToDo.Contains(name) || name.StartsWith("monster_", StringComparison.Ordinal));
        }

        if (Volume(e, models) is not { } volume)
        {
            Problem($"{name} at {Where(e)} has no usable model, so it does nothing");
            return new Placeholder(index, e, known: true);
        }

        return name switch
        {
            "trigger_multiple" => new TouchTrigger(index, e, volume, once: false),
            "trigger_once" => new TouchTrigger(index, e, volume, once: true),
            "trigger_push" => new PushZone(index, e, volume),
            "trigger_hurt" => new HurtZone(index, e, volume),
            _ => new Teleporter(index, e, volume, pad: null),
        };
    }

    /// <summary>A brush entity's volume: its model's bounds, moved by its origin if it has one.</summary>
    private static Box? Volume(BspEntity e, IReadOnlyList<Box> models)
    {
        string? model = e["model"];
        if (model is not ['*', ..]
            || !int.TryParse(model.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            || index <= 0
            || index >= models.Count)
        {
            return null;
        }

        return models[index].Offset(EntityKeys.Origin(e));
    }

    private void FindExit(Teleporter teleporter)
    {
        string where = Where(teleporter.Fields);
        if (teleporter.Target is not { } target)
        {
            Problem($"{teleporter.ClassName} at {where} has no target, so it goes nowhere");
            return;
        }

        if (FirstNamed(target) is not { } exit)
        {
            Problem($"{teleporter.ClassName} at {where} targets \"{target}\", which names nothing, so it goes nowhere");
            return;
        }

        teleporter.Exit = exit;
        if (exit is not Teleporter)
        {
            teleporter.ExitPad = Floor(EntityKeys.Origin(exit.Fields));
        }
    }

    private void Fire(MapObject source, string? target, string? killTarget, string? message, int activator)
    {
        if (message is not null)
        {
            Say(activator, MapCredits.Clean(message));
        }

        foreach (MapObject o in Named(killTarget))
        {
            Remove(o);
        }

        if (target is null)
        {
            return;
        }

        if (_depth >= ChainLimit)
        {
            Problem($"{source.ClassName} \"{source.TargetName}\" sets off a loop through \"{target}\", cut off {ChainLimit} deep");
            return;
        }

        _depth++;
        try
        {
            foreach (MapObject o in Named(target))
            {
                if (o == source)
                {
                    Problem($"{source.ClassName} at {Where(source.Fields)} targets itself, which is skipped");
                }
                else if (!o.Removed)
                {
                    o.Use(this, activator);
                }
            }
        }
        finally
        {
            _depth--;
        }
    }
}
