using System.Numerics;
using SimpleFps.Core.Collision;
using SimpleFps.Core.Movement;

namespace SimpleFps.Core.Tests;

public class MovementTests
{
    private const float FeetOnFloor = 24.1f;

    private static PlayerState Standing(float x = 0, float y = 0) => PlayerState.At(new Vector3(x, y, FeetOnFloor));

    private static PlayerState Run(ICollisionWorld world, PlayerState s, MoveCommand cmd, int ticks, MoveSettings? settings = null)
    {
        var move = new PlayerMove(world, settings ?? new MoveSettings());
        for (int i = 0; i < ticks; i++)
        {
            move.Step(ref s, cmd);
        }

        return s;
    }

    private static MoveCommand Forward(float yaw = 0, bool jump = false, bool crouch = false, float pitch = 0) =>
        new(yaw, pitch, 1, 0, jump, crouch);

    private static MoveCommand Idle(float yaw = 0, bool jump = false) => new(yaw, 0, 0, 0, jump);

    /// <summary>A player already moving along +X at a given speed.</summary>
    private static PlayerState Moving(float speed)
    {
        PlayerState s = Standing();
        s.Velocity = new Vector3(speed, 0, 0);
        return s;
    }

    private static float Heading(PlayerState s) => float.RadiansToDegrees(MathF.Atan2(s.Velocity.Y, s.Velocity.X));

    // Basics.

    [Fact]
    public void StandingStillStaysPut()
    {
        PlayerState s = Run(new TestWorld().Floor(), Standing(), Idle(), 120);

        Assert.True(s.OnGround);
        Assert.Equal(0f, s.Position.X);
        Assert.Equal(24f, s.Position.Z, 0.5f);
    }

    [Fact]
    public void RunningReachesRunSpeedAndHoldsIt()
    {
        PlayerState s = Run(new TestWorld().Floor(), Standing(), Forward(), 120);

        Assert.Equal(300f, s.FlatSpeed, 1f);
    }

    [Fact]
    public void DiagonalKeysAreNoFasterThanForward()
    {
        PlayerState s = Run(new TestWorld().Floor(), Standing(), new MoveCommand(0, 0, 1, 1), 120);

        Assert.Equal(300f, s.FlatSpeed, 1f);
    }

    [Fact]
    public void FrictionStopsARunnerQuickly()
    {
        PlayerState s = Run(new TestWorld().Floor(), Moving(300), Idle(), 40);

        Assert.Equal(0f, s.FlatSpeed);
    }

    [Fact]
    public void AJumpPeaksAtAboutFortyFiveUnits()
    {
        var world = new TestWorld().Floor();
        var move = new PlayerMove(world, new MoveSettings());
        PlayerState s = Standing();
        float peak = 0;
        for (int i = 0; i < 60; i++)
        {
            move.Step(ref s, Idle(jump: i == 0));
            peak = MathF.Max(peak, s.Position.Z - 24f);
        }

        Assert.InRange(peak, 43f, 47f);
    }

    [Fact]
    public void FallingLandsOnTheFloor()
    {
        PlayerState s = Run(new TestWorld().Floor(), PlayerState.At(new Vector3(0, 0, 300)), Idle(), 120);

        Assert.True(s.OnGround);
        Assert.Equal(24f, s.Position.Z, 0.5f);
        Assert.Equal(0f, s.Velocity.Z);
    }

    // Hopping, the ice part.

    private const float LongRun = 900;

    [Fact]
    public void HoldingJumpKeepsSpeedThroughManyHops()
    {
        PlayerState s = Run(new TestWorld().Floor(), Moving(600), Forward(jump: true), 600);

        Assert.InRange(s.FlatSpeed, 595f, 601f);
    }

    [Fact]
    public void LandingWithoutJumpScrubsSpeedOff()
    {
        var world = new TestWorld().Floor();
        PlayerState s = PlayerState.At(new Vector3(0, 0, 60));
        s.Velocity = new Vector3(600, 0, 0);

        PlayerState landed = Run(world, s, Idle(), 90);

        Assert.True(landed.FlatSpeed < 300f, $"Still at {landed.FlatSpeed}.");
    }

    [Fact]
    public void HoppingFromAStandstillBuildsToRunSpeed()
    {
        PlayerState s = Run(new TestWorld().Floor(), Standing(), Forward(jump: true), 600);

        Assert.InRange(s.FlatSpeed, 250f, 301f);
    }

    [Fact]
    public void StrafingInTheAirNeverGainsSpeed()
    {
        var world = new TestWorld().Floor();
        var move = new PlayerMove(world, new MoveSettings());
        PlayerState s = Moving(500);
        float fastest = 0;

        // The classic trick: swing the view and strafe together, in both directions.
        for (int i = 0; i < 1200; i++)
        {
            int phase = i / 40 % 2;
            float side = phase == 0 ? 1 : -1;
            float yaw = float.RadiansToDegrees(MathF.Atan2(s.Velocity.Y, s.Velocity.X)) + (side * -70f);
            move.Step(ref s, new MoveCommand(yaw, 0, 0.3f, side, Jump: true));
            fastest = MathF.Max(fastest, s.FlatSpeed);
        }

        Assert.True(fastest <= 501f, $"Reached {fastest}.");
    }

    [Fact]
    public void AirSteeringBarelyChangesDirection()
    {
        var world = new TestWorld().Floor();
        PlayerState s = PlayerState.At(new Vector3(0, 0, 2000));
        s.Velocity = new Vector3(500, 0, 0);

        // Half a second in the air, pushing hard to the side.
        PlayerState after = Run(world, s, new MoveCommand(0, 0, 0, -1), 30);

        Assert.InRange(Heading(after), 12f, 18f);
        Assert.Equal(500f, after.FlatSpeed, 1f);
    }

    [Fact]
    public void ATouchOfTheGroundChangesDirectionMuchMoreThanTheAirCan()
    {
        var world = new TestWorld().Floor();
        PlayerState s = PlayerState.At(new Vector3(0, 0, 24.5f));
        s.Velocity = new Vector3(500, 0, 0);

        // Facing 60 degrees off the direction of travel. Three ticks of 15 degrees on the ground.
        PlayerState after = Run(world, s, Forward(yaw: 60, jump: true), 6);

        Assert.InRange(Heading(after), 45f, 48f);
    }

    [Fact]
    public void ASharpHopTurnCostsMoreSpeedThanAGentleOne()
    {
        PlayerState Hop(float yaw)
        {
            PlayerState s = PlayerState.At(new Vector3(0, 0, 24.5f));
            s.Velocity = new Vector3(600, 0, 0);
            return Run(new TestWorld().Floor(), s, Forward(yaw: yaw, jump: true), 6);
        }

        float gentle = Hop(10).FlatSpeed;
        float sharp = Hop(90).FlatSpeed;

        Assert.True(gentle > sharp + 30f, $"gentle {gentle}, sharp {sharp}");
        Assert.True(gentle > 580f, $"gentle {gentle}");
        Assert.True(sharp < 560f, $"sharp {sharp}");
    }

    [Fact]
    public void TurnCostCanBeSwitchedOff()
    {
        PlayerState s = PlayerState.At(new Vector3(0, 0, 24.5f));
        s.Velocity = new Vector3(600, 0, 0);

        PlayerState after = Run(new TestWorld().Floor(), s, Forward(yaw: 90, jump: true), 6, new MoveSettings { HopTurnCost = 0 });

        Assert.Equal(600f, after.FlatSpeed, 1f);
    }

    // Recoil.

    [Fact]
    public void RecoilInTheAirPushesYouBackwards()
    {
        var sim = new Simulation(new TestWorld().Floor(), new MoveSettings());
        int player = sim.AddPlayer(new Vector3(0, 0, 2000));

        // Facing -X while firing should thrust the player along +X.
        for (int i = 0; i < 30; i++)
        {
            sim.Tick([new MoveCommand(180, 0, 0, 0, Attack: true)]);
        }

        PlayerState s = sim.Players[player];
        Assert.InRange(s.Velocity.X, 150f, 250f);
    }

    [Fact]
    public void RecoilAloneCanTakeAHopperPastRunSpeed()
    {
        var world = new TestWorld().Floor();
        var sim = new Simulation(world, new MoveSettings());
        sim.AddPlayer(new Vector3(0, 0, 24));
        sim.Teleport(0, new Vector3(0, 0, FeetOnFloor));

        for (int i = 0; i < 600; i++)
        {
            sim.Tick([new MoveCommand(180, 0, 0, 0, Jump: true, Attack: true)]);
        }

        Assert.InRange(sim.Players[0].FlatSpeed, 400f, 701f);
    }

    [Fact]
    public void RecoilStopsAtItsTopSpeed()
    {
        PlayerState s = Moving(690);

        PlayerMove.AddKick(ref s, Vector3.UnitX, 40, 700);
        Assert.Equal(700f, s.Velocity.X, 0.01f);

        PlayerMove.AddKick(ref s, Vector3.UnitX, 40, 700);
        Assert.Equal(700f, s.Velocity.X, 0.01f);
    }

    [Fact]
    public void RecoilTopSpeedDoesNotSlowSomeoneAlreadyFaster()
    {
        PlayerState s = Moving(900);

        PlayerMove.AddKick(ref s, Vector3.UnitX, 40, 700);

        Assert.Equal(900f, s.Velocity.X, 0.01f);
    }

    // Stairs and slopes.

    [Theory]
    [InlineData(10, true)]
    [InlineData(18, true)]
    [InlineData(20, false)]
    public void StepsUpToStepHeightAreClimbedWithoutJumping(float height, bool climbs)
    {
        var world = new TestWorld().Floor().Box(new Vector3(100, -500, 0), new Vector3(1000, 500, height));

        PlayerState s = Run(world, Standing(), Forward(), 120);

        Assert.Equal(climbs, s.Position.X > 200f);
    }

    [Fact]
    public void AWalkableSlopeCanBeRunUp()
    {
        PlayerState s = Run(new TestWorld().Floor().Ramp(100, 30), Standing(), Forward(), 120);

        Assert.True(s.Position.Z > 80f, $"Only reached height {s.Position.Z}.");
        Assert.True(s.OnGround);
    }

    [Fact]
    public void ASlopeTooSteepToWalkOnStopsARunner()
    {
        PlayerState s = Run(new TestWorld().Floor().Ramp(100, 60), Standing(), Forward(), 120);

        Assert.True(s.Position.Z < 60f, $"Climbed to {s.Position.Z}.");
    }

    // Walls.

    [Fact]
    public void RunningStraightIntoAWallStopsAtTheWall()
    {
        var world = new TestWorld().Floor().Box(new Vector3(100, -500, 0), new Vector3(200, 500, 500));

        PlayerState s = Run(world, Standing(), Forward(), 120);

        Assert.InRange(s.Position.X, 84f, 88.1f);
    }

    [Fact]
    public void RunningAlongAWallKeepsMostOfTheSpeed()
    {
        var world = new TestWorld().Floor().Box(new Vector3(-2000, 100, 0), new Vector3(2000, 200, 500));

        PlayerState s = Run(world, Standing(0, 50), Forward(yaw: 45), 120);

        Assert.True(s.Position.X > 200f, $"Only reached {s.Position.X}.");
        Assert.InRange(s.Position.Y, 80f, 88.1f);
    }

    // Wall grace: corner slide.

    /// <summary>A column whose near corner sticks across the player's path by the given amount.</summary>
    private static TestWorld CornerInTheWay(float overlap) => new TestWorld().Floor()
        .Box(new Vector3(100, 16 - overlap, 0), new Vector3(200, 300, 500));

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void ClippingACornerEasesYouRoundItWithNoSpeedLost(float overlap)
    {
        PlayerState s = Run(CornerInTheWay(overlap), Moving(300), Forward(), 60);

        Assert.True(s.Position.X > 280f, $"Stuck at {s.Position.X}.");
        Assert.InRange(s.Position.Y, -overlap - 2.1f, -overlap + 1f);
        Assert.Equal(300f, s.FlatSpeed, 1f);
    }

    [Fact]
    public void ClippingACornerAtSpeedInTheAirLosesNothing()
    {
        PlayerState s = Moving(600);
        s.Position.Z = 30f;

        PlayerState after = Run(CornerInTheWay(6), s, Forward(jump: true), 30);

        Assert.True(after.Position.X > 200f, $"Stuck at {after.Position.X}.");
        Assert.Equal(600f, after.FlatSpeed, 1f);
    }

    [Fact]
    public void WithCornerSlideOffACornerStopsYou()
    {
        var settings = new MoveSettings { CornerSlideReach = 0, SqueezeAmount = 0 };

        PlayerState s = Run(CornerInTheWay(6), Moving(300), Forward(), 60, settings);

        Assert.True(s.Position.X < 90f, $"Got past to {s.Position.X}.");
    }

    [Fact]
    public void ACornerTooFarAcrossYourPathStillStopsYou()
    {
        PlayerState s = Run(CornerInTheWay(20), Moving(300), Forward(), 60);

        Assert.True(s.Position.X < 90f, $"Got past to {s.Position.X}.");
    }

    [Fact]
    public void AFlatWallNeverTriggersACornerSlide()
    {
        var world = new TestWorld().Floor().Box(new Vector3(100, -2000, 0), new Vector3(200, 2000, 500));
        var move = new PlayerMove(world, new MoveSettings { SqueezeAmount = 0 });
        PlayerState s = Moving(300);
        var straightOn = Forward();

        for (int i = 0; i < 120; i++)
        {
            move.Step(ref s, straightOn);
        }

        Assert.Equal(0f, s.Position.Y, 0.01f);
    }

    [Fact]
    public void RunningThroughADoorwayOffCentreLinesYouUp()
    {
        // A 48 wide door, with the player 14 off centre: one jamb catches them by 6.
        var world = new TestWorld().Floor()
            .Box(new Vector3(100, 24, 0), new Vector3(120, 400, 500))
            .Box(new Vector3(100, -400, 0), new Vector3(120, -24, 500));

        PlayerState s = Run(world, Moving(300) with { Position = new Vector3(0, 14, FeetOnFloor) }, Forward(), 60);

        Assert.True(s.Position.X > 200f, $"Stuck at {s.Position.X}.");
    }

    // Wall grace: squeeze.

    private static TestWorld Gap(float width) => new TestWorld().Floor()
        .Box(new Vector3(100, width / 2f, 0), new Vector3(300, 500, 500))
        .Box(new Vector3(100, -500, 0), new Vector3(300, -width / 2f, 500));

    [Fact]
    public void SqueezingGetsYouThroughAGapNarrowerThanYourBody()
    {
        PlayerState s = Run(Gap(26), Standing(), Forward(), 180);

        Assert.True(s.Position.X > 340f, $"Stuck at {s.Position.X}.");
    }

    [Fact]
    public void YourBodySpringsBackAfterSqueezingThrough()
    {
        PlayerState s = Run(Gap(26), Standing(), Forward(), 240);

        Assert.Equal(PlayerHull.FullHalfWidth, s.HalfWidth);
    }

    [Fact]
    public void AGapTooNarrowEvenWhenSqueezedStopsYou()
    {
        PlayerState s = Run(Gap(20), Standing(), Forward(), 180);

        Assert.True(s.Position.X < 90f, $"Got through to {s.Position.X}.");
    }

    [Fact]
    public void WithSqueezeOffANarrowGapStopsYou()
    {
        PlayerState s = Run(Gap(26), Standing(), Forward(), 180, new MoveSettings { SqueezeAmount = 0 });

        Assert.True(s.Position.X < 90f, $"Got through to {s.Position.X}.");
    }

    [Fact]
    public void PressingIntoAWallLetsYouGetALittleCloser()
    {
        var world = new TestWorld().Floor().Box(new Vector3(100, -500, 0), new Vector3(200, 500, 500));

        PlayerState s = Run(world, Standing(), Forward(), 120);

        Assert.True(s.Position.X > 87f, $"Only reached {s.Position.X}.");
    }

    [Fact]
    public void SpringingBackBesideAWallNeverEndsUpInsideIt()
    {
        var world = new TestWorld().Floor().Box(new Vector3(-500, 16.5f, 0), new Vector3(500, 200, 500));
        var move = new PlayerMove(world, new MoveSettings());
        PlayerState s = Standing();
        s.HalfWidth = 12f;

        for (int i = 0; i < 300; i++)
        {
            move.Step(ref s, new MoveCommand(0, 0, 1, 0.02f));
            Assert.False(world.Trace(s.Position, s.Position, s.Mins, s.Maxs, Contents.PlayerSolid).StartSolid, $"Inside the wall on tick {i}.");
        }
    }

    [Fact]
    public void SpawningInABodyWidthPocketSqueezesYouFree()
    {
        // Walls exactly touching both sides, which counts as inside them.
        var world = new TestWorld().Floor()
            .Box(new Vector3(-100, 16, 0), new Vector3(100, 100, 500))
            .Box(new Vector3(-100, -100, 0), new Vector3(100, -16, 500));

        PlayerState s = Run(world, Standing(), Idle(), 2);

        Assert.True(s.HalfWidth < PlayerHull.FullHalfWidth);
        Assert.False(world.Trace(s.Position, s.Position, s.Mins, s.Maxs, Contents.PlayerSolid).StartSolid);
    }

    [Fact]
    public void WalkingAwayFromAWallRestoresYourFullWidth()
    {
        var world = new TestWorld().Floor().Box(new Vector3(100, -500, 0), new Vector3(200, 500, 500));
        PlayerState pressed = Run(world, Standing(), Forward(), 120);

        PlayerState s = Run(world, pressed, Forward(yaw: 180), 60);

        Assert.Equal(PlayerHull.FullHalfWidth, s.HalfWidth);
    }

    // Crouching.

    private static TestWorld LowTunnel() => new TestWorld().Floor()
        .Box(new Vector3(100, -500, 40), new Vector3(400, 500, 300));

    [Fact]
    public void YouCanOnlyGetUnderALowCeilingByCrouching()
    {
        Assert.True(Run(LowTunnel(), Standing(), Forward(), 180).Position.X < 90f);
        Assert.True(Run(LowTunnel(), Standing(), Forward(crouch: true), 300).Position.X > 200f);
    }

    [Fact]
    public void CrouchingSlowsYouDown()
    {
        PlayerState s = Run(new TestWorld().Floor(), Standing(), Forward(crouch: true), 120);

        Assert.Equal(100f, s.FlatSpeed, 1f);
    }

    [Fact]
    public void YouCannotStandUpUnderALowCeiling()
    {
        PlayerState under = Run(LowTunnel(), Standing(150), Forward(crouch: true), 10);
        Assert.True(under.Crouched);

        PlayerState s = Run(LowTunnel(), under, Idle(), 30);

        Assert.True(s.Crouched);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void CrouchingInMidAirDependsOnTheSetting(bool allowed, bool expected)
    {
        var settings = new MoveSettings { AllowCrouchJump = allowed };
        PlayerState s = PlayerState.At(new Vector3(0, 0, 2000));

        PlayerState after = Run(new TestWorld().Floor(), s, new MoveCommand(0, 0, 0, 0, Crouch: true), 5, settings);

        Assert.Equal(expected, after.Crouched);
    }

    // Ladders.

    private static TestWorld LadderAtX100() => new TestWorld().Floor()
        .Box(new Vector3(100, -64, 0), new Vector3(116, 64, 500), Contents.Solid | Contents.Ladder);

    [Fact]
    public void LookingUpAndPushingForwardClimbsALadder()
    {
        PlayerState s = Standing(83);

        PlayerState after = Run(LadderAtX100(), s, Forward(pitch: -30), 60);

        Assert.True(after.OnLadder);
        Assert.InRange(after.Position.Z, 180f, 240f);
    }

    [Fact]
    public void YouHangOnALadderInsteadOfFalling()
    {
        PlayerState s = Standing(83);
        s.Position.Z = 200f;

        PlayerState after = Run(LadderAtX100(), s, Idle(), 60);

        Assert.Equal(200f, after.Position.Z, 1f);
    }

    // Water.

    private static TestWorld Pool() => new TestWorld().Floor()
        .Box(new Vector3(-1000, -1000, 0), new Vector3(1000, 1000, 200), Contents.Water);

    [Theory]
    [InlineData(30, 1)]
    [InlineData(10, 2)]
    [InlineData(-20, 3)]
    public void WaterLevelFollowsDepth(float feetHeight, int level)
    {
        // The surface is at z = 40. Waist is 23 above the feet and eyes are 46 above.
        var world = new TestWorld().Box(new Vector3(-1000, -1000, -500), new Vector3(1000, 1000, 40), Contents.Water);
        PlayerState s = PlayerState.At(new Vector3(0, 0, feetHeight - PlayerHull.Bottom));

        PlayerState after = Run(world, s, Idle(), 1);

        Assert.Equal(level, after.WaterLevel);
    }

    [Fact]
    public void HoldingJumpSwimsUp()
    {
        PlayerState s = PlayerState.At(new Vector3(0, 0, 100));

        PlayerState after = Run(Pool(), s, new MoveCommand(0, 0, 0, 0, Jump: true), 60);

        Assert.True(after.Position.Z > 150f, $"Only reached {after.Position.Z}.");
    }

    [Fact]
    public void DoingNothingDriftsSlowlyDown()
    {
        PlayerState s = PlayerState.At(new Vector3(0, 0, 150));

        PlayerState after = Run(Pool(), s, Idle(), 60);

        Assert.InRange(after.Position.Z, 100f, 149f);
    }

    [Fact]
    public void SwimmingIsSlowerThanRunning()
    {
        PlayerState s = PlayerState.At(new Vector3(0, 0, 100));

        PlayerState after = Run(Pool(), s, Forward(), 120);

        Assert.InRange(after.FlatSpeed, 100f, 160f);
    }

    [Fact]
    public void YouCanClimbOutOfWaterOntoALedge()
    {
        // A pool wall whose top sits just above the surface, with dry land beyond.
        var world = new TestWorld().Box(new Vector3(-1000, -1000, -200), new Vector3(1000, 1000, -100))
            .Box(new Vector3(-1000, -1000, -100), new Vector3(100, 1000, 0), Contents.Water)
            .Box(new Vector3(100, -1000, -100), new Vector3(1000, 1000, 8));
        PlayerState s = PlayerState.At(new Vector3(84, 0, 0));

        PlayerState after = Run(world, s, Forward(jump: true), 120);

        Assert.True(after.Position.X > 110f, $"Stuck at {after.Position}.");
        Assert.True(after.Position.Z >= 31f, $"Below the ledge at {after.Position}.");
    }

    // Settings, and whether the whole thing can be trusted over a network.

    [Fact]
    public void SameCommandsGiveExactlyTheSameResult()
    {
        PlayerState Script()
        {
            TestWorld world = CornerInTheWay(6).Ramp(-800, 25);
            var move = new PlayerMove(world, new MoveSettings());
            PlayerState s = Standing();
            for (int i = 0; i < 800; i++)
            {
                move.Step(ref s, new MoveCommand(i * 0.7f, 0, 1, i % 90 < 45 ? 0.5f : -0.5f, Jump: i % 200 < 120));
            }

            return s;
        }

        PlayerState a = Script();
        PlayerState b = Script();

        Assert.Equal(a.Position, b.Position);
        Assert.Equal(a.Velocity, b.Velocity);
    }

    [Fact]
    public void SettingsSurviveBeingSaved()
    {
        var settings = new MoveSettings { AirSteer = 12, CornerSlideReach = 3, AllowCrouchJump = true };

        MoveSettings loaded = MoveSettings.FromJson(settings.ToJson());

        Assert.Equal(12f, loaded.AirSteer);
        Assert.Equal(3f, loaded.CornerSlideReach);
        Assert.True(loaded.AllowCrouchJump);
    }

    [Fact]
    public void UnreadableSavedSettingsFallBackToDefaults()
    {
        Assert.Equal(new MoveSettings().AirSteer, MoveSettings.FromJson("not json").AirSteer);
    }

    [Fact]
    public void EveryTunableReadsAndWritesItsOwnSetting()
    {
        foreach (Tunable tunable in MoveSettings.Tunables)
        {
            var settings = new MoveSettings();
            Assert.InRange(tunable.Get(settings), tunable.Min, tunable.Max);

            tunable.Set(settings, tunable.Max);

            Assert.Equal(tunable.Max, tunable.Get(settings));
            Assert.NotEqual(new MoveSettings().ToJson(), settings.ToJson());
        }
    }
}
