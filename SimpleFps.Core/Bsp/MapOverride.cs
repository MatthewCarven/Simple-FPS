using System.Buffers.Binary;
using System.Text;

namespace SimpleFps.Core.Bsp;

/// <summary>
/// A <c>name.bsp.override</c> file: a replacement entity list for a map, and the map file to take
/// the geometry from. That map can be another one, so <c>q2dm1-nade.bsp.override</c> plays
/// <c>q2dm1.bsp</c> with its own items and no <c>q2dm1-nade.bsp</c> exists.
/// </summary>
/// <remarks>
/// Laid out as a 4-byte set of flags, then each part the flags name, in order: a 64-byte map path
/// padded with zeros (1), a 4-byte checksum (2), and the entity text with its length in front (4).
/// All 25 files in the collection have flags 5, a path and entities. None has a checksum.
/// </remarks>
public sealed record MapOverride(string? MapPath, string? Entities)
{
    private const int HasPath = 0x1;
    private const int HasChecksum = 0x2;
    private const int HasEntities = 0x4;
    private const int PathSize = 64;

    /// <summary>The file name of the map to use, such as <c>q2dm1.bsp</c>, or null to use the override's own.</summary>
    public string? MapFile => MapPath is null ? null : Path.GetFileName(MapPath.Replace('\\', '/'));

    /// <exception cref="InvalidDataException">The data isn't a readable override file.</exception>
    public static MapOverride Read(ReadOnlySpan<byte> data)
    {
        int flags = ReadInt(data, 0);
        if ((flags & ~(HasPath | HasChecksum | HasEntities)) != 0)
        {
            throw new InvalidDataException($"Unknown override flags {flags:x}.");
        }

        int at = 4;
        string? path = null;
        if ((flags & HasPath) != 0)
        {
            ReadOnlySpan<byte> field = Slice(data, at, PathSize);
            int end = field.IndexOf((byte)0);
            path = Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
            at += PathSize;
        }

        if ((flags & HasChecksum) != 0)
        {
            at += 4;
        }

        string? entities = null;
        if ((flags & HasEntities) != 0)
        {
            int length = ReadInt(data, at);
            entities = Encoding.Latin1.GetString(Slice(data, at + 4, length));
        }

        return new MapOverride(path, entities);
    }

    private static int ReadInt(ReadOnlySpan<byte> data, int at) =>
        BinaryPrimitives.ReadInt32LittleEndian(Slice(data, at, 4));

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int at, int length) =>
        at >= 0 && length >= 0 && at <= data.Length - length
            ? data.Slice(at, length)
            : throw new InvalidDataException("Override file is shorter than its header says.");
}
