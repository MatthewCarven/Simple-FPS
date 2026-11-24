using SimpleFps.Core.Bake;

namespace SimpleFps.Core.Tests;

/// <summary>The cache on disk, in a folder of its own that's deleted afterwards.</summary>
public sealed class MapCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "simplefps-tests", Guid.NewGuid().ToString("N"));
    private readonly string _maps;
    private readonly MapCache _cache;

    public MapCacheTests()
    {
        _maps = Path.Combine(_root, "maps");
        Directory.CreateDirectory(_maps);
        _cache = new MapCache(_maps, Path.Combine(_root, "cache"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void AddMap(string name, string texture = "base/floor", string message = "Tiny Room by Someone") =>
        File.WriteAllBytes(Path.Combine(_maps, name + ".bsp"), TinyBsp.Build(texture: texture, entities: Entities(message), solidBox: true));

    private void AddOverride(string name, string? mapPath, string message) =>
        File.WriteAllBytes(Path.Combine(_maps, name + ".bsp.override"), TinyBsp.Override(mapPath, Entities(message)));

    private static string Entities(string message) =>
        $$"""{ "classname" "worldspawn" "message" "{{message}}" } { "classname" "info_player_deathmatch" "origin" "16 8 24" }""";

    [Fact]
    public void TheFirstLoadBakesAndTheSecondLoadsTheBake()
    {
        AddMap("tiny");

        (BakedMap first, bool bakedFirst) = _cache.Load("tiny");
        (BakedMap second, bool bakedSecond) = _cache.Load("tiny");

        Assert.True(bakedFirst);
        Assert.False(bakedSecond);
        Assert.True(File.Exists(Path.Combine(_cache.Folder, "tiny" + MapCache.Extension)));
        Assert.Equal(first.Info, second.Info);
        BakeAssert.SameGeometry(first.Geometry, second.Geometry);
        BakeAssert.SameCollision(first.Collision, second.Collision);
        BakeAssert.SameEntities(first.Entities, second.Entities);
    }

    [Fact]
    public void ChangingTheMapFileBakesItAgain()
    {
        AddMap("tiny");
        _cache.Load("tiny");

        AddMap("tiny", texture: "base/wall");
        (BakedMap map, bool baked) = _cache.Load("tiny");

        Assert.True(baked);
        Assert.Equal("base/wall", map.Geometry.Models[0][0].Texture);
    }

    [Fact]
    public void ADamagedBakeIsBakedAgain()
    {
        AddMap("tiny");
        _cache.Load("tiny");

        string bake = Path.Combine(_cache.Folder, "tiny" + MapCache.Extension);
        byte[] whole = File.ReadAllBytes(bake);
        File.WriteAllBytes(bake, whole[..(whole.Length / 2)]);

        Assert.True(_cache.Load("tiny").Baked);
        Assert.False(_cache.Load("tiny").Baked);
    }

    [Fact]
    public void AnOverrideReplacesTheMapsEntities()
    {
        AddMap("tiny");
        AddOverride("tiny", "maps/tiny.bsp", "Tiny Room Remix by Someone Else");

        (BakedMap map, _) = _cache.Load("tiny");

        Assert.Equal("Tiny Room Remix", map.Info.Title);
        Assert.Equal("Someone Else", map.Info.Author);
        Assert.Equal("tiny", map.Info.Bsp);

        AddOverride("tiny", "maps/tiny.bsp", "Tiny Room Remix 2 by Someone Else");
        (map, bool baked) = _cache.Load("tiny");
        Assert.True(baked);
        Assert.Equal("Tiny Room Remix 2", map.Info.Title);
    }

    [Fact]
    public void AnOverrideCanPlayAnotherMapUnderItsOwnName()
    {
        AddMap("tiny");
        AddOverride("tiny-nade", "maps/tiny.bsp", "Tiny Grenades");

        (BakedMap map, _) = _cache.Load("tiny-nade");

        Assert.Equal("tiny-nade", map.Info.Name);
        Assert.Equal("tiny", map.Info.Bsp);
        Assert.Equal("Tiny Grenades", map.Info.Title);
        Assert.Equal(["tiny", "tiny-nade"], _cache.Names());
    }

    [Fact]
    public void CopiesUnderAnotherNameAreMarkedInTheIndex()
    {
        AddMap("copy");
        AddMap("alpha");
        AddMap("other", texture: "base/wall");
        AddOverride("alpha-nade", "maps/alpha.bsp", "Tiny Grenades");

        foreach (string name in _cache.Names())
        {
            _cache.Load(name);
        }

        IReadOnlyList<MapIndexEntry> maps = _cache.Index().Maps;
        Assert.Equal(["alpha", "alpha-nade", "copy", "other"], maps.Select(m => m.Name));
        Assert.Equal([null, null, "alpha", null], maps.Select(m => m.DuplicateOf));
        Assert.Equal("Tiny Room", maps[0].Title);
        Assert.Equal("Someone", maps[0].Author);
        Assert.Equal("alpha", maps[1].Bsp);
    }

    [Fact]
    public void TheIndexIsRebuiltFromTheBakesWhenItsMissingOrDamaged()
    {
        AddMap("tiny");
        AddMap("other", texture: "base/wall");
        _cache.Load("tiny");
        _cache.Load("other");
        string index = Path.Combine(_cache.Folder, MapIndex.FileName);

        File.Delete(index);
        Assert.Equal(["other", "tiny"], _cache.Index().Maps.Select(m => m.Name));

        File.WriteAllText(index, "{ not json");
        Assert.Equal(["other", "tiny"], _cache.Index().Maps.Select(m => m.Name));
    }

    [Fact]
    public void ABakeTheIndexMissedIsStillListed()
    {
        AddMap("tiny");
        AddMap("other", texture: "base/wall");
        _cache.Load("tiny");
        var onlyTiny = new MapIndex();
        onlyTiny.Add(_cache.Load("tiny").Map.Info);

        // As if the game stopped after saving the bake but before saving the index.
        _cache.Load("other");
        onlyTiny.Save(_cache.Folder);

        Assert.Equal(["other", "tiny"], _cache.Index().Maps.Select(m => m.Name));
    }

    [Fact]
    public void TheIndexDropsMapsWhoseBakeHasGone()
    {
        AddMap("tiny");
        AddMap("other", texture: "base/wall");
        _cache.Load("tiny");
        _cache.Load("other");

        File.Delete(Path.Combine(_cache.Folder, "other" + MapCache.Extension));

        Assert.Equal(["tiny"], _cache.Index().Maps.Select(m => m.Name));
    }

    [Fact]
    public void NamesListsOnlyQuake2Maps()
    {
        AddMap("tiny");
        File.WriteAllBytes(Path.Combine(_maps, "old.bsp"), [29, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        File.WriteAllText(Path.Combine(_maps, "notes.txt"), "not a map");

        Assert.Equal(["tiny"], _cache.Names());
        Assert.Throws<InvalidDataException>(() => _cache.Load("old"));
    }

    [Theory]
    [InlineData("nothere")]
    [InlineData("../maps/tiny")]
    [InlineData("")]
    public void AMissingMapIsNotFound(string name)
    {
        AddMap("tiny");

        Assert.ThrowsAny<FileNotFoundException>(() => _cache.Load(name));
    }
}
