using System.Globalization;
using System.Numerics;

namespace SimpleFps.Core.Bsp;

/// <summary>One entry from a map's entity list: spawn points, lights, items, doors and so on.</summary>
public sealed class BspEntity
{
    private readonly Dictionary<string, string> _fields;

    public BspEntity(Dictionary<string, string> fields) => _fields = fields;

    public IReadOnlyDictionary<string, string> Fields => _fields;

    public string ClassName => this["classname"] ?? "";

    public string? this[string key] => _fields.TryGetValue(key, out string? value) ? value : null;

    public float? GetFloat(string key) =>
        float.TryParse(this[key], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : null;

    public Vector3? GetVector(string key)
    {
        string[]? parts = this[key]?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 3 })
        {
            return null;
        }

        var values = new float[3];
        for (int i = 0; i < 3; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    /// <summary>
    /// Reads the text entity lump: <c>{ "key" "value" ... }</c> blocks. Old maps can be sloppy, so
    /// anything outside a block is skipped and an unfinished last block is kept rather than rejected.
    /// </summary>
    public static List<BspEntity> Parse(string text)
    {
        var entities = new List<BspEntity>();
        int i = 0;

        while (true)
        {
            i = text.IndexOf('{', i);
            if (i < 0)
            {
                return entities;
            }

            i++;
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            while (true)
            {
                while (i < text.Length && text[i] != '"' && text[i] != '}')
                {
                    i++;
                }

                if (i >= text.Length || text[i] == '}')
                {
                    i++;
                    break;
                }

                string key = ReadQuoted(text, ref i);
                while (i < text.Length && text[i] != '"' && text[i] != '}')
                {
                    i++;
                }

                if (i >= text.Length || text[i] == '}')
                {
                    i++;
                    break;
                }

                // The first value wins, which is how the original game's parser left things.
                fields.TryAdd(key, ReadQuoted(text, ref i));
            }

            entities.Add(new BspEntity(fields));
            if (i >= text.Length)
            {
                return entities;
            }
        }
    }

    private static string ReadQuoted(string text, ref int i)
    {
        int start = i + 1;
        int end = text.IndexOf('"', start);
        if (end < 0)
        {
            i = text.Length;
            return text[start..];
        }

        i = end + 1;
        return text[start..end];
    }
}
