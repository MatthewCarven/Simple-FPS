using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Bake;

/// <summary>
/// Hands out baked maps. The first time a map is asked for, or after its files or the bake
/// version change, it's baked and saved as <c>name.bake</c> in the cache folder. After that the
/// saved bake is loaded instead. The map's files are still read each time, but only to hash them.
/// </summary>
/// <param name="mapsFolder">Where the .bsp and .bsp.override files are: <c>content/maps</c>.</param>
/// <param name="cacheFolder">Where bakes go: <c>cache/maps</c>.</param>
public sealed class MapCache(string mapsFolder, string cacheFolder)
{
    public const string Extension = ".bake";

    private const string BspExtension = ".bsp";
    private const string OverrideExtension = ".bsp.override";

    public string Folder { get; } = cacheFolder;

    /// <summary>The maps baked so far, with their titles, authors and copies.</summary>
    public MapIndex Index() => MapIndex.Load(Folder);

    /// <summary>
    /// Every Quake 2 map that can be loaded by name, sorted: one per .bsp, plus one per override
    /// file that names another map's .bsp.
    /// </summary>
    public IReadOnlyList<string> Names() => Candidates().Where(IsQuake2).ToList();

    /// <summary>
    /// Every name that has a .bsp or an override file, sorted, before checking that it's a Quake 2
    /// map, so <see cref="MapList"/> can check that while it reads the map's entities.
    /// </summary>
    internal IReadOnlyCollection<string> Candidates()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(mapsFolder))
        {
            string fileName = Path.GetFileName(file).ToLowerInvariant();
            if (fileName.EndsWith(OverrideExtension, StringComparison.Ordinal))
            {
                names.Add(fileName[..^OverrideExtension.Length]);
            }
            else if (fileName.EndsWith(BspExtension, StringComparison.Ordinal))
            {
                names.Add(fileName[..^BspExtension.Length]);
            }
        }

        return names;
    }

    /// <summary>Loads a map, baking it first if there's no current bake. Baked is true if it had to bake.</summary>
    /// <exception cref="FileNotFoundException">There's no map by that name.</exception>
    /// <exception cref="InvalidDataException">The map isn't a readable Quake 2 map.</exception>
    public (BakedMap Map, bool Baked) Load(string name)
    {
        Source source = Find(name);
        byte[] bsp = File.ReadAllBytes(source.BspPath);
        string hash = MapBaker.Hash(bsp, source.Override);

        string bakePath = Path.Combine(Folder, source.Name + Extension);
        if (TryRead(bakePath, hash) is { } cached && cached.Info.Name == source.Name)
        {
            return (cached, false);
        }

        BakedMap map = MapBaker.Bake(source.Name, source.BspName, bsp, source.Override);
        try
        {
            Save(map, bakePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The map still plays. It just bakes again next time, which the game's log shows.
        }

        return (map, true);
    }

    /// <param name="Override">The override file's bytes, or null if the map has none.</param>
    internal sealed record Source(string Name, string BspPath, byte[]? Override)
    {
        public string BspName => Path.GetFileNameWithoutExtension(BspPath).ToLowerInvariant();
    }

    /// <summary>Works out which files a map is made from. Doesn't check that they exist.</summary>
    /// <exception cref="FileNotFoundException">The name can't be a map's.</exception>
    /// <exception cref="InvalidDataException">The map's override file can't be read.</exception>
    internal Source Find(string name)
    {
        name = name.ToLowerInvariant();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new FileNotFoundException($"There's no map called \"{name}\".");
        }

        string overridePath = Path.Combine(mapsFolder, name + OverrideExtension);
        byte[]? overrideFile = File.Exists(overridePath) ? File.ReadAllBytes(overridePath) : null;
        string bspFile = (overrideFile is null ? null : MapOverride.Read(overrideFile).MapFile) ?? name + BspExtension;
        return new Source(name, Path.Combine(mapsFolder, bspFile), overrideFile);
    }

    private bool IsQuake2(string name)
    {
        try
        {
            using FileStream stream = File.OpenRead(Find(name).BspPath);
            Span<byte> header = stackalloc byte[BspFormat.HeaderLength];
            return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
                && BspFormat.Identify(header) == BspKind.Quake2;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Returns the bake if it's current. A stale or damaged one comes back null, to be baked again.</summary>
    private static BakedMap? TryRead(string path, string hash)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return BakeFile.Read(stream, hash);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Writes beside the old bake and then swaps, so a crash never leaves half a file.</summary>
    private void Save(BakedMap map, string path)
    {
        Directory.CreateDirectory(Folder);
        string temporary = $"{path}.{Environment.ProcessId}.tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            BakeFile.Write(stream, map);
        }

        File.Move(temporary, path, overwrite: true);

        MapIndex index = MapIndex.Load(Folder);
        index.Add(map.Info);
        index.Save(Folder);
    }
}
