using System.Numerics;
using SimpleFps.Core.Bake;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Tests;

public class BakeTests
{
    private const string Worldspawn = """{ "classname" "worldspawn" "message" "Tiny Room\nby Someone" }""";

    private static BakedMap BakeTiny(string entities = Worldspawn) =>
        MapBaker.Bake("tiny", "tiny", TinyBsp.Build(entities: entities, solidBox: true), null);

    private static byte[] Write(BakedMap map)
    {
        using var stream = new MemoryStream();
        BakeFile.Write(stream, map);
        return stream.ToArray();
    }

    private static BakedMap? Read(byte[] file, string hash) => BakeFile.Read(new MemoryStream(file), hash);

    [Fact]
    public void ABakeReadsBackEqualToBuildingStraightFromTheMap()
    {
        byte[] file = TinyBsp.Build(entities: Worldspawn, solidBox: true);
        Q2Bsp bsp = Q2Bsp.Read(file);

        BakedMap baked = MapBaker.Bake("tiny", "tiny", file, null);
        BakedMap read = Read(Write(baked), baked.Info.Hash)!;

        Assert.NotNull(read);
        Assert.Equal(baked.Info, read.Info);
        BakeAssert.SameGeometry(MapMesher.Build(bsp), read.Geometry);
        BakeAssert.SameCollision(CollisionData.From(bsp), read.Collision);
        BakeAssert.SameEntities(bsp.Entities, read.Entities);
        BakeAssert.SameVisibility(MapVisibility.None, read.Visibility);
        Assert.NotEmpty(read.Collision.Nodes);
        Assert.NotEmpty(read.Collision.Brushes);
    }

    [Fact]
    public void TheMapsVisibilityIsBakedAsItIs()
    {
        byte[] lump = HearingTests.VisLump(3, (a, b) => a != 2 || b == 2, (a, b) => true);
        BakedMap baked = MapBaker.Bake("tiny", "tiny", TinyBsp.Build(entities: Worldspawn, solidBox: true, visibility: lump), null);

        BakedMap read = Read(Write(baked), baked.Info.Hash)!;

        Assert.Equal(lump, read.Visibility.Lump);
        Assert.Equal(3, read.Visibility.ClusterCount);
        Assert.True(read.Visibility.CanSee(0, 2));
        Assert.False(read.Visibility.CanSee(2, 0));
    }

    [Fact]
    public void BakedCollisionAnswersEveryTraceExactlyAsTheMapDoes()
    {
        byte[] file = TinyBsp.Build(solidBox: true);
        var direct = new BspCollision(Q2Bsp.Read(file));
        BakedMap baked = MapBaker.Bake("tiny", "tiny", file, null);
        var fromBake = new BspCollision(Read(Write(baked), baked.Info.Hash)!.Collision);

        var hull = (Mins: new Vector3(-16, -16, -24), Maxs: new Vector3(16, 16, 32));
        var random = new Random(7);
        int hits = 0;
        for (int i = 0; i < 500; i++)
        {
            // From all round the box, aimed at it.
            Vector3 start = new Vector3(16, 8, -8) + Scatter(random, 80f);
            Vector3 end = new Vector3(16, 8, -8) + Scatter(random, 24f);
            bool point = i % 2 == 0;
            Vector3 mins = point ? Vector3.Zero : hull.Mins;
            Vector3 maxs = point ? Vector3.Zero : hull.Maxs;

            TraceResult want = direct.Trace(start, end, mins, maxs, Contents.PlayerSolid);
            BakeAssert.SameTrace(want, fromBake.Trace(start, end, mins, maxs, Contents.PlayerSolid), $"{start} to {end}");
            Assert.Equal(direct.PointContents(end), fromBake.PointContents(end));
            hits += want.Hit ? 1 : 0;
        }

        Assert.True(hits > 50, $"Only {hits} traces hit the box, so this proved little.");
    }

    [Fact]
    public void ABakeFromOtherFilesOrAnotherVersionReadsAsStale()
    {
        BakedMap baked = BakeTiny();
        byte[] file = Write(baked);

        Assert.Null(Read(file, "some other hash"));

        // The version follows the four-letter magic word.
        BitConverter.GetBytes(BakeFile.Version + 1).CopyTo(file, 4);
        Assert.Null(Read(file, baked.Info.Hash));
    }

    [Fact]
    public void ADamagedBakeIsRejected()
    {
        BakedMap baked = BakeTiny();
        byte[] file = Write(baked);

        Assert.Throws<InvalidDataException>(() => Read(file[..(file.Length / 2)], baked.Info.Hash));
        Assert.Throws<InvalidDataException>(() => Read(file[..^1], baked.Info.Hash));

        // The hash's length, after the magic word and version, made unreadable.
        byte[] badLength = [.. file];
        badLength.AsSpan(8, 5).Fill(0xFF);
        Assert.Throws<InvalidDataException>(() => Read(badLength, baked.Info.Hash));

        file[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => Read(file, baked.Info.Hash));
    }

    [Fact]
    public void SectionsThisCodeDoesntKnowAreSkipped()
    {
        BakedMap baked = BakeTiny();
        byte[] file = Write(baked);

        // A made-up section slipped in before the END section, which is the last 12 bytes.
        byte[] extra = [.. "BOTS"u8, .. BitConverter.GetBytes(3L), 1, 2, 3];
        byte[] longer = [.. file[..^12], .. extra, .. file[^12..]];

        BakedMap read = Read(longer, baked.Info.Hash)!;
        BakeAssert.SameGeometry(baked.Geometry, read.Geometry);
    }

    [Fact]
    public void TitleAndAuthorComeFromTheMessage()
    {
        MapInfo info = BakeTiny().Info;

        Assert.Equal("Tiny Room", info.Title);
        Assert.Equal("Someone", info.Author);
    }

    private static Vector3 Scatter(Random random, float size) => new(
        (float)(random.NextDouble() * 2 - 1) * size,
        (float)(random.NextDouble() * 2 - 1) * size,
        (float)(random.NextDouble() * 2 - 1) * size);
}
