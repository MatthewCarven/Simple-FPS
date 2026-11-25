using System.Numerics;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;
using SimpleFps.Core.Movement;
using SimpleFps.Core.Objects;

namespace SimpleFps.Core.Tests;

public class MapObjectTests
{
    private const float FeetOnFloor = 24.1f;

    /// <summary>A door with a name, to be set off. Doors have no map object yet, so a use shows as an event.</summary>
    private const string Door = """{ "classname" "func_door" "targetname" "door" }""";

    /// <summary>Spawns entities over a big floor. Brush models are numbered from *1 in the order given.</summary>
    private static MapObjects Spawn(string entities, params Box[] brushModels)
    {
        var models = new List<Box> { default };
        models.AddRange(brushModels);
        return MapObjects.Spawn(BspEntity.Parse(entities), models, new TestWorld().Floor());
    }

    private static Box Cube(Vector3 centre, float half) => new(centre - new Vector3(half), centre + new Vector3(half));

    private static PlayerState StandingAt(float x, float y) => PlayerState.At(new Vector3(x, y, FeetOnFloor));

    private static MoveCommand Idle(float yaw = 0, float pitch = 0) => new(yaw, pitch, 0, 0);

    private static void Touch(MapObjects map, ref PlayerState s, MoveCommand? cmd = null)
    {
        map.ClearEvents();
        map.Touch(0, ref s, cmd ?? Idle());
    }

    /// <summary>Runs the clock, gathering each tick's events with the tick they happened on.</summary>
    private static List<(long Tick, MapEvent Event)> Run(MapObjects map, int ticks)
    {
        var seen = new List<(long, MapEvent)>();
        for (int i = 0; i < ticks; i++)
        {
            map.ClearEvents();
            map.Advance();
            seen.AddRange(map.Events.Select(e => (map.Ticks, e)));
        }

        return seen;
    }

    private static List<long> UsesOfTheDoor(IEnumerable<(long Tick, MapEvent Event)> events) =>
        events.Where(e => e.Event.Kind == MapEventKind.Used).Select(e => e.Tick).ToList();

    // Teleporters.

    private const string PadAndExit = """
        { "classname" "misc_teleporter" "origin" "0 0 24" "target" "out" }
        { "classname" "misc_teleporter_dest" "origin" "1000 500 24" "angle" "90" "targetname" "out" }
        """;

    [Fact]
    public void ATeleporterKeepsFlatSpeedPointedTheWayTheExitFaces()
    {
        MapObjects map = Spawn(PadAndExit);
        PlayerState s = StandingAt(4, -3);
        s.Velocity = new Vector3(300, -400, -50);

        Touch(map, ref s);

        Assert.Equal(new Vector3(1000, 500, 24 + Teleporter.ExitLift), s.Position);
        Assert.Equal(0f, s.Velocity.X, 0.01f);
        Assert.Equal(500f, s.Velocity.Y, 0.01f);
        Assert.Equal(-50f, s.Velocity.Z);
        Assert.Equal(1, s.Teleports);
        Assert.Equal(90f, s.TeleportYaw);
        Assert.Equal(MapEventKind.Teleported, Assert.Single(map.Events).Kind);
    }

    [Fact]
    public void RunningOntoAPadSendsYouOutAtRunSpeed()
    {
        var world = new TestWorld().Floor();
        MapObjects objects = MapObjects.Spawn(BspEntity.Parse(PadAndExit), [default], world);
        var sim = new Simulation(world, new MoveSettings(), objects);
        sim.AddPlayer(new Vector3(-300, 0, 16));

        float before = 0;
        int ticks = 0;
        while (sim.Players[0].Teleports == 0 && ticks++ < 300)
        {
            before = sim.Players[0].FlatSpeed;
            sim.Tick([new MoveCommand(0, 0, 1, 0)]);
        }

        PlayerState s = sim.Players[0];
        Assert.Equal(1, s.Teleports);
        Assert.Equal(300f, before, 1f);
        Assert.Equal(1000f, s.Position.X);
        Assert.Equal(500f, s.Position.Y);
        Assert.Equal(0f, s.Velocity.X, 0.01f);
        Assert.Equal(before, s.Velocity.Y, 10f);
    }

    [Fact]
    public void ThePadIsOnlyTouchedNearItsMiddle()
    {
        MapObjects map = Spawn(PadAndExit);

        // The old pad's touch box is 16 wide and a body 32, so the centres must come within 24, plus
        // the reach on both boxes.
        PlayerState near = StandingAt(23, 0);
        PlayerState far = StandingAt(27, 0);
        Touch(map, ref near);
        Touch(map, ref far);

        Assert.Equal(1, near.Teleports);
        Assert.Equal(0, far.Teleports);
    }

    [Fact]
    public void PadsSitOnTheFloorUnderTheirTeleporterAndExit()
    {
        MapObjects map = Spawn(PadAndExit);
        Teleporter teleporter = Assert.Single(map.Teleporters);

        Assert.Equal(new Vector3(0, 0, 0), teleporter.Pad!.Value, new Vector3Within(0.1f));
        Assert.Equal(new Vector3(1000, 500, 0), teleporter.ExitPad!.Value, new Vector3Within(0.1f));
        Assert.Equal("misc_teleporter_dest", teleporter.Exit!.ClassName);
    }

    [Fact]
    public void APadWhoseTeleporterSitsInsideAStepIsDrawnOnItsTop()
    {
        var world = new TestWorld().Floor().Box(new Vector3(-40, -40, 0), new Vector3(40, 40, 12));
        MapObjects map = MapObjects.Spawn(BspEntity.Parse(PadAndExit.Replace("\"0 0 24\"", "\"0 0 10\"")), [default], world);

        Assert.Equal(12f, map.Teleporters[0].Pad!.Value.Z, 0.1f);
    }

    [Theory]
    [InlineData(0f, 24f + ICollisionWorld.SurfaceGap)] // feet exactly on the floor, as most such exits leave them
    [InlineData(12f, 40f)] // feet inside a step 12 high
    [InlineData(-1f, 34f)] // nothing in the way, so 10 above the exit as usual
    public void AnExitThatLeavesYouInsideSomethingLiftsYouClear(float step, float arrivesAt)
    {
        var world = new TestWorld().Floor();
        if (step > 0)
        {
            world.Box(new Vector3(960, 460, 0), new Vector3(1040, 540, step));
        }

        string entities = PadAndExit.Replace("\"1000 500 24\"", step < 0 ? "\"1000 500 24\"" : "\"1000 500 14\"");
        MapObjects map = MapObjects.Spawn(BspEntity.Parse(entities), [default], world);
        PlayerState s = StandingAt(0, 0);

        Touch(map, ref s);

        Assert.Equal(arrivesAt, s.Position.Z);
    }

    [Fact]
    public void AnythingNamedCanBeAnExitAndTheFirstWins()
    {
        MapObjects map = Spawn("""
            { "classname" "misc_teleporter" "origin" "0 0 24" "target" "out" }
            { "classname" "info_notnull" "origin" "500 0 100" "angle" "180" "targetname" "out" }
            { "classname" "misc_teleporter_dest" "origin" "-500 0 100" "targetname" "out" }
            """);
        PlayerState s = StandingAt(0, 0);

        Touch(map, ref s);

        Assert.Equal(new Vector3(500, 0, 110), s.Position);
        Assert.Equal(180f, s.TeleportYaw);
        Assert.Empty(map.Problems);
    }

    [Fact]
    public void ATeleporterThatGoesNowhereIsListedAndDoesNothing()
    {
        MapObjects map = Spawn("""
            { "classname" "misc_teleporter" "origin" "0 0 24" "target" "missing" }
            { "classname" "misc_teleporter" "origin" "500 0 24" }
            """);
        PlayerState s = StandingAt(0, 0);

        Touch(map, ref s);

        Assert.Equal(0, s.Teleports);
        Assert.Equal(2, map.Problems.Count);
        Assert.All(map.Teleporters, t => Assert.Null(t.Exit));
    }

    [Fact]
    public void ATeleporterOntoAnotherPadSendsYouOnTheNextTick()
    {
        var world = new TestWorld().Floor();
        MapObjects objects = MapObjects.Spawn(BspEntity.Parse("""
            { "classname" "misc_teleporter" "origin" "0 0 24" "target" "middle" }
            { "classname" "misc_teleporter" "origin" "500 0 24" "targetname" "middle" "target" "end" }
            { "classname" "misc_teleporter_dest" "origin" "1000 0 24" "targetname" "end" }
            """), [default], world);
        var sim = new Simulation(world, new MoveSettings(), objects);
        sim.AddPlayer(new Vector3(0, 0, 16));

        sim.Tick([Idle()]);
        Assert.Equal(500f, sim.Players[0].Position.X);
        Assert.Null(objects.Teleporters[0].ExitPad);

        sim.Tick([Idle()]);
        Assert.Equal(1000f, sim.Players[0].Position.X);
        Assert.Equal(2, sim.Players[0].Teleports);
    }

    [Fact]
    public void AfterATeleportNothingElseAtTheOldSpotIsTouched()
    {
        MapObjects map = Spawn(PadAndExit + """
            { "classname" "trigger_push" "model" "*1" "angle" "-1" "speed" "100" }
            """, Cube(new Vector3(0, 0, 32), 64));
        PlayerState s = StandingAt(0, 0);

        Touch(map, ref s);

        Assert.Equal(1, s.Teleports);
        Assert.Equal(0f, s.Velocity.Z);
    }

    [Fact]
    public void ATeleporterVolumeWorksLikeAPad()
    {
        MapObjects map = Spawn("""
            { "classname" "trigger_teleport" "model" "*1" "target" "out" }
            { "classname" "misc_teleporter_dest" "origin" "1000 0 24" "targetname" "out" }
            """, Cube(new Vector3(200, 0, 32), 32));
        PlayerState s = StandingAt(200, 0);

        Touch(map, ref s);

        Assert.Equal(1, s.Teleports);
        Assert.Null(map.Teleporters[0].Pad);
    }

    // Push zones.

    [Theory]
    [InlineData("\"angle\" \"-1\"", 0f, 0f, 1f)]
    [InlineData("\"angle\" \"-2\"", 0f, 0f, -1f)]
    [InlineData("\"angle\" \"90\"", 0f, 1f, 0f)]
    [InlineData("\"angles\" \"-90 0 0\"", 0f, 0f, 1f)]
    [InlineData("\"angles\" \"-45 180 0\"", -0.7071f, 0f, 0.7071f)]
    [InlineData("\"angles\" \"-74 0 0 0\"", 0.2756f, 0f, 0.9613f)]
    [InlineData("", 1f, 0f, 0f)]
    public void APushZoneGoesTheWayTheMapSays(string direction, float x, float y, float z)
    {
        MapObjects map = Spawn($$"""{ "classname" "trigger_push" "model" "*1" "speed" "70" {{direction}} }""", Cube(Vector3.Zero, 64));

        PushZone push = Assert.Single(map.PushZones);
        Assert.Equal(700f, push.Speed);
        Assert.Equal(new Vector3(x, y, z), push.Direction, new Vector3Within(0.001f));
    }

    [Fact]
    public void APushZoneWithNoSpeedIsHeldToTheTopSpeed()
    {
        MapObjects map = Spawn("""{ "classname" "trigger_push" "model" "*1" "angle" "-1" }""", Cube(Vector3.Zero, 64));

        Assert.Equal(PushZone.TopSpeed, Assert.Single(map.PushZones).Speed);
    }

    [Fact]
    public void APushZoneSetsYourVelocityEveryTickYouAreInIt()
    {
        MapObjects map = Spawn("""{ "classname" "trigger_push" "model" "*1" "angle" "90" "speed" "50" }""", Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0) with { OnGround = true };
        s.Velocity = new Vector3(100, 0, 0);

        Touch(map, ref s);
        Assert.Equal(new Vector3(0, 500, 0), s.Velocity, new Vector3Within(0.01f));
        Assert.Equal(MapEventKind.Pushed, Assert.Single(map.Events).Kind);

        s.Velocity = Vector3.Zero;
        Touch(map, ref s);
        Assert.Equal(new Vector3(0, 500, 0), s.Velocity, new Vector3Within(0.01f));
        Assert.Empty(map.Events);
    }

    [Fact]
    public void AJumpPadLaunchesYouAboveItself()
    {
        var world = new TestWorld().Floor();
        MapObjects objects = MapObjects.Spawn(
            BspEntity.Parse("""{ "classname" "trigger_push" "model" "*1" "angle" "-1" "speed" "70" }"""),
            [default, new Box(new Vector3(-32, -32, 0), new Vector3(32, 32, 64))],
            world);
        var sim = new Simulation(world, new MoveSettings(), objects);
        sim.AddPlayer(new Vector3(0, 0, 16));

        float highest = 0;
        for (int i = 0; i < 120; i++)
        {
            sim.Tick([Idle()]);
            highest = MathF.Max(highest, sim.Players[0].Position.Z);
        }

        // A body touches the zone, which reaches z 64, until it stands at 90: feet 24 below it, plus
        // the reach on both boxes. Leaving at 700 a second then climbs 700 * 700 / (2 * 800) = 306.
        Assert.InRange(highest, 90 + 306 - 20, 90 + 306 + 20);
    }

    [Fact]
    public void APushOnceZoneGoesAfterOnePush()
    {
        MapObjects map = Spawn("""{ "classname" "trigger_push" "model" "*1" "angle" "-1" "speed" "70" "spawnflags" "1" }""", Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0) with { OnGround = true };

        Touch(map, ref s);
        Assert.Equal(700f, s.Velocity.Z);

        s.Velocity = Vector3.Zero;
        Touch(map, ref s);
        Assert.Equal(0f, s.Velocity.Z);
        Assert.True(map.PushZones[0].Removed);
    }

    private const string JumpPad = """{ "classname" "trigger_push" "model" "*1" "angle" "-1" "speed" "70" }""";

    [Fact]
    public void AJumpPadLetsYouFallThroughIt()
    {
        MapObjects map = Spawn(JumpPad, Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0) with { Velocity = new Vector3(50, 0, -300) };

        Touch(map, ref s);

        Assert.True(map.PushZones[0].Lifts);
        Assert.Equal(new Vector3(50, 0, -300), s.Velocity);
        Assert.Empty(map.Events);
    }

    [Theory]
    [InlineData(true, -10f)] // just landed on it
    [InlineData(false, 100f)] // rising into it
    public void AJumpPadThrowsYouUpWhenYouLandOnItOrRiseIntoIt(bool onGround, float upward)
    {
        MapObjects map = Spawn(JumpPad, Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0) with { OnGround = onGround, Velocity = new Vector3(0, 0, upward) };

        Touch(map, ref s);

        Assert.Equal(700f, s.Velocity.Z);
    }

    [Theory]
    [InlineData(false, -300f, false)] // falling through it
    [InlineData(false, 0f, false)] // stepping off an edge into it
    [InlineData(false, 100f, true)] // rising into it
    [InlineData(true, 0f, true)] // walking on its floor
    public void ASidewaysPushActsOnItsFloorAndWhileYouRise(bool onGround, float upward, bool pushed)
    {
        MapObjects map = Spawn("""{ "classname" "trigger_push" "model" "*1" "angle" "90" "speed" "50" }""", Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0) with { OnGround = onGround, Velocity = new Vector3(0, 0, upward) };

        Touch(map, ref s);

        Assert.False(map.PushZones[0].Lifts);
        Assert.Equal(pushed ? 500f : 0f, s.Velocity.Y, 0.01f);
    }

    [Fact]
    public void StandingOnALedgeWithYourHeadInAPushZoneDoesntPushYou()
    {
        // Like q2pduel1's: a sideways zone floats over the shaft, 40 above the ledge beside it.
        var world = new TestWorld().Floor().Box(new Vector3(-200, -64, 0), new Vector3(-64, 64, 128));
        MapObjects map = MapObjects.Spawn(
            BspEntity.Parse("""{ "classname" "trigger_push" "model" "*1" "angle" "180" "speed" "30" }"""),
            [default, new Box(new Vector3(-64, -64, 168), new Vector3(64, 64, 240))],
            world);
        PlayerState s = PlayerState.At(new Vector3(-70, 0, 128 + FeetOnFloor)) with { OnGround = true };
        Assert.True(map.PushZones[0].Volume.Touches(s.Position + s.Mins, s.Position + s.Maxs));
        Assert.Equal(0f, map.PushZones[0].Floor!.Value, 0.1f);

        Touch(map, ref s);
        Assert.Equal(Vector3.Zero, s.Velocity);

        map.ClearEvents();
        map.Touch(0, ref s, Idle(), new MoveSettings { OriginalPushZones = true });
        Assert.Equal(-300f, s.Velocity.X, 0.01f);
    }

    [Fact]
    public void SteppingOffAnEdgeIntoAJumpPadDoesntCatchYou()
    {
        // In the air with no fall speed yet, as on the tick you walk off an edge.
        MapObjects map = Spawn(JumpPad, Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0) with { OnGround = false, Velocity = new Vector3(100, 0, 0) };

        Touch(map, ref s);

        Assert.Equal(new Vector3(100, 0, 0), s.Velocity);
    }

    /// <summary>A pad shaft 128 deep with its column poking 16 above a ledge beside it, like q2pduel1's.</summary>
    private static (MapObjects Map, TestWorld World) PadBesideALedge()
    {
        var world = new TestWorld().Floor().Box(new Vector3(-200, -64, 0), new Vector3(-64, 64, 128));
        MapObjects map = MapObjects.Spawn(
            BspEntity.Parse(JumpPad),
            [default, new Box(new Vector3(-64, -64, 0), new Vector3(64, 64, 144))],
            world);
        return (map, world);
    }

    [Fact]
    public void AJumpPadsFloorIsTheGroundInsideIt()
    {
        (MapObjects map, _) = PadBesideALedge();
        MapObjects raised = MapObjects.Spawn(
            BspEntity.Parse(JumpPad),
            [default, new Box(new Vector3(-64, -64, -32), new Vector3(64, 64, 200))],
            new TestWorld().Floor().Box(new Vector3(-40, -40, 0), new Vector3(40, 40, 24)));

        Assert.Equal(0f, map.PushZones[0].Floor!.Value, 0.1f);
        Assert.Equal(24f, raised.PushZones[0].Floor!.Value, 0.1f);
    }

    [Fact]
    public void StandingOnALedgeAtAJumpPadsEdgeDoesntThrowYou()
    {
        (MapObjects map, _) = PadBesideALedge();

        // On the ledge, 128 up, with the body reaching over the shaft's edge into the pad's column.
        PlayerState s = PlayerState.At(new Vector3(-70, 0, 128 + FeetOnFloor)) with { OnGround = true };
        Assert.True(map.PushZones[0].Volume.Touches(s.Position + s.Mins, s.Position + s.Maxs));

        Touch(map, ref s);
        Assert.Equal(0f, s.Velocity.Z);

        map.ClearEvents();
        map.Touch(0, ref s, Idle(), new MoveSettings { OriginalPushZones = true });
        Assert.Equal(700f, s.Velocity.Z);
    }

    [Fact]
    public void CrouchingOffALedgeDropsYouDownAJumpPadAndItThrowsYouBackUp()
    {
        (MapObjects objects, TestWorld world) = PadBesideALedge();
        var sim = new Simulation(world, new MoveSettings(), objects);
        sim.AddPlayer(new Vector3(-150, 0, 128 + 16));

        float lowest = float.MaxValue, highestAfter = 0;
        for (int i = 0; i < 300; i++)
        {
            sim.Tick([i < 120 ? new MoveCommand(0, 0, 1, 0, Crouch: true) : Idle()]);
            PlayerState s = sim.Players[0];
            lowest = MathF.Min(lowest, s.Position.Z);
            if (lowest < 30)
            {
                highestAfter = MathF.Max(highestAfter, s.Position.Z);
            }
        }

        Assert.True(lowest < 30, $"never reached the pad's floor, lowest {lowest}");
        Assert.True(highestAfter > 144 + 200, $"wasn't thrown back up, reached {highestAfter}");
    }

    [Fact]
    public void TheSettingBringsBackPadsThatCatchYouFalling()
    {
        MapObjects map = Spawn(JumpPad, Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0) with { Velocity = new Vector3(0, 0, -300) };

        map.ClearEvents();
        map.Touch(0, ref s, Idle(), new MoveSettings { OriginalPushZones = true });

        Assert.Equal(700f, s.Velocity.Z);
    }

    [Fact]
    public void DroppingIntoAJumpPadLandsYouOnItThenThrowsYouUp()
    {
        var world = new TestWorld().Floor();
        MapObjects objects = MapObjects.Spawn(
            BspEntity.Parse(JumpPad),
            [default, new Box(new Vector3(-64, -64, 0), new Vector3(64, 64, 200))],
            world);
        var sim = new Simulation(world, new MoveSettings(), objects);
        sim.AddPlayer(new Vector3(0, 0, 400));

        float? pushedFrom = null;
        float highest = 0;
        for (int i = 0; i < 240; i++)
        {
            sim.Tick([Idle()]);
            if (pushedFrom is null && objects.Events.Any(e => e.Kind == MapEventKind.Pushed))
            {
                pushedFrom = sim.Players[0].Position.Z;
            }

            if (pushedFrom is not null)
            {
                highest = MathF.Max(highest, sim.Players[0].Position.Z);
            }
        }

        // Fell the whole column to its floor before the first push, then went back out of its top.
        Assert.Equal(FeetOnFloor, pushedFrom!.Value, 1f);
        Assert.True(highest > 200 + 24 + 250, $"only reached {highest}");
    }

    // Touch triggers.

    private static string Trigger(string classname, string keys = "") =>
        $$"""{ "classname" "{{classname}}" "model" "*1" "target" "door" {{keys}} }""" + Door;

    [Fact]
    public void ATriggerMultipleFiresAgainOnlyAfterItsWait()
    {
        MapObjects map = Spawn(Trigger("trigger_multiple", "\"wait\" \"1\""), Cube(Vector3.Zero, 64));
        var fired = new List<long>();
        for (int i = 0; i < 150; i++)
        {
            PlayerState s = StandingAt(0, 0);
            Touch(map, ref s);
            fired.AddRange(map.Events.Where(e => e.Kind == MapEventKind.Used).Select(_ => map.Ticks));
            map.Advance();
        }

        Assert.Equal([0L, 60L, 120L], fired);
    }

    [Fact]
    public void ATriggerMultipleWaitsAFifthOfASecondByDefault()
    {
        MapObjects map = Spawn(Trigger("trigger_multiple"), Cube(Vector3.Zero, 64));

        Assert.Equal(12, ((TouchTrigger)map.All[0]).WaitTicks);
    }

    [Fact]
    public void ATriggerOnceFiresOnceAndGoes()
    {
        MapObjects map = Spawn(Trigger("trigger_once"), Cube(Vector3.Zero, 64));
        int fired = 0;
        for (int i = 0; i < 100; i++)
        {
            PlayerState s = StandingAt(0, 0);
            Touch(map, ref s);
            fired += map.Events.Count(e => e.Kind == MapEventKind.Used);
            map.Advance();
        }

        Assert.Equal(1, fired);
        Assert.True(map.All[0].Removed);
    }

    [Fact]
    public void ATriggerThatStartsOffIsSwitchedOnByItsFirstUse()
    {
        MapObjects map = Spawn(Trigger("trigger_multiple", "\"targetname\" \"arm\" \"spawnflags\" \"4\""), Cube(Vector3.Zero, 64));
        var trigger = (TouchTrigger)map.All[0];
        PlayerState s = StandingAt(0, 0);

        Touch(map, ref s);
        Assert.Empty(map.Events);

        trigger.Use(map, -1);
        Assert.True(trigger.IsOn);
        Assert.Empty(map.Events);

        Touch(map, ref s);
        Assert.Single(map.Events, e => e.Kind == MapEventKind.Used);
    }

    [Fact]
    public void ANotPlayerTriggerIgnoresPlayersButWorksByName()
    {
        MapObjects map = Spawn(Trigger("trigger_multiple", "\"spawnflags\" \"2\""), Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0);

        Touch(map, ref s);
        Assert.Empty(map.Events);

        map.All[0].Use(map, 0);
        Assert.Single(map.Events, e => e.Kind == MapEventKind.Used);
    }

    [Fact]
    public void ATriggerWithAnAngleOnlyFiresForSomeoneFacingThatWay()
    {
        MapObjects map = Spawn(Trigger("trigger_multiple", "\"angle\" \"90\""), Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0);

        Touch(map, ref s, Idle(yaw: 270));
        Assert.Empty(map.Events);

        Touch(map, ref s, Idle(yaw: 45));
        Assert.Single(map.Events, e => e.Kind == MapEventKind.Used);
    }

    [Fact]
    public void ATriggerIsTouchedFromJustOutsideItsFace()
    {
        // The player's side is at x = 16 when standing at 0, so a face at 17.5 is 1.5 away and one at 18.5 is 2.5.
        MapObjects near = Spawn(Trigger("trigger_once"), new Box(new Vector3(17.5f, -64, 0), new Vector3(100, 64, 64)));
        MapObjects far = Spawn(Trigger("trigger_once"), new Box(new Vector3(18.5f, -64, 0), new Vector3(100, 64, 64)));
        PlayerState s = StandingAt(0, 0);

        Touch(near, ref s);
        Touch(far, ref s);

        Assert.NotEmpty(near.Events);
        Assert.Empty(far.Events);
    }

    [Fact]
    public void ABrushEntityWithAnOriginIsMovedByIt()
    {
        MapObjects map = Spawn(Trigger("trigger_once", "\"origin\" \"500 0 0\""), Cube(Vector3.Zero, 32));

        Assert.Equal(new Vector3(500, 0, 0), ((TouchObject)map.All[0]).Volume.Centre);
    }

    [Fact]
    public void ABrushEntityWithoutAModelIsListedAndDoesNothing()
    {
        MapObjects map = Spawn("""{ "classname" "trigger_push" "model" "*7" }{ "classname" "trigger_once" }""");

        Assert.Empty(map.PushZones);
        Assert.Equal(2, map.Problems.Count);
    }

    // Names, targets, delays and messages.

    [Fact]
    public void ADelayHoldsTheTargetsBack()
    {
        MapObjects map = Spawn("""{ "classname" "trigger_relay" "targetname" "go" "target" "door" "delay" "0.5" }""" + Door);

        map.All[0].Use(map, 0);
        Assert.Empty(map.Events);

        Assert.Equal([30L], UsesOfTheDoor(Run(map, 60)));
    }

    [Fact]
    public void AKilltargetTakesThingsOutOfTheMap()
    {
        MapObjects map = Spawn(PadAndExit.Replace("\"target\" \"out\"", "\"target\" \"out\" \"targetname\" \"pad\"") + """
            { "classname" "trigger_relay" "targetname" "go" "killtarget" "pad" }
            """);
        MapObject relay = map.Named("go")[0];

        relay.Use(map, 0);
        Assert.Single(map.Events, e => e.Kind == MapEventKind.Removed && e.Entity == 0);

        PlayerState s = StandingAt(0, 0);
        Touch(map, ref s);
        Assert.Equal(0, s.Teleports);
    }

    [Fact]
    public void KillingTheExitStopsTheTeleporter()
    {
        MapObjects map = Spawn(PadAndExit + """{ "classname" "trigger_relay" "targetname" "go" "killtarget" "out" }""");
        map.Named("go")[0].Use(map, 0);

        PlayerState s = StandingAt(0, 0);
        Touch(map, ref s);

        Assert.Equal(0, s.Teleports);
    }

    [Fact]
    public void AMessageGoesToThePlayerWhoSetItOffWithItsLineBreaks()
    {
        MapObjects map = Spawn(Trigger("trigger_once", """ "message" "You found\na secret" """), Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0);

        Touch(map, ref s);

        MapEvent message = Assert.Single(map.Events, e => e.Kind == MapEventKind.Message);
        Assert.Equal(0, message.Player);
        Assert.Equal("You found\na secret", message.Text);
    }

    [Fact]
    public void AMessageSetOffByTheWorldGoesToNobody()
    {
        MapObjects map = Spawn("""{ "classname" "trigger_always" "target" "door" "message" "hello" }""" + Door);

        List<(long Tick, MapEvent Event)> events = Run(map, 30);

        Assert.DoesNotContain(events, e => e.Event.Kind == MapEventKind.Message);
        Assert.Equal([12L], UsesOfTheDoor(events));
    }

    [Fact]
    public void ACounterFiresOnItsLastUseAndCountsDownOnTheWay()
    {
        MapObjects map = Spawn("""{ "classname" "trigger_counter" "targetname" "count" "count" "3" "target" "door" }""" + Door);
        MapObject counter = map.All[0];
        var said = new List<string?>();
        int fired = 0;
        for (int i = 0; i < 4; i++)
        {
            map.ClearEvents();
            counter.Use(map, 0);
            said.AddRange(map.Events.Where(e => e.Kind == MapEventKind.Message).Select(e => e.Text));
            fired += map.Events.Count(e => e.Kind == MapEventKind.Used);
        }

        Assert.Equal(["2 more to go...", "1 more to go...", "Sequence completed!"], said);
        Assert.Equal(1, fired);
        Assert.True(counter.Removed);
    }

    [Fact]
    public void AQuietCounterSaysNothing()
    {
        MapObjects map = Spawn("""{ "classname" "trigger_counter" "targetname" "count" "spawnflags" "769" "target" "door" }""" + Door);

        map.All[0].Use(map, 0);
        map.All[0].Use(map, 0);

        Assert.DoesNotContain(map.Events, e => e.Kind == MapEventKind.Message);
        Assert.Single(map.Events, e => e.Kind == MapEventKind.Used);
    }

    [Fact]
    public void ARelayThatTargetsItselfIsSkippedAndALoopIsCutOff()
    {
        MapObjects map = Spawn("""
            { "classname" "trigger_relay" "targetname" "self" "target" "self" }
            { "classname" "trigger_relay" "targetname" "a" "target" "b" }
            { "classname" "trigger_relay" "targetname" "b" "target" "a" }
            """);

        map.Named("self")[0].Use(map, 0);
        map.Named("a")[0].Use(map, 0);

        Assert.Equal(2, map.Problems.Count);
    }

    // Timers.

    [Fact]
    public void ATimerThatStartsOnFiresEveryWait()
    {
        MapObjects map = Spawn("""{ "classname" "func_timer" "spawnflags" "1" "wait" "1" "target" "door" }""" + Door);

        // Its first firing waits a second past the first wait.
        Assert.Equal([120L, 180L, 240L, 300L], UsesOfTheDoor(Run(map, 300)));
    }

    [Fact]
    public void ATimersSpreadStaysInsideItsRandomAndIsTheSameEachTime()
    {
        const string timer = """{ "classname" "func_timer" "spawnflags" "1" "wait" "1" "random" "0.5" "target" "door" }""" + Door;
        List<long> first = UsesOfTheDoor(Run(Spawn(timer), 6000));
        List<long> second = UsesOfTheDoor(Run(Spawn(timer), 6000));

        Assert.Equal(first, second);
        List<long> gaps = first.Zip(first.Skip(1), (a, b) => b - a).ToList();
        Assert.All(gaps, gap => Assert.InRange(gap, 30, 90));
        Assert.True(gaps.Distinct().Count() > 20);
    }

    [Fact]
    public void UsingATimerSwitchesItOnAndOff()
    {
        MapObjects map = Spawn("""{ "classname" "func_timer" "targetname" "clock" "wait" "0.5" "target" "door" }""" + Door);
        MapObject timer = map.All[0];

        Assert.Empty(UsesOfTheDoor(Run(map, 100)));

        timer.Use(map, 0);
        Assert.Single(map.Events, e => e.Kind == MapEventKind.Used);
        Assert.Equal([130L, 160L], UsesOfTheDoor(Run(map, 60)));

        timer.Use(map, 0);
        Assert.Empty(UsesOfTheDoor(Run(map, 200)));
    }

    // Hurt zones.

    [Fact]
    public void AHurtZoneIsKeptForLaterAndSwitchesAsItsFlagsSay()
    {
        MapObjects map = Spawn("""
            { "classname" "trigger_hurt" "model" "*1" }
            { "classname" "trigger_hurt" "model" "*1" "dmg" "100" "spawnflags" "1" "targetname" "once" }
            { "classname" "trigger_hurt" "model" "*1" "spawnflags" "3" "targetname" "toggle" }
            """, Cube(Vector3.Zero, 64));
        PlayerState s = StandingAt(0, 0);
        HurtZone plain = map.HurtZones[0], startsOff = map.HurtZones[1], toggle = map.HurtZones[2];

        Assert.Equal(5, plain.Damage);
        Assert.Equal(100, startsOff.Damage);
        Assert.True(plain.Touches(s));
        Assert.False(startsOff.Touches(s));

        plain.Use(map, 0);
        startsOff.Use(map, 0);
        startsOff.Use(map, 0);
        toggle.Use(map, 0);
        toggle.Use(map, 0);

        Assert.True(plain.IsOn);
        Assert.True(startsOff.IsOn);
        Assert.False(toggle.IsOn);
        Assert.Equal(Vector3.Zero, s.Velocity);
    }

    // Spawning.

    [Fact]
    public void EntitiesNotForDeathmatchAreLeftOut()
    {
        MapObjects map = Spawn("""
            { "classname" "misc_teleporter" "origin" "0 0 24" "target" "out" }
            { "classname" "misc_teleporter_dest" "origin" "1000 0 24" "targetname" "out" "spawnflags" "2048" }
            { "classname" "misc_teleporter_dest" "origin" "-1000 0 24" "targetname" "out" "spawnflags" "1792" }
            """);

        Assert.Equal(1, map.LeftOut);
        Assert.Equal(-1000f, EntityPosition(map.Teleporters[0].Exit!).X);
    }

    private static Vector3 EntityPosition(MapObject o) => o.Fields.GetVector("origin")!.Value;

    [Fact]
    public void ClassesNotBuiltYetAreListedOnceWithCounts()
    {
        MapObjects map = Spawn("""
            { "classname" "worldspawn" }
            { "classname" "light" }
            { "classname" "info_player_deathmatch" }
            { "classname" "monster_soldier" }
            { "classname" "func_door" }
            { "classname" "func_door" }
            { "classname" "weapon_railgun" }
            """);

        Assert.Equal([("func_door", 2), ("weapon_railgun", 1)], map.Unhandled);
        Assert.Contains("not handled yet: func_door 2, weapon_railgun 1", map.Summary());
    }

    [Fact]
    public void MovementIsUntouchedAwayFromMapObjects()
    {
        var world = new TestWorld().Floor().Box(new Vector3(300, -400, 0), new Vector3(332, 400, 80));
        MapObjects objects = MapObjects.Spawn(BspEntity.Parse("""
            { "classname" "misc_teleporter" "origin" "-2000 0 24" "target" "out" }
            { "classname" "misc_teleporter_dest" "origin" "1000 0 24" "targetname" "out" }
            { "classname" "trigger_push" "model" "*1" "angle" "-1" }
            { "classname" "func_timer" "spawnflags" "1" "wait" "0.1" "target" "door" }
            """ + Door), [default, Cube(new Vector3(0, 2000, 0), 64)], world);
        var plain = new Simulation(world, new MoveSettings());
        var withObjects = new Simulation(world, new MoveSettings(), objects);
        plain.AddPlayer(new Vector3(0, 0, 16));
        withObjects.AddPlayer(new Vector3(0, 0, 16));

        int doorUses = 0;
        for (int i = 0; i < 600; i++)
        {
            var cmd = new MoveCommand(i * 0.7f, 0, 1, (i / 50) % 2 == 0 ? 1 : -1, Jump: true, Attack: i % 90 < 20);
            plain.Tick([cmd]);
            withObjects.Tick([cmd]);
            doorUses += objects.Events.Count(e => e.Kind == MapEventKind.Used);
            Assert.Equal(plain.Players[0].Position, withObjects.Players[0].Position);
            Assert.Equal(plain.Players[0].Velocity, withObjects.Players[0].Velocity);
        }

        // The timer was running all along, so the objects were live, just never in the way.
        Assert.True(doorUses > 50);
        Assert.Equal(0, withObjects.Players[0].Teleports);
    }

    /// <summary>Compares vectors to within a tolerance on each axis.</summary>
    private sealed class Vector3Within(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) =>
            MathF.Abs(a.X - b.X) <= tolerance && MathF.Abs(a.Y - b.Y) <= tolerance && MathF.Abs(a.Z - b.Z) <= tolerance;

        public int GetHashCode(Vector3 v) => 0;
    }
}
