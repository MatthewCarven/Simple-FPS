namespace SimpleFps.Core.Tests;

public class PlayerOptionsTests
{
    [Fact]
    public void HighResTexturesAreOnUntilTurnedOff()
    {
        Assert.True(new PlayerOptions().HighResTextures);
        Assert.True(PlayerOptions.FromJson("{}").HighResTextures);
    }

    [Fact]
    public void OptionsSurviveSavingAndLoading()
    {
        var options = new PlayerOptions { HighResTextures = false };

        Assert.False(PlayerOptions.FromJson(options.ToJson()).HighResTextures);
    }

    [Fact]
    public void UnreadableOptionsFallBackToTheDefaults()
    {
        Assert.True(PlayerOptions.FromJson("not json").HighResTextures);
    }
}
