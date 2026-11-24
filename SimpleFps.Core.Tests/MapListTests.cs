using SimpleFps.Core.Bake;
using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Tests;

/// <summary>The map list's quick read, on tiny maps in a folder of their own that's deleted afterwards.</summary>
public sealed class MapListTests : IDisposable
{
    private const string Deathmatch = """{ "classname" "info_player_deathmatch" "origin" "16 8 24" }""";
    private const string SinglePlayer = """{ "classname" "info_player_start" "origin" "16 8 24" }""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "simplefps-tests", Guid.NewGuid().ToString("N"));
    private readonly string _maps;
    private readonly MapCache _cache;

    public MapListTests()
    {
        _maps = Path.Combine(_root, "maps");
        Directory.CreateDirectory(_maps);
        _cache = new MapCache(_maps, Path.Combine(_root, "cache"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Entities(string? message, string spawn = Deathmatch) =>
        message is null
            ? $$"""{ "classname" "worldspawn" } {{spawn}}"""
            : $$"""{ "classname" "worldspawn" "message" "{{message}}" } {{spawn}}""";

    private void AddMap(string name, string? message = "Tiny Room by Someone", string texture = "base/floor", string spawn = Deathmatch) =>
        File.WriteAllBytes(Path.Combine(_maps, name + ".bsp"), TinyBsp.Build(texture: texture, entities: Entities(message, spawn), solidBox: true));

    private void AddOverride(string name, string? mapPath, string message) =>
        File.WriteAllBytes(Path.Combine(_maps, name + ".bsp.override"), TinyBsp.Override(mapPath, Entities(message)));

    private MapListEntry Entry(string name) => MapList.Read(_cache).All.Single(m => m.Name == name);

    [Fact]
    public void AMapIsListedWithItsTitleAuthorAndName()
    {
        AddMap("tiny");

        MapListEntry map = Assert.Single(MapList.Read(_cache).Shown);

        Assert.Equal(new MapListEntry("tiny", "tiny", "Tiny Room", "Someone", Deathmatch: true, CopyOf: null), map);
    }

    [Fact]
    public void TheListGivesTheSameTitleAndAuthorAsTheBake()
    {
        AddMap("tiny", message: "^Reign Of Fire\\nby Mr.CleaN (c) 1998");

        (BakedMap baked, _) = _cache.Load("tiny");
        MapListEntry listed = Entry("tiny");

        Assert.Equal(baked.Info.Title, listed.Title);
        Assert.Equal(baked.Info.Author, listed.Author);
    }

    [Fact]
    public void AMapWithNoTitleShowsItsName()
    {
        AddMap("bare", message: null);

        MapListEntry map = Entry("bare");

        Assert.Null(map.Title);
        Assert.Equal("bare", map.DisplayTitle);
        Assert.True(map.Shown);
    }

    [Fact]
    public void MapsWithoutDeathmatchSpawnsAreHidden()
    {
        AddMap("story", spawn: SinglePlayer);
        AddMap("arena");

        MapList list = MapList.Read(_cache);

        Assert.Equal(["arena", "story"], list.All.Select(m => m.Name));
        Assert.False(list.All[1].Deathmatch);
        Assert.Equal(["arena"], list.Shown.Select(m => m.Name));
    }

    [Fact]
    public void OfASetOfCopiesTheShortestNameIsShown()
    {
        AddMap("alpha");
        AddMap("zed");
        AddMap("bb", texture: "base/wall");
        AddMap("aa", texture: "base/wall");

        MapList list = MapList.Read(_cache);

        Assert.Equal("zed", list.All.Single(m => m.Name == "alpha").CopyOf);
        Assert.Equal("aa", list.All.Single(m => m.Name == "bb").CopyOf);
        Assert.Equal(["aa", "zed"], list.Shown.Select(m => m.Name).Order());
    }

    [Fact]
    public void MapsOfTheSameSizeAreOnlyCopiesIfTheirFilesMatch()
    {
        AddMap("one", texture: "base/floor");
        AddMap("two", texture: "base/flooz");

        MapList list = MapList.Read(_cache);

        Assert.Equal(new FileInfo(Path.Combine(_maps, "one.bsp")).Length, new FileInfo(Path.Combine(_maps, "two.bsp")).Length);
        Assert.All(list.All, m => Assert.Null(m.CopyOf));
        Assert.Equal(2, list.Shown.Count);
    }

    [Fact]
    public void OverrideVariantsAreListedUnderTheirOwnNameAndTitle()
    {
        AddMap("tiny");
        AddOverride("tiny-nade", "maps/tiny.bsp", "Tiny Grenades");
        AddOverride("tiny-nade2", "maps/tiny.bsp", "Tiny Grenades");

        MapList list = MapList.Read(_cache);

        MapListEntry nade = list.All.Single(m => m.Name == "tiny-nade");
        Assert.Equal("tiny", nade.Bsp);
        Assert.Equal("Tiny Grenades", nade.Title);
        Assert.Null(nade.CopyOf);
        Assert.Null(list.All.Single(m => m.Name == "tiny").CopyOf);
        Assert.Equal("tiny-nade", list.All.Single(m => m.Name == "tiny-nade2").CopyOf);
    }

    [Fact]
    public void QuakeOneAndDamagedMapsAreLeftOut()
    {
        AddMap("tiny");
        File.WriteAllBytes(Path.Combine(_maps, "old.bsp"), [29, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        File.WriteAllBytes(Path.Combine(_maps, "cut.bsp"), TinyBsp.Build()[..40]);
        File.WriteAllBytes(Path.Combine(_maps, "broken.bsp.override"), [9, 9, 9, 9]);

        Assert.Equal(["tiny"], MapList.Read(_cache).All.Select(m => m.Name));
    }

    [Fact]
    public void TheListIsSortedByTitleIgnoringCaseThenByName()
    {
        AddMap("m1", message: "beta");
        AddMap("m2", message: "Alpha", texture: "base/a");
        AddMap("a", message: null, texture: "base/b");
        AddMap("m3", message: "alpha", texture: "base/c");

        Assert.Equal(["a", "m2", "m3", "m1"], MapList.Read(_cache).Shown.Select(m => m.Name));
    }

    [Fact]
    public void PunctuationInFrontOfATitleIsIgnoredWhenSorting()
    {
        AddMap("edge", message: "'The Edge' - Modified");
        AddMap("rail", message: "(www.tfu.org) Rail Arena", texture: "base/a");
        AddMap("cap", message: ">ES< Capital", texture: "base/b");
        AddMap("bang", message: "!!!", texture: "base/c");
        AddMap("zed", message: "Zed", texture: "base/d");

        // A title that's all punctuation sorts as it is.
        Assert.Equal(["bang", "cap", "edge", "rail", "zed"], MapList.Read(_cache).Shown.Select(m => m.Name));
    }

    [Theory]
    [InlineData("", "q2duel5 ringbase rring")]
    [InlineData("RING", "ringbase rring")]
    [InlineData("towers", "q2duel5")]
    [InlineData("tokay", "q2duel5")]
    [InlineData("ring someone", "ringbase")]
    [InlineData("  ring   death  ", "ringbase")]
    [InlineData("nothing", "")]
    public void SearchingMatchesEveryWordInTheTitleAuthorOrName(string search, string expected)
    {
        MapListEntry[] maps =
        [
            new("q2duel5", "q2duel5", "Tokay's Towers", "Tokay", true, null),
            new("ringbase", "ringbase", "Ring of Death", "Someone", true, null),
            new("rring", "rring", null, null, true, null),
        ];

        IReadOnlyList<MapListEntry> found = MapList.Filter(maps, search);

        Assert.Equal(expected, string.Join(' ', found.Select(m => m.Name)));
    }

    [Fact]
    public void ReadingOnlyTheEntitiesGivesTheSameAsReadingTheWholeMap()
    {
        byte[] file = TinyBsp.Build(entities: Entities("Tiny Room by Someone"), solidBox: true);

        List<BspEntity> whole = Q2Bsp.Read(file).Entities;
        List<BspEntity> only = Q2Bsp.ReadEntities(new MemoryStream(file));

        BakeAssert.SameEntities(whole, only);
    }

    [Fact]
    public void ReadingOnlyTheEntitiesRefusesWhatReadingTheWholeMapRefuses()
    {
        byte[] file = TinyBsp.Build();

        Assert.Throws<InvalidDataException>(() => Q2Bsp.ReadEntities(new MemoryStream(file[..100])));
        Assert.Throws<InvalidDataException>(() => Q2Bsp.ReadEntities(new MemoryStream([29, 0, 0, 0, 0, 0, 0, 0])));
        // The entity text comes straight after the 160-byte header, so this cuts it short.
        Assert.Throws<InvalidDataException>(() => Q2Bsp.ReadEntities(new MemoryStream(file[..165])));
    }
}
