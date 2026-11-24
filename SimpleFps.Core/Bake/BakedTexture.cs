namespace SimpleFps.Core.Bake;

/// <summary>Which pictures to draw: the maps' own, or the high-res pack's wherever it has a match.</summary>
public enum TextureSet
{
    Original,
    HighRes,
}

/// <summary>A picture ready to hand to the graphics card, as 8-bit RGBA, top row first.</summary>
/// <param name="Levels">How many mipmap levels <paramref name="Rgba"/> holds, the full-size picture included.</param>
public sealed record BakedImage(int Width, int Height, int Levels, byte[] Rgba);

/// <summary>A texture or sky in our own form: already decoded, already mipmapped.</summary>
public sealed class BakedTexture
{
    /// <summary>The name the map asks for, such as <c>e1u1/floor1_3</c>, or the sky's name.</summary>
    public required string Name { get; init; }

    /// <summary>SHA-256 of the files it was baked from.</summary>
    public required string Hash { get; init; }

    /// <summary>Where it came from, relative to the content folder, for the log.</summary>
    public required string Source { get; init; }

    /// <summary>
    /// The size the map's texture coordinates are measured in. For a high-res picture this is still
    /// the .wal's size, so the bigger picture covers exactly what the original did.
    /// </summary>
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>One picture with its mipmaps for a wall; six cube map faces, +X to -Z, for a sky.</summary>
    public required IReadOnlyList<BakedImage> Images { get; init; }
}
