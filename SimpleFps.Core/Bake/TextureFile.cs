using System.Text;
using SimpleFps.Core.Textures;
using static SimpleFps.Core.Bake.BakeSections;

namespace SimpleFps.Core.Bake;

/// <summary>
/// Reads and writes our baked texture and sky files. A file starts with "SFBT", the version and
/// the hash of the files it was baked from, then the sections described in <see cref="BakeSections"/>:
/// INFO (name, source, size) and IMGS (the pictures).
/// </summary>
public static class TextureFile
{
    /// <summary>Bump this whenever what a baked texture holds or how it's built changes, and every texture re-bakes.</summary>
    public const int Version = 1;

    private static readonly byte[] Magic = "SFBT"u8.ToArray();

    private static readonly uint InfoTag = Tag("INFO");
    private static readonly uint ImagesTag = Tag("IMGS");

    public static void Write(Stream stream, BakedTexture texture)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(texture.Hash);

        Section(w, InfoTag, () =>
        {
            w.Write(texture.Name);
            w.Write(texture.Source);
            w.Write(texture.Width);
            w.Write(texture.Height);
        });

        Section(w, ImagesTag, () =>
        {
            w.Write(texture.Images.Count);
            foreach (BakedImage image in texture.Images)
            {
                w.Write(image.Width);
                w.Write(image.Height);
                w.Write(image.Levels);
                WriteArray<byte>(w, image.Rgba);
            }
        });

        Section(w, EndTag, () => { });
    }

    /// <summary>
    /// Reads a baked texture, or returns null if it's from another version or was baked from files
    /// with a different hash, either of which means it needs baking again.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is damaged or isn't a baked texture.</exception>
    public static BakedTexture? Read(Stream stream, string expectedHash)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            if (!r.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
            {
                throw new InvalidDataException("Not a baked texture.");
            }

            if (r.ReadInt32() != Version || r.ReadString() is not { } hash || hash != expectedHash)
            {
                return null;
            }

            (string Name, string Source, int Width, int Height)? info = null;
            List<BakedImage>? images = null;
            while (true)
            {
                (uint tag, long end) = ReadSectionStart(r);
                if (tag == EndTag)
                {
                    break;
                }

                if (tag == InfoTag)
                {
                    info = (r.ReadString(), r.ReadString(), r.ReadInt32(), r.ReadInt32());
                }
                else if (tag == ImagesTag)
                {
                    images = ReadImages(r);
                }

                EndSection(r, end);
            }

            (string name, string source, int width, int height) = info ?? throw Missing("INFO");
            return new BakedTexture
            {
                Name = name,
                Hash = hash,
                Source = source,
                Width = width,
                Height = height,
                Images = images ?? throw Missing("IMGS"),
            };
        }
        catch (Exception e) when (IsDamage(e))
        {
            throw new InvalidDataException($"Baked texture is damaged: {e.Message}", e);
        }
    }

    private static List<BakedImage> ReadImages(BinaryReader r)
    {
        int count = ReadCount(r);
        var images = new List<BakedImage>(count);
        for (int i = 0; i < count; i++)
        {
            int width = r.ReadInt32();
            int height = r.ReadInt32();
            int levels = r.ReadInt32();
            if (width <= 0 || height <= 0 || width > RgbaImage.MaxSide || height > RgbaImage.MaxSide
                || levels <= 0 || levels > MipChain.LevelCount(width, height))
            {
                throw new InvalidDataException($"Baked picture has a bad size: {width} x {height}, {levels} levels.");
            }

            byte[] rgba = ReadArray<byte>(r);
            if (rgba.Length != MipChain.ByteCount(width, height, levels))
            {
                throw new InvalidDataException($"Baked {width} x {height} picture holds {rgba.Length} bytes.");
            }

            images.Add(new BakedImage(width, height, levels, rgba));
        }

        return images;
    }

    private static InvalidDataException Missing(string tag) => new($"Baked texture has no {tag} section.");
}
