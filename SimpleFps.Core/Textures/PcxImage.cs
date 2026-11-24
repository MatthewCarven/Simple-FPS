using System.Buffers.Binary;

namespace SimpleFps.Core.Textures;

/// <summary>
/// Reads PCX files, the older copy of most sky pictures in <c>content/env</c>: 256 colours with
/// the palette at the end, or full colour stored as three planes.
/// </summary>
public static class PcxImage
{
    private const int HeaderLength = 128;
    private const int PaletteLength = 768;
    private const byte PaletteMarker = 12;

    /// <exception cref="InvalidDataException">It isn't a PCX file this can read, or it's damaged.</exception>
    public static RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength || data[0] != 10)
        {
            throw new InvalidDataException("Not a PCX file.");
        }

        int encoding = data[2];
        int bitsPerPlane = data[3];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) - BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) + 1;
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]) - BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) + 1;
        int planes = data[65];
        int lineBytes = BinaryPrimitives.ReadUInt16LittleEndian(data[66..]);

        RgbaImage.CheckSize(width, height, "PCX");
        if (encoding != 1 || bitsPerPlane != 8 || planes is not (1 or 3) || lineBytes < width)
        {
            throw new InvalidDataException($"PCX with {planes} planes of {bitsPerPlane} bits (packing {encoding}) isn't supported.");
        }

        ReadOnlySpan<byte> palette = default;
        int end = data.Length;
        if (planes == 1)
        {
            int marker = data.Length - PaletteLength - 1;
            if (marker < HeaderLength || data[marker] != PaletteMarker)
            {
                throw new InvalidDataException("PCX has no 256-colour palette at its end.");
            }

            palette = data.Slice(marker + 1, PaletteLength);
            end = marker;
        }

        int rowBytes = planes * lineBytes;
        byte[] stored = Unpack(data[HeaderLength..end], rowBytes * height);

        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int row = y * rowBytes;
            for (int x = 0; x < width; x++)
            {
                int to = (y * width + x) * 4;
                if (planes == 1)
                {
                    int index = stored[row + x];
                    pixels[to] = palette[index * 3];
                    pixels[to + 1] = palette[index * 3 + 1];
                    pixels[to + 2] = palette[index * 3 + 2];
                }
                else
                {
                    pixels[to] = stored[row + x];
                    pixels[to + 1] = stored[row + lineBytes + x];
                    pixels[to + 2] = stored[row + 2 * lineBytes + x];
                }

                pixels[to + 3] = 255;
            }
        }

        return new RgbaImage(width, height, pixels);
    }

    /// <summary>A byte with its top two bits set repeats the next byte; the other six bits say how often.</summary>
    private static byte[] Unpack(ReadOnlySpan<byte> data, int byteCount)
    {
        var stored = new byte[byteCount];
        int from = 0;
        int to = 0;
        while (to < byteCount)
        {
            if (from >= data.Length)
            {
                throw new InvalidDataException("PCX picture data is shorter than its size says.");
            }

            byte b = data[from++];
            if ((b & 0xC0) != 0xC0)
            {
                stored[to++] = b;
                continue;
            }

            if (from >= data.Length)
            {
                throw new InvalidDataException("PCX picture data is shorter than its size says.");
            }

            int count = Math.Min(b & 0x3F, byteCount - to);
            stored.AsSpan(to, count).Fill(data[from++]);
            to += count;
        }

        return stored;
    }
}
