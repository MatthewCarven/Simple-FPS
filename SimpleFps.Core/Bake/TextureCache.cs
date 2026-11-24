using System.Security.Cryptography;
using System.Text;
using SimpleFps.Core.Textures;

namespace SimpleFps.Core.Bake;

/// <summary>
/// Hands out baked textures and skies. The first time one is asked for, or after its files or
/// <see cref="TextureFile.Version"/> change, it's decoded, mipmapped and saved in the cache folder;
/// after that the saved copy is loaded. Its source files are still read each time, but only to
/// hash them, the same way <see cref="MapCache"/> keys maps.
/// </summary>
/// <remarks>
/// Each texture name has its own file, so maps that share textures share bakes:
/// <c>cache/textures/original/e1u1/floor1_3.tex</c>, <c>cache/textures/highres/...</c>, and
/// <c>cache/skies/original/unit1_.sky</c>. Every lookup returns null when there's nothing to
/// show, and the game then falls back: a high-res texture to the original, an original to a
/// stand-in, a sky to a flat colour.
/// </remarks>
/// <param name="contentFolder">The content folder, holding textures, env, palette.act and the high-res pack.</param>
/// <param name="cacheFolder">The cache folder: bakes go in its <c>textures</c> and <c>skies</c> folders.</param>
public sealed class TextureCache(string contentFolder, string cacheFolder)
{
    public const string TextureExtension = ".tex";
    public const string SkyExtension = ".sky";

    private const string PngExtension = ".png";
    private static readonly string[] SkyExtensions = [".tga", ".pcx"];

    private readonly string _paletteFile = Path.Combine(contentFolder, "palette.act");
    private readonly string _highResFolder = Path.Combine(contentFolder, "q2textures-master", "pak99_textures", "textures");
    private readonly string _skyFolder = Path.Combine(contentFolder, "env");
    private readonly string _highResSkyFolder = Path.Combine(contentFolder, "q2textures-master", "pak96_env", "env");
    private readonly TextureIndex _walIndex = new(Path.Combine(contentFolder, "textures"));
    private (byte[] Bytes, Palette Palette)? _palette;

    /// <summary>Source files that couldn't be read, for the log.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>High-res pictures left out because their shape isn't the .wal's, so they'd come out stretched.</summary>
    public List<string> OddShapes { get; } = [];

    /// <summary>The pack's skies left out because they're different pictures, not the originals redrawn.</summary>
    public List<string> UnlikeSkies { get; } = [];

    /// <summary>
    /// A wall texture by the name a map asks for. Null if there's no .wal for it, or, for the
    /// high-res set, no high-res picture of the .wal's shape. Baked is true if it had to bake.
    /// </summary>
    /// <exception cref="IOException">A file exists but can't be read.</exception>
    public (BakedTexture? Texture, bool Baked) Wall(string name, TextureSet set)
    {
        string? walPath = _walIndex.Find(name);
        if (walPath is null)
        {
            return (null, false);
        }

        byte[] wal = File.ReadAllBytes(walPath);
        return set == TextureSet.Original ? OriginalWall(name, walPath, wal) : HighResWall(name, walPath, wal);
    }

    /// <summary>
    /// A sky by the name a map gives in its <c>sky</c> key, as six cube map faces. Null if any of its
    /// six pictures is missing or unreadable, or, for the high-res set, if the pack has no full set
    /// or its sky is a different picture from the original.
    /// </summary>
    /// <exception cref="IOException">A file exists but can't be read.</exception>
    public (BakedTexture? Texture, bool Baked) Sky(string name, TextureSet set) =>
        set == TextureSet.Original ? OriginalSky(name) : HighResSky(name);

    private (BakedTexture?, bool) OriginalWall(string name, string walPath, byte[] wal)
    {
        (byte[] paletteBytes, Palette palette) = LoadPalette();
        string hash = Hash([wal, paletteBytes]);
        string bakePath = BakePath("textures", TextureSet.Original, name, TextureExtension);
        if (TryRead(bakePath, hash, name) is { } cached)
        {
            return (cached, false);
        }

        WalTexture texture;
        try
        {
            texture = WalTexture.Read(wal);
        }
        catch (InvalidDataException e)
        {
            Problems.Add($"{Relative(walPath)}: {e.Message}");
            return (null, false);
        }

        var image = new RgbaImage(texture.Width, texture.Height, texture.ToRgba(palette));
        return (Save(bakePath, Mipmapped(name, hash, Relative(walPath), texture.Width, texture.Height, image)), true);
    }

    private (BakedTexture?, bool) HighResWall(string name, string walPath, byte[] wal)
    {
        string pngPath = Path.Combine(_highResFolder, name + PngExtension);
        if (!SafePath.IsRelative(name) || !File.Exists(pngPath))
        {
            return (null, false);
        }

        byte[] png = File.ReadAllBytes(pngPath);
        int width, height;
        try
        {
            WalTexture original = WalTexture.Read(wal);
            (width, height) = (original.Width, original.Height);
            (int pngWidth, int pngHeight) = PngImage.ReadSize(png);
            if ((long)pngWidth * height != (long)pngHeight * width)
            {
                OddShapes.Add($"{name}: {pngWidth} x {pngHeight} for a {width} x {height} .wal");
                return (null, false);
            }
        }
        catch (InvalidDataException e)
        {
            Problems.Add($"{name}: {e.Message}");
            return (null, false);
        }

        string hash = Hash([png, wal]);
        string bakePath = BakePath("textures", TextureSet.HighRes, name, TextureExtension);
        if (TryRead(bakePath, hash, name) is { } cached)
        {
            return (cached, false);
        }

        RgbaImage image;
        try
        {
            image = PngImage.Decode(png);
        }
        catch (InvalidDataException e)
        {
            Problems.Add($"{Relative(pngPath)}: {e.Message}");
            return (null, false);
        }

        return (Save(bakePath, Mipmapped(name, hash, Relative(pngPath), width, height, image)), true);
    }

    /// <summary>The .tga for each side, or the .pcx where there's no .tga or it won't read.</summary>
    private (BakedTexture?, bool) OriginalSky(string name)
    {
        if (OriginalSkyFiles(name) is not { } files)
        {
            return (null, false);
        }

        string hash = Hash(files.Parts);
        string bakePath = BakePath("skies", TextureSet.Original, name, SkyExtension);
        if (TryRead(bakePath, hash, name) is { } cached)
        {
            return (cached, false);
        }

        if (DecodeOriginalSky(files.Candidates) is not { } decoded)
        {
            return (null, false);
        }

        string source = $"{Relative(Path.Combine(_skyFolder, name))}*{decoded.Extensions}";
        return (Save(bakePath, Cube(name, hash, source, decoded.Pictures)), true);
    }

    /// <summary>
    /// The pack's sky, but only where it's the original redrawn: <see cref="SkyBox.Difference"/>
    /// under <see cref="SkyBox.SamePictureLimit"/>. A sky that's a different picture is baked as an
    /// empty file, so the comparison isn't made again on every load.
    /// </summary>
    private (BakedTexture?, bool) HighResSky(string name)
    {
        if (!SafePath.IsRelative(name))
        {
            return (null, false);
        }

        string[] paths = SkyBox.Sides.Select(side => Path.Combine(_highResSkyFolder, name + side + PngExtension)).ToArray();
        if (!paths.All(File.Exists) || OriginalSkyFiles(name) is not { } original)
        {
            return (null, false);
        }

        // The original's files are part of the key, since whether the pack's sky is used depends on them.
        byte[][] files = paths.Select(File.ReadAllBytes).ToArray();
        string hash = Hash(files.Concat(original.Parts));
        string bakePath = BakePath("skies", TextureSet.HighRes, name, SkyExtension);
        if (TryRead(bakePath, hash, name) is { } cached)
        {
            return Usable(cached, baked: false);
        }

        var pictures = new RgbaImage[files.Length];
        for (int side = 0; side < files.Length; side++)
        {
            try
            {
                pictures[side] = PngImage.Decode(files[side]);
            }
            catch (InvalidDataException e)
            {
                Problems.Add($"{Relative(paths[side])}: {e.Message}");
                return (null, false);
            }
        }

        if (DecodeOriginalSky(original.Candidates) is not { } decoded)
        {
            return (null, false);
        }

        string pack = Relative(Path.Combine(_highResSkyFolder, name)) + "*.png";
        double difference = SkyBox.Difference(pictures, decoded.Pictures);
        if (difference > SkyBox.SamePictureLimit)
        {
            var notUsed = new BakedTexture
            {
                Name = name,
                Hash = hash,
                Source = $"{pack} differs from the original by {difference:F0} on average, so it isn't used",
                Width = 0,
                Height = 0,
                Images = [],
            };
            return Usable(Save(bakePath, notUsed), baked: true);
        }

        return (Save(bakePath, Cube(name, hash, pack, pictures)), true);
    }

    /// <summary>An empty high-res sky bake means the pack's picture isn't the original's.</summary>
    private (BakedTexture?, bool) Usable(BakedTexture sky, bool baked)
    {
        if (sky.Images.Count > 0)
        {
            return (sky, baked);
        }

        UnlikeSkies.Add($"{sky.Name}: {sky.Source}");
        return (null, baked);
    }

    /// <summary>
    /// Every .tga and .pcx there is for each side, read but not decoded, and the parts of the key
    /// they make. Null if a side has neither.
    /// </summary>
    private (List<byte[]> Parts, List<(string Path, byte[] Bytes)>[] Candidates)? OriginalSkyFiles(string name)
    {
        if (!SafePath.IsRelative(name))
        {
            return null;
        }

        // Every candidate goes into the key, so a cached sky still knows which file each side used.
        var parts = new List<byte[]>();
        var candidates = new List<(string Path, byte[] Bytes)>[SkyBox.Sides.Count];
        for (int side = 0; side < SkyBox.Sides.Count; side++)
        {
            candidates[side] = [];
            foreach (string extension in SkyExtensions)
            {
                string path = Path.Combine(_skyFolder, name + SkyBox.Sides[side] + extension);
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    candidates[side].Add((path, bytes));
                    parts.Add(Encoding.UTF8.GetBytes(SkyBox.Sides[side] + extension));
                    parts.Add(bytes);
                }
            }

            if (candidates[side].Count == 0)
            {
                return null;
            }
        }

        return (parts, candidates);
    }

    /// <summary>Decodes each side from the first of its files that reads, or returns null if one has none.</summary>
    private (RgbaImage[] Pictures, string Extensions)? DecodeOriginalSky(List<(string Path, byte[] Bytes)>[] candidates)
    {
        var pictures = new RgbaImage[candidates.Length];
        var used = new List<string>();
        for (int side = 0; side < pictures.Length; side++)
        {
            foreach ((string path, byte[] bytes) in candidates[side])
            {
                try
                {
                    pictures[side] = path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? TgaImage.Decode(bytes) : PcxImage.Decode(bytes);
                    used.Add(Path.GetExtension(path).ToLowerInvariant());
                    break;
                }
                catch (InvalidDataException e)
                {
                    Problems.Add($"{Relative(path)}: {e.Message}");
                }
            }

            if (pictures[side] is null)
            {
                return null;
            }
        }

        return (pictures, string.Join(" and ", used.Distinct().OrderBy(e => e != ".tga")));
    }

    private static BakedTexture Mipmapped(string name, string hash, string source, int width, int height, RgbaImage image) => new()
    {
        Name = name,
        Hash = hash,
        Source = source,
        Width = width,
        Height = height,
        Images = [new BakedImage(image.Width, image.Height, MipChain.LevelCount(image.Width, image.Height), MipChain.Build(image))],
    };

    private static BakedTexture Cube(string name, string hash, string source, RgbaImage[] pictures)
    {
        RgbaImage[] faces = SkyBox.ToCubeFaces(pictures);
        return new BakedTexture
        {
            Name = name,
            Hash = hash,
            Source = source,
            Width = faces[0].Width,
            Height = faces[0].Height,
            Images = faces.Select(f => new BakedImage(f.Width, f.Height, 1, f.Pixels)).ToArray(),
        };
    }

    private (byte[] Bytes, Palette Palette) LoadPalette()
    {
        if (_palette is null)
        {
            byte[] bytes = File.ReadAllBytes(_paletteFile);
            _palette = (bytes, Palette.FromRgb(bytes));
        }

        return _palette.Value;
    }

    /// <summary>SHA-256 of each part with its length in front, so where one file ends and the next starts is part of the key.</summary>
    internal static string Hash(IEnumerable<byte[]> parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(long)];
        foreach (byte[] part in parts)
        {
            BitConverter.TryWriteBytes(length, (long)part.Length);
            hash.AppendData(length);
            hash.AppendData(part);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Where a bake goes. A name that can't safely be a path, such as one with <c>..</c> in it, is
    /// stored under its hash instead.
    /// </summary>
    internal string BakePath(string kind, TextureSet set, string name, string extension)
    {
        string folder = set == TextureSet.Original ? "original" : "highres";
        string file = SafePath.IsRelative(name)
            ? name
            : Path.Combine("_named", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant());
        return Path.Combine(cacheFolder, kind, folder, file + extension);
    }

    private string Relative(string path) => Path.GetRelativePath(contentFolder, path).Replace('\\', '/');

    /// <summary>Returns the bake if it's current. A stale or damaged one comes back null, to be baked again.</summary>
    private static BakedTexture? TryRead(string path, string hash, string name)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            BakedTexture? texture = TextureFile.Read(stream, hash);
            return texture?.Name == name ? texture : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Writes beside the old bake and then swaps, so a crash never leaves half a file. A failed save only means baking again next time.</summary>
    private static BakedTexture Save(string path, BakedTexture texture)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = $"{path}.{Environment.ProcessId}.tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                TextureFile.Write(stream, texture);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The texture still shows. It just bakes again next time.
        }

        return texture;
    }
}
