using System.Buffers.Binary;

namespace SimpleFps.Core.Textures;

/// <summary>A Quake 2 .wal texture. Only the full-size image is kept; mipmaps get rebuilt.</summary>
public sealed class WalTexture
{
    private const int NameSize = 32;
    private const int HeaderSize = 100;

    private readonly byte[] _pixels;

    private WalTexture(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }

    public static WalTexture Load(string path) => Read(File.ReadAllBytes(path));

    /// <exception cref="InvalidDataException">The header doesn't describe readable pixels.</exception>
    public static WalTexture Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("File is too short to hold a texture header.");
        }

        int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[NameSize..]);
        int height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(NameSize + 4)..]);
        int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(NameSize + 8)..]);

        if (width <= 0 || height <= 0 || offset < 0 || offset > data.Length - (long)width * height)
        {
            throw new InvalidDataException($"Bad texture header ({width} x {height}, pixels at {offset}).");
        }

        return new WalTexture(width, height, data.Slice(offset, width * height).ToArray());
    }

    /// <summary>Expands to RGBA8. The transparent index gets zero alpha.</summary>
    public byte[] ToRgba(Palette palette)
    {
        byte[] rgba = new byte[_pixels.Length * 4];
        for (int i = 0; i < _pixels.Length; i++)
        {
            (byte r, byte g, byte b) = palette[_pixels[i]];
            rgba[i * 4] = r;
            rgba[i * 4 + 1] = g;
            rgba[i * 4 + 2] = b;
            rgba[i * 4 + 3] = _pixels[i] == Palette.TransparentIndex ? (byte)0 : (byte)255;
        }

        return rgba;
    }
}
