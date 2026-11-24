using System.Buffers.Binary;
using System.IO.Compression;

namespace SimpleFps.Core.Textures;

/// <summary>
/// Reads PNG files: the high-res texture pack is all PNG. Handles every colour type and bit depth,
/// and see-through colours, but not interlaced files, which nothing in the pack uses.
/// </summary>
public static class PngImage
{
    private const int SignatureLength = 8;
    private const int HeaderLength = 13;

    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    private enum ColourType
    {
        Grey = 0,
        Rgb = 2,
        Indexed = 3,
        GreyAlpha = 4,
        Rgba = 6,
    }

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= SignatureLength && data[..SignatureLength].SequenceEqual(Signature);

    /// <summary>The picture's size, read from its header alone.</summary>
    /// <exception cref="InvalidDataException">It isn't a PNG.</exception>
    public static (int Width, int Height) ReadSize(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data) || data.Length < SignatureLength + 8 + HeaderLength || !data[12..16].SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException("Not a PNG.");
        }

        return ((int)BinaryPrimitives.ReadUInt32BigEndian(data[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(data[20..]));
    }

    /// <exception cref="InvalidDataException">It isn't a PNG this can read, or it's damaged.</exception>
    public static RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data))
        {
            throw new InvalidDataException("Not a PNG.");
        }

        int width = 0, height = 0, depth = 0;
        var colour = ColourType.Grey;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var compressed = new MemoryStream();
        bool sawHeader = false;

        int at = SignatureLength;
        while (true)
        {
            if (at + 8 > data.Length)
            {
                throw new InvalidDataException("PNG ends before its last chunk.");
            }

            uint length = BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
            ReadOnlySpan<byte> type = data.Slice(at + 4, 4);
            if (length > int.MaxValue || at + 12 + (long)length > data.Length)
            {
                throw new InvalidDataException("A PNG chunk runs past the end of the file.");
            }

            ReadOnlySpan<byte> body = data.Slice(at + 8, (int)length);
            at += 12 + (int)length;

            if (type.SequenceEqual("IHDR"u8))
            {
                if (body.Length < HeaderLength)
                {
                    throw new InvalidDataException("PNG header is too short.");
                }

                width = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body), int.MaxValue);
                height = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body[4..]), int.MaxValue);
                depth = body[8];
                colour = (ColourType)body[9];
                if (body[10] != 0 || body[11] != 0)
                {
                    throw new InvalidDataException("PNG uses an unknown compression or filter method.");
                }

                if (body[12] != 0)
                {
                    throw new InvalidDataException("Interlaced PNGs aren't supported.");
                }

                RgbaImage.CheckSize(width, height, "PNG");
                CheckDepth(colour, depth);
                sawHeader = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = body.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                transparency = body.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (!sawHeader)
        {
            throw new InvalidDataException("PNG has no header.");
        }

        if (colour == ColourType.Indexed && palette is null)
        {
            throw new InvalidDataException("PNG uses a palette but has none.");
        }

        int bitsPerPixel = Channels(colour) * depth;
        int stride = (int)(((long)width * bitsPerPixel + 7) / 8);
        byte[] raw = Inflate(compressed, (long)height * (stride + 1));
        Unfilter(raw, height, stride, Math.Max(1, bitsPerPixel / 8));
        return ToRgba(raw, width, height, stride, colour, depth, palette, transparency);
    }

    private static int Channels(ColourType colour) => colour switch
    {
        ColourType.Grey or ColourType.Indexed => 1,
        ColourType.GreyAlpha => 2,
        ColourType.Rgb => 3,
        ColourType.Rgba => 4,
        _ => throw new InvalidDataException($"Unknown PNG colour type {(int)colour}."),
    };

    private static void CheckDepth(ColourType colour, int depth)
    {
        bool fine = colour switch
        {
            ColourType.Grey => depth is 1 or 2 or 4 or 8 or 16,
            ColourType.Indexed => depth is 1 or 2 or 4 or 8,
            ColourType.Rgb or ColourType.GreyAlpha or ColourType.Rgba => depth is 8 or 16,
            _ => false,
        };

        if (!fine)
        {
            throw new InvalidDataException($"PNG colour type {(int)colour} can't have {depth}-bit samples.");
        }
    }

    private static byte[] Inflate(MemoryStream compressed, long size)
    {
        if (size > int.MaxValue)
        {
            throw new InvalidDataException("PNG is too large.");
        }

        var raw = new byte[size];
        compressed.Position = 0;
        try
        {
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            inflater.ReadExactly(raw);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("PNG's picture data is shorter than its size says.");
        }

        return raw;
    }

    /// <summary>Undoes the per-row filters in place. Each row starts with a byte naming its filter.</summary>
    private static void Unfilter(byte[] raw, int height, int stride, int pixelBytes)
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * (stride + 1);
            int filter = raw[row];
            Span<byte> line = raw.AsSpan(row + 1, stride);
            ReadOnlySpan<byte> above = y == 0 ? default : raw.AsSpan(row - stride, stride);
            switch (filter)
            {
                case 0:
                    break;
                case 1:
                    for (int i = pixelBytes; i < stride; i++)
                    {
                        line[i] += line[i - pixelBytes];
                    }

                    break;
                case 2:
                    for (int i = 0; i < stride && y > 0; i++)
                    {
                        line[i] += above[i];
                    }

                    break;
                case 3:
                    for (int i = 0; i < stride; i++)
                    {
                        int left = i >= pixelBytes ? line[i - pixelBytes] : 0;
                        int up = y > 0 ? above[i] : 0;
                        line[i] += (byte)((left + up) >> 1);
                    }

                    break;
                case 4:
                    for (int i = 0; i < stride; i++)
                    {
                        int left = i >= pixelBytes ? line[i - pixelBytes] : 0;
                        int up = y > 0 ? above[i] : 0;
                        int upLeft = i >= pixelBytes && y > 0 ? above[i - pixelBytes] : 0;
                        line[i] += Paeth(left, up, upLeft);
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unknown PNG row filter {filter}.");
            }
        }
    }

    private static byte Paeth(int left, int up, int upLeft)
    {
        int guess = left + up - upLeft;
        int toLeft = Math.Abs(guess - left);
        int toUp = Math.Abs(guess - up);
        int toUpLeft = Math.Abs(guess - upLeft);
        return (byte)(toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft);
    }

    private static RgbaImage ToRgba(
        byte[] raw, int width, int height, int stride, ColourType colour, int depth, byte[]? palette, byte[]? transparency)
    {
        var pixels = new byte[(long)width * height * 4];
        int channels = Channels(colour);
        int maxSample = (1 << depth) - 1;

        // The one sample value, at full depth, that tRNS marks see-through for grey and RGB pictures.
        int[]? clear = null;
        if (transparency is not null && colour is ColourType.Grey or ColourType.Rgb)
        {
            int count = colour == ColourType.Grey ? 1 : 3;
            if (transparency.Length >= count * 2)
            {
                clear = new int[count];
                for (int c = 0; c < count; c++)
                {
                    clear[c] = BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(c * 2)) & maxSample;
                }
            }
        }

        Span<int> sample = stackalloc int[4];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> line = raw.AsSpan(y * (stride + 1) + 1, stride);
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < channels; c++)
                {
                    sample[c] = Sample(line, x * channels + c, depth);
                }

                int to = (y * width + x) * 4;
                switch (colour)
                {
                    case ColourType.Indexed:
                    {
                        int index = sample[0];
                        if (index * 3 + 2 >= palette!.Length)
                        {
                            throw new InvalidDataException($"PNG colour {index} is past the end of its palette.");
                        }

                        pixels[to] = palette[index * 3];
                        pixels[to + 1] = palette[index * 3 + 1];
                        pixels[to + 2] = palette[index * 3 + 2];
                        pixels[to + 3] = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
                        break;
                    }

                    case ColourType.Grey:
                    case ColourType.GreyAlpha:
                    {
                        byte grey = ToByte(sample[0], depth);
                        pixels[to] = pixels[to + 1] = pixels[to + 2] = grey;
                        pixels[to + 3] = colour == ColourType.GreyAlpha ? ToByte(sample[1], depth)
                            : clear is not null && sample[0] == clear[0] ? (byte)0 : (byte)255;
                        break;
                    }

                    default:
                        pixels[to] = ToByte(sample[0], depth);
                        pixels[to + 1] = ToByte(sample[1], depth);
                        pixels[to + 2] = ToByte(sample[2], depth);
                        pixels[to + 3] = colour == ColourType.Rgba ? ToByte(sample[3], depth)
                            : clear is not null && sample[0] == clear[0] && sample[1] == clear[1] && sample[2] == clear[2] ? (byte)0 : (byte)255;
                        break;
                }
            }
        }

        return new RgbaImage(width, height, pixels);
    }

    /// <summary>The n-th sample of a row, at its full depth. Samples under a byte are packed from the top bit down.</summary>
    private static int Sample(ReadOnlySpan<byte> line, int n, int depth) => depth switch
    {
        8 => line[n],
        16 => (line[n * 2] << 8) | line[n * 2 + 1],
        _ => (line[n * depth / 8] >> (8 - depth - (n * depth % 8))) & ((1 << depth) - 1),
    };

    private static byte ToByte(int sample, int depth) => depth switch
    {
        8 => (byte)sample,
        16 => (byte)(sample >> 8),
        _ => (byte)(sample * 255 / ((1 << depth) - 1)),
    };
}
