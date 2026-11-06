using System.Numerics;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Tests;

public class CollisionTests
{
    private static readonly Vector3 Mins = new(-16, -16, -24);
    private static readonly Vector3 Maxs = new(16, 16, 32);

    private static TraceResult Sweep(ICollisionWorld world, Vector3 from, Vector3 to, Contents mask = Contents.PlayerSolid) =>
        world.Trace(from, to, Mins, Maxs, mask);

    private static TestWorld WallAtX100() =>
        new TestWorld().Box(new Vector3(100, -200, -200), new Vector3(200, 200, 200));

    [Fact]
    public void AClearSweepReachesTheEnd()
    {
        TraceResult t = Sweep(WallAtX100(), Vector3.Zero, new Vector3(50, 0, 0));

        Assert.True(t.Clear);
        Assert.Equal(new Vector3(50, 0, 0), t.End);
    }

    [Fact]
    public void ABoxStopsWithItsFrontJustShortOfAWall()
    {
        TraceResult t = Sweep(WallAtX100(), Vector3.Zero, new Vector3(200, 0, 0));

        Assert.True(t.Hit);
        Assert.Equal(84f - 1f / 32f, t.End.X, 0.01f);
        Assert.Equal(-Vector3.UnitX, t.Normal);
    }

    [Fact]
    public void APointGetsCloserThanABox()
    {
        TraceResult t = WallAtX100().Trace(Vector3.Zero, new Vector3(200, 0, 0), Vector3.Zero, Vector3.Zero, Contents.Solid);

        Assert.Equal(100f - 1f / 32f, t.End.X, 0.01f);
    }

    [Fact]
    public void StartingOutsideAndMovingAwayNeverHits()
    {
        Assert.True(Sweep(WallAtX100(), new Vector3(83, 0, 0), new Vector3(-100, 0, 0)).Clear);
    }

    [Fact]
    public void ABoxAlreadyInsideTheGapCannotCreepThroughASurface()
    {
        // Found by the whole-collection test: 0.012 short of the wall, moving 0.017 toward it.
        TraceResult t = Sweep(WallAtX100(), new Vector3(83.988f, 0, 0), new Vector3(84.005f, 0, 0));

        Assert.True(t.Hit);
        Assert.Equal(0f, t.Fraction);
        Assert.Equal(83.988f, t.End.X, 0.0001f);
    }

    [Fact]
    public void ABoxBurriedInABrushReportsAllSolid()
    {
        TraceResult t = Sweep(WallAtX100(), new Vector3(150, 0, 0), new Vector3(160, 0, 0));

        Assert.True(t.AllSolid);
        Assert.True(t.StartSolid);
        Assert.Equal(0f, t.Fraction);
    }

    [Fact]
    public void ABoxCanMoveOutOfABrushItStartedIn()
    {
        TraceResult t = Sweep(WallAtX100(), new Vector3(90, 0, 0), new Vector3(0, 0, 0));

        Assert.True(t.StartSolid);
        Assert.False(t.AllSolid);
        Assert.False(t.Hit);
    }

    [Fact]
    public void ContentsOutsideTheMaskAreIgnored()
    {
        var world = new TestWorld().Box(new Vector3(100, -200, -200), new Vector3(200, 200, 200), Contents.Water);

        Assert.True(Sweep(world, Vector3.Zero, new Vector3(300, 0, 0)).Clear);
        Assert.Equal(Contents.Water, world.PointContents(new Vector3(150, 0, 0)));
        Assert.Equal(Contents.None, world.PointContents(Vector3.Zero));
    }

    [Fact]
    public void TheNearestOfTwoBrushesWins()
    {
        TestWorld world = WallAtX100().Box(new Vector3(50, -200, -200), new Vector3(60, 200, 200));

        Assert.Equal(34f - 1f / 32f, Sweep(world, Vector3.Zero, new Vector3(300, 0, 0)).End.X, 0.01f);
    }

    [Fact]
    public void ABrushSquashedFlatHasNoVolume()
    {
        ClipPlane[] flat = [new(Vector3.UnitZ, 128), new(-Vector3.UnitZ, -128), new(Vector3.UnitX, 8192)];
        ClipPlane[] thin = [new(Vector3.UnitZ, 129), new(-Vector3.UnitZ, -128), new(Vector3.UnitX, 8192)];

        Assert.True(BspCollision.HasNoVolume(flat));
        Assert.False(BspCollision.HasNoVolume(thin));
    }

    [Fact]
    public void ABrushWithNoSidesIsNotSolid()
    {
        var world = new TestWorld().Add([]);

        Assert.True(Sweep(world, Vector3.Zero, new Vector3(100, 0, 0)).Clear);
    }
}
