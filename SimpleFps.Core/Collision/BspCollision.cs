using System.Numerics;
using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Collision;

/// <summary>
/// Collision against a map's world brushes. The map's tree finds the few brushes near a sweep.
/// Not safe to share between threads, because of the stamps that stop a brush being tested twice.
/// </summary>
public sealed class BspCollision : ICollisionWorld
{
    private const int MaxTreeDepth = 1024;

    /// <summary>Widens the tree search a touch, so a box that only just reaches a plane still counts.</summary>
    private const float SearchMargin = 1f;

    private readonly BspPlane[] _planes;
    private readonly BspNode[] _nodes;
    private readonly BspLeaf[] _leaves;
    private readonly ushort[] _leafBrushes;
    private readonly BspBrush[] _brushes;
    private readonly Contents[] _brushContents;
    private readonly ClipPlane[] _sides;
    private readonly int[] _stamps;
    private readonly int _root;
    private int _stamp;

    public BspCollision(Q2Bsp bsp)
    {
        _planes = bsp.Planes;
        _nodes = bsp.Nodes;
        _leaves = bsp.Leaves;
        _leafBrushes = bsp.LeafBrushes;
        _brushes = bsp.Brushes;
        _stamps = new int[bsp.Brushes.Length];
        _root = bsp.Models[0].HeadNode;

        _sides = new ClipPlane[bsp.BrushSides.Length];
        for (int i = 0; i < _sides.Length; i++)
        {
            BspBrushSide side = bsp.BrushSides[i];
            BspPlane plane = bsp.Planes[side.PlaneIndex];
            SurfaceFlags surface = side.TexInfoIndex >= 0 ? bsp.TexInfos[side.TexInfoIndex].Flags : SurfaceFlags.None;
            _sides[i] = new ClipPlane(plane.Normal, plane.Distance, surface);
        }

        _brushContents = new Contents[bsp.Brushes.Length];
        for (int i = 0; i < _brushContents.Length; i++)
        {
            ref readonly BspBrush brush = ref _brushes[i];
            bool flat = HasNoVolume(_sides.AsSpan(brush.FirstSide, brush.SideCount));
            _brushContents[i] = flat ? Contents.None : (Contents)brush.Contents;
        }
    }

    /// <summary>
    /// True for a brush squashed flat, with two opposite sides in the same place. A few maps have
    /// these as compiler leftovers, some of them map-wide, and they'd act as invisible floors.
    /// </summary>
    internal static bool HasNoVolume(ReadOnlySpan<ClipPlane> sides)
    {
        const float Opposite = -0.9999f;

        for (int a = 0; a < sides.Length; a++)
        {
            for (int b = a + 1; b < sides.Length; b++)
            {
                if (Vector3.Dot(sides[a].Normal, sides[b].Normal) < Opposite
                    && sides[a].Distance + sides[b].Distance <= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public TraceResult Trace(Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs, Contents mask)
    {
        var result = new TraceResult { Fraction = 1f };
        _stamp++;

        Vector3 low = Vector3.Min(start, end) + mins - new Vector3(SearchMargin);
        Vector3 high = Vector3.Max(start, end) + maxs + new Vector3(SearchMargin);
        Vector3 centre = (low + high) / 2f;
        Vector3 half = (high - low) / 2f;

        Span<int> stack = stackalloc int[MaxTreeDepth];
        int depth = 0;
        stack[depth++] = _root;

        while (depth > 0)
        {
            int node = stack[--depth];
            if (node < 0)
            {
                ClipLeaf(-1 - node, start, end, mins, maxs, mask, ref result);
                continue;
            }

            ref readonly BspNode current = ref _nodes[node];
            ref readonly BspPlane plane = ref _planes[current.PlaneIndex];
            float distance = Vector3.Dot(plane.Normal, centre) - plane.Distance;
            float reach = Vector3.Dot(Vector3.Abs(plane.Normal), half);

            if (distance >= -reach)
            {
                stack[depth++] = current.FrontChild;
            }

            if (distance <= reach)
            {
                stack[depth++] = current.BackChild;
            }
        }

        BrushClipper.Finish(ref result, start, end);
        return result;
    }

    public Contents PointContents(Vector3 point)
    {
        int node = _root;
        while (node >= 0)
        {
            ref readonly BspNode current = ref _nodes[node];
            ref readonly BspPlane plane = ref _planes[current.PlaneIndex];
            node = Vector3.Dot(plane.Normal, point) - plane.Distance < 0 ? current.BackChild : current.FrontChild;
        }

        return (Contents)_leaves[-1 - node].Contents;
    }

    private void ClipLeaf(int leaf, Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs, Contents mask, ref TraceResult result)
    {
        ref readonly BspLeaf current = ref _leaves[leaf];
        for (int i = 0; i < current.LeafBrushCount; i++)
        {
            int index = _leafBrushes[current.FirstLeafBrush + i];
            if (_stamps[index] == _stamp)
            {
                continue;
            }

            _stamps[index] = _stamp;
            Contents contents = _brushContents[index];
            if ((contents & mask) != 0)
            {
                ref readonly BspBrush brush = ref _brushes[index];
                BrushClipper.Clip(_sides.AsSpan(brush.FirstSide, brush.SideCount), contents, start, end, mins, maxs, ref result);
            }
        }
    }
}
