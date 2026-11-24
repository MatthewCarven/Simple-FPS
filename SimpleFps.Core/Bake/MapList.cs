using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Bake;

/// <param name="Name">What <c>--map=</c> takes.</param>
/// <param name="Bsp">The .bsp it plays, without the extension. Only differs from the name when an override names another map.</param>
/// <param name="Deathmatch">True if it has at least one deathmatch spawn.</param>
/// <param name="CopyOf">Set when another map is made from exactly the same files, and that one is listed instead.</param>
public sealed record MapListEntry(string Name, string Bsp, string? Title, string? Author, bool Deathmatch, string? CopyOf)
{
    /// <summary>The title, or the name for a map that has none.</summary>
    public string DisplayTitle => Title ?? Name;

    /// <summary>
    /// Whether the map list shows it: maps with deathmatch spawns, one name per set of copies. The
    /// rest still load with <c>--map=</c> (Matthew, 2026-11-24).
    /// </summary>
    public bool Shown => Deathmatch && CopyOf is null;
}

/// <summary>
/// Every map there is, with its title and author, for the map list at start. Unlike the bake's
/// index it covers maps never played. It reads only each map's entity text, which is where the
/// title and author are, and hashes a map's files only when another map's are the same size, to
/// find copies. So listing every map is quick and writes nothing to disk.
/// </summary>
public sealed class MapList
{
    private MapList(List<MapListEntry> all)
    {
        All = all;
        Shown = all
            .Where(m => m.Shown)
            .OrderBy(m => SortKey(m.DisplayTitle), StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every readable Quake 2 map, sorted by name, including the ones the list hides.</summary>
    public IReadOnlyList<MapListEntry> All { get; }

    /// <summary>
    /// The maps the list shows, sorted by title ignoring case and any punctuation in front, so
    /// <c>'The Edge'</c> files under T, then by name.
    /// </summary>
    public IReadOnlyList<MapListEntry> Shown { get; }

    /// <summary>
    /// Reads every map the cache can load. Maps that aren't Quake 2 or can't be read are left out,
    /// since they can't be played either.
    /// </summary>
    public static MapList Read(MapCache cache)
    {
        var found = new List<Found>();
        foreach (string name in cache.Candidates())
        {
            try
            {
                MapCache.Source source = cache.Find(name);
                using FileStream stream = File.OpenRead(source.BspPath);
                List<BspEntity> entities = Q2Bsp.ReadEntities(stream);
                if (source.Override is not null && MapOverride.Read(source.Override).Entities is { } replacement)
                {
                    entities = BspEntity.Parse(replacement);
                }

                found.Add(new Found(source, stream.Length, entities));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // Not a Quake 2 map, or not readable, so it isn't listed.
            }
        }

        Dictionary<string, string> copies = MapCopies.Find(HashSameSized(found));
        return new MapList(found
            .Select(f =>
            {
                (string? title, string? author) = MapCredits.From(f.Entities);
                bool deathmatch = f.Entities.Any(e => e.ClassName == "info_player_deathmatch");
                return new MapListEntry(f.Source.Name, f.Source.BspName, title, author, deathmatch, copies.GetValueOrDefault(f.Source.Name));
            })
            .ToList());
    }

    /// <summary>
    /// The entries whose title, author or name holds every word of the search, ignoring case, kept
    /// in the order given. An empty search matches everything.
    /// </summary>
    public static IReadOnlyList<MapListEntry> Filter(IEnumerable<MapListEntry> entries, string search)
    {
        string[] words = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return entries.Where(m => words.All(word => Matches(m, word))).ToList();
    }

    private static string SortKey(string title)
    {
        int start = 0;
        while (start < title.Length && !char.IsLetterOrDigit(title[start]))
        {
            start++;
        }

        return start < title.Length ? title[start..] : title;
    }

    private static bool Matches(MapListEntry map, string word) =>
        map.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
        || (map.Title?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false)
        || (map.Author?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Hashes, the same way as the bake, only the maps that could be a copy: those whose .bsp and
    /// override file are the same sizes as another map's. A map that can't be read now is skipped.
    /// </summary>
    private static List<(string Name, string Hash)> HashSameSized(List<Found> found)
    {
        var hashes = new List<(string Name, string Hash)>();
        foreach (IGrouping<(long, int), Found> sameSize in found.GroupBy(f => (f.BspLength, f.Source.Override?.Length ?? -1)))
        {
            if (sameSize.Count() < 2)
            {
                continue;
            }

            foreach (Found f in sameSize)
            {
                try
                {
                    hashes.Add((f.Source.Name, MapBaker.Hash(File.ReadAllBytes(f.Source.BspPath), f.Source.Override)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Gone or locked since it was listed. It's shown as no one's copy.
                }
            }
        }

        return hashes;
    }

    private sealed record Found(MapCache.Source Source, long BspLength, List<BspEntity> Entities);
}
