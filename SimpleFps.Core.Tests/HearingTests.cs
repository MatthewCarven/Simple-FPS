using System.Numerics;
using System.Runtime.InteropServices;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;
using SimpleFps.Core.Sounds;

namespace SimpleFps.Core.Tests;

/// <summary>
/// Four rooms in a row along X, split at x = 100, 200 and 300, each its own cluster. Room 0 is
/// solid beyond y = 500. Each room sees its neighbours, and hears two rooms either way.
/// </summary>
public class HearingTests
{
    private static readonly CollisionData Rooms = BuildRooms();

    private static Hearing NewHearing(MapVisibility? visibility = null) =>
        new(Rooms, visibility ?? MapVisibility.Read(VisLump(4, (a, b) => Math.Abs(a - b) <= 1, (a, b) => Math.Abs(a - b) <= 2)));

    [Fact]
    public void PointsFindTheirLeafAndCluster()
    {
        Hearing hearing = NewHearing();

        Assert.Equal(new[] { 0, 1, 2, 3, -1 }, new[] { 50f, 150f, 250f, 350f }.Select(x => hearing.ClusterAt(new Vector3(x, 0, 0))).Append(hearing.ClusterAt(new Vector3(50, 600, 0))));
        Assert.Equal(4, hearing.LeafAt(new Vector3(50, 600, 0)));
        Assert.Equal(1, hearing.LeafAt(new Vector3(100, 0, 0)));
    }

    [Fact]
    public void ALoopIsHeardOnlyFromWhereItsSpotIsInSight()
    {
        Hearing hearing = NewHearing();
        hearing.Listen(new Vector3(50, 0, 0));

        Assert.True(Hears(hearing, new Vector3(150, 0, 0)));
        Assert.False(Hears(hearing, new Vector3(250, 0, 0)));

        // Within a few units of the next room, what that room sees counts too.
        hearing.Listen(new Vector3(95, 0, 0));
        Assert.True(Hears(hearing, new Vector3(250, 0, 0)));
    }

    [Fact]
    public void ALoopIsNeverHeardFromFartherThan400Units()
    {
        Hearing hearing = NewHearing();
        hearing.Listen(new Vector3(50, 0, 0));

        Assert.True(Hears(hearing, new Vector3(150, 300, 0)));
        Assert.False(Hears(hearing, new Vector3(150, 450, 0)));
    }

    [Fact]
    public void ALoopBuriedInAWallIsNeverHeardButOneTouchingOpenSpaceIs()
    {
        Hearing hearing = NewHearing();
        hearing.Listen(new Vector3(50, 400, 0));

        Assert.Empty(hearing.SpotClusters(new Vector3(50, 600, 0)));
        Assert.False(Hears(hearing, new Vector3(50, 600, 0)));
        Assert.Equal(new[] { 0 }, hearing.SpotClusters(new Vector3(50, 500.5f, 0)));
        Assert.True(Hears(hearing, new Vector3(50, 500.5f, 0)));
    }

    [Fact]
    public void AListenerInsideSolidHearsNoLoops()
    {
        Hearing hearing = NewHearing();
        hearing.Listen(new Vector3(50, 700, 0));

        Assert.False(Hears(hearing, new Vector3(50, 450, 0)));
    }

    [Fact]
    public void AOneOffIsHeardWhereItsSpotCanBeHeardUnlessItsHeardEverywhere()
    {
        Hearing hearing = NewHearing();
        hearing.Listen(new Vector3(50, 0, 0));

        Assert.True(hearing.CanHearOnce(new Vector3(250, 0, 0), SoundFalloff.Normal));
        Assert.False(hearing.CanHearOnce(new Vector3(350, 0, 0), SoundFalloff.Normal));
        Assert.True(hearing.CanHearOnce(new Vector3(350, 0, 0), SoundFalloff.None));
        Assert.False(hearing.CanHearOnce(new Vector3(50, 600, 0), SoundFalloff.Static));
    }

    [Fact]
    public void AMapWithNoVisibilityDataLetsEverythingThroughButSolid()
    {
        Hearing hearing = NewHearing(MapVisibility.None);
        hearing.Listen(new Vector3(50, 0, 0));

        Assert.True(Hears(hearing, new Vector3(350, 0, 0)));
        Assert.False(Hears(hearing, new Vector3(50, 600, 0)));
        Assert.True(hearing.CanHearOnce(new Vector3(350, 0, 0), SoundFalloff.Normal));
        Assert.False(hearing.CanHearOnce(new Vector3(50, 600, 0), SoundFalloff.Normal));
    }

    [Fact]
    public void LoopsFadeWithDistanceAndCopiesOfOneSoundNeverAddUpPastFull()
    {
        Hearing hearing = NewHearing();
        var mix = new LoopMix(hearing);
        LoopingSound[] loops =
        [
            new("hum.wav", new Vector3(50, 10, 0), true),
            new("hum.wav", new Vector3(50, -10, 0), true),
            new("fan.wav", new Vector3(50, 10, 0), true),
            new("fan.wav", new Vector3(150, 200, 0), true),
            new("drip.wav", new Vector3(60, 0, 0), false),
            new("far.wav", new Vector3(250, 0, 0), true),
        ];
        var gains = new float[loops.Length];

        hearing.Listen(new Vector3(50, 0, 0));
        mix.Mix(loops, gains);

        float fanFar = SoundFalloff.LoopGain(Vector3.Distance(loops[3].Origin, new Vector3(50, 0, 0)));
        Assert.Equal(0.5f, gains[0]);
        Assert.Equal(0.5f, gains[1]);
        Assert.Equal(1f / (1f + fanFar), gains[2], 5);
        Assert.Equal(fanFar / (1f + fanFar), gains[3], 5);
        Assert.InRange(fanFar, 0.1f, 0.9f);
        Assert.Equal(0f, gains[4]);
        Assert.Equal(0f, gains[5]);
    }

    [Fact]
    public void AMovedLoopIsCheckedAgainAtItsNewSpot()
    {
        Hearing hearing = NewHearing();
        var mix = new LoopMix(hearing);
        var sounds = new SoundEvents();
        int lift = sounds.AddLoop("lift.wav", new Vector3(250, 0, 0), on: true);
        var gains = new float[1];
        hearing.Listen(new Vector3(50, 0, 0));

        mix.Mix(sounds.Loops, gains);
        float before = gains[0];
        sounds.MoveLoop(lift, new Vector3(150, 0, 0));
        mix.Mix(sounds.Loops, gains);

        Assert.Equal(0f, before);
        Assert.True(gains[0] > 0f);
    }

    [Fact]
    public void VisibilityRowsUnpackWithTheirRunsOfZeros()
    {
        // 20 clusters make 3-byte rows, and most of each row is a run of zeros.
        static bool Sees(int a, int b) => b == a || b == 19 - a;
        MapVisibility visibility = MapVisibility.Read(VisLump(20, Sees, (a, b) => true));

        Assert.Equal(20, visibility.ClusterCount);
        for (int a = 0; a < 20; a++)
        {
            for (int b = 0; b < 20; b++)
            {
                Assert.Equal(Sees(a, b), visibility.CanSee(a, b));
                Assert.True(visibility.CanHear(a, b));
            }
        }

        Assert.False(visibility.CanSee(-1, 0));
        Assert.False(visibility.CanSee(0, -1));
        Assert.False(visibility.CanSee(0, 20));
    }

    [Fact]
    public void MissingOrBrokenVisibilityCountsAsNone()
    {
        Assert.Same(MapVisibility.None, MapVisibility.Read([]));
        Assert.Same(MapVisibility.None, MapVisibility.Read([1, 0, 0]));
        Assert.Same(MapVisibility.None, MapVisibility.Read(Lump(1_000_000)));
        Assert.Same(MapVisibility.None, MapVisibility.Read(Lump(1, 4, 400)));
        Assert.True(MapVisibility.None.CanSee(0, 5));
        Assert.False(MapVisibility.None.CanSee(-1, 0));
    }

    [Fact]
    public void ARowThatRunsLongIsCutAtItsEnd()
    {
        // One cluster, so a one-byte row: a run of five zeros is cut to one, and a row off the lump's end stays empty.
        byte[] overrun = [.. Lump(1, 12, 12), 0, 5];
        byte[] offTheEnd = [.. Lump(1, 12, 12), 0];

        Assert.False(MapVisibility.Read(overrun).CanSee(0, 0));
        Assert.Equal(1, MapVisibility.Read(offTheEnd).ClusterCount);
        Assert.False(MapVisibility.Read(offTheEnd).CanSee(0, 0));
    }

    private static bool Hears(Hearing hearing, Vector3 spot) => hearing.CanHearLoop(spot, hearing.SpotClusters(spot));

    private static byte[] Lump(params int[] values) => MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    /// <summary>A visibility lump as the map compiler writes it, zero bytes squeezed to a zero and a count.</summary>
    internal static byte[] VisLump(int clusters, Func<int, int, bool> pvs, Func<int, int, bool> phs)
    {
        int rowBytes = (clusters + 7) >> 3;
        var rows = new List<byte>();
        var offsets = new int[clusters * 2];
        int header = 4 + (clusters * 8);
        for (int c = 0; c < clusters; c++)
        {
            for (int kind = 0; kind < 2; kind++)
            {
                Func<int, int, bool> test = kind == 0 ? pvs : phs;
                var row = new byte[rowBytes];
                for (int other = 0; other < clusters; other++)
                {
                    if (test(c, other))
                    {
                        row[other >> 3] |= (byte)(1 << (other & 7));
                    }
                }

                offsets[(c * 2) + kind] = header + rows.Count;
                for (int i = 0; i < rowBytes; i++)
                {
                    if (row[i] != 0)
                    {
                        rows.Add(row[i]);
                        continue;
                    }

                    int run = 0;
                    while (i < rowBytes && row[i] == 0 && run < 255)
                    {
                        run++;
                        i++;
                    }

                    i--;
                    rows.Add(0);
                    rows.Add((byte)run);
                }
            }
        }

        return [.. Lump([clusters, .. offsets]), .. rows];
    }

    private static CollisionData BuildRooms()
    {
        BspPlane[] planes =
        [
            Make<BspPlane>(w => Plane(w, Vector3.UnitX, 200, 0)),
            Make<BspPlane>(w => Plane(w, Vector3.UnitX, 300, 0)),
            Make<BspPlane>(w => Plane(w, Vector3.UnitX, 100, 0)),
            Make<BspPlane>(w => Plane(w, Vector3.UnitY, 500, 1)),
        ];

        // Children below zero are leaves: -1 is leaf 0.
        BspNode[] nodes =
        [
            Node(0, 1, 2),
            Node(1, -4, -3),
            Node(2, -2, 3),
            Node(3, -5, -1),
        ];

        BspLeaf[] leaves = [Leaf(0, 0), Leaf(0, 1), Leaf(0, 2), Leaf(0, 3), Leaf((int)Contents.Solid, -1)];
        return new CollisionData
        {
            Planes = planes,
            Nodes = nodes,
            Leaves = leaves,
            LeafBrushes = [],
            Brushes = [],
            BrushContents = [],
            Sides = [],
            Models = [Make<BspModel>(w => w.Write(new byte[36 + 4 + 8]))],
        };
    }

    private static void Plane(BinaryWriter w, Vector3 normal, float distance, int type)
    {
        w.Write(normal.X);
        w.Write(normal.Y);
        w.Write(normal.Z);
        w.Write(distance);
        w.Write(type);
    }

    private static BspNode Node(int plane, int front, int back) => Make<BspNode>(w =>
    {
        w.Write(plane);
        w.Write(front);
        w.Write(back);
        w.Write(new byte[12 + 4]);
    });

    private static BspLeaf Leaf(int contents, short cluster) => Make<BspLeaf>(w =>
    {
        w.Write(contents);
        w.Write(cluster);
        w.Write((short)0);
        w.Write(new byte[12 + 8]);
    });

    private static T Make<T>(Action<BinaryWriter> write)
        where T : unmanaged
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            write(w);
        }

        byte[] bytes = stream.ToArray();
        Assert.Equal(System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), bytes.Length);
        return MemoryMarshal.Read<T>(bytes);
    }
}
