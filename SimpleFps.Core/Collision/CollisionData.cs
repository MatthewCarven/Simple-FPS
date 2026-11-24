using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Collision;

/// <summary>
/// Everything collision needs from a map: its tree, its brushes, and each brush side as a plane
/// carrying its surface flags. Worked out once from the map file and then kept in the bake, so
/// loading a baked map never needs the file again.
/// </summary>
public sealed class CollisionData
{
    public required BspPlane[] Planes { get; init; }
    public required BspNode[] Nodes { get; init; }
    public required BspLeaf[] Leaves { get; init; }
    public required ushort[] LeafBrushes { get; init; }
    public required BspBrush[] Brushes { get; init; }

    /// <summary>One per brush. Brushes with no volume are already set to nothing.</summary>
    public required Contents[] BrushContents { get; init; }

    /// <summary>Every brush side, in the map's order, as a plane with its surface flags.</summary>
    public required ClipPlane[] Sides { get; init; }

    /// <summary>
    /// Model 0 is the world. The rest are doors, lifts and other moving brushes, each with its own
    /// branch of the tree. Only the world collides until map objects arrive.
    /// </summary>
    public required BspModel[] Models { get; init; }

    public static CollisionData From(Q2Bsp bsp)
    {
        var sides = new ClipPlane[bsp.BrushSides.Length];
        for (int i = 0; i < sides.Length; i++)
        {
            BspBrushSide side = bsp.BrushSides[i];
            BspPlane plane = bsp.Planes[side.PlaneIndex];
            SurfaceFlags surface = side.TexInfoIndex >= 0 ? bsp.TexInfos[side.TexInfoIndex].Flags : SurfaceFlags.None;
            sides[i] = new ClipPlane(plane.Normal, plane.Distance, surface);
        }

        var contents = new Contents[bsp.Brushes.Length];
        for (int i = 0; i < contents.Length; i++)
        {
            BspBrush brush = bsp.Brushes[i];
            bool flat = BspCollision.HasNoVolume(sides.AsSpan(brush.FirstSide, brush.SideCount));
            contents[i] = flat ? Contents.None : (Contents)brush.Contents;
        }

        return new CollisionData
        {
            Planes = bsp.Planes,
            Nodes = bsp.Nodes,
            Leaves = bsp.Leaves,
            LeafBrushes = bsp.LeafBrushes,
            Brushes = bsp.Brushes,
            BrushContents = contents,
            Sides = sides,
            Models = bsp.Models,
        };
    }
}
