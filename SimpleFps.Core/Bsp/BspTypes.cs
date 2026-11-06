using System.Numerics;
using System.Runtime.InteropServices;

namespace SimpleFps.Core.Bsp;

// The layouts below match the file format byte for byte, so lumps can be read straight into arrays.
// Field names follow the q2pro header (inc/format/bsp.h).

[Flags]
public enum SurfaceFlags
{
    None = 0,
    Light = 0x1,
    Slick = 0x2,
    Sky = 0x4,
    Warp = 0x8,
    Trans33 = 0x10,
    Trans66 = 0x20,
    Flowing = 0x40,
    NoDraw = 0x80,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BspPlane
{
    public readonly Vector3 Normal;
    public readonly float Distance;
    public readonly int Type;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BspNode
{
    public readonly int PlaneIndex;
    public readonly int FrontChild;
    public readonly int BackChild;
    private readonly short _minX, _minY, _minZ, _maxX, _maxY, _maxZ;
    public readonly ushort FirstFace;
    public readonly ushort FaceCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BspFace
{
    public readonly ushort PlaneIndex;
    public readonly short Side;
    public readonly int FirstEdge;
    public readonly short EdgeCount;
    public readonly short TexInfoIndex;
    public readonly byte Style0, Style1, Style2, Style3;
    public readonly int LightOffset;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BspLeaf
{
    public readonly int Contents;
    public readonly short Cluster;
    public readonly short Area;
    private readonly short _minX, _minY, _minZ, _maxX, _maxY, _maxZ;
    public readonly ushort FirstLeafFace;
    public readonly ushort LeafFaceCount;
    public readonly ushort FirstLeafBrush;
    public readonly ushort LeafBrushCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BspEdge
{
    public readonly ushort V0;
    public readonly ushort V1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BspModel
{
    public readonly Vector3 Mins;
    public readonly Vector3 Maxs;
    public readonly Vector3 Origin;
    public readonly int HeadNode;
    public readonly int FirstFace;
    public readonly int FaceCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BspBrush
{
    public readonly int FirstSide;
    public readonly int SideCount;
    public readonly int Contents;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BspBrushSide
{
    public readonly ushort PlaneIndex;
    public readonly short TexInfoIndex;
}

/// <summary>How a face's texture is laid onto it. Not blittable, because of the name.</summary>
public sealed record BspTexInfo(Vector4 S, Vector4 T, SurfaceFlags Flags, int Value, string Texture, int NextFrame)
{
    /// <summary>
    /// Texture coordinates in texels. Worked in double because the lighting tools did: rounding
    /// differently here gives a face the wrong lightmap size, which shows up as corrupt patches.
    /// </summary>
    public (double S, double T) TexelsAt(Vector3 p) => (Project(S, p), Project(T, p));

    private static double Project(Vector4 axis, Vector3 p) =>
        (double)p.X * axis.X + (double)p.Y * axis.Y + (double)p.Z * axis.Z + axis.W;
}
