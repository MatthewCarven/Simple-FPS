using System.Buffers.Binary;

namespace SimpleFps.Core.Textures;

/// <summary>
/// Reads Targa files, which hold most of the sky pictures in <c>content/env</c>: true colour,
/// greyscale or colour-mapped, plain or run-length packed, stored from either corner.
/// </summary>
public static class TgaImage
{
    private const int HeaderLength = 18;
    private const int TopToBottom = 0x20;
    private const int RightToLeft = 0x10;

    /// <exception cref="InvalidDataException">It isn't a Targa file this can read, or it's damaged.</exception>
    public static RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength)
        {
            throw new InvalidDataException("Too short to be a Targa file.");
        }

        int idLength = data[0];
        int mapType = data[1];
        int imageType = data[2];
        int mapFirst = BinaryPrimitives.ReadUInt16LittleEndian(data[3..]);
        int mapLength = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
        int mapEntryBits = data[7];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        int pixelBits = data[16];
        int descriptor = data[17];

        RgbaImage.CheckSize(width, height, "Targa");
        bool packed = imageType is 9 or 10 or 11;
        int kind = packed ? imageType - 8 : imageType;
        if (kind is not (1 or 2 or 3))
        {
            throw new InvalidDataException($"Targa image type {imageType} isn't supported.");
        }

        int at = HeaderLength + idLength;
        byte[]? map = null;
        if (mapType == 1)
        {
            int entryBytes = (mapEntryBits + 7) / 8;
            int mapBytes = mapLength * entryBytes;
            if (at + mapBytes > data.Length)
            {
                throw new InvalidDataException("Targa colour map runs past the end of the file.");
            }

            map = new byte[(mapFirst + mapLength) * 4];
            for (int i = 0; i < mapLength; i++)
            {
                ReadColour(data.Slice(at + i * entryBytes, entryBytes), mapEntryBits, map.AsSpan((mapFirst + i) * 4, 4));
            }

            at += mapBytes;
        }

        bool fine = kind switch
        {
            1 => map is not null && pixelBits == 8,
            2 => pixelBits is 15 or 16 or 24 or 32,
            _ => pixelBits == 8,
        };

        if (!fine)
        {
            throw new InvalidDataException($"Targa type {imageType} with {pixelBits}-bit pixels isn't supported.");
        }

        int pixelBytes = (pixelBits + 7) / 8;
        byte[] stored = packed ? Unpack(data[at..], width * height, pixelBytes) : Plain(data[at..], width * height * pixelBytes);

        var pixels = new byte[width * height * 4];
        bool flipRows = (descriptor & TopToBottom) == 0;
        bool flipColumns = (descriptor & RightToLeft) != 0;
        for (int y = 0; y < height; y++)
        {
            int row = flipRows ? height - 1 - y : y;
            for (int x = 0; x < width; x++)
            {
                int column = flipColumns ? width - 1 - x : x;
                ReadOnlySpan<byte> from = stored.AsSpan((y * width + x) * pixelBytes, pixelBytes);
                Span<byte> to = pixels.AsSpan((row * width + column) * 4, 4);
                switch (kind)
                {
                    case 1:
                        if (from[0] * 4 + 3 >= map!.Length)
                        {
                            throw new InvalidDataException($"Targa colour {from[0]} is past the end of its colour map.");
                        }

                        map.AsSpan(from[0] * 4, 4).CopyTo(to);
                        break;
                    case 2:
                        ReadColour(from, pixelBits, to);
                        break;
                    default:
                        to[0] = to[1] = to[2] = from[0];
                        to[3] = 255;
                        break;
                }
            }
        }

        return new RgbaImage(width, height, pixels);
    }

    /// <summary>Targa stores blue first. 16-bit colours are five bits a channel, with the top bit unused here.</summary>
    private static void ReadColour(ReadOnlySpan<byte> from, int bits, Span<byte> to)
    {
        switch (bits)
        {
            case 15:
            case 16:
            {
                int packed = from[0] | (from[1] << 8);
                to[0] = (byte)(((packed >> 10) & 31) * 255 / 31);
                to[1] = (byte)(((packed >> 5) & 31) * 255 / 31);
                to[2] = (byte)((packed & 31) * 255 / 31);
                to[3] = 255;
                break;
            }

            case 24:
            case 32:
                to[0] = from[2];
                to[1] = from[1];
                to[2] = from[0];
                to[3] = bits == 32 ? from[3] : (byte)255;
                break;
            default:
                throw new InvalidDataException($"Targa colours of {bits} bits aren't supported.");
        }
    }

    private static byte[] Plain(ReadOnlySpan<byte> data, int byteCount)
    {
        if (byteCount > data.Length)
        {
            throw new InvalidDataException("Targa picture data is shorter than its size says.");
        }

        return data[..byteCount].ToArray();
    }

    /// <summary>Each packet is a count byte: top bit set means one pixel repeated, clear means that many stored pixels.</summary>
    private static byte[] Unpack(ReadOnlySpan<byte> data, int pixelCount, int pixelBytes)
    {
        var stored = new byte[pixelCount * pixelBytes];
        int from = 0;
        int to = 0;
        while (to < stored.Length)
        {
            if (from >= data.Length)
            {
                throw new InvalidDataException("Targa picture data is shorter than its size says.");
            }

            int header = data[from++];
            int count = (header & 0x7F) + 1;
            int bytes = Math.Min(count * pixelBytes, stored.Length - to);
            if ((header & 0x80) != 0)
            {
                if (from + pixelBytes > data.Length)
                {
                    throw new InvalidDataException("Targa picture data is shorter than its size says.");
                }

                ReadOnlySpan<byte> pixel = data.Slice(from, pixelBytes);
                from += pixelBytes;
                for (int i = 0; i < bytes; i += pixelBytes)
                {
                    pixel.CopyTo(stored.AsSpan(to + i));
                }
            }
            else
            {
                if (from + bytes > data.Length)
                {
                    throw new InvalidDataException("Targa picture data is shorter than its size says.");
                }

                data.Slice(from, bytes).CopyTo(stored.AsSpan(to));
                from += count * pixelBytes;
            }

            to += bytes;
        }

        return stored;
    }
}
