using System.Numerics;
using SimpleFps.Core.Textures;

namespace SimpleFps.Core.Tests;

/// <summary>
/// Measures how well a cube map's faces join. For every pixel on a face's edge, compares it with
/// the pixel just across the edge on the neighbouring face, and with the pixel just inside its
/// own face. Faces laid out right join as smoothly as a picture does inside; a face turned or
/// swapped shows up as a much bigger change across its edges.
/// </summary>
internal static class SkySeams
{
    /// <summary>Mean change in red, green and blue (0 to 255) across the edges, and one pixel in from them.</summary>
    public static (double Across, double Inside) Measure(IReadOnlyList<RgbaImage> faces)
    {
        int size = faces[0].Width;
        double across = 0, inside = 0;
        int count = 0;
        for (int face = 0; face < 6; face++)
        {
            for (int k = 0; k < size; k++)
            {
                // Left, right, top and bottom edges: the edge pixel, one step out of the face, one step in.
                foreach ((int i, int j, int di, int dj) in new[] { (0, k, -1, 0), (size - 1, k, 1, 0), (k, 0, 0, -1), (k, size - 1, 0, 1) })
                {
                    float s = (2 * (i + di) + 1) / (float)size - 1;
                    float t = (2 * (j + dj) + 1) / (float)size - 1;
                    (int other, int oi, int oj) = Find(SkyBox.CubeDirection(face, s, t), size);
                    across += Difference(faces[face], i, j, faces[other], oi, oj);
                    inside += Difference(faces[face], i, j, faces[face], i - di, j - dj);
                    count++;
                }
            }
        }

        return (across / count, inside / count);
    }

    /// <summary>Which face and pixel a direction in the cube's frame lands on.</summary>
    public static (int Face, int I, int J) Find(Vector3 direction, int size)
    {
        int face = 0;
        float best = float.MinValue;
        for (int f = 0; f < 6; f++)
        {
            float along = Vector3.Dot(direction, SkyBox.CubeDirection(f, 0, 0));
            if (along > best)
            {
                best = along;
                face = f;
            }
        }

        Vector3 centre = SkyBox.CubeDirection(face, 0, 0);
        float s = Vector3.Dot(direction, SkyBox.CubeDirection(face, 1, 0) - centre) / best;
        float t = Vector3.Dot(direction, SkyBox.CubeDirection(face, 0, 1) - centre) / best;
        int i = Math.Clamp((int)MathF.Floor((s + 1) / 2 * size), 0, size - 1);
        int j = Math.Clamp((int)MathF.Floor((t + 1) / 2 * size), 0, size - 1);
        return (face, i, j);
    }

    private static double Difference(RgbaImage a, int ai, int aj, RgbaImage b, int bi, int bj)
    {
        int pa = (aj * a.Width + ai) * 4;
        int pb = (bj * b.Width + bi) * 4;
        return (Math.Abs(a.Pixels[pa] - b.Pixels[pb]) + Math.Abs(a.Pixels[pa + 1] - b.Pixels[pb + 1]) + Math.Abs(a.Pixels[pa + 2] - b.Pixels[pb + 2])) / 3.0;
    }
}
