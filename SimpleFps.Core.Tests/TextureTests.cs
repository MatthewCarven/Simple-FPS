using SimpleFps.Core.Textures;

namespace SimpleFps.Core.Tests;

public class TextureTests
{
    private static Palette TestPalette()
    {
        byte[] rgb = new byte[768];
        rgb[3] = 10; rgb[4] = 20; rgb[5] = 30;
        rgb[6] = 40; rgb[7] = 50; rgb[8] = 60;
        rgb[765] = 159; rgb[766] = 91; rgb[767] = 83;
        return Palette.FromRgb(rgb);
    }

    private static byte[] Wal(int width, int height, byte[] pixels)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[32]);
        writer.Write((uint)width);
        writer.Write((uint)height);
        writer.Write((uint)100);
        writer.Write(new byte[56]);
        writer.Write(pixels);
        return stream.ToArray();
    }

    [Fact]
    public void WalPixelsExpandThroughThePalette()
    {
        WalTexture wal = WalTexture.Read(Wal(2, 1, [1, 2]));

        Assert.Equal(2, wal.Width);
        Assert.Equal(1, wal.Height);
        Assert.Equal([10, 20, 30, 255, 40, 50, 60, 255], wal.ToRgba(TestPalette()));
    }

    [Fact]
    public void TheTransparentIndexGetsZeroAlpha()
    {
        byte[] rgba = WalTexture.Read(Wal(1, 1, [255])).ToRgba(TestPalette());

        Assert.Equal([159, 91, 83, 0], rgba);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(500, 500)]
    public void ABadWalHeaderIsRefused(int width, int height)
    {
        byte[] bad = Wal(width, height, [1, 2, 3, 4]);

        Assert.Throws<InvalidDataException>(() => WalTexture.Read(bad));
    }

    [Fact]
    public void AShortPaletteIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => Palette.FromRgb(new byte[767]));
    }

    [Fact]
    public void AnExactPathBeatsTheSameBareNameElsewhere()
    {
        string root = Directory.CreateTempSubdirectory("simplefps-tex-").FullName;
        try
        {
            string other = Directory.CreateDirectory(Path.Combine(root, "aaa")).FullName;
            string wanted = Directory.CreateDirectory(Path.Combine(root, "zzz")).FullName;
            File.WriteAllBytes(Path.Combine(other, "metal.wal"), []);
            File.WriteAllBytes(Path.Combine(wanted, "Metal.wal"), []);

            var index = new TextureIndex(root);

            Assert.Equal(Path.Combine(wanted, "Metal.wal"), index.Find("zzz/metal"));
            Assert.Equal(Path.Combine(other, "metal.wal"), index.Find("aaa/METAL"));
            Assert.NotNull(index.Find("somewhere/else/metal"));
            Assert.Null(index.Find("zzz/brick"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
