using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Godot;
using SimpleFps.Core.Bake;

namespace SimpleFps.Game.World;

/// <summary>
/// Hands the map's textures and sky to the graphics card, from our baked files. In the high-res
/// set, the pack's picture is used wherever it has one of the right shape, and the original
/// everywhere else. A name with no .wal at all gets a plain checker, in either set.
/// </summary>
public sealed class WorldTextures
{
    private const int StandInSize = 64;
    private const int StandInSquare = 16;

    private readonly TextureCache _cache;
    private readonly Stopwatch _cacheClock = new();
    private readonly Stopwatch _uploadClock = new();
    private readonly Dictionary<(string, TextureSet), (Texture2D Texture, Vector2 Size)?> _walls = new();
    private readonly Dictionary<(string, TextureSet), (Cubemap Box, string Source)?> _skies = new();
    private readonly HashSet<string> _asked = new();
    private readonly HashSet<string> _highRes = new();
    private readonly List<string> _missing = new();
    private (Texture2D Texture, Vector2 Size)? _standIn;

    public WorldTextures(string contentRoot, string cacheRoot, TextureSet set)
    {
        _cache = new TextureCache(contentRoot, cacheRoot);
        Set = set;
    }

    /// <summary>Which set <see cref="Get"/> and <see cref="Sky"/> hand out. Changing it takes effect on the next call.</summary>
    public TextureSet Set { get; set; }

    /// <summary>Names with no .wal, drawn as a checker.</summary>
    public IReadOnlyList<string> Missing => _missing;

    /// <summary>Different names asked for so far.</summary>
    public int LoadedCount => _asked.Count;

    /// <summary>Names drawn from the high-res pack so far.</summary>
    public int HighResCount => _highRes.Count;

    /// <summary>Textures and skies that had to be baked rather than read from the cache.</summary>
    public int BakedCount { get; private set; }

    /// <summary>Time spent reading bakes and hashing their source files, baking included.</summary>
    public TimeSpan CacheTime => _cacheClock.Elapsed;

    /// <summary>Time spent handing pictures to Godot.</summary>
    public TimeSpan UploadTime => _uploadClock.Elapsed;

    public IReadOnlyList<string> Problems => _cache.Problems;

    public IReadOnlyList<string> OddShapes => _cache.OddShapes;

    public IReadOnlyList<string> UnlikeSkies => _cache.UnlikeSkies;

    /// <summary>The texture for a name in the current set, and the size its texture coordinates are measured in.</summary>
    public (Texture2D Texture, Vector2 Size) Get(string name)
    {
        _asked.Add(name);
        if (Set == TextureSet.HighRes && Load(name, TextureSet.HighRes) is { } high)
        {
            return high;
        }

        if (Load(name, TextureSet.Original) is { } original)
        {
            return original;
        }

        if (!_missing.Contains(name))
        {
            _missing.Add(name);
        }

        return StandIn();
    }

    /// <summary>The sky by its name in the current set, as a cube map, or null if its pictures are missing.</summary>
    public (Cubemap Box, string Source)? Sky(string name)
    {
        if (Set == TextureSet.HighRes && LoadSky(name, TextureSet.HighRes) is { } high)
        {
            return high;
        }

        return LoadSky(name, TextureSet.Original);
    }

    private (Texture2D Texture, Vector2 Size)? Load(string name, TextureSet set)
    {
        if (_walls.TryGetValue((name, set), out var known))
        {
            return known;
        }

        (Texture2D, Vector2)? result = null;
        try
        {
            _cacheClock.Start();
            (BakedTexture? baked, bool fresh) = _cache.Wall(name, set);
            _cacheClock.Stop();
            if (baked is not null)
            {
                _uploadClock.Start();
                BakedImage image = baked.Images[0];
                Image picture = Image.CreateFromData(image.Width, image.Height, image.Levels > 1, Image.Format.Rgba8, image.Rgba);
                result = (ImageTexture.CreateFromImage(picture), new Vector2(baked.Width, baked.Height));
                _uploadClock.Stop();
                BakedCount += fresh ? 1 : 0;
                if (set == TextureSet.HighRes)
                {
                    _highRes.Add(name);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _cacheClock.Stop();
            _uploadClock.Stop();
            GD.PrintErr($"Couldn't load texture {name}: {e.Message}");
        }

        _walls[(name, set)] = result;
        return result;
    }

    private (Cubemap Box, string Source)? LoadSky(string name, TextureSet set)
    {
        if (_skies.TryGetValue((name, set), out var known))
        {
            return known;
        }

        (Cubemap, string)? result = null;
        try
        {
            _cacheClock.Start();
            (BakedTexture? baked, bool fresh) = _cache.Sky(name, set);
            _cacheClock.Stop();
            if (baked is not null)
            {
                _uploadClock.Start();
                var faces = new Godot.Collections.Array<Image>();
                foreach (BakedImage face in baked.Images)
                {
                    faces.Add(Image.CreateFromData(face.Width, face.Height, false, Image.Format.Rgba8, face.Rgba));
                }

                var box = new Cubemap();
                box.CreateFromImages(faces);
                result = (box, baked.Source);
                _uploadClock.Stop();
                BakedCount += fresh ? 1 : 0;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _cacheClock.Stop();
            _uploadClock.Stop();
            GD.PrintErr($"Couldn't load sky {name}: {e.Message}");
        }

        _skies[(name, set)] = result;
        return result;
    }

    private (Texture2D Texture, Vector2 Size) StandIn()
    {
        if (_standIn is { } made)
        {
            return made;
        }

        byte[] rgba = new byte[StandInSize * StandInSize * 4];
        for (int y = 0; y < StandInSize; y++)
        {
            for (int x = 0; x < StandInSize; x++)
            {
                bool odd = ((x / StandInSquare) + (y / StandInSquare)) % 2 == 1;
                byte shade = odd ? (byte)150 : (byte)110;
                int at = (y * StandInSize + x) * 4;
                rgba[at] = shade;
                rgba[at + 1] = shade;
                rgba[at + 2] = shade;
                rgba[at + 3] = 255;
            }
        }

        Image image = Image.CreateFromData(StandInSize, StandInSize, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        _standIn = (ImageTexture.CreateFromImage(image), new Vector2(StandInSize, StandInSize));
        return _standIn.Value;
    }
}
