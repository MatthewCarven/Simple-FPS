namespace SimpleFps.Core.Textures;

/// <summary>
/// Texture and sky names come from map files, and turn into paths under the content and cache
/// folders. A name only becomes a path if it can't climb out of the folder it's meant for.
/// </summary>
public static class SafePath
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars().Where(c => c is not ('/' or '\\')).ToArray();

    /// <summary>
    /// True for a name made of plain folder and file names joined by slashes: nothing empty, no
    /// <c>.</c> or <c>..</c>, no drive or leading slash, and no name ending in a dot or space,
    /// which Windows would quietly change.
    /// </summary>
    public static bool IsRelative(string name)
    {
        if (name.Length == 0 || name.IndexOfAny(Invalid) >= 0)
        {
            return false;
        }

        foreach (string part in name.Split('/', '\\'))
        {
            if (part.Length == 0 || part is "." or ".." || part[^1] is '.' or ' ')
            {
                return false;
            }
        }

        return true;
    }
}
