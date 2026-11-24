using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Tests;

/// <summary>Messages taken from real maps in the collection. A backslash and n is how maps write a line break.</summary>
public class MapCreditsTests
{
    [Theory]
    [InlineData("\\nThe Ringbase\\n\\n(c) by Roger Soder 1998\\nhttp://www.mordor.ch/\\n", "The Ringbase", "Roger Soder")]
    [InlineData("Mortal\u0085Coil\u0085By\u0085Retinal", "Mortal Coil", "Retinal")]
    [InlineData("Reign Of Fire\\nby Mr.CleaN", "Reign Of Fire", "Mr.CleaN")]
    [InlineData("  - Arena Gate -\\n\\n a Q3 to Q2 conversion\\n\\n - by Grieve[AI] - \\n", "Arena Gate", "Grieve[AI]")]
    [InlineData("the last refuge by: snt\\n Modified by Spawn Girl", "the last refuge", "snt")]
    [InlineData("Stress Fractures --by Jester--", "Stress Fractures", "Jester")]
    [InlineData("New Hope By: _R41L_Drifter", "New Hope", "_R41L_Drifter")]
    [InlineData("Temple Of Handa.By #Riff_Raff#", "Temple Of Handa", "#Riff_Raff#")]
    [InlineData("Hammertime !    by Shao Kahn", "Hammertime !", "Shao Kahn")]
    [InlineData("Corridors - Quake 2 for N64\\n    a remake by peewee_RotA", "Corridors - Quake 2 for N64", "peewee_RotA")]
    [InlineData("by Someone\\nThe Title", "The Title", "Someone")]
    [InlineData("Lost Hallways", "Lost Hallways", null)]
    [InlineData("Lobby Hobby", "Lobby Hobby", null)]
    [InlineData("Âòïîúå Base", "Bronze Base", null)]
    [InlineData("   ", null, null)]
    public void SplitsTitleAndAuthor(string message, string? title, string? author)
    {
        Assert.Equal((title, author), MapCredits.Split(message));
    }

    [Fact]
    public void ComesFromTheWorldspawn()
    {
        List<BspEntity> entities = BspEntity.Parse("""
            { "classname" "worldspawn" "message" "Rail City by (LGM)Dragon" }
            { "classname" "target_speaker" "message" "Not this by Anyone" }
            """);

        Assert.Equal(("Rail City", "(LGM)Dragon"), MapCredits.From(entities));
    }

    [Fact]
    public void NoMessageMeansNoCredits()
    {
        Assert.Equal((null, null), MapCredits.From(BspEntity.Parse("""{ "classname" "worldspawn" }""")));
        Assert.Equal((null, null), MapCredits.From([]));
    }
}
