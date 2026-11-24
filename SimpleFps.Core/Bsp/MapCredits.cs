using System.Text;
using System.Text.RegularExpressions;

namespace SimpleFps.Core.Bsp;

/// <summary>
/// A map's title and author, read from the worldspawn's <c>message</c>, which is what the original
/// game showed on loading. No map in the collection has a separate author key, so the author is
/// whatever follows the first "by", as in "Reign Of Fire\nby Mr.CleaN".
/// </summary>
public static partial class MapCredits
{
    /// <summary>Trimmed from both ends of a title or author: padding and decoration, not names.</summary>
    private static readonly char[] Decoration = [' ', '-', ':', ',', '.', '|', '*', '~', '='];

    public static (string? Title, string? Author) From(IEnumerable<BspEntity> entities)
    {
        string? message = entities.FirstOrDefault(e => e.ClassName == "worldspawn")?["message"];
        return message is null ? (null, null) : Split(message);
    }

    /// <summary>Splits a raw message into a title and author. Either can be null.</summary>
    public static (string? Title, string? Author) Split(string message)
    {
        List<string> lines = Clean(message)
            .Split('\n')
            .Select(line => Spaces().Replace(line, " ").Trim())
            .Where(line => line.Length > 0)
            .ToList();

        string? author = null;
        int authorLine = -1;
        string? before = null;
        for (int i = 0; i < lines.Count && authorLine < 0; i++)
        {
            Match by = ByWord().Match(lines[i]);
            if (by.Success)
            {
                authorLine = i;
                before = lines[i][..by.Index];
                author = Tidy(Year().Replace(lines[i][(by.Index + by.Length)..], ""));
            }
        }

        string? title = authorLine == 0 ? Tidy(before!) : null;
        title ??= lines.Where((_, i) => i != authorLine).Select(Tidy).FirstOrDefault(t => t is not null);
        return (title, author);
    }

    /// <summary>
    /// Turns the game's own character set into plain text. Bytes from 128 up are the same letters in
    /// another colour, the low control codes are decorations (dots and bar pieces) except for the
    /// coloured digits and brackets, and a backslash and n is a line break.
    /// </summary>
    internal static string Clean(string raw)
    {
        var text = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (c == '\n')
            {
                text.Append('\n');
                continue;
            }

            int plain = c & 0x7F;
            text.Append(plain switch
            {
                0x10 => '[',
                0x11 => ']',
                >= 0x12 and <= 0x1B => (char)('0' + plain - 0x12),
                < 0x20 or 0x7F => ' ',
                _ => (char)plain,
            });
        }

        return text.Replace("\\n", "\n").Replace('\r', ' ').ToString();
    }

    private static string? Tidy(string text)
    {
        string tidy = text.Trim(Decoration);
        return tidy.Length > 0 ? tidy : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\bby\b\s*:?", RegexOptions.IgnoreCase)]
    private static partial Regex ByWord();

    /// <summary>A year after the name, as in "(c) by Roger Soder 1998".</summary>
    [GeneratedRegex(@"\s*\(?\b(19|20)\d\d\)?\s*$")]
    private static partial Regex Year();
}
