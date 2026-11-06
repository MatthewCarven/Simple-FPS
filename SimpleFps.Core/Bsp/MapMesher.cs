using System.Numerics;

namespace SimpleFps.Core.Bsp;

/// <summary>Turns a map's faces into triangles and packs its baked light into one atlas.</summary>
public static class MapMesher
{
    /// <summary>Quake 2 stores one light sample every 16 texels.</summary>
    private const int LuxelSize = 16;
    private const int BytesPerLuxel = 3;
    private const int AtlasGap = 1;
    private const int NoStyle = 255;

    /// <summary>
    /// Light given to surfaces with no baked light. The world shader brightens light 2.6 times,
    /// so 255 / 2.6 comes out at the texture's own colour.
    /// </summary>
    public const byte UnlitValue = 98;

    private const int UnlitBlockSize = 2;

    private static readonly HashSet<string> ToolTextures = ["clip", "trigger", "origin", "hint", "skip", "null"];

    private sealed record LightBlock(int Face, int LuxelsWide, int LuxelsHigh, double MinS, double MinT, int Source)
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public static MapGeometry Build(Q2Bsp bsp)
    {
        var blocks = new Dictionary<int, LightBlock>();
        foreach (int face in DrawnFaces(bsp))
        {
            if (MeasureLight(bsp, face) is { } block)
            {
                blocks[face] = block;
            }
        }

        (int width, int height) = Pack(blocks.Values);
        byte[] rgb = new byte[width * height * BytesPerLuxel];
        Array.Fill(rgb, UnlitValue, 0, UnlitBlockSize * width * BytesPerLuxel);
        foreach (LightBlock block in blocks.Values)
        {
            Blit(bsp.Lighting, block, rgb, width);
        }

        var unlitCoord = new Vector2(1f / width, 1f / height);
        var models = new List<IReadOnlyList<MeshBatch>>(bsp.Models.Length);
        foreach (BspModel model in bsp.Models)
        {
            var batches = new Dictionary<(string, SurfaceFlags), MeshBatch>();
            for (int f = model.FirstFace; f < model.FirstFace + model.FaceCount; f++)
            {
                if (!IsDrawn(bsp, f))
                {
                    continue;
                }

                blocks.TryGetValue(f, out LightBlock? block);
                EmitFace(bsp, f, block, width, height, unlitCoord, batches);
            }

            models.Add([.. batches.Values]);
        }

        return new MapGeometry(models, new LightmapAtlas(width, height, rgb));
    }

    private static IEnumerable<int> DrawnFaces(Q2Bsp bsp)
    {
        for (int f = 0; f < bsp.Faces.Length; f++)
        {
            if (IsDrawn(bsp, f))
            {
                yield return f;
            }
        }
    }

    private static bool IsDrawn(Q2Bsp bsp, int face)
    {
        BspTexInfo info = bsp.TexInfos[bsp.Faces[face].TexInfoIndex];
        if ((info.Flags & (SurfaceFlags.NoDraw | SurfaceFlags.Sky)) != 0)
        {
            return false;
        }

        string name = info.Texture[(info.Texture.LastIndexOf('/') + 1)..];
        return !ToolTextures.Contains(name);
    }

    private static IEnumerable<Vector3> Corners(Q2Bsp bsp, BspFace face)
    {
        for (int i = 0; i < face.EdgeCount; i++)
        {
            int surfEdge = bsp.SurfEdges[face.FirstEdge + i];
            BspEdge edge = bsp.Edges[Math.Abs(surfEdge)];
            yield return bsp.Vertices[surfEdge >= 0 ? edge.V0 : edge.V1];
        }
    }

    /// <summary>Works out a face's lightmap size and where its data sits, or null if it has none.</summary>
    private static LightBlock? MeasureLight(Q2Bsp bsp, int index)
    {
        BspFace face = bsp.Faces[index];
        BspTexInfo info = bsp.TexInfos[face.TexInfoIndex];
        if (face.LightOffset < 0 || face.Style0 == NoStyle
            || (info.Flags & (SurfaceFlags.Warp | SurfaceFlags.Trans33 | SurfaceFlags.Trans66)) != 0)
        {
            return null;
        }

        double minS = double.MaxValue, maxS = double.MinValue, minT = double.MaxValue, maxT = double.MinValue;
        foreach (Vector3 corner in Corners(bsp, face))
        {
            (double s, double t) = info.TexelsAt(corner);
            minS = Math.Min(minS, s);
            maxS = Math.Max(maxS, s);
            minT = Math.Min(minT, t);
            maxT = Math.Max(maxT, t);
        }

        double floorS = Math.Floor(minS / LuxelSize);
        double floorT = Math.Floor(minT / LuxelSize);
        int wide = (int)(Math.Ceiling(maxS / LuxelSize) - floorS) + 1;
        int high = (int)(Math.Ceiling(maxT / LuxelSize) - floorT) + 1;

        // Some compilers leave offsets that run off the end. Those faces fall back to flat light.
        return face.LightOffset + wide * high * BytesPerLuxel > bsp.Lighting.Length
            ? null
            : new LightBlock(index, wide, high, floorS * LuxelSize, floorT * LuxelSize, face.LightOffset);
    }

    /// <summary>Lays blocks out in rows, tallest first. The top rows are kept for the unlit block.</summary>
    private static (int Width, int Height) Pack(IEnumerable<LightBlock> blocks)
    {
        List<LightBlock> tallestFirst = [.. blocks.OrderByDescending(b => b.LuxelsHigh)];
        long area = tallestFirst.Sum(b => (long)(b.LuxelsWide + AtlasGap) * (b.LuxelsHigh + AtlasGap));
        int width = 256;
        while (width * (long)width < area * 2 && width < 4096)
        {
            width *= 2;
        }

        width = Math.Max(width, (tallestFirst.Count == 0 ? 0 : tallestFirst.Max(b => b.LuxelsWide)) + AtlasGap);

        int x = 0;
        int y = UnlitBlockSize + AtlasGap;
        int rowHeight = 0;
        foreach (LightBlock block in tallestFirst)
        {
            if (x + block.LuxelsWide > width)
            {
                x = 0;
                y += rowHeight + AtlasGap;
                rowHeight = 0;
            }

            block.X = x;
            block.Y = y;
            x += block.LuxelsWide + AtlasGap;
            rowHeight = Math.Max(rowHeight, block.LuxelsHigh);
        }

        return (width, y + rowHeight);
    }

    private static void Blit(byte[] lighting, LightBlock block, byte[] rgb, int atlasWidth)
    {
        int rowBytes = block.LuxelsWide * BytesPerLuxel;
        for (int row = 0; row < block.LuxelsHigh; row++)
        {
            int to = ((block.Y + row) * atlasWidth + block.X) * BytesPerLuxel;
            Buffer.BlockCopy(lighting, block.Source + row * rowBytes, rgb, to, rowBytes);
        }
    }

    private static void EmitFace(
        Q2Bsp bsp,
        int index,
        LightBlock? block,
        int atlasWidth,
        int atlasHeight,
        Vector2 unlitCoord,
        Dictionary<(string, SurfaceFlags), MeshBatch> batches)
    {
        BspFace face = bsp.Faces[index];
        BspTexInfo info = bsp.TexInfos[face.TexInfoIndex];
        BspPlane plane = bsp.Planes[face.PlaneIndex];
        Vector3 normal = face.Side == 0 ? plane.Normal : -plane.Normal;

        var key = (info.Texture, info.Flags);
        if (!batches.TryGetValue(key, out MeshBatch? batch))
        {
            batch = new MeshBatch(info.Texture, info.Flags);
            batches[key] = batch;
        }

        int first = batch.Vertices.Count;
        List<Vector3> corners = [.. Corners(bsp, face)];
        foreach (Vector3 corner in corners)
        {
            (double s, double t) = info.TexelsAt(corner);
            Vector2 light = unlitCoord;
            if (block is not null)
            {
                // Half a luxel in, so a corner lands on the centre of its sample.
                light = new Vector2(
                    (float)((s - block.MinS) / LuxelSize + 0.5 + block.X) / atlasWidth,
                    (float)((t - block.MinT) / LuxelSize + 0.5 + block.Y) / atlasHeight);
            }

            batch.Vertices.Add(new MapVertex(corner, normal, new Vector2((float)s, (float)t), light));
        }

        Triangulate(corners, normal, first, batch.Indices);
    }

    /// <summary>
    /// Ear clipping, because some map compilers merge surfaces into L shapes and other non-convex
    /// faces, which a simple fan would spill past. Every corner is kept, even ones partway along a
    /// straight edge: those are what stop cracks where a neighbouring face meets this one.
    /// </summary>
    private static void Triangulate(List<Vector3> corners, Vector3 normal, int first, List<int> indices)
    {
        List<int> remaining = [.. Enumerable.Range(0, corners.Count)];
        RemoveSpikes(corners, remaining);

        while (remaining.Count > 3)
        {
            int ear = FindEar(corners, remaining, normal);
            if (ear < 0)
            {
                // Only straight-line slivers are left, with no real area for a fan to get wrong.
                break;
            }

            int count = remaining.Count;
            AddTriangle(remaining[(ear + count - 1) % count], remaining[ear], remaining[(ear + 1) % count]);
            remaining.RemoveAt(ear);
        }

        for (int i = 1; i < remaining.Count - 1; i++)
        {
            AddTriangle(remaining[0], remaining[i], remaining[i + 1]);
        }

        void AddTriangle(int a, int b, int c)
        {
            indices.Add(first + a);
            indices.Add(first + b);
            indices.Add(first + c);
        }
    }

    /// <summary>
    /// A few maps have outlines that run out to a point and straight back along the same line.
    /// The spike has no area, so dropping the tip and the repeated corner loses nothing.
    /// </summary>
    private static void RemoveSpikes(List<Vector3> corners, List<int> remaining)
    {
        int i = 0;
        while (i < remaining.Count && remaining.Count > 3)
        {
            int count = remaining.Count;
            int next = (i + 1) % count;
            if (corners[remaining[(i + count - 1) % count]] != corners[remaining[next]])
            {
                i++;
                continue;
            }

            remaining.RemoveAt(Math.Max(i, next));
            remaining.RemoveAt(Math.Min(i, next));
            i = Math.Max(0, i - 1);
        }
    }

    /// <summary>
    /// Finds a corner that can be cut off cleanly, starting from the second so a plain convex
    /// face comes out as an ordinary fan. Returns -1 if there isn't one.
    /// </summary>
    private static int FindEar(List<Vector3> corners, List<int> remaining, Vector3 normal)
    {
        int count = remaining.Count;
        for (int step = 1; step <= count; step++)
        {
            int i = step % count;
            Vector3 a = corners[remaining[(i + count - 1) % count]];
            Vector3 b = corners[remaining[i]];
            Vector3 c = corners[remaining[(i + 1) % count]];

            if (TurnsClockwise(a, b, c, normal) && !AnyCornerInside(corners, remaining, a, b, c, normal))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TurnsClockwise(Vector3 a, Vector3 b, Vector3 c, Vector3 normal) =>
        Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0;

    private static bool AnyCornerInside(
        List<Vector3> corners, List<int> remaining, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
    {
        foreach (int index in remaining)
        {
            Vector3 p = corners[index];
            if (p == a || p == b || p == c)
            {
                continue;
            }

            // A point on an edge counts as inside, or cutting here would hide a crack-stopping corner.
            if (!TurnsAntiClockwise(a, b, p, normal)
                && !TurnsAntiClockwise(b, c, p, normal)
                && !TurnsAntiClockwise(c, a, p, normal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TurnsAntiClockwise(Vector3 a, Vector3 b, Vector3 p, Vector3 normal) =>
        Vector3.Dot(Vector3.Cross(b - a, p - a), normal) > 0;
}
