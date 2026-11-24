namespace SimpleFps.Core.Bake;

/// <summary>
/// Maps made from exactly the same files under different names, such as <c>q2rdm2</c> and
/// <c>q2pduel7</c>. One name of each set is kept: the shortest, and of those the one that sorts
/// first (Matthew, 2026-11-24). Shorter names tend to be the originals, since packs that renamed
/// maps added to the name, as <c>q2pduel1</c> did to <c>q2duel1</c>.
/// </summary>
public static class MapCopies
{
    /// <summary>Orders names so the one to keep comes first.</summary>
    public static readonly IComparer<string> Preference = Comparer<string>.Create(
        (a, b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b));

    /// <summary>For every map that's a copy, the name kept in its place. Maps with no copy are left out.</summary>
    public static Dictionary<string, string> Find(IEnumerable<(string Name, string Hash)> maps)
    {
        var copies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (IGrouping<string, (string Name, string Hash)> set in maps.GroupBy(m => m.Hash, StringComparer.Ordinal))
        {
            string kept = set.Select(m => m.Name).Min(Preference)!;
            foreach ((string name, _) in set)
            {
                if (name != kept)
                {
                    copies[name] = kept;
                }
            }
        }

        return copies;
    }
}
