namespace SimpleFps.Core.Textures;

/// <summary>
/// The 256 colours that .wal textures index into. A .wal holds no colours of its own.
/// Stored as raw RGB triples, which is also the layout of an .act file.
/// </summary>
public sealed class Palette
{
    public const int ColourCount = 256;
    private const int ByteCount = ColourCount * 3;

    /// <summary>Index 255 means "see through" on surfaces that use it.</summary>
    public const int TransparentIndex = 255;

    private readonly byte[] _rgb;

    private Palette(byte[] rgb) => _rgb = rgb;

    /// <exception cref="InvalidDataException">There aren't 256 colours in the data.</exception>
    public static Palette FromRgb(ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length < ByteCount)
        {
            throw new InvalidDataException($"A palette needs {ByteCount} bytes, got {rgb.Length}.");
        }

        return new Palette(rgb[..ByteCount].ToArray());
    }

    public static Palette Load(string path) => FromRgb(File.ReadAllBytes(path));

    public (byte R, byte G, byte B) this[int index] =>
        (_rgb[index * 3], _rgb[index * 3 + 1], _rgb[index * 3 + 2]);
}
