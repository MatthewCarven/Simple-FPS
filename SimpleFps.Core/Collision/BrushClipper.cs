using System.Numerics;

namespace SimpleFps.Core.Collision;

/// <summary>Sweeps a box against one convex brush.</summary>
internal static class BrushClipper
{
    /// <summary>Stops boxes this far short of a surface, so they never quite touch it.</summary>
    public const float Gap = 1f / 32f;

    /// <summary>Keeps the nearest hit in <paramref name="best"/>.</summary>
    public static void Clip(
        ReadOnlySpan<ClipPlane> planes,
        Contents contents,
        Vector3 start,
        Vector3 end,
        Vector3 mins,
        Vector3 maxs,
        ref TraceResult best)
    {
        if (planes.IsEmpty)
        {
            return;
        }

        // Starts unreachably low, not at -1. A box already inside the gap and creeping forward
        // gives a large negative fraction, and a -1 here would read that as never having entered.
        float enter = float.NegativeInfinity;
        float leave = 1f;
        bool startsOutside = false;
        bool endsOutside = false;
        ClipPlane entered = default;

        foreach (ref readonly ClipPlane plane in planes)
        {
            // Moving each plane out by the box's nearest corner lets the box be treated as a point.
            Vector3 n = plane.Normal;
            var corner = new Vector3(n.X < 0 ? maxs.X : mins.X, n.Y < 0 ? maxs.Y : mins.Y, n.Z < 0 ? maxs.Z : mins.Z);
            float distance = plane.Distance - Vector3.Dot(corner, n);

            float from = Vector3.Dot(start, n) - distance;
            float to = Vector3.Dot(end, n) - distance;

            startsOutside |= from > 0;
            endsOutside |= to > 0;

            if (from > 0 && to >= from)
            {
                return; // Outside this side and not heading in, so it can never touch the brush.
            }

            if (from <= 0 && to <= 0)
            {
                continue;
            }

            if (from > to)
            {
                float fraction = (from - Gap) / (from - to);
                if (fraction > enter)
                {
                    enter = fraction;
                    entered = plane;
                }
            }
            else
            {
                leave = MathF.Min(leave, (from + Gap) / (from - to));
            }
        }

        if (!startsOutside)
        {
            best.StartSolid = true;
            best.AllSolid |= !endsOutside;
            return;
        }

        if (enter < leave && !float.IsNegativeInfinity(enter) && enter < best.Fraction)
        {
            best.Fraction = MathF.Max(enter, 0f);
            best.Normal = entered.Normal;
            best.Surface = entered.Surface;
            best.Contents = contents;
        }
    }

    /// <summary>Fills in the end point once every brush has had its say.</summary>
    public static void Finish(ref TraceResult result, Vector3 start, Vector3 end)
    {
        if (result.AllSolid)
        {
            result.Fraction = 0f;
        }

        result.End = result.Fraction >= 1f ? end : Vector3.Lerp(start, end, result.Fraction);
    }
}
