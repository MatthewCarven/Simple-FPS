namespace SimpleFps.Core.Textures;

/// <summary>A picture as 8-bit red, green, blue and alpha, top row first.</summary>
public sealed class RgbaImage
{
    public RgbaImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0 || pixels.Length != (long)width * height * 4)
        {
            throw new ArgumentException($"{pixels.Length} bytes can't be a {width} x {height} RGBA picture.");
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    /// <summary>The largest picture any reader here accepts, so a damaged header can't ask for gigabytes.</summary>
    public const int MaxSide = 16384;

    /// <exception cref="InvalidDataException">The size is zero, negative, or past <see cref="MaxSide"/>.</exception>
    internal static void CheckSize(int width, int height, string kind)
    {
        if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide)
        {
            throw new InvalidDataException($"Bad {kind} size: {width} x {height}.");
        }
    }
}
