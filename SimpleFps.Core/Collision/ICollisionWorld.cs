using System.Numerics;
using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Collision;

/// <summary>One flat side of a convex brush. Space on the normal's side is outside the brush.</summary>
public readonly record struct ClipPlane(Vector3 Normal, float Distance, SurfaceFlags Surface = SurfaceFlags.None);

/// <summary>The result of sweeping a box through the world.</summary>
public struct TraceResult
{
    /// <summary>How far the box got, from 0 to 1. 1 means it reached the end.</summary>
    public float Fraction;

    public Vector3 End;
    public Vector3 Normal;
    public Contents Contents;
    public SurfaceFlags Surface;

    /// <summary>The box began inside something.</summary>
    public bool StartSolid;

    /// <summary>The box was inside something for the whole sweep.</summary>
    public bool AllSolid;

    public readonly bool Hit => Fraction < 1f;

    /// <summary>The box travelled the whole way and never touched anything solid.</summary>
    public readonly bool Clear => Fraction >= 1f && !StartSolid;
}

public interface ICollisionWorld
{
    /// <summary>How far short of a surface sweeps stop. Anything else moving a body should leave this much too.</summary>
    const float SurfaceGap = BrushClipper.Gap;

    /// <summary>
    /// Sweeps a box from start to end. <paramref name="mins"/> and <paramref name="maxs"/> are its
    /// corners relative to the position. Use zero for both to trace a point.
    /// </summary>
    TraceResult Trace(Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs, Contents mask);

    Contents PointContents(Vector3 point);
}
