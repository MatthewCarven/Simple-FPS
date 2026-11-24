using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Tests;

public class MapOverrideTests
{
    private const string Entities = """{ "classname" "worldspawn" }""";

    [Fact]
    public void ReadsTheMapPathAndEntities()
    {
        MapOverride read = MapOverride.Read(TinyBsp.Override("maps/q2dm1.bsp", Entities));

        Assert.Equal("maps/q2dm1.bsp", read.MapPath);
        Assert.Equal("q2dm1.bsp", read.MapFile);
        Assert.Equal(Entities, read.Entities);
    }

    [Fact]
    public void ThePathIsOptional()
    {
        MapOverride read = MapOverride.Read(TinyBsp.Override(null, Entities));

        Assert.Null(read.MapFile);
        Assert.Equal(Entities, read.Entities);
    }

    [Fact]
    public void APathCantLeadOutOfTheMapsFolder()
    {
        Assert.Equal("evil.bsp", MapOverride.Read(TinyBsp.Override("maps/../../evil.bsp", Entities)).MapFile);
        Assert.Equal("evil.bsp", MapOverride.Read(TinyBsp.Override("..\\evil.bsp", Entities)).MapFile);
    }

    [Fact]
    public void AChecksumIsSkipped()
    {
        byte[] withPath = TinyBsp.Override("maps/a.bsp", Entities);
        byte[] file = [.. BitConverter.GetBytes(7), .. withPath[4..68], 1, 2, 3, 4, .. withPath[68..]];

        MapOverride read = MapOverride.Read(file);

        Assert.Equal("a.bsp", read.MapFile);
        Assert.Equal(Entities, read.Entities);
    }

    [Fact]
    public void UnknownFlagsOrAShortFileAreRejected()
    {
        byte[] file = TinyBsp.Override("maps/a.bsp", Entities);

        Assert.Throws<InvalidDataException>(() => MapOverride.Read(file[..^1]));
        Assert.Throws<InvalidDataException>(() => MapOverride.Read(file[..10]));
        file[0] = 0x9;
        Assert.Throws<InvalidDataException>(() => MapOverride.Read(file));
    }
}
