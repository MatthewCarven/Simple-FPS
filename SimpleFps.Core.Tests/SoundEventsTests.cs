using System.Numerics;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Sounds;

namespace SimpleFps.Core.Tests;

public class SoundEventsTests
{
    [Theory]
    [InlineData(0f, 1f, 1f)]
    [InlineData(80f, 1f, 1f)]
    [InlineData(1080f, 1f, 0.5f)]
    [InlineData(2080f, 1f, 0f)]
    [InlineData(5000f, 1f, 0f)]
    [InlineData(580f, 2f, 0.5f)]
    [InlineData(1080f, 2f, 0f)]
    [InlineData(80f + (500f / 3f), 3f, 0.5f)]
    [InlineData(1e6f, 0f, 1f)]
    [InlineData(580f, 4f, 0f)]
    public void SoundsFadeAsTheOriginalsDid(float distance, float attenuation, float gain)
    {
        Assert.Equal(gain, SoundFalloff.Gain(distance, attenuation), 4);
    }

    [Fact]
    public void LoopsFadeLikeStaticSoundsAndAreGoneByAbout413Units()
    {
        Assert.Equal(1f, SoundFalloff.LoopGain(80f));
        Assert.Equal(SoundFalloff.Gain(300f, SoundFalloff.Static), SoundFalloff.LoopGain(300f), 5);
        Assert.Equal(0f, SoundFalloff.LoopGain(414f));
    }

    [Fact]
    public void TheMapsKeysMeanWhatTheyMeantInTheOriginal()
    {
        Assert.Equal(SoundFalloff.Normal, SoundFalloff.FromKey(null));
        Assert.Equal(SoundFalloff.Normal, SoundFalloff.FromKey(0f));
        Assert.Equal(SoundFalloff.None, SoundFalloff.FromKey(-1f));
        Assert.Equal(SoundFalloff.Static, SoundFalloff.FromKey(3f));
        Assert.Equal(0f, SoundFalloff.FromKey(-5f));
        Assert.Equal(1f, SoundFalloff.VolumeFromKey(null));
        Assert.Equal(1f, SoundFalloff.VolumeFromKey(0f));
        Assert.Equal(0.3f, SoundFalloff.VolumeFromKey(0.3f));
        Assert.Equal(1f, SoundFalloff.VolumeFromKey(4f));
    }

    [Fact]
    public void OneOffSoundsWaitUntilTakenAndTheOldestGoFirstWhenNobodyTakesThem()
    {
        var sounds = new SoundEvents();
        for (int i = 0; i < SoundEvents.MaxWaiting + 3; i++)
        {
            sounds.Play(new SoundEvent($"s{i}.wav", new Vector3(i, 0, 0)));
        }

        var taken = new List<SoundEvent>();
        sounds.TakeWaiting(taken);

        Assert.Equal(SoundEvents.MaxWaiting, taken.Count);
        Assert.Equal("s3.wav", taken[0].Sound);
        Assert.Equal(0, sounds.Waiting);
        sounds.TakeWaiting(taken);
        Assert.Equal(SoundEvents.MaxWaiting, taken.Count);
    }

    [Fact]
    public void LoopsAreStateThatCanBeTurnedAndMoved()
    {
        var sounds = new SoundEvents();
        int hum = sounds.AddLoop("world/hum.wav", Vector3.Zero, on: true);
        int lift = sounds.AddLoop("plats/pt1_mid.wav", Vector3.One, on: false);

        sounds.SetLoop(hum, false);
        sounds.SetLoop(lift, true);
        sounds.MoveLoop(lift, new Vector3(5, 6, 7));

        Assert.Equal((0, 1), (hum, lift));
        Assert.Equal(new LoopingSound("world/hum.wav", Vector3.Zero, false), sounds.Loops[hum]);
        Assert.Equal(new LoopingSound("plats/pt1_mid.wav", new Vector3(5, 6, 7), true), sounds.Loops[lift]);
    }

    private const string Speakers = """
        { "classname" "worldspawn" }
        { "classname" "target_speaker" "origin" "1 2 3" "noise" "world/Amb10" "spawnflags" "1" }
        { "classname" "target_speaker" "origin" "4 5 6" "noise" "world/klaxon2.wav" "spawnflags" "2" "targetname" "alarm" "attenuation" "-1" "volume" ".5" }
        { "classname" "target_speaker" "origin" "7 8 9" "noise" "world/button.wav" "targetname" "beep" "attenuation" "3" }
        { "classname" "target_speaker" "origin" "0 0 0" "noise" "world/amb10.wav" "spawnflags" "2049" }
        { "classname" "target_speaker" "origin" "0 0 0" }
        { "classname" "light" "noise" "world/amb10.wav" }
        """;

    [Fact]
    public void SpeakersLoopFromTheStartOrWaitToBeTriggered()
    {
        var sounds = new SoundEvents();

        List<TargetSpeaker> speakers = TargetSpeakers.Start(BspEntity.Parse(Speakers), sounds);

        Assert.Equal(3, speakers.Count);
        Assert.Equal(new TargetSpeaker(1, "world/amb10.wav", new Vector3(1, 2, 3), 1f, SoundFalloff.Normal, 1, null, 0), speakers[0]);
        Assert.Equal(new TargetSpeaker(2, "world/klaxon2.wav", new Vector3(4, 5, 6), 0.5f, SoundFalloff.None, 2, "alarm", 1), speakers[1]);
        Assert.Equal(new TargetSpeaker(3, "world/button.wav", new Vector3(7, 8, 9), 1f, SoundFalloff.Static, 0, "beep", -1), speakers[2]);
        Assert.Equal(new[] { true, false }, sounds.Loops.Select(l => l.On));
        Assert.Equal(0, sounds.Waiting);
    }

    [Fact]
    public void UsingASpeakerTogglesItsLoopOrPlaysItOnce()
    {
        var sounds = new SoundEvents();
        List<TargetSpeaker> speakers = TargetSpeakers.Start(BspEntity.Parse(Speakers), sounds);

        TargetSpeakers.Use(speakers[1], sounds);
        bool onAfterOne = sounds.Loops[1].On;
        TargetSpeakers.Use(speakers[1], sounds);
        TargetSpeakers.Use(speakers[2], sounds);
        var taken = new List<SoundEvent>();
        sounds.TakeWaiting(taken);

        Assert.True(onAfterOne);
        Assert.False(sounds.Loops[1].On);
        Assert.Equal(new SoundEvent("world/button.wav", new Vector3(7, 8, 9), 1f, SoundFalloff.Static, 4), Assert.Single(taken));
    }
}
