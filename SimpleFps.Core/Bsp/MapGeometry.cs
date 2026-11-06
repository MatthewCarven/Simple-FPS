using System.Numerics;

namespace SimpleFps.Core.Bsp;

/// <param name="TexCoord">In texels. Divide by the texture's size to use it.</param>
/// <param name="LightCoord">Across the whole lightmap atlas, 0 to 1.</param>
public readonly record struct MapVertex(Vector3 Position, Vector3 Normal, Vector2 TexCoord, Vector2 LightCoord);

/// <summary>Triangles sharing a texture and surface flags. Winding is clockwise from the front.</summary>
public sealed class MeshBatch(string texture, SurfaceFlags flags)
{
    public string Texture { get; } = texture;
    public SurfaceFlags Flags { get; } = flags;
    public List<MapVertex> Vertices { get; } = [];
    public List<int> Indices { get; } = [];
}

/// <summary>Baked light for the whole map, packed into one RGB image.</summary>
public sealed record LightmapAtlas(int Width, int Height, byte[] Rgb);

/// <summary>
/// A map's drawable surfaces. Model 0 is the world; the rest are the moving brushes, such as doors
/// and lifts, each placed by the entity that names it.
/// </summary>
public sealed record MapGeometry(IReadOnlyList<IReadOnlyList<MeshBatch>> Models, LightmapAtlas Lightmap);
