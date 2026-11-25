using SimpleFps.Core.Bake;
using SimpleFps.Core.Sounds;

namespace SimpleFps.Core.Tests;

public sealed class SoundCacheTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("simplefps-soundcache-").FullName;

    private string Content => Path.Combine(_root, "content");

    private string Cache => Path.Combine(_root, "cache");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SoundCache NewCache() => new(Content, Cache);

    private void AddSound(string relative, byte[] file)
    {
        string path = Path.Combine(Content, "sound", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, file);
    }

    private string BakePath(string name) => Path.Combine(Cache, "sounds", name + SoundCache.Extension);

    [Fact]
    public void ASoundBakesOnceAndLoadsFromTheCacheAfter()
    {
        AddSound("world/amb10.wav", TinyWavs.File(TinyWavs.Fmt(1, 1, 11025, 8), TinyWavs.Data([128, 200, 50]), TinyWavs.Cue(1)));

        SoundLoad first = NewCache().Load("world/amb10.wav");
        SoundLoad second = NewCache().Load("world/amb10.wav");

        Assert.True(first.Baked);
        Assert.False(second.Baked);
        Assert.Null(second.Refused);
        Assert.True(File.Exists(BakePath("world/amb10")));
        BakedSound sound = second.Sound!;
        Assert.Equal("world/amb10.wav", sound.Name);
        Assert.Equal("sound/world/amb10.wav", sound.Source);
        Assert.Equal(11025, sound.SampleRate);
        Assert.Equal(1, sound.LoopStart);
        Assert.Equal(new short[] { 0, 72 << 8, -78 << 8 }, sound.Samples);
        Assert.Equal(first.Sound!.Samples, sound.Samples);
    }

    [Fact]
    public void TheMapsWaysOfNamingASoundAllFindTheSameFileAndBake()
    {
        AddSound("world/amb4.wav", TinyWavs.Mono16(22050, 1, 2));

        Assert.True(NewCache().Load("World\\Amb4").Baked);
        Assert.False(NewCache().Load("world/amb4.wav").Baked);
        Assert.False(NewCache().Load("/world/AMB4.WAV").Baked);
        Assert.Equal("world/amb4.wav", SoundNames.ToFile("World\\Amb4"));
        Assert.Equal("suds/bell.wav", SoundNames.ToFile("/suds/bell.wav"));
    }

    [Fact]
    public void AChangedFileBakesAgain()
    {
        AddSound("a.wav", TinyWavs.Mono16(22050, 1, 2));
        NewCache().Load("a");

        AddSound("a.wav", TinyWavs.Mono16(22050, 3, 4));
        SoundLoad changed = NewCache().Load("a");

        Assert.True(changed.Baked);
        Assert.Equal(new short[] { 3, 4 }, changed.Sound!.Samples);
    }

    [Fact]
    public void ADamagedOrOutOfDateBakeIsBakedAgain()
    {
        AddSound("a.wav", TinyWavs.Mono16(22050, 1, 2, 3));
        NewCache().Load("a");
        string bake = BakePath("a");

        byte[] whole = File.ReadAllBytes(bake);
        File.WriteAllBytes(bake, whole[..(whole.Length / 2)]);
        Assert.True(NewCache().Load("a").Baked);

        // The version sits right after the four-letter magic.
        whole = File.ReadAllBytes(bake);
        BitConverter.GetBytes(SoundFile.Version + 1).CopyTo(whole, 4);
        File.WriteAllBytes(bake, whole);
        Assert.True(NewCache().Load("a").Baked);
        Assert.False(NewCache().Load("a").Baked);
    }

    [Fact]
    public void SoundsThatCantPlayAreRefusedWithTheReason()
    {
        AddSound("items/mega3.wav", "set sw_stipple 1\n"u8.ToArray());
        AddSound("door.wav", TinyWavs.File(TinyWavs.Fmt(17, 2, 22050, 4), TinyWavs.Data(new byte[64])));

        Assert.Equal("there's no sound/world/pump.wav", NewCache().Load("world/pump").Refused);
        Assert.Equal("the map names no sound", NewCache().Load("").Refused);
        Assert.Equal("sound/../maps/x.wav isn't a safe path", NewCache().Load("../maps/x").Refused);
        Assert.Equal("sound/items/mega3.wav: It isn't a WAV file.", NewCache().Load("items/mega3.wav").Refused);
        Assert.Contains("IMA ADPCM", NewCache().Load("door").Refused);
        Assert.False(Directory.Exists(Path.Combine(Cache, "sounds")));
    }

    [Fact]
    public void AFileCutShortStillPlaysAndIsNoted()
    {
        AddSound("misc/mm.wav", TinyWavs.Mono16(22050, 1, 2, 3, 4)[..^3]);
        var cache = NewCache();

        SoundLoad load = cache.Load("misc/mm");

        Assert.Equal(new short[] { 1, 2 }, load.Sound!.Samples);
        Assert.Contains("sound/misc/mm.wav", Assert.Single(cache.Problems));
    }

    [Fact]
    public void ABakedSoundReadsBackExactlyAndRejectsDamage()
    {
        var sound = new BakedSound
        {
            Name = "world/x.wav",
            Hash = "abc",
            Source = "sound/world/x.wav",
            SampleRate = 8000,
            LoopStart = 2,
            Samples = [1, -2, 3, short.MinValue],
        };
        using var stream = new MemoryStream();
        SoundFile.Write(stream, sound);
        byte[] file = stream.ToArray();

        BakedSound read = SoundFile.Read(new MemoryStream(file), "abc")!;

        Assert.Equal((sound.Name, sound.Source, sound.SampleRate, sound.LoopStart), (read.Name, read.Source, read.SampleRate, read.LoopStart));
        Assert.Equal(sound.Samples, read.Samples);
        Assert.Null(SoundFile.Read(new MemoryStream(file), "other"));
        Assert.Throws<InvalidDataException>(() => SoundFile.Read(new MemoryStream(file[..^20]), "abc"));
        Assert.Throws<InvalidDataException>(() => SoundFile.Read(new MemoryStream("SFBT"u8.ToArray()), "abc"));
    }
}
