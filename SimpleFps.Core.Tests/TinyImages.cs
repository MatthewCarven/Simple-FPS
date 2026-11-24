using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SimpleFps.Core.Tests;

/// <summary>Writes small PNG, Targa, PCX and .wal files by hand, so the readers can be checked against known pixels.</summary>
internal static class TinyImages
{
    /// <summary>
    /// A PNG from unfiltered rows (no filter byte), filtering row y with <paramref name="filterFor"/>(y).
    /// </summary>
    public static byte[] Png(
        int width,
        int height,
        int colourType,
        int depth,
        byte[][] rows,
        Func<int, int>? filterFor = null,
        byte[]? palette = null,
        byte[]? transparency = null,
        bool interlaced = false)
    {
        int channels = colourType switch { 0 or 3 => 1, 4 => 2, 2 => 3, _ => 4 };
        int pixelBytes = Math.Max(1, channels * depth / 8);

        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            int filter = filterFor?.Invoke(y) ?? 0;
            raw.WriteByte((byte)filter);
            byte[] row = rows[y];
            byte[]? above = y > 0 ? rows[y - 1] : null;
            for (int i = 0; i < row.Length; i++)
            {
                int left = i >= pixelBytes ? row[i - pixelBytes] : 0;
                int up = above?[i] ?? 0;
                int upLeft = i >= pixelBytes && above is not null ? above[i - pixelBytes] : 0;
                int predicted = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };

                raw.WriteByte((byte)(row[i] - predicted));
            }
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(deflate);
        }

        using var file = new MemoryStream();
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = (byte)depth;
        header[9] = (byte)colourType;
        header[12] = interlaced ? (byte)1 : (byte)0;
        Chunk(file, "IHDR", header);
        if (palette is not null)
        {
            Chunk(file, "PLTE", palette);
        }

        if (transparency is not null)
        {
            Chunk(file, "tRNS", transparency);
        }

        // Split in two, as encoders often do.
        byte[] data = compressed.ToArray();
        Chunk(file, "IDAT", data[..(data.Length / 2)]);
        Chunk(file, "IDAT", data[(data.Length / 2)..]);
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    /// <summary>A plain 8-bit RGB or RGBA PNG from top-row-first pixels.</summary>
    public static byte[] Png(int width, int height, byte[] pixels, bool alpha = false)
    {
        int channels = alpha ? 4 : 3;
        byte[][] rows = Enumerable.Range(0, height).Select(y => pixels.AsSpan(y * width * channels, width * channels).ToArray()).ToArray();
        return Png(width, height, alpha ? 6 : 2, 8, rows, y => y % 5);
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int guess = left + up - upLeft;
        int toLeft = Math.Abs(guess - left), toUp = Math.Abs(guess - up), toUpLeft = Math.Abs(guess - upLeft);
        return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
    }

    private static void Chunk(Stream file, string type, byte[] body)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)body.Length);
        file.Write(number);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        file.Write(typeBytes);
        file.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32([.. typeBytes, .. body]));
        file.Write(number);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }

    /// <summary>A Targa header followed by whatever else is given: colour map, then picture data.</summary>
    public static byte[] Tga(int imageType, int width, int height, int pixelBits, bool topFirst, byte[] body, int mapLength = 0, int mapBits = 0)
    {
        var header = new byte[18];
        header[1] = (byte)(mapLength > 0 ? 1 : 0);
        header[2] = (byte)imageType;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)mapLength);
        header[7] = (byte)mapBits;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)height);
        header[16] = (byte)pixelBits;
        header[17] = (byte)(topFirst ? 0x20 : 0);
        return [.. header, .. body];
    }

    /// <summary>A 24-bit, plain, top-row-first Targa from RGBA pixels (alpha dropped).</summary>
    public static byte[] Tga(int width, int height, byte[] rgba)
    {
        var body = new byte[width * height * 3];
        for (int i = 0; i < width * height; i++)
        {
            body[i * 3] = rgba[i * 4 + 2];
            body[i * 3 + 1] = rgba[i * 4 + 1];
            body[i * 3 + 2] = rgba[i * 4];
        }

        return Tga(2, width, height, 24, topFirst: true, body);
    }

    /// <summary>A 256-colour PCX from palette indices, packed the usual way, with the palette at the end.</summary>
    public static byte[] Pcx(int width, int height, byte[] indices, byte[]? palette, int lineBytes = 0)
    {
        lineBytes = lineBytes == 0 ? width : lineBytes;
        using var file = new MemoryStream();
        file.Write(PcxHeader(width, height, planes: 1, lineBytes));
        for (int y = 0; y < height; y++)
        {
            var line = new byte[lineBytes];
            indices.AsSpan(y * width, width).CopyTo(line);
            file.Write(Pack(line));
        }

        if (palette is not null)
        {
            file.WriteByte(12);
            file.Write(palette);
        }

        return file.ToArray();
    }

    /// <summary>A full-colour PCX: each row stored as a red, a green and a blue plane.</summary>
    public static byte[] Pcx24(int width, int height, byte[] rgba)
    {
        using var file = new MemoryStream();
        file.Write(PcxHeader(width, height, planes: 3, width));
        for (int y = 0; y < height; y++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                byte[] plane = Enumerable.Range(0, width).Select(x => rgba[(y * width + x) * 4 + channel]).ToArray();
                file.Write(Pack(plane));
            }
        }

        return file.ToArray();
    }

    private static byte[] PcxHeader(int width, int height, int planes, int lineBytes)
    {
        var header = new byte[128];
        header[0] = 10;
        header[1] = 5;
        header[2] = 1;
        header[3] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)(width - 1));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)(height - 1));
        header[65] = (byte)planes;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(66), (ushort)lineBytes);
        return header;
    }

    /// <summary>Runs of up to 63, and any byte with its top two bits set stored as a run of one.</summary>
    private static byte[] Pack(byte[] line)
    {
        var packed = new List<byte>();
        int i = 0;
        while (i < line.Length)
        {
            int run = 1;
            while (i + run < line.Length && line[i + run] == line[i] && run < 63)
            {
                run++;
            }

            if (run > 1 || (line[i] & 0xC0) == 0xC0)
            {
                packed.Add((byte)(0xC0 | run));
            }

            packed.Add(line[i]);
            i += run;
        }

        return [.. packed];
    }

    public static byte[] Wal(int width, int height, byte[] pixels)
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

    /// <summary>Colour i is (i, 255 - i, i / 2), so every index is told apart by its red.</summary>
    public static byte[] Palette()
    {
        var rgb = new byte[768];
        for (int i = 0; i < 256; i++)
        {
            rgb[i * 3] = (byte)i;
            rgb[i * 3 + 1] = (byte)(255 - i);
            rgb[i * 3 + 2] = (byte)(i / 2);
        }

        return rgb;
    }

    /// <summary>RGBA pixels where each one is different and nothing is symmetric: handy for catching flips.</summary>
    public static byte[] Pattern(int width, int height, int seed = 1)
    {
        var random = new Random(seed);
        var rgba = new byte[width * height * 4];
        random.NextBytes(rgba);
        for (int i = 3; i < rgba.Length; i += 4)
        {
            rgba[i] = 255;
        }

        return rgba;
    }
}
