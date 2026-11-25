using System.Numerics;

namespace SimpleFps.Core.Objects;

/// <summary>A box lined up with the axes, in map units.</summary>
public readonly record struct Box(Vector3 Min, Vector3 Max)
{
    /// <summary>
    /// Both boxes are grown by this much before checking. Movement stops a body a hair short of
    /// anything it runs into, so without it a player pressed against a trigger's face could miss it.
    /// The original game grew boxes the same way.
    /// </summary>
    public const float Reach = 1f;

    public Vector3 Centre => (Min + Max) / 2f;

    public bool Touches(Vector3 min, Vector3 max) =>
        Min.X - Reach <= max.X + Reach && Max.X + Reach >= min.X - Reach
        && Min.Y - Reach <= max.Y + Reach && Max.Y + Reach >= min.Y - Reach
        && Min.Z - Reach <= max.Z + Reach && Max.Z + Reach >= min.Z - Reach;

    public Box Offset(Vector3 by) => new(Min + by, Max + by);
}
