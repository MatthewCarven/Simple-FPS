using System.Collections.Generic;
using System.IO;
using Godot;
using SimpleFps.Core.Textures;

namespace SimpleFps.Game.World;

/// <summary>Loads the textures a map asks for, using a plain checker for any that can't be found.</summary>
public sealed class WorldTextures
{
    private const int StandInSize = 64;
    private const int StandInSquare = 16;

    private readonly Palette _palette;
    private readonly TextureIndex _index;
    private readonly Dictionary<string, (Texture2D Texture, Vector2 Size)> _loaded = new();
    private readonly List<string> _missing = new();
    private (Texture2D Texture, Vector2 Size)? _standIn;

    public WorldTextures(string contentRoot)
    {
        _palette = Palette.Load(Path.Combine(contentRoot, "palette.act"));
        _index = new TextureIndex(Path.Combine(contentRoot, "textures"));
    }

    public IReadOnlyList<string> Missing => _missing;

    public int LoadedCount => _loaded.Count;

    public (Texture2D Texture, Vector2 Size) Get(string name)
    {
        if (_loaded.TryGetValue(name, out var known))
        {
            return known;
        }

        var result = Load(name);
        _loaded[name] = result;
        return result;
    }

    private (Texture2D Texture, Vector2 Size) Load(string name)
    {
        string? path = _index.Find(name);
        if (path is null)
        {
            _missing.Add(name);
            return StandIn();
        }

        try
        {
            WalTexture wal = WalTexture.Load(path);
            Image image = Image.CreateFromData(wal.Width, wal.Height, false, Image.Format.Rgba8, wal.ToRgba(_palette));
            image.GenerateMipmaps();
            return (ImageTexture.CreateFromImage(image), new Vector2(wal.Width, wal.Height));
        }
        catch (InvalidDataException e)
        {
            GD.PrintErr($"{path}: {e.Message}");
            _missing.Add(name);
            return StandIn();
        }
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
