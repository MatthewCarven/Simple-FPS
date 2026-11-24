using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Bake;

/// <summary>
/// Reads and writes our baked map files.
/// </summary>
/// <remarks>
/// A file starts with "SFBM", the bake version and the hash of the files it was baked from. Then come
/// sections, each a four-letter tag, a length and its contents, ending with an END section. A reader
/// skips tags it doesn't know, so a section can be added (bot paths in milestone 6) without breaking
/// older code, though adding one still bumps the version so every map gets it. Arrays of plain
/// structs are stored as their memory, with the element size in front so a changed struct is caught
/// rather than misread. Little-endian only, like the machines this runs on.
/// </remarks>
public static class BakeFile
{
    /// <summary>Bump this whenever what a bake holds or how it's built changes, and every map re-bakes.</summary>
    public const int Version = 1;

    private static readonly byte[] Magic = "SFBM"u8.ToArray();

    private static readonly uint InfoTag = Tag("INFO");
    private static readonly uint EntitiesTag = Tag("ENTS");
    private static readonly uint GeometryTag = Tag("GEOM");
    private static readonly uint LightTag = Tag("LMAP");
    private static readonly uint CollisionTag = Tag("COLL");
    private static readonly uint EndTag = Tag("END ");

    public static void Write(Stream stream, BakedMap map)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(map.Info.Hash);

        Section(w, InfoTag, () =>
        {
            w.Write(map.Info.Name);
            w.Write(map.Info.Bsp);
            WriteOptional(w, map.Info.Title);
            WriteOptional(w, map.Info.Author);
        });

        Section(w, EntitiesTag, () =>
        {
            w.Write(map.Entities.Count);
            foreach (BspEntity entity in map.Entities)
            {
                w.Write(entity.Fields.Count);
                foreach ((string key, string value) in entity.Fields)
                {
                    w.Write(key);
                    w.Write(value);
                }
            }
        });

        Section(w, GeometryTag, () =>
        {
            w.Write(map.Geometry.Models.Count);
            foreach (IReadOnlyList<MeshBatch> model in map.Geometry.Models)
            {
                w.Write(model.Count);
                foreach (MeshBatch batch in model)
                {
                    w.Write(batch.Texture);
                    w.Write((int)batch.Flags);
                    WriteArray<MapVertex>(w, CollectionsMarshal.AsSpan(batch.Vertices));
                    WriteArray<int>(w, CollectionsMarshal.AsSpan(batch.Indices));
                }
            }
        });

        Section(w, LightTag, () =>
        {
            LightmapAtlas atlas = map.Geometry.Lightmap;
            w.Write(atlas.Width);
            w.Write(atlas.Height);
            WriteArray<byte>(w, atlas.Rgb);
        });

        Section(w, CollisionTag, () =>
        {
            CollisionData c = map.Collision;
            WriteArray<BspPlane>(w, c.Planes);
            WriteArray<BspNode>(w, c.Nodes);
            WriteArray<BspLeaf>(w, c.Leaves);
            WriteArray<ushort>(w, c.LeafBrushes);
            WriteArray<BspBrush>(w, c.Brushes);
            WriteArray<Contents>(w, c.BrushContents);
            WriteArray<ClipPlane>(w, c.Sides);
            WriteArray<BspModel>(w, c.Models);
        });

        Section(w, EndTag, () => { });
    }

    /// <summary>
    /// Reads a baked map, or returns null if it's from another bake version or was baked from
    /// files with a different hash, either of which means it needs baking again.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is damaged or isn't a baked map.</exception>
    public static BakedMap? Read(Stream stream, string expectedHash)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            if (ReadHeader(r) is not { } hash || hash != expectedHash)
            {
                return null;
            }

            MapInfo? info = null;
            List<BspEntity>? entities = null;
            List<IReadOnlyList<MeshBatch>>? models = null;
            LightmapAtlas? atlas = null;
            CollisionData? collision = null;

            while (true)
            {
                (uint tag, long end) = ReadSectionStart(r);
                if (tag == EndTag)
                {
                    break;
                }

                if (tag == InfoTag)
                {
                    info = ReadInfo(r, hash);
                }
                else if (tag == EntitiesTag)
                {
                    entities = ReadEntities(r);
                }
                else if (tag == GeometryTag)
                {
                    models = ReadModels(r);
                }
                else if (tag == LightTag)
                {
                    atlas = new LightmapAtlas(r.ReadInt32(), r.ReadInt32(), ReadArray<byte>(r));
                }
                else if (tag == CollisionTag)
                {
                    collision = ReadCollision(r);
                }

                EndSection(r, end);
            }

            return new BakedMap
            {
                Info = info ?? throw Missing("INFO"),
                Entities = entities ?? throw Missing("ENTS"),
                Geometry = new MapGeometry(models ?? throw Missing("GEOM"), atlas ?? throw Missing("LMAP")),
                Collision = collision ?? throw Missing("COLL"),
            };
        }
        catch (Exception e) when (IsDamage(e))
        {
            throw new InvalidDataException($"Baked map is damaged: {e.Message}", e);
        }
    }

    /// <summary>Reads just the map's details, or null if the file is from another bake version.</summary>
    /// <exception cref="InvalidDataException">The file is damaged or isn't a baked map.</exception>
    public static MapInfo? ReadInfo(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            if (ReadHeader(r) is not { } hash)
            {
                return null;
            }

            while (true)
            {
                (uint tag, long end) = ReadSectionStart(r);
                if (tag == InfoTag)
                {
                    return ReadInfo(r, hash);
                }

                if (tag == EndTag)
                {
                    throw Missing("INFO");
                }

                EndSection(r, end);
            }
        }
        catch (Exception e) when (IsDamage(e))
        {
            throw new InvalidDataException($"Baked map is damaged: {e.Message}", e);
        }
    }

    /// <summary>What reading garbage can throw: running off the end, a bad string length, a repeated key.</summary>
    private static bool IsDamage(Exception e) =>
        e is EndOfStreamException or FormatException or ArgumentException or OverflowException;

    /// <summary>Returns the hash, or null if the file is from another bake version.</summary>
    private static string? ReadHeader(BinaryReader r)
    {
        if (!r.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a baked map.");
        }

        return r.ReadInt32() == Version ? r.ReadString() : null;
    }

    private static MapInfo ReadInfo(BinaryReader r, string hash) =>
        new(r.ReadString(), hash, r.ReadString(), ReadOptional(r), ReadOptional(r));

    private static List<BspEntity> ReadEntities(BinaryReader r)
    {
        int count = ReadCount(r);
        var entities = new List<BspEntity>(count);
        for (int i = 0; i < count; i++)
        {
            int fieldCount = ReadCount(r);
            var fields = new Dictionary<string, string>(fieldCount, StringComparer.Ordinal);
            for (int f = 0; f < fieldCount; f++)
            {
                fields.Add(r.ReadString(), r.ReadString());
            }

            entities.Add(new BspEntity(fields));
        }

        return entities;
    }

    private static List<IReadOnlyList<MeshBatch>> ReadModels(BinaryReader r)
    {
        int modelCount = ReadCount(r);
        var models = new List<IReadOnlyList<MeshBatch>>(modelCount);
        for (int m = 0; m < modelCount; m++)
        {
            int batchCount = ReadCount(r);
            var batches = new List<MeshBatch>(batchCount);
            for (int b = 0; b < batchCount; b++)
            {
                var batch = new MeshBatch(r.ReadString(), (SurfaceFlags)r.ReadInt32());
                ReadArray(r, batch.Vertices);
                ReadArray(r, batch.Indices);
                batches.Add(batch);
            }

            models.Add(batches);
        }

        return models;
    }

    private static CollisionData ReadCollision(BinaryReader r) => new()
    {
        Planes = ReadArray<BspPlane>(r),
        Nodes = ReadArray<BspNode>(r),
        Leaves = ReadArray<BspLeaf>(r),
        LeafBrushes = ReadArray<ushort>(r),
        Brushes = ReadArray<BspBrush>(r),
        BrushContents = ReadArray<Contents>(r),
        Sides = ReadArray<ClipPlane>(r),
        Models = ReadArray<BspModel>(r),
    };

    private static uint Tag(string name) => BitConverter.ToUInt32(Encoding.ASCII.GetBytes(name));

    private static InvalidDataException Missing(string tag) => new($"Baked map has no {tag} section.");

    /// <summary>Writes a tag and a length to fill in once the contents are written.</summary>
    private static void Section(BinaryWriter w, uint tag, Action contents)
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

    private static (uint Tag, long End) ReadSectionStart(BinaryReader r)
    {
        uint tag = r.ReadUInt32();
        long length = r.ReadInt64();
        long end = r.BaseStream.Position + length;
        if (length < 0 || end > r.BaseStream.Length)
        {
            throw new InvalidDataException("A section runs past the end of the baked map.");
        }

        return (tag, end);
    }

    /// <summary>Moves past anything left in a section, which also skips sections this code doesn't know.</summary>
    private static void EndSection(BinaryReader r, long end)
    {
        if (r.BaseStream.Position > end)
        {
            throw new InvalidDataException("A section of the baked map is longer than its length says.");
        }

        r.BaseStream.Position = end;
    }

    private static void WriteOptional(BinaryWriter w, string? text)
    {
        w.Write(text is not null);
        if (text is not null)
        {
            w.Write(text);
        }
    }

    private static string? ReadOptional(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

    private static int ReadCount(BinaryReader r)
    {
        int count = r.ReadInt32();
        return count >= 0 ? count : throw new InvalidDataException("Negative count in baked map.");
    }

    private static void WriteArray<T>(BinaryWriter w, ReadOnlySpan<T> items)
        where T : unmanaged
    {
        w.Write(items.Length);
        w.Write(Unsafe.SizeOf<T>());
        w.Write(MemoryMarshal.AsBytes(items));
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

    private static T[] ReadArray<T>(BinaryReader r)
        where T : unmanaged
    {
        var items = new T[ReadArrayLength<T>(r)];
        r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(items.AsSpan()));
        return items;
    }

    private static void ReadArray<T>(BinaryReader r, List<T> into)
        where T : unmanaged
    {
        int count = ReadArrayLength<T>(r);
        CollectionsMarshal.SetCount(into, count);
        r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(into)));
    }
}
