using System.Numerics;

namespace SimpleFps.Core.Textures;

/// <summary>
/// A map's sky: six pictures, one for each side of a box around the world, seen through the
/// map's sky surfaces as if infinitely far away. Turns Quake 2's six pictures into the cube map
/// layout graphics cards use.
/// </summary>
/// <remarks>
/// Quake 2 names the pictures by suffix, and their names don't come from one viewpoint: looking
/// east (+X) you see <c>rt</c>, looking north (+Y) you see <c>bk</c>. The four walls are upright;
/// <c>up</c> is seen as if facing east and tipping your head back, and <c>dn</c> as if facing east
/// and looking at your feet. Checked against 223 sky boxes in <c>content/env</c>: their edges meet
/// with no more change than between neighbouring pixels inside a picture.
/// </remarks>
public static class SkyBox
{
    /// <summary>What Quake 2 uses when a map names no sky.</summary>
    public const string DefaultName = "unit1_";

    /// <summary>The picture suffixes, in the order <see cref="ToCubeFaces"/> takes them.</summary>
    public static IReadOnlyList<string> Sides { get; } = ["rt", "bk", "lf", "ft", "up", "dn"];

    /// <summary>In the map's frame (X east, Y north, Z up): where each picture faces, and where its right and top edges point.</summary>
    private static readonly (Vector3 Facing, Vector3 Right, Vector3 Top)[] Pictures =
    [
        (Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ),
        (Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
        (-Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
        (-Vector3.UnitY, -Vector3.UnitX, Vector3.UnitZ),
        (Vector3.UnitZ, -Vector3.UnitY, -Vector3.UnitX),
        (-Vector3.UnitZ, -Vector3.UnitY, Vector3.UnitX),
    ];

    /// <summary>The direction, in the map's frame, of a point on a picture: u from its left edge, v from its top, both 0 to 1.</summary>
    public static Vector3 PictureDirection(int side, float u, float v)
    {
        (Vector3 facing, Vector3 right, Vector3 top) = Pictures[side];
        return facing + right * (u * 2 - 1) + top * (1 - v * 2);
    }

    /// <summary>Which picture a direction in the map's frame lands on, and where on it: u from the left, v from the top.</summary>
    public static (int Side, float U, float V) Lookup(Vector3 direction)
    {
        int side = 0;
        float best = float.MinValue;
        for (int i = 0; i < Pictures.Length; i++)
        {
            float along = Vector3.Dot(direction, Pictures[i].Facing);
            if (along > best)
            {
                best = along;
                side = i;
            }
        }

        (_, Vector3 right, Vector3 top) = Pictures[side];
        return (side, (Vector3.Dot(direction, right) / best + 1) / 2, (1 - Vector3.Dot(direction, top) / best) / 2);
    }

    /// <summary>
    /// The direction of a point on a cube map face, in the cube's frame: Y up, which is the map's
    /// (x, y, z) as (x, z, -y). Faces go +X, -X, +Y, -Y, +Z, -Z. s runs across a face from its left
    /// edge and t down from its top, both -1 to 1, as graphics cards lay cube maps out.
    /// </summary>
    public static Vector3 CubeDirection(int face, float s, float t) => face switch
    {
        0 => new Vector3(1, -t, -s),
        1 => new Vector3(-1, -t, s),
        2 => new Vector3(s, 1, t),
        3 => new Vector3(s, -1, -t),
        4 => new Vector3(s, -t, 1),
        5 => new Vector3(-s, -t, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    /// <summary>The cube's frame to the map's.</summary>
    public static Vector3 CubeToMap(Vector3 cube) => new(cube.X, -cube.Z, cube.Y);

    /// <summary>
    /// Builds the six cube map faces from Quake 2's six pictures, given in <see cref="Sides"/> order.
    /// Every face comes out square, at the size of the largest picture, so pictures of another size
    /// are stretched to fit.
    /// </summary>
    public static RgbaImage[] ToCubeFaces(IReadOnlyList<RgbaImage> pictures)
    {
        if (pictures.Count != Sides.Count)
        {
            throw new ArgumentException($"A sky needs {Sides.Count} pictures, got {pictures.Count}.");
        }

        int size = pictures.Max(p => Math.Max(p.Width, p.Height));
        var faces = new RgbaImage[6];
        for (int face = 0; face < 6; face++)
        {
            var pixels = new byte[size * size * 4];
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    float s = (2 * i + 1) / (float)size - 1;
                    float t = (2 * j + 1) / (float)size - 1;
                    (int side, float u, float v) = Lookup(CubeToMap(CubeDirection(face, s, t)));
                    SampleInto(pictures[side], u, v, pixels.AsSpan((j * size + i) * 4, 4));
                }
            }

            faces[face] = new RgbaImage(size, size, pixels);
        }

        return faces;
    }

    /// <summary>
    /// Two skies that differ by more than this are different pictures, not one redrawn at a higher
    /// resolution. Measured by <see cref="Difference"/>: the pack's night1 differs from the original
    /// by about 1, while its unit1_ to unit9_, one mountain range in two tints, differ from Quake 2's
    /// red skies by 91 to 100.
    /// </summary>
    public const double SamePictureLimit = 32;

    private const int ThumbnailSize = 16;

    /// <summary>
    /// How different two skies look: each side shrunk to 16 x 16 by averaging, then the mean change
    /// in red, green and blue (0 to 255) between the two, over all six sides. Both are in
    /// <see cref="Sides"/> order.
    /// </summary>
    public static double Difference(IReadOnlyList<RgbaImage> a, IReadOnlyList<RgbaImage> b)
    {
        double total = 0;
        for (int side = 0; side < Sides.Count; side++)
        {
            double[] ta = Thumbnail(a[side]);
            double[] tb = Thumbnail(b[side]);
            for (int i = 0; i < ta.Length; i++)
            {
                total += Math.Abs(ta[i] - tb[i]);
            }
        }

        return total / (Sides.Count * ThumbnailSize * ThumbnailSize * 3);
    }

    /// <summary>
    /// Red, green and blue averaged over each cell of a 16 x 16 grid laid over the picture. A picture
    /// under 16 pixels across gives each of its pixels to several cells.
    /// </summary>
    private static double[] Thumbnail(RgbaImage picture)
    {
        var cells = new double[ThumbnailSize * ThumbnailSize * 3];
        for (int cy = 0; cy < ThumbnailSize; cy++)
        {
            int y0 = cy * picture.Height / ThumbnailSize;
            int y1 = Math.Max(y0 + 1, (cy + 1) * picture.Height / ThumbnailSize);
            for (int cx = 0; cx < ThumbnailSize; cx++)
            {
                int x0 = cx * picture.Width / ThumbnailSize;
                int x1 = Math.Max(x0 + 1, (cx + 1) * picture.Width / ThumbnailSize);
                int cell = (cy * ThumbnailSize + cx) * 3;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        int at = (y * picture.Width + x) * 4;
                        cells[cell] += picture.Pixels[at];
                        cells[cell + 1] += picture.Pixels[at + 1];
                        cells[cell + 2] += picture.Pixels[at + 2];
                    }
                }

                int count = (y1 - y0) * (x1 - x0);
                cells[cell] /= count;
                cells[cell + 1] /= count;
                cells[cell + 2] /= count;
            }
        }

        return cells;
    }

    /// <summary>Blends the four nearest pixels, keeping to the picture's own edges.</summary>
    private static void SampleInto(RgbaImage picture, float u, float v, Span<byte> to)
    {
        float x = u * picture.Width - 0.5f;
        float y = v * picture.Height - 0.5f;
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;
        int left = Math.Clamp(x0, 0, picture.Width - 1);
        int right = Math.Clamp(x0 + 1, 0, picture.Width - 1);
        int top = Math.Clamp(y0, 0, picture.Height - 1);
        int bottom = Math.Clamp(y0 + 1, 0, picture.Height - 1);
        byte[] p = picture.Pixels;
        int w = picture.Width;
        for (int c = 0; c < 4; c++)
        {
            float upper = p[(top * w + left) * 4 + c] * (1 - fx) + p[(top * w + right) * 4 + c] * fx;
            float lower = p[(bottom * w + left) * 4 + c] * (1 - fx) + p[(bottom * w + right) * 4 + c] * fx;
            to[c] = (byte)Math.Clamp(MathF.Round(upper * (1 - fy) + lower * fy), 0, 255);
        }
    }
}
