using System.Numerics;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Tests;

/// <summary>A hand-built world of brushes, checked one by one with no tree.</summary>
internal sealed class TestWorld : ICollisionWorld
{
    private readonly List<(ClipPlane[] Planes, Contents Contents)> _brushes = [];

    public TestWorld Add(ClipPlane[] planes, Contents contents = Contents.Solid)
    {
        _brushes.Add((planes, contents));
        return this;
    }

    public TestWorld Box(Vector3 min, Vector3 max, Contents contents = Contents.Solid) => Add(
    [
        new ClipPlane(Vector3.UnitX, max.X),
        new ClipPlane(-Vector3.UnitX, -min.X),
        new ClipPlane(Vector3.UnitY, max.Y),
        new ClipPlane(-Vector3.UnitY, -min.Y),
        new ClipPlane(Vector3.UnitZ, max.Z),
        new ClipPlane(-Vector3.UnitZ, -min.Z),
    ], contents);

    /// <summary>A big flat floor with its top at z = 0.</summary>
    public TestWorld Floor() => Box(new Vector3(-4000, -4000, -64), new Vector3(4000, 4000, 0));

    /// <summary>A wedge rising along +X from <paramref name="startX"/>, at the given angle in degrees.</summary>
    public TestWorld Ramp(float startX, float degrees)
    {
        float radians = float.DegreesToRadians(degrees);
        var normal = new Vector3(-MathF.Sin(radians), 0, MathF.Cos(radians));
        return Add(
        [
            new ClipPlane(normal, Vector3.Dot(normal, new Vector3(startX, 0, 0))),
            new ClipPlane(-Vector3.UnitZ, 0),
            new ClipPlane(Vector3.UnitX, startX + 1000),
            new ClipPlane(Vector3.UnitY, 500),
            new ClipPlane(-Vector3.UnitY, 500),
        ]);
    }

    public TraceResult Trace(Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs, Contents mask)
    {
        var result = new TraceResult { Fraction = 1f };
        foreach ((ClipPlane[] planes, Contents contents) in _brushes)
        {
            if ((contents & mask) != 0)
            {
                BrushClipper.Clip(planes, contents, start, end, mins, maxs, ref result);
            }
        }

        BrushClipper.Finish(ref result, start, end);
        return result;
    }

    public Contents PointContents(Vector3 point)
    {
        Contents found = Contents.None;
        foreach ((ClipPlane[] planes, Contents contents) in _brushes)
        {
            if (planes.All(p => Vector3.Dot(p.Normal, point) - p.Distance <= 0))
            {
                found |= contents;
            }
        }

        return found;
    }
}
