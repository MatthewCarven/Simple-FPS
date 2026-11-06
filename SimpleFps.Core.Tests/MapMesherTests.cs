using System.Numerics;
using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Tests;

public class MapMesherTests
{
    private static MapGeometry Mesh(byte[] file) => MapMesher.Build(Q2Bsp.Read(file));

    private static (byte R, byte G, byte B) LightAt(LightmapAtlas atlas, Vector2 coord)
    {
        int x = (int)(coord.X * atlas.Width);
        int y = (int)(coord.Y * atlas.Height);
        int at = (y * atlas.Width + x) * 3;
        return (atlas.Rgb[at], atlas.Rgb[at + 1], atlas.Rgb[at + 2]);
    }

    [Fact]
    public void AQuadBecomesTwoTrianglesOnOneBatch()
    {
        MeshBatch batch = Assert.Single(Mesh(TinyBsp.Build()).Models[0]);

        Assert.Equal("base/floor", batch.Texture);
        Assert.Equal(4, batch.Vertices.Count);
        Assert.Equal([0, 1, 2, 0, 2, 3], batch.Indices);
    }

    [Fact]
    public void VerticesCarryTheFaceNormalAndTexelCoordinates()
    {
        MeshBatch batch = Assert.Single(Mesh(TinyBsp.Build()).Models[0]);

        Assert.All(batch.Vertices, v => Assert.Equal(Vector3.UnitZ, v.Normal));
        Assert.Equal(new Vector2(32, 16), batch.Vertices[2].TexCoord);
    }

    [Fact]
    public void TrianglesAreClockwiseFromTheFront()
    {
        MeshBatch batch = Assert.Single(Mesh(TinyBsp.Build()).Models[0]);
        Vector3 a = batch.Vertices[batch.Indices[0]].Position;
        Vector3 b = batch.Vertices[batch.Indices[1]].Position;
        Vector3 c = batch.Vertices[batch.Indices[2]].Position;

        // Clockwise from the front means the right-handed cross product points away from the viewer.
        Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), Vector3.UnitZ) < 0);
    }

    [Fact]
    public void EachCornerSamplesItsOwnLightValue()
    {
        MapGeometry geometry = Mesh(TinyBsp.Build());
        MeshBatch batch = Assert.Single(geometry.Models[0]);

        Assert.Equal((10, 11, 12), ToInts(LightAt(geometry.Lightmap, batch.Vertices[0].LightCoord)));
        Assert.Equal((60, 61, 62), ToInts(LightAt(geometry.Lightmap, batch.Vertices[2].LightCoord)));
    }

    private static (int, int, int) ToInts((byte R, byte G, byte B) c) => (c.R, c.G, c.B);

    private static MeshBatch Outline(params Vector3[] outline) =>
        Assert.Single(Mesh(TinyBsp.Build(lightOffset: -1, outline: outline)).Models[0]);

    /// <summary>Adds up triangle areas, failing on any that faces the wrong way.</summary>
    private static float CoveredArea(MeshBatch batch)
    {
        float total = 0;
        for (int i = 0; i < batch.Indices.Count; i += 3)
        {
            Vector3 a = batch.Vertices[batch.Indices[i]].Position;
            Vector3 b = batch.Vertices[batch.Indices[i + 1]].Position;
            Vector3 c = batch.Vertices[batch.Indices[i + 2]].Position;
            Vector3 cross = Vector3.Cross(b - a, c - a);
            Assert.True(cross.Z <= 0, $"Triangle {i / 3} faces the wrong way.");
            total += cross.Length() / 2;
        }

        return total;
    }

    [Fact]
    public void AnLShapedFaceCoversExactlyItsOwnArea()
    {
        // The notch makes this non-convex: a fan from the first corner would spill across it.
        // A 40 x 40 square missing a 20 x 20 corner, with the fan's start beside the notch.
        MeshBatch batch = Outline(
            new Vector3(40, 20, 0),
            new Vector3(40, 0, 0),
            new Vector3(0, 0, 0),
            new Vector3(0, 40, 0),
            new Vector3(20, 40, 0),
            new Vector3(20, 20, 0));

        Assert.Equal(1200f, CoveredArea(batch), 0.01f);
        Assert.Equal(12, batch.Indices.Count);
    }

    [Fact]
    public void ACornerPartwayAlongAnEdgeIsStillUsed()
    {
        // The extra corner at (16, 16) stops a crack against a face that ends there.
        MeshBatch batch = Outline(
            new Vector3(0, 0, 0),
            new Vector3(0, 16, 0),
            new Vector3(16, 16, 0),
            new Vector3(32, 16, 0),
            new Vector3(32, 0, 0));

        Assert.Equal(512f, CoveredArea(batch), 0.01f);
        Assert.Equal(5, batch.Indices.Distinct().Count());
    }

    [Fact]
    public void AZeroWidthSpikeInAnOutlineIsDropped()
    {
        // Real maps do this: the outline runs out to (48, 16) and straight back.
        MeshBatch batch = Outline(
            new Vector3(0, 0, 0),
            new Vector3(0, 16, 0),
            new Vector3(32, 16, 0),
            new Vector3(48, 16, 0),
            new Vector3(32, 16, 0),
            new Vector3(32, 0, 0));

        Assert.Equal(512f, CoveredArea(batch), 0.01f);
    }

    [Fact]
    public void ARepeatedCornerDoesNotBreakTheFace()
    {
        MeshBatch batch = Outline(
            new Vector3(0, 0, 0),
            new Vector3(0, 16, 0),
            new Vector3(0, 16, 0),
            new Vector3(32, 16, 0),
            new Vector3(32, 0, 0));

        Assert.Equal(512f, CoveredArea(batch), 0.01f);
    }

    [Theory]
    [InlineData(SurfaceFlags.Warp)]
    [InlineData(SurfaceFlags.Trans33)]
    [InlineData(SurfaceFlags.Trans66)]
    public void LiquidsAndGlassUseTheFlatUnlitBlock(SurfaceFlags flags)
    {
        MapGeometry geometry = Mesh(TinyBsp.Build(flags));
        MeshBatch batch = Assert.Single(geometry.Models[0]);

        Assert.All(batch.Vertices, v =>
            Assert.Equal(
                (MapMesher.UnlitValue, MapMesher.UnlitValue, MapMesher.UnlitValue),
                LightAt(geometry.Lightmap, v.LightCoord)));
    }

    [Fact]
    public void AFaceWithNoLightDataUsesTheFlatUnlitBlock()
    {
        MapGeometry geometry = Mesh(TinyBsp.Build(lightOffset: -1));
        MeshBatch batch = Assert.Single(geometry.Models[0]);

        Assert.Equal(
            (MapMesher.UnlitValue, MapMesher.UnlitValue, MapMesher.UnlitValue),
            LightAt(geometry.Lightmap, batch.Vertices[0].LightCoord));
    }

    [Fact]
    public void ALightOffsetPastTheEndFallsBackInsteadOfCrashing()
    {
        MapGeometry geometry = Mesh(TinyBsp.Build(lightOffset: TinyBsp.Light.Length));

        Assert.Single(geometry.Models[0]);
    }

    [Theory]
    [InlineData(SurfaceFlags.NoDraw, "base/floor")]
    [InlineData(SurfaceFlags.Sky, "base/floor")]
    [InlineData(SurfaceFlags.None, "e1u1/clip")]
    [InlineData(SurfaceFlags.None, "e1u1/trigger")]
    public void HiddenSurfacesAreLeftOut(SurfaceFlags flags, string texture)
    {
        Assert.Empty(Mesh(TinyBsp.Build(flags, texture)).Models[0]);
    }
}
