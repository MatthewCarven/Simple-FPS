using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SimpleFps.Core.Bake;

/// <summary>
/// The pieces every baked file is made of, shared by maps and textures. After a file's header
/// come sections, each a four-letter tag, a length and its contents, ending with an END section.
/// A reader skips tags it doesn't know. Arrays of plain structs are stored as their memory, with
/// the element size in front so a changed struct is caught rather than misread. Little-endian
/// only, like the machines this runs on.
/// </summary>
internal static class BakeSections
{
    public static readonly uint EndTag = Tag("END ");

    public static uint Tag(string name) => BitConverter.ToUInt32(Encoding.ASCII.GetBytes(name));

    /// <summary>What reading garbage can throw: running off the end, a bad string length, a repeated key.</summary>
    public static bool IsDamage(Exception e) =>
        e is EndOfStreamException or FormatException or ArgumentException or OverflowException;

    /// <summary>Writes a tag and a length to fill in once the contents are written.</summary>
    public static void Section(BinaryWriter w, uint tag, Action contents)
    {
        w.Write(tag);
        long lengthAt = w.BaseStream.Position;
        w.Write(0L);
        contents();
        long end = w.BaseStream.Position;
        w.BaseStream.Position = lengthAt;
        w.Write(end - lengthAt - sizeof(long));
        w.BaseStream.Position = end;
    }

    public static (uint Tag, long End) ReadSectionStart(BinaryReader r)
    {
        uint tag = r.ReadUInt32();
        long length = r.ReadInt64();
        long end = r.BaseStream.Position + length;
        if (length < 0 || end > r.BaseStream.Length)
        {
            throw new InvalidDataException("A section runs past the end of the baked file.");
        }

        return (tag, end);
    }

    /// <summary>Moves past anything left in a section, which also skips sections this code doesn't know.</summary>
    public static void EndSection(BinaryReader r, long end)
    {
        if (r.BaseStream.Position > end)
        {
            throw new InvalidDataException("A section of the baked file is longer than its length says.");
        }

        r.BaseStream.Position = end;
    }

    public static void WriteOptional(BinaryWriter w, string? text)
    {
        w.Write(text is not null);
        if (text is not null)
        {
            w.Write(text);
        }
    }

    public static string? ReadOptional(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

    public static int ReadCount(BinaryReader r)
    {
        int count = r.ReadInt32();
        return count >= 0 ? count : throw new InvalidDataException("Negative count in baked file.");
    }

    public static void WriteArray<T>(BinaryWriter w, ReadOnlySpan<T> items)
        where T : unmanaged
    {
        w.Write(items.Length);
        w.Write(Unsafe.SizeOf<T>());
        w.Write(MemoryMarshal.AsBytes(items));
    }

    public static T[] ReadArray<T>(BinaryReader r)
        where T : unmanaged
    {
        var items = new T[ReadArrayLength<T>(r)];
        r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(items.AsSpan()));
        return items;
    }

    public static void ReadArray<T>(BinaryReader r, List<T> into)
        where T : unmanaged
    {
        int count = ReadArrayLength<T>(r);
        CollectionsMarshal.SetCount(into, count);
        r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(into)));
    }

    private static int ReadArrayLength<T>(BinaryReader r)
        where T : unmanaged
    {
        int count = ReadCount(r);
        int size = r.ReadInt32();
        if (size != Unsafe.SizeOf<T>())
        {
            throw new InvalidDataException($"Baked {typeof(T).Name} is {size} bytes, expected {Unsafe.SizeOf<T>()}.");
        }

        if ((long)count * size > r.BaseStream.Length - r.BaseStream.Position)
        {
            throw new InvalidDataException($"Baked {typeof(T).Name} array runs past the end of the file.");
        }

        return count;
    }
}
