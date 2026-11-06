using System.Buffers.Binary;

namespace SimpleFps.Core.Bsp;

public enum BspKind
{
    Unknown,
    Quake1,
    Quake2,
}

/// <summary>
/// Works out what kind of map a .bsp is from its header. File names can't be trusted:
/// q3dm7.bsp in the collection is a Quake 2 map.
/// </summary>
public static class BspFormat
{
    private const int Quake1Version = 29;
    private const int Quake2Version = 38;

    /// <summary>Bytes needed to tell the formats apart.</summary>
    public const int HeaderLength = 8;

    public static BspKind Identify(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderLength)
        {
            return BspKind.Unknown;
        }

        if (header[..4].SequenceEqual("IBSP"u8))
        {
            int version = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            return version == Quake2Version ? BspKind.Quake2 : BspKind.Unknown;
        }

        // Quake 1 has no magic word: the file simply starts with its version number.
        return BinaryPrimitives.ReadInt32LittleEndian(header) == Quake1Version
            ? BspKind.Quake1
            : BspKind.Unknown;
    }
}
