namespace SimpleFps.Core.Textures;

/// <summary>
/// Smaller copies of a texture, each half the size of the one before, down to a single pixel.
/// Built once at bake time, so loading a texture only has to hand them over. The layout is the
/// one Godot expects: the full-size picture, then each smaller one straight after it.
/// </summary>
public static class MipChain
{
    /// <summary>How many pictures the chain holds, the full-size one included.</summary>
    public static int LevelCount(int width, int height)
    {
        int levels = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width >> 1);
            height = Math.Max(1, height >> 1);
            levels++;
        }

        return levels;
    }

    /// <summary>The bytes taken by the first <paramref name="levels"/> pictures of the chain, or all of them.</summary>
    public static long ByteCount(int width, int height, int levels = int.MaxValue)
    {
        long bytes = 0;
        for (int level = 0; level < Math.Min(levels, LevelCount(width, height)); level++)
        {
            bytes += (long)Math.Max(1, width >> level) * Math.Max(1, height >> level) * 4;
        }

        return bytes;
    }

    /// <summary>
    /// Each pixel of a smaller picture is the average of the two by two block above it. Halving
    /// rounds down, so an odd width or height leaves its last column or row out of that step, and a
    /// picture one pixel wide or high uses its one column or row twice.
    /// </summary>
    public static byte[] Build(RgbaImage image)
    {
        var chain = new byte[ByteCount(image.Width, image.Height)];
        image.Pixels.CopyTo(chain, 0);

        int width = image.Width, height = image.Height;
        int from = 0;
        int to = image.Pixels.Length;
        while (width > 1 || height > 1)
        {
            int smallWidth = Math.Max(1, width >> 1);
            int smallHeight = Math.Max(1, height >> 1);
            for (int y = 0; y < smallHeight; y++)
            {
                int y0 = Math.Min(y * 2, height - 1);
                int y1 = Math.Min(y * 2 + 1, height - 1);
                for (int x = 0; x < smallWidth; x++)
                {
                    int x0 = Math.Min(x * 2, width - 1);
                    int x1 = Math.Min(x * 2 + 1, width - 1);
                    int a = from + (y0 * width + x0) * 4;
                    int b = from + (y0 * width + x1) * 4;
                    int c = from + (y1 * width + x0) * 4;
                    int d = from + (y1 * width + x1) * 4;
                    int at = to + (y * smallWidth + x) * 4;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        chain[at + channel] = (byte)((chain[a + channel] + chain[b + channel] + chain[c + channel] + chain[d + channel] + 2) >> 2);
                    }
                }
            }

            from = to;
            to += smallWidth * smallHeight * 4;
            width = smallWidth;
            height = smallHeight;
        }

        return chain;
    }
}
