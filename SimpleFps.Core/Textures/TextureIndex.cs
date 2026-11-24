namespace SimpleFps.Core.Textures;

/// <summary>
/// Finds the .wal for a name a map asks for. Maps ask for a path such as <c>e1u1/floor1_3</c>.
/// The exact path comes first, because hundreds of bare names exist in more than one folder. The
/// bare name is the fallback, since the collection also holds flat folders.
/// </summary>
/// <remarks>
/// The exact path is checked on its own, so a map whose textures are all where it says (330 of
/// the 363) never lists the folder. Listing all 31,000 .wal files took about 100 ms, most of a
/// map's texture loading before 3b, so it only happens the first time a bare name is needed.
/// </remarks>
public sealed class TextureIndex(string root)
{
    private const string Extension = ".wal";

    private Dictionary<string, string>? _byBareName;

    /// <summary>The file for a name, or null if there's none by its path or its bare name.</summary>
    public string? Find(string name)
    {
        if (SafePath.IsRelative(name))
        {
            string exact = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar) + Extension);
            if (File.Exists(exact))
            {
                return exact;
            }
        }

        string bare = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        return BareNames().GetValueOrDefault(bare);
    }

    private Dictionary<string, string> BareNames()
    {
        if (_byBareName is null)
        {
            _byBareName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(root))
            {
                foreach (string file in Directory.EnumerateFiles(root, "*" + Extension, SearchOption.AllDirectories))
                {
                    _byBareName.TryAdd(Path.GetFileNameWithoutExtension(file), file);
                }
            }
        }

        return _byBareName;
    }
}
