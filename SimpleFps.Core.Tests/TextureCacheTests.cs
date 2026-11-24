using SimpleFps.Core.Bake;
using SimpleFps.Core.Textures;

namespace SimpleFps.Core.Tests;

public sealed class TextureCacheTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("simplefps-texcache-").FullName;
    private readonly byte[] _palette = TinyImages.Palette();

    public TextureCacheTests()
    {
        Directory.CreateDirectory(Content);
        File.WriteAllBytes(Path.Combine(Content, "palette.act"), _palette);
    }

    private string Content => Path.Combine(_root, "content");

    private string Cache => Path.Combine(_root, "cache");

    private TextureCache NewCache() => new(Content, Cache);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private byte[] AddWal(string name, int width, int height, int seed = 1)
    {
        byte[] pixels = new byte[width * height];
        new Random(seed).NextBytes(pixels);
        Write(Path.Combine("textures", name + ".wal"), TinyImages.Wal(width, height, pixels));
        return pixels;
    }

    private byte[] AddHighRes(string name, int width, int height, int seed = 2)
    {
        byte[] rgba = TinyImages.Pattern(width, height, seed);
        Write(Path.Combine("q2textures-master", "pak99_textures", "textures", name + ".png"), TinyImages.Png(width, height, rgba, alpha: true));
        return rgba;
    }

    /// <summary>Six pictures, each a solid red of 10 times its side's number plus one.</summary>
    private void AddSky(string folder, string name, string extension, int size = 8)
    {
        for (int side = 0; side < 6; side++)
        {
            byte[] rgba = Enumerable.Range(0, size * size).SelectMany(_ => new byte[] { (byte)(10 * (side + 1)), 0, 0, 255 }).ToArray();
            byte[] file = extension switch
            {
                ".tga" => TinyImages.Tga(size, size, rgba),
                ".pcx" => TinyImages.Pcx24(size, size, rgba),
                _ => TinyImages.Png(size, size, rgba, alpha: true),
            };

            Write(Path.Combine(folder, name + SkyBox.Sides[side] + extension), file);
        }
    }

    private void Write(string relative, byte[] bytes)
    {
        string path = Path.Combine(Content, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private string BakeFile(string kind, string set, string name, string extension) =>
        Path.Combine(Cache, kind, set, name + extension);

    [Fact]
    public void AWallBakesOnceAndLoadsFromTheCacheAfter()
    {
        byte[] pixels = AddWal("e1u1/floor", 16, 8);

        (BakedTexture? first, bool baked) = NewCache().Wall("e1u1/floor", TextureSet.Original);
        (BakedTexture? second, bool bakedAgain) = NewCache().Wall("e1u1/floor", TextureSet.Original);

        Assert.True(baked);
        Assert.False(bakedAgain);
        Assert.True(File.Exists(BakeFile("textures", "original", "e1u1/floor", ".tex")));
        Assert.Equal((16, 8), (second!.Width, second.Height));
        Assert.Equal("textures/e1u1/floor.wal", second.Source);
        BakedImage image = Assert.Single(second.Images);
        Assert.Equal(MipChain.LevelCount(16, 8), image.Levels);
        Assert.Equal(first!.Images[0].Rgba, image.Rgba);

        byte[] expected = WalTexture.Read(TinyImages.Wal(16, 8, pixels)).ToRgba(Palette.FromRgb(_palette));
        Assert.Equal(expected, image.Rgba[..expected.Length]);
    }

    [Fact]
    public void AChangedWalOrPaletteBakesAgain()
    {
        AddWal("base/wall", 8, 8, seed: 1);
        NewCache().Wall("base/wall", TextureSet.Original);

        AddWal("base/wall", 8, 8, seed: 2);
        (_, bool afterWal) = NewCache().Wall("base/wall", TextureSet.Original);

        byte[] palette = TinyImages.Palette();
        palette[0] = 99;
        File.WriteAllBytes(Path.Combine(Content, "palette.act"), palette);
        (BakedTexture? texture, bool afterPalette) = NewCache().Wall("base/wall", TextureSet.Original);

        Assert.True(afterWal);
        Assert.True(afterPalette);
        Assert.False(NewCache().Wall("base/wall", TextureSet.Original).Baked);
        Assert.NotNull(texture);
    }

    [Fact]
    public void ADamagedOrOutOfDateBakeIsBakedAgain()
    {
        AddWal("base/wall", 8, 8);
        NewCache().Wall("base/wall", TextureSet.Original);
        string bake = BakeFile("textures", "original", "base/wall", ".tex");

        byte[] whole = File.ReadAllBytes(bake);
        File.WriteAllBytes(bake, whole[..(whole.Length / 2)]);
        Assert.True(NewCache().Wall("base/wall", TextureSet.Original).Baked);

        // The version sits right after the four-letter magic.
        whole = File.ReadAllBytes(bake);
        BitConverter.TryWriteBytes(whole.AsSpan(4), TextureFile.Version + 1);
        File.WriteAllBytes(bake, whole);
        Assert.True(NewCache().Wall("base/wall", TextureSet.Original).Baked);
        Assert.False(NewCache().Wall("base/wall", TextureSet.Original).Baked);
    }

    [Fact]
    public void AHighResPictureKeepsTheWalsSizeForTextureCoordinates()
    {
        AddWal("e1u1/floor", 16, 8);
        byte[] rgba = AddHighRes("e1u1/floor", 64, 32);

        (BakedTexture? texture, bool baked) = NewCache().Wall("e1u1/floor", TextureSet.HighRes);

        Assert.True(baked);
        Assert.Equal((16, 8), (texture!.Width, texture.Height));
        Assert.Equal((64, 32), (texture.Images[0].Width, texture.Images[0].Height));
        Assert.Equal(rgba, texture.Images[0].Rgba[..rgba.Length]);
        Assert.Equal("q2textures-master/pak99_textures/textures/e1u1/floor.png", texture.Source);
        Assert.False(NewCache().Wall("e1u1/floor", TextureSet.HighRes).Baked);
        Assert.True(File.Exists(BakeFile("textures", "highres", "e1u1/floor", ".tex")));
    }

    [Fact]
    public void AHighResPictureOfAnotherShapeIsLeftOut()
    {
        AddWal("e1u1/box", 16, 16);
        AddHighRes("e1u1/box", 48, 16);
        TextureCache cache = NewCache();

        Assert.Null(cache.Wall("e1u1/box", TextureSet.HighRes).Texture);
        Assert.Equal("e1u1/box: 48 x 16 for a 16 x 16 .wal", Assert.Single(cache.OddShapes));
        Assert.NotNull(cache.Wall("e1u1/box", TextureSet.Original).Texture);
    }

    [Fact]
    public void WithoutAWalThereIsNothingInEitherSet()
    {
        AddHighRes("e1u1/ghost", 32, 32);

        Assert.Null(NewCache().Wall("e1u1/ghost", TextureSet.Original).Texture);
        Assert.Null(NewCache().Wall("e1u1/ghost", TextureSet.HighRes).Texture);
    }

    [Fact]
    public void WithoutAHighResPictureTheHighResSetHasNothing()
    {
        AddWal("e1u1/plain", 8, 8);

        Assert.Null(NewCache().Wall("e1u1/plain", TextureSet.HighRes).Texture);
    }

    [Fact]
    public void ANameInTheWrongFolderIsFoundByItsBareName()
    {
        AddWal("other/metal", 8, 4);

        BakedTexture? texture = NewCache().Wall("e2u3/metal", TextureSet.Original).Texture;

        Assert.Equal("textures/other/metal.wal", texture!.Source);
        Assert.True(File.Exists(BakeFile("textures", "original", "e2u3/metal", ".tex")));
    }

    [Fact]
    public void ANameThatCouldClimbOutOfTheFolderIsStoredUnderItsHash()
    {
        AddWal("metal", 8, 4);
        TextureCache cache = NewCache();

        BakedTexture? texture = cache.Wall("../metal", TextureSet.Original).Texture;

        Assert.Equal("textures/metal.wal", texture!.Source);
        string path = cache.BakePath("textures", TextureSet.Original, "../metal", TextureCache.TextureExtension);
        Assert.StartsWith(Path.Combine(Cache, "textures", "original", "_named"), path);
        Assert.True(File.Exists(path));
        Assert.Null(cache.Wall("../metal", TextureSet.HighRes).Texture);
    }

    [Fact]
    public void AnUnreadableWalIsReportedAndShowsNothing()
    {
        Write(Path.Combine("textures", "bad.wal"), new byte[20]);
        TextureCache cache = NewCache();

        Assert.Null(cache.Wall("bad", TextureSet.Original).Texture);
        Assert.Contains("textures/bad.wal", Assert.Single(cache.Problems));
    }

    [Fact]
    public void ASkyBecomesSixCubeFacesAndIsCached()
    {
        AddSky("env", "space1", ".tga");

        (BakedTexture? sky, bool baked) = NewCache().Sky("space1", TextureSet.Original);

        Assert.True(baked);
        Assert.Equal(6, sky!.Images.Count);
        Assert.All(sky.Images, f => Assert.Equal((8, 8, 1), (f.Width, f.Height, f.Levels)));
        Assert.Equal("env/space1*.tga", sky.Source);

        // Cube face +X is east, which is rt, side 0, red 10.
        Assert.Equal(10, sky.Images[0].Rgba[0]);
        Assert.True(File.Exists(BakeFile("skies", "original", "space1", ".sky")));
        Assert.False(NewCache().Sky("space1", TextureSet.Original).Baked);
    }

    [Fact]
    public void ASkySideWithABrokenTgaFallsBackToItsPcx()
    {
        AddSky("env", "lake", ".tga");
        AddSky("env", "lake", ".pcx");
        Write(Path.Combine("env", "lakert.tga"), new byte[30]);
        TextureCache cache = NewCache();

        BakedTexture? sky = cache.Sky("lake", TextureSet.Original).Texture;

        Assert.Equal("env/lake*.tga and .pcx", sky!.Source);
        Assert.Contains("env/lakert.tga", Assert.Single(cache.Problems));
        Assert.Equal(10, sky.Images[0].Rgba[0]);
    }

    [Fact]
    public void ASkyMissingASideHasNothingToShow()
    {
        AddSky("env", "holes", ".tga");
        File.Delete(Path.Combine(Content, "env", "holesdn.tga"));

        Assert.Null(NewCache().Sky("holes", TextureSet.Original).Texture);
    }

    [Fact]
    public void AHighResSkyComesFromThePackAndOnlyWithAllSixSides()
    {
        string pack = Path.Combine("q2textures-master", "pak96_env", "env");
        AddSky(pack, "unit1_", ".png", size: 16);
        AddSky(pack, "half", ".png");
        File.Delete(Path.Combine(Content, pack, "halfup.png"));

        BakedTexture? sky = NewCache().Sky("unit1_", TextureSet.HighRes).Texture;

        Assert.Equal(16, sky!.Width);
        Assert.Equal("q2textures-master/pak96_env/env/unit1_*.png", sky.Source);
        Assert.Null(NewCache().Sky("half", TextureSet.HighRes).Texture);
        Assert.Null(NewCache().Sky("unit1_", TextureSet.Original).Texture);
    }

    [Fact]
    public void ATextureFileReadsBackExactly()
    {
        var texture = new BakedTexture
        {
            Name = "e1u1/floor",
            Hash = "abc",
            Source = "textures/e1u1/floor.wal",
            Width = 4,
            Height = 2,
            Images = [new BakedImage(4, 2, 3, MipChain.Build(new RgbaImage(4, 2, TinyImages.Pattern(4, 2))))],
        };
        using var stream = new MemoryStream();
        TextureFile.Write(stream, texture);

        stream.Position = 0;
        BakedTexture read = TextureFile.Read(stream, "abc")!;
        stream.Position = 0;
        BakedTexture? stale = TextureFile.Read(stream, "abd");

        Assert.Null(stale);
        Assert.Equal((texture.Name, texture.Source, texture.Width, texture.Height), (read.Name, read.Source, read.Width, read.Height));
        Assert.Equal(texture.Images[0], read.Images[0] with { Rgba = texture.Images[0].Rgba });
        Assert.Equal(texture.Images[0].Rgba, read.Images[0].Rgba);
    }

    [Fact]
    public void ADamagedTextureFileIsRefused()
    {
        var texture = new BakedTexture
        {
            Name = "x",
            Hash = "h",
            Source = "s",
            Width = 2,
            Height = 2,
            Images = [new BakedImage(2, 2, 1, new byte[16])],
        };
        using var stream = new MemoryStream();
        TextureFile.Write(stream, texture);
        byte[] whole = stream.ToArray();

        Assert.Throws<InvalidDataException>(() => TextureFile.Read(new MemoryStream(whole[..^20]), "h"));
        Assert.Throws<InvalidDataException>(() => TextureFile.Read(new MemoryStream([.. "SFBM"u8, .. whole[4..]]), "h"));

        // Claims two mip levels for 16 bytes, which only hold one.
        int levelsAt = whole.AsSpan().IndexOf(new byte[] { 2, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0 }) + 8;
        whole[levelsAt] = 2;
        Assert.Throws<InvalidDataException>(() => TextureFile.Read(new MemoryStream(whole), "h"));
    }
}
