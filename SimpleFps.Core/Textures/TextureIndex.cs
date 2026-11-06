namespace SimpleFps.Core.Textures;

/// <summary>
/// Finds the .wal for a name a map asks for. Maps ask for a path such as <c>e1u1/floor1_3</c>.
/// The exact path comes first, because hundreds of bare names exist in more than one folder. The
/// bare name is the fallback, since the collection also holds flat folders.
/// </summary>
public sealed class TextureIndex
{
    private const string Extension = ".wal";

    private readonly Dictionary<string, string> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byBareName = new(StringComparer.OrdinalIgnoreCase);

    public TextureIndex(string root)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*" + Extension, SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            _byPath.TryAdd(relative[..^Extension.Length], file);
            _byBareName.TryAdd(Path.GetFileNameWithoutExtension(file), file);
        }
    }

    public int Count => _byPath.Count;

    public string? Find(string name)
    {
        if (_byPath.TryGetValue(name, out string? exact))
        {
            return exact;
        }

        string bare = name[(name.LastIndexOf('/') + 1)..];
        return _byBareName.GetValueOrDefault(bare);
    }
}
