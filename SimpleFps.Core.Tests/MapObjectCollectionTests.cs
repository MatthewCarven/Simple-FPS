using System.Numerics;
using SimpleFps.Core.Bake;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;
using SimpleFps.Core.Movement;
using SimpleFps.Core.Objects;
using Xunit.Abstractions;

namespace SimpleFps.Core.Tests;

/// <summary>
/// Spawns every real map's objects and works them. Like <see cref="WholeCollectionTests"/>, it
/// passes without proving anything when there's no content folder.
/// </summary>
public class MapObjectCollectionTests(ITestOutputHelper output)
{
    private const int TicksToRun = 600;

    [Fact]
    public void EveryMapSpawnsItsObjectsAndEveryTeleporterFindsItsExit()
    {
        string? maps = WholeCollectionTests.FindMaps();
        if (maps is null)
        {
            output.WriteLine("No content folder, so nothing was checked.");
            return;
        }

        // Only used to list and find maps. Nothing is baked or written.
        var cache = new MapCache(maps, Path.Combine(Path.GetTempPath(), "simplefps-unused"));
        var found = new Findings();
        var classMaps = new Dictionary<string, int>(StringComparer.Ordinal);
        var unhandledMaps = new Dictionary<string, int>(StringComparer.Ordinal);
        int mapCount = 0, teleporters = 0, pushes = 0, pads = 0, padFloors = 0, hurts = 0, leftOut = 0, problems = 0, fired = 0;
        float fastestPush = 0;

        foreach (string name in cache.Names())
        {
            try
            {
                MapCache.Source source = cache.Find(name);
                Q2Bsp bsp = Q2Bsp.Read(File.ReadAllBytes(source.BspPath));
                IReadOnlyList<BspEntity> entities = source.Override is { } file && MapOverride.Read(file).Entities is { } text
                    ? BspEntity.Parse(text)
                    : bsp.Entities;
                Box[] models = bsp.Models.Select(m => new Box(m.Mins, m.Maxs)).ToArray();
                var world = new BspCollision(bsp);
                mapCount++;

                MapObjects objects = MapObjects.Spawn(entities, models, world);
                leftOut += objects.LeftOut;
                foreach (string handled in objects.All.Where(o => o is not Placeholder).Select(o => o.ClassName).Distinct())
                {
                    classMaps[handled] = classMaps.GetValueOrDefault(handled) + 1;
                }

                foreach ((string unhandled, _) in objects.Unhandled)
                {
                    unhandledMaps[unhandled] = unhandledMaps.GetValueOrDefault(unhandled) + 1;
                }

                pushes += objects.PushZones.Count;
                pads += objects.PushZones.Count(p => p.Lifts);
                padFloors += objects.PushZones.Count(p => p.Floor is { } f && f <= p.Volume.Max.Z);
                hurts += objects.HurtZones.Count;
                foreach (PushZone push in objects.PushZones)
                {
                    Assert.True(float.IsFinite(push.Velocity.Length()), $"{name}: push zone {push.Fields["model"]} has no usable direction");
                    fastestPush = MathF.Max(fastestPush, MathF.Abs(push.Speed));
                }

                foreach (Teleporter teleporter in objects.Teleporters)
                {
                    teleporters++;
                    CheckTeleporter(name, entities, models, world, teleporter, found);
                }

                problems += Work(entities, models, world, ref fired);
            }
            catch (Exception e) when (e is not Xunit.Sdk.XunitException)
            {
                found.Failures.Add($"{name}: {e.GetType().Name}: {e.Message}");
            }
        }

        output.WriteLine($"{mapCount} maps, {teleporters} teleporters, {pushes} push zones (fastest {fastestPush:F0}; {pads} jump pads, {padFloors} with their floor found), {hurts} hurt zones, {leftOut} entities left out as not for deathmatch");
        output.WriteLine($"maps with each object: {string.Join(", ", classMaps.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value}"))}");
        output.WriteLine($"most common not handled yet, by maps: {string.Join(", ", unhandledMaps.OrderByDescending(c => c.Value).Take(25).Select(c => $"{c.Key} {c.Value}"))}");
        output.WriteLine($"after touching every trigger and running {TicksToRun} ticks: {fired} events, {problems} problems listed");
        Report("teleporters with no target, which go nowhere", found.NoTarget);
        Report("pads whose teleporter sits inside a step, drawn on its top", found.RaisedPads);
        Report("teleporters you have to crouch to reach", found.CrouchOnly);
        Report("exits still inside something after lifting, to be pushed clear on the next move", found.BuriedExits);
        Report("teleporters no body can touch", found.Unreachable);
        Report("teleporters whose target names nothing", found.Lost);
        Report("maps that failed", found.Failures);

        Assert.True(mapCount == 0 || teleporters > 0, "no teleporters found at all");
        Assert.Empty(found.Failures);
        Assert.Empty(found.Lost);
        Assert.Empty(found.Unreachable);
    }

    /// <summary>
    /// Matthew's hand test on q2pduel1 (2026-11-25): walking off the upper ledge drops you down the
    /// jump pad's shaft, at full speed far enough to land past the pad. Crouch-walking off lands on
    /// the pad, which throws you back up, and its top zone carries you to the ledge. With the
    /// original push zones the ledge's edge turns you back instead.
    /// </summary>
    [Fact]
    public void OnQ2pduel1YouCanWalkOffTheLedgeDownTheJumpPadAndRideItBack()
    {
        string? maps = WholeCollectionTests.FindMaps();
        if (maps is null || !File.Exists(Path.Combine(maps, "q2pduel1.bsp")))
        {
            output.WriteLine("No q2pduel1, so nothing was checked.");
            return;
        }

        Q2Bsp bsp = Q2Bsp.Read(File.ReadAllBytes(Path.Combine(maps, "q2pduel1.bsp")));
        var world = new BspCollision(bsp);
        Box[] models = bsp.Models.Select(m => new Box(m.Mins, m.Maxs)).ToArray();

        (float Lowest, PlayerState End) Walk(Vector3 start, float forward, MoveSettings settings, bool crouch = false, float yaw = 90)
        {
            MapObjects objects = MapObjects.Spawn(bsp.Entities, models, world);
            var sim = new Simulation(world, settings, objects);
            sim.AddPlayer(start - (Vector3.UnitZ * PlayerHull.SpawnLift));
            float lowest = float.MaxValue;
            for (int t = 0; t < 420; t++)
            {
                sim.Tick([new MoveCommand(yaw, 0, t < 120 ? forward : 0, 0, Crouch: crouch && t < 120)]);
                lowest = MathF.Min(lowest, sim.Players[0].Position.Z);
            }

            return (lowest, sim.Players[0]);
        }

        var ledge = new Vector3(128, -230, 440.03125f);
        (float walkedLowest, PlayerState walkedEnd) = Walk(ledge, 1, new MoveSettings());
        (float crouchedLowest, PlayerState crouchedEnd) = Walk(ledge, 1, new MoveSettings(), crouch: true);
        (float launchLowest, PlayerState launchEnd) = Walk(new Vector3(128, -132, 248.03125f), 0, new MoveSettings());

        // Walking into the pad from the corridor, as Matthew does, touches the column, the strip
        // beside it and the carry zone above all at once.
        (_, PlayerState fromCorridorEnd) = Walk(new Vector3(128, 40, 248.03125f), 1, new MoveSettings(), yaw: 270);
        (float originalLowest, _) = Walk(ledge, 1, new MoveSettings { OriginalPushZones = true });
        output.WriteLine($"walked off: lowest {walkedLowest:F0}, ended at {walkedEnd.Position}; crouched off: lowest {crouchedLowest:F0}, ended at {crouchedEnd.Position}; launched: ended at {launchEnd.Position}; original rule: lowest {originalLowest:F0}");

        Assert.True(walkedLowest < 260, "walking off the ledge didn't get you down");
        Assert.True(crouchedLowest < 260, "crouching off the ledge didn't get you down");
        Assert.True(crouchedEnd.Position.Z > 400 && crouchedEnd.Position.Y < -176, "the pad didn't carry you back to the ledge");
        Assert.True(launchEnd.Position.Z > 400 && launchEnd.Position.Y < -176, "the launch didn't reach the ledge");
        Assert.True(fromCorridorEnd.Position.Z > 400 && fromCorridorEnd.Position.Y < -176, $"walking in from the corridor ended at {fromCorridorEnd.Position}, not on the ledge");
        Assert.True(originalLowest > 400, "the original rule let you off the ledge");
    }

    /// <summary>
    /// Finds somewhere a body fits and touches the teleporter, standing on its pad if it can, and
    /// checks it arrives at the exit.
    /// </summary>
    private static void CheckTeleporter(string name, IReadOnlyList<BspEntity> entities, Box[] models, BspCollision world, Teleporter teleporter, Findings found)
    {
        string where = $"{name} {teleporter.ClassName} at {teleporter.Fields["origin"] ?? teleporter.Fields["model"]}";
        if (teleporter.Target is null)
        {
            found.NoTarget.Add(where);
            return;
        }

        if (teleporter.Exit is null)
        {
            found.Lost.Add($"{where} (target \"{teleporter.Target}\")");
            return;
        }

        Vector3 origin = teleporter.Fields.GetVector("origin") ?? Vector3.Zero;
        if (teleporter.Pad is { } pad && pad.Z > origin.Z)
        {
            found.RaisedPads.Add($"{where}, drawn {pad.Z - origin.Z:F0} up");
        }

        // A fresh set each time, and only this teleporter touched, so nothing else gets in the way.
        MapObjects fresh = MapObjects.Spawn(entities, models, world);
        Teleporter same = fresh.Teleporters.Single(t => t.Entity == teleporter.Entity);
        if ((SpotTouching(world, same, crouched: false) ?? SpotTouching(world, same, crouched: true)) is not { } s)
        {
            found.Unreachable.Add(where);
            return;
        }

        if (s.Crouched)
        {
            found.CrouchOnly.Add(where);
        }

        same.Touch(fresh, 0, ref s, s, new MoveCommand(0, 0, 0, 0), new MoveSettings());
        (Vector3 arrival, _) = Teleporter.Arrival(teleporter.Exit);
        Assert.True(
            s.Teleports == 1 && s.Position.X == arrival.X && s.Position.Y == arrival.Y && s.Position.Z >= arrival.Z && s.Position.Z <= arrival.Z + 32,
            $"{where} arrived at {s.Position}, not at its exit's {arrival}");
        if (world.Trace(s.Position, s.Position, s.Mins, s.Maxs, Contents.PlayerSolid).StartSolid)
        {
            found.BuriedExits.Add($"{where} to {teleporter.Exit.ClassName} at {teleporter.Exit.Fields["origin"]}");
        }
    }

    /// <summary>A body standing on the pad, or else at the lowest clear spot found near the teleporter that touches it.</summary>
    private static PlayerState? SpotTouching(BspCollision world, Teleporter teleporter, bool crouched)
    {
        PlayerState body = PlayerState.At(Vector3.Zero);
        body.Crouched = crouched;
        bool Fits(Vector3 p) =>
            teleporter.Volume.Touches(p + body.Mins, p + body.Maxs)
            && !world.Trace(p, p, body.Mins, body.Maxs, Contents.PlayerSolid).StartSolid;

        Vector3 centre = teleporter.Pad is { } pad
            ? pad + new Vector3(0, 0, -PlayerHull.Bottom + ICollisionWorld.SurfaceGap)
            : teleporter.Volume.Centre;
        if (Fits(centre))
        {
            return body with { Position = centre };
        }

        for (float z = -64; z <= 64; z += 4)
        {
            for (float y = -24; y <= 24; y += 8)
            {
                for (float x = -24; x <= 24; x += 8)
                {
                    Vector3 p = centre + new Vector3(x, y, z);
                    if (Fits(p))
                    {
                        return body with { Position = p };
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Touches every trigger and runs the clock, to shake out anything that throws. Returns problems found.</summary>
    private static int Work(IReadOnlyList<BspEntity> entities, Box[] models, BspCollision world, ref int fired)
    {
        MapObjects objects = MapObjects.Spawn(entities, models, world);
        var sim = new Simulation(world, new MoveSettings(), objects);
        foreach (TouchObject o in objects.All.OfType<TouchObject>())
        {
            PlayerState s = PlayerState.At(o.Volume.Centre);
            objects.ClearEvents();
            objects.Touch(0, ref s, new MoveCommand(0, 0, 0, 0));
            fired += objects.Events.Count;
        }

        for (int i = 0; i < TicksToRun; i++)
        {
            sim.Tick([]);
            fired += objects.Events.Count;
        }

        return objects.Problems.Count;
    }

    private void Report(string what, List<string> items)
    {
        output.WriteLine($"{items.Count} {what}{(items.Count > 0 ? ":" : "")}");
        foreach (string item in items)
        {
            output.WriteLine("  " + item);
        }
    }

    private sealed class Findings
    {
        public List<string> Failures { get; } = [];
        public List<string> NoTarget { get; } = [];
        public List<string> Lost { get; } = [];
        public List<string> Unreachable { get; } = [];
        public List<string> CrouchOnly { get; } = [];
        public List<string> RaisedPads { get; } = [];
        public List<string> BuriedExits { get; } = [];
    }
}
