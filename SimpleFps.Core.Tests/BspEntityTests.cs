using System.Numerics;
using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Tests;

public class BspEntityTests
{
    [Fact]
    public void ReadsSeveralEntitiesAndTheirFields()
    {
        List<BspEntity> entities = BspEntity.Parse("""
            {
            "classname" "worldspawn"
            "message" "The Test\nby Someone"
            }
            {
            "origin" "-864 -240.5 112"
            "angle" "90"
            "classname" "info_player_deathmatch"
            }
            """);

        Assert.Equal(2, entities.Count);
        Assert.Equal("worldspawn", entities[0].ClassName);
        Assert.Equal("The Test\\nby Someone", entities[0]["message"]);
        Assert.Equal("info_player_deathmatch", entities[1].ClassName);
        Assert.Equal(new Vector3(-864, -240.5f, 112), entities[1].GetVector("origin"));
        Assert.Equal(90f, entities[1].GetFloat("angle"));
    }

    [Fact]
    public void MissingAndMalformedValuesComeBackNull()
    {
        BspEntity entity = Assert.Single(BspEntity.Parse("""{ "origin" "1 2" "angle" "east" }"""));

        Assert.Null(entity.GetVector("origin"));
        Assert.Null(entity.GetFloat("angle"));
        Assert.Null(entity["nothing"]);
        Assert.Equal("", entity.ClassName);
    }

    [Theory]
    [InlineData("""{ "classname" "light" """)]
    [InlineData("""{ "classname" "light" "style""")]
    [InlineData("""{ "classname" "light" "style" "3""")]
    public void AnUnfinishedLastEntityIsKept(string text)
    {
        Assert.Equal("light", Assert.Single(BspEntity.Parse(text)).ClassName);
    }

    [Fact]
    public void JunkOutsideBlocksIsSkipped()
    {
        Assert.Equal("a", Assert.Single(BspEntity.Parse("junk \"x\" { \"classname\" \"a\" } more")).ClassName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no blocks at all")]
    public void NoBlocksMeansNoEntities(string text)
    {
        Assert.Empty(BspEntity.Parse(text));
    }
}
