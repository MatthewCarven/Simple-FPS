using System.Runtime.InteropServices;
using System.Text;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;
using static SimpleFps.Core.Bake.BakeSections;

namespace SimpleFps.Core.Bake;

/// <summary>
/// Reads and writes our baked map files.
/// </summary>
/// <remarks>
/// A file starts with "SFBM", the bake version and the hash of the files it was baked from. Then come
/// the sections described in <see cref="BakeSections"/>. A reader skips tags it doesn't know, so a
/// section can be added (bot paths in milestone 6) without breaking older code, though adding one
/// still bumps the version so every map gets it.
/// </remarks>
public static class BakeFile
{
    /// <summary>
    /// Bump this whenever what a bake holds or how it's built changes, and every map re-bakes.
    /// 2 (3b): sky surfaces are meshed, so they can be drawn.
    /// </summary>
    public const int Version = 2;

    private static readonly byte[] Magic = "SFBM"u8.ToArray();

    private static readonly uint InfoTag = Tag("INFO");
    private static readonly uint EntitiesTag = Tag("ENTS");
    private static readonly uint GeometryTag = Tag("GEOM");
    private static readonly uint LightTag = Tag("LMAP");
    private static readonly uint CollisionTag = Tag("COLL");

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

    private static InvalidDataException Missing(string tag) => new($"Baked map has no {tag} section.");
}
