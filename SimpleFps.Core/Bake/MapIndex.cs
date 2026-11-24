using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleFps.Core.Bake;

/// <param name="DuplicateOf">
/// Set when another map was baked from exactly the same files under another name, such as
/// <c>q2rdm2</c> and <c>q2pduel7</c>. Of a set of copies, the name that sorts first is the one
/// kept, and the others name it here, so a map list can leave them out.
/// </param>
public sealed record MapIndexEntry(string Name, string Hash, string Bsp, string? Title, string? Author, string? DuplicateOf);

/// <summary>
/// The list of baked maps, kept as <c>index.json</c> beside the bakes and updated after each bake.
/// If the file is missing, damaged or from another bake version, it's rebuilt from the bakes.
/// </summary>
public sealed class MapIndex
{
    public const string FileName = "index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SortedDictionary<string, MapInfo> _maps = new(StringComparer.Ordinal);

    /// <summary>Every baked map, sorted by name.</summary>
    public IReadOnlyList<MapIndexEntry> Maps
    {
        get
        {
            var kept = new Dictionary<string, string>();
            var entries = new List<MapIndexEntry>(_maps.Count);
            foreach (MapInfo map in _maps.Values)
            {
                string? original = kept.TryAdd(map.Hash, map.Name) ? null : kept[map.Hash];
                entries.Add(new MapIndexEntry(map.Name, map.Hash, map.Bsp, map.Title, map.Author, original));
            }

            return entries;
        }
    }

    /// <summary>Adds a map, or replaces the entry with the same name.</summary>
    public void Add(MapInfo map) => _maps[map.Name] = map;

    /// <summary>
    /// Reads <c>index.json</c>, leaving out maps whose bake has gone, and reads the details of any
    /// bake it doesn't list straight from the bake. That covers a missing or outdated index, and a
    /// crash between saving a bake and saving the index.
    /// </summary>
    public static MapIndex Load(string folder)
    {
        var index = new MapIndex();
        if (!Directory.Exists(folder))
        {
            return index;
        }

        IndexFile? file = null;
        try
        {
            file = JsonSerializer.Deserialize<IndexFile>(File.ReadAllText(Path.Combine(folder, FileName)), JsonOptions);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Missing or unreadable, so every bake is read below.
        }

        if (file is { Maps: not null } && file.BakeVersion == BakeFile.Version)
        {
            foreach (MapIndexEntry entry in file.Maps)
            {
                if (File.Exists(Path.Combine(folder, entry.Name + MapCache.Extension)))
                {
                    index.Add(new MapInfo(entry.Name, entry.Hash, entry.Bsp, entry.Title, entry.Author));
                }
            }
        }

        foreach (string bake in Directory.EnumerateFiles(folder, "*" + MapCache.Extension))
        {
            string name = Path.GetFileNameWithoutExtension(bake);
            if (index._maps.ContainsKey(name))
            {
                continue;
            }

            try
            {
                using FileStream stream = File.OpenRead(bake);
                if (BakeFile.ReadInfo(stream) is { } info && info.Name == name)
                {
                    index.Add(info);
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // A damaged bake is replaced the next time its map loads.
            }
        }

        return index;
    }

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, FileName);
        string temporary = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new IndexFile(BakeFile.Version, [.. Maps]), JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record IndexFile(int BakeVersion, List<MapIndexEntry>? Maps);
}
