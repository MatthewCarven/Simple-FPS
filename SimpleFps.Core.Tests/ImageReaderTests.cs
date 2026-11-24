using SimpleFps.Core.Textures;

namespace SimpleFps.Core.Tests;

public class ImageReaderTests
{
    [Fact]
    public void PngRowsComeBackThroughEveryFilter()
    {
        // Five rows, one for each filter, so each is undone against a real row above it.
        byte[] rgba = TinyImages.Pattern(7, 5);
        RgbaImage image = PngImage.Decode(TinyImages.Png(7, 5, rgba, alpha: true));

        Assert.Equal(7, image.Width);
        Assert.Equal(5, image.Height);
        Assert.Equal(rgba, image.Pixels);
    }

    [Fact]
    public void PngRgbGetsFullAlpha()
    {
        byte[] rgba = TinyImages.Pattern(4, 6);
        byte[] rgb = rgba.Where((_, i) => i % 4 != 3).ToArray();

        RgbaImage image = PngImage.Decode(TinyImages.Png(4, 6, rgb));

        Assert.Equal(rgba, image.Pixels);
    }

    [Fact]
    public void PngPalettesWithSeeThroughEntriesAndPackedIndices()
    {
        // Four colours at two bits each: 5 pixels a row crosses a byte boundary.
        byte[] palette = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120];
        byte[] seeThrough = [0, 128];
        int[] indices = [0, 1, 2, 3, 1, 3, 2, 1, 0, 0];
        byte[][] rows =
        [
            [(byte)(0 << 6 | 1 << 4 | 2 << 2 | 3), 1 << 6],
            [(byte)(3 << 6 | 2 << 4 | 1 << 2 | 0), 0],
        ];

        RgbaImage image = PngImage.Decode(TinyImages.Png(5, 2, 3, 2, rows, palette: palette, transparency: seeThrough));

        for (int i = 0; i < indices.Length; i++)
        {
            int c = indices[i];
            byte alpha = c < seeThrough.Length ? seeThrough[c] : (byte)255;
            Assert.Equal(new[] { palette[c * 3], palette[c * 3 + 1], palette[c * 3 + 2], alpha }, image.Pixels[(i * 4)..(i * 4 + 4)]);
        }
    }

    [Fact]
    public void PngOneBitGreyScalesToBlackAndWhite()
    {
        // Nine pixels: 1 0 1 1 0 0 1 0 | 1
        byte[][] rows = [[0b1011_0010, 0b1000_0000]];

        RgbaImage image = PngImage.Decode(TinyImages.Png(9, 1, 0, 1, rows));

        byte[] grey = Enumerable.Range(0, 9).Select(i => image.Pixels[i * 4]).ToArray();
        Assert.Equal(new byte[] { 255, 0, 255, 255, 0, 0, 255, 0, 255 }, grey);
    }

    [Fact]
    public void PngSixteenBitSamplesKeepTheirTopByte()
    {
        byte[][] rows = [[0x12, 0x34, 0xAB, 0xCD, 0xFF, 0x00]];

        RgbaImage image = PngImage.Decode(TinyImages.Png(1, 1, 2, 16, rows));

        Assert.Equal(new byte[] { 0x12, 0xAB, 0xFF, 255 }, image.Pixels);
    }

    [Fact]
    public void PngGreyWithAlphaAndASeeThroughRgbColour()
    {
        RgbaImage grey = PngImage.Decode(TinyImages.Png(2, 1, 4, 8, [[100, 50, 200, 255]]));
        Assert.Equal(new byte[] { 100, 100, 100, 50, 200, 200, 200, 255 }, grey.Pixels);

        byte[] clear = [0, 1, 0, 2, 0, 3];
        RgbaImage rgb = PngImage.Decode(TinyImages.Png(2, 1, 2, 8, [[1, 2, 3, 1, 2, 4]], transparency: clear));
        Assert.Equal(new byte[] { 1, 2, 3, 0, 1, 2, 4, 255 }, rgb.Pixels);
    }

    [Fact]
    public void PngSizeIsReadFromTheHeaderAlone()
    {
        byte[] png = TinyImages.Png(3, 9, TinyImages.Pattern(3, 9), alpha: true);

        Assert.Equal((3, 9), PngImage.ReadSize(png.AsSpan(0, 33)));
    }

    [Fact]
    public void PngsThisCantReadAreRefused()
    {
        byte[] good = TinyImages.Png(4, 4, TinyImages.Pattern(4, 4), alpha: true);

        Assert.Throws<InvalidDataException>(() => PngImage.Decode(good[..^30]));
        Assert.Throws<InvalidDataException>(() => PngImage.Decode([.. "GIF89a"u8, .. good[6..]]));
        Assert.Throws<InvalidDataException>(() => PngImage.Decode(TinyImages.Png(1, 1, 2, 8, [[1, 2, 3]], interlaced: true)));
        Assert.Throws<InvalidDataException>(() => PngImage.Decode(TinyImages.Png(1, 1, 2, 4, [[1, 2]])));
        Assert.Throws<InvalidDataException>(() => PngImage.Decode(TinyImages.Png(1, 1, 3, 8, [[0]])));
    }

    [Fact]
    public void TgaStoredBottomRowFirstComesOutTopRowFirst()
    {
        // Two rows, bottom first in the file: the bottom row is blue, the top row red then green.
        byte[] body =
        [
            255, 0, 0, 255, 0, 0,
            0, 0, 255, 0, 255, 0,
        ];

        RgbaImage image = TgaImage.Decode(TinyImages.Tga(2, 2, 2, 24, topFirst: false, body));

        Assert.Equal(
            new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 255, 255 },
            image.Pixels);
    }

    [Fact]
    public void Tga32BitKeepsItsAlpha()
    {
        RgbaImage image = TgaImage.Decode(TinyImages.Tga(2, 1, 1, 32, topFirst: true, [3, 2, 1, 77]));

        Assert.Equal(new byte[] { 1, 2, 3, 77 }, image.Pixels);
    }

    [Fact]
    public void TgaPackedRunsAndStoredPixelsCanCrossRows()
    {
        // A run of three greens, then two stored pixels, red and blue: once as a 5 x 1 picture, and once
        // as a 1 x 5, where both packets run across rows.
        byte[] body = [0x82, 0, 255, 0, 0x01, 0, 0, 255, 255, 0, 0];

        RgbaImage wide = TgaImage.Decode(TinyImages.Tga(10, 5, 1, 24, topFirst: true, body));
        RgbaImage tall = TgaImage.Decode(TinyImages.Tga(10, 1, 5, 24, topFirst: true, body));

        byte[] expected = [0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 255, 0, 0, 255, 0, 0, 255, 255];
        Assert.Equal(expected, wide.Pixels);
        Assert.Equal(expected, tall.Pixels);
    }

    [Fact]
    public void TgaGreyColourMappedAndSixteenBit()
    {
        RgbaImage grey = TgaImage.Decode(TinyImages.Tga(3, 2, 1, 8, topFirst: true, [9, 200]));
        Assert.Equal(new byte[] { 9, 9, 9, 255, 200, 200, 200, 255 }, grey.Pixels);

        byte[] map = [30, 20, 10, 60, 50, 40];
        RgbaImage mapped = TgaImage.Decode(TinyImages.Tga(1, 2, 1, 8, topFirst: true, [.. map, 1, 0], mapLength: 2, mapBits: 24));
        Assert.Equal(new byte[] { 40, 50, 60, 255, 10, 20, 30, 255 }, mapped.Pixels);

        // 16 bits: red 31, green 0, blue 0.
        RgbaImage sixteen = TgaImage.Decode(TinyImages.Tga(2, 1, 1, 16, topFirst: true, [0x00, 0x7C]));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, sixteen.Pixels);
    }

    [Fact]
    public void TgasThisCantReadAreRefused()
    {
        Assert.Throws<InvalidDataException>(() => TgaImage.Decode(TinyImages.Tga(2, 2, 2, 24, topFirst: true, new byte[11])));
        Assert.Throws<InvalidDataException>(() => TgaImage.Decode(TinyImages.Tga(10, 4, 1, 24, topFirst: true, [0x83, 1, 2])));
        Assert.Throws<InvalidDataException>(() => TgaImage.Decode(TinyImages.Tga(2, 0, 1, 24, topFirst: true, [])));
        Assert.Throws<InvalidDataException>(() => TgaImage.Decode(TinyImages.Tga(4, 1, 1, 24, topFirst: true, [1, 2, 3])));
        Assert.Throws<InvalidDataException>(() => TgaImage.Decode(new byte[10]));
    }

    [Fact]
    public void PcxIndicesGoThroughThePaletteAtTheEnd()
    {
        // 70 of one colour packs as runs of 63 and 7; 0xC5 has its top bits set, so it has to be stored as a run.
        byte[] indices = [.. Enumerable.Repeat((byte)3, 70), 0xC5, 1];
        byte[] palette = TinyImages.Palette();

        RgbaImage image = PcxImage.Decode(TinyImages.Pcx(72, 1, indices, palette));

        for (int x = 0; x < indices.Length; x++)
        {
            int c = indices[x];
            Assert.Equal(new[] { palette[c * 3], palette[c * 3 + 1], palette[c * 3 + 2], (byte)255 }, image.Pixels[(x * 4)..(x * 4 + 4)]);
        }
    }

    [Fact]
    public void PcxRowsArePaddedToTheirLineLength()
    {
        byte[] indices = [1, 2, 3, 4, 5, 6];

        RgbaImage image = PcxImage.Decode(TinyImages.Pcx(3, 2, indices, TinyImages.Palette(), lineBytes: 4));

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, Enumerable.Range(0, 6).Select(i => image.Pixels[i * 4]).ToArray());
    }

    [Fact]
    public void PcxFullColourReadsItsThreePlanes()
    {
        byte[] rgba = TinyImages.Pattern(5, 3);

        Assert.Equal(rgba, PcxImage.Decode(TinyImages.Pcx24(5, 3, rgba)).Pixels);
    }

    [Fact]
    public void PcxsThisCantReadAreRefused()
    {
        byte[] good = TinyImages.Pcx(4, 4, new byte[16], TinyImages.Palette());

        Assert.Throws<InvalidDataException>(() => PcxImage.Decode(TinyImages.Pcx(4, 4, new byte[16], palette: null)));
        Assert.Throws<InvalidDataException>(() => PcxImage.Decode(good[..200]));
        Assert.Throws<InvalidDataException>(() => PcxImage.Decode([0, .. good[1..]]));
    }

    [Theory]
    [InlineData(256, 128, 9)]
    [InlineData(1, 1, 1)]
    [InlineData(5, 3, 3)]
    [InlineData(64, 64, 7)]
    public void AMipChainGoesDownToOnePixel(int width, int height, int levels)
    {
        Assert.Equal(levels, MipChain.LevelCount(width, height));
    }

    [Fact]
    public void MipLevelsFollowTheFullSizePicture()
    {
        // 4 x 2: two 2 x 2 blocks, then the two of those averaged.
        byte[] pixels =
        [
            0, 0, 0, 0, 4, 4, 4, 4, 100, 0, 0, 0, 200, 0, 0, 0,
            8, 8, 8, 8, 12, 12, 12, 12, 100, 0, 0, 0, 200, 0, 0, 0,
        ];

        byte[] chain = MipChain.Build(new RgbaImage(4, 2, pixels));

        Assert.Equal(MipChain.ByteCount(4, 2), chain.Length);
        Assert.Equal(32 + 8 + 4, chain.Length);
        Assert.Equal(pixels, chain[..32]);
        Assert.Equal(new byte[] { 6, 6, 6, 6, 150, 0, 0, 0 }, chain[32..40]);
        Assert.Equal(new byte[] { 78, 3, 3, 3 }, chain[40..44]);
    }

    [Fact]
    public void AOnePixelWideColumnStillHalvesDownwards()
    {
        byte[] pixels = [10, 0, 0, 0, 20, 0, 0, 0, 30, 0, 0, 0, 40, 0, 0, 0];

        byte[] chain = MipChain.Build(new RgbaImage(1, 4, pixels));

        Assert.Equal(new byte[] { 15, 0, 0, 0, 35, 0, 0, 0, 25, 0, 0, 0 }, chain[16..]);
    }
}
