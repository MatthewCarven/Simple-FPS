using System.Runtime.InteropServices;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Tests;

/// <summary>Exact comparisons of maps, bakes and their parts. Every float has to match to the bit.</summary>
internal static class BakeAssert
{
    public static void SameGeometry(MapGeometry expected, MapGeometry actual)
    {
        Assert.Equal(expected.Models.Count, actual.Models.Count);
        for (int m = 0; m < expected.Models.Count; m++)
        {
            Assert.Equal(expected.Models[m].Count, actual.Models[m].Count);
            for (int b = 0; b < expected.Models[m].Count; b++)
            {
                MeshBatch want = expected.Models[m][b];
                MeshBatch got = actual.Models[m][b];
                Assert.Equal(want.Texture, got.Texture);
                Assert.Equal(want.Flags, got.Flags);
                SameBytes<MapVertex>(CollectionsMarshal.AsSpan(want.Vertices), CollectionsMarshal.AsSpan(got.Vertices), $"model {m} {want.Texture} vertices");
                SameBytes<int>(CollectionsMarshal.AsSpan(want.Indices), CollectionsMarshal.AsSpan(got.Indices), $"model {m} {want.Texture} indices");
            }
        }

        Assert.Equal(expected.Lightmap.Width, actual.Lightmap.Width);
        Assert.Equal(expected.Lightmap.Height, actual.Lightmap.Height);
        SameBytes<byte>(expected.Lightmap.Rgb, actual.Lightmap.Rgb, "light atlas");
    }

    public static void SameCollision(CollisionData expected, CollisionData actual)
    {
        SameBytes<BspPlane>(expected.Planes, actual.Planes, "planes");
        SameBytes<BspNode>(expected.Nodes, actual.Nodes, "nodes");
        SameBytes<BspLeaf>(expected.Leaves, actual.Leaves, "leaves");
        SameBytes<ushort>(expected.LeafBrushes, actual.LeafBrushes, "leaf brushes");
        SameBytes<BspBrush>(expected.Brushes, actual.Brushes, "brushes");
        SameBytes<Contents>(expected.BrushContents, actual.BrushContents, "brush contents");
        SameBytes<ClipPlane>(expected.Sides, actual.Sides, "sides");
        SameBytes<BspModel>(expected.Models, actual.Models, "models");
    }

    public static void SameEntities(IReadOnlyList<BspEntity> expected, IReadOnlyList<BspEntity> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Fields, actual[i].Fields);
        }
    }

    /// <summary>Two collision worlds give exactly the same answer to the same trace.</summary>
    public static void SameTrace(TraceResult expected, TraceResult actual, string where)
    {
        bool same = expected.Fraction.Equals(actual.Fraction)
            && expected.End.Equals(actual.End)
            && expected.Normal.Equals(actual.Normal)
            && expected.Contents == actual.Contents
            && expected.Surface == actual.Surface
            && expected.StartSolid == actual.StartSolid
            && expected.AllSolid == actual.AllSolid;
        Assert.True(same, $"{where}: {expected.Fraction} {expected.Normal} vs {actual.Fraction} {actual.Normal}");
    }

    private static void SameBytes<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, string what)
        where T : unmanaged
    {
        Assert.True(expected.Length == actual.Length, $"{what}: {expected.Length} vs {actual.Length}");
        Assert.True(MemoryMarshal.AsBytes(expected).SequenceEqual(MemoryMarshal.AsBytes(actual)), $"{what} differ");
    }
}
