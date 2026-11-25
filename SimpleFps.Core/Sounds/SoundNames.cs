namespace SimpleFps.Core.Sounds;

/// <summary>How a map's name for a sound becomes a file under the sound folder.</summary>
public static class SoundNames
{
    public const string Extension = ".wav";

    /// <summary>
    /// The file a map means by a sound's name, relative to the sound folder: <c>World\Amb4</c>
    /// becomes <c>world/amb4.wav</c>. As in the original, <c>.wav</c> is added when the name
    /// doesn't hold it. Case is folded, as Windows did for the maps' authors, and leading slashes
    /// are dropped, which Windows also forgave (<c>/suds/bell.wav</c>).
    /// </summary>
    public static string ToFile(string name)
    {
        string file = name.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        return file.Contains(Extension, StringComparison.Ordinal) ? file : file + Extension;
    }
}
