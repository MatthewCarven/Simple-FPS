using SimpleFps.Core.Sounds;
using SimpleFps.Core.Textures;

namespace SimpleFps.Core.Bake;

/// <summary>A sound handed out by <see cref="SoundCache"/>, or why there isn't one.</summary>
/// <param name="Baked">True if it had to be baked rather than loaded from the cache.</param>
/// <param name="Refused">Why there's no sound: no such file, not a WAV file, a format we don't read.</param>
public sealed record SoundLoad(BakedSound? Sound, bool Baked, string? Refused);

/// <summary>
/// Hands out baked sounds. The first time one is asked for, or after its file or
/// <see cref="SoundFile.Version"/> changes, it's decoded and saved as <c>cache/sounds/name.snd</c>;
/// after that the saved copy is loaded. The .wav is still read each time, but only to hash it,
/// the same way textures are keyed.
/// </summary>
/// <param name="contentFolder">The content folder, holding <c>sound</c>.</param>
/// <param name="cacheFolder">The cache folder: bakes go in its <c>sounds</c> folder.</param>
public sealed class SoundCache(string contentFolder, string cacheFolder)
{
    public const string Extension = ".snd";

    private readonly string _soundFolder = Path.Combine(contentFolder, "sound");

    /// <summary>Files that baked with something wrong in them, for the log.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>A sound by the name a map gives it, such as <c>world/amb10</c> or <c>world/amb10.wav</c>.</summary>
    /// <exception cref="IOException">The file exists but can't be read.</exception>
    public SoundLoad Load(string name)
    {
        string file = SoundNames.ToFile(name);
        if (file == SoundNames.Extension)
        {
            return Refuse("the map names no sound");
        }

        string source = "sound/" + file;
        if (!SafePath.IsRelative(file))
        {
            return Refuse($"{source} isn't a safe path");
        }

        string path = Path.Combine(_soundFolder, file);
        if (!File.Exists(path))
        {
            return Refuse($"there's no {source}");
        }

        byte[] wav = File.ReadAllBytes(path);
        string hash = BakeSections.Hash([wav]);
        string bakePath = BakePath(file);
        if (TryRead(bakePath, hash, file) is { } cached)
        {
            return new SoundLoad(cached, false, null);
        }

        PcmSound pcm;
        try
        {
            pcm = WavReader.Read(wav);
        }
        catch (InvalidDataException e)
        {
            return Refuse($"{source}: {e.Message}");
        }

        if (pcm.CutShort)
        {
            Problems.Add($"{source}: the file ends before its sound does, so the end is missing");
        }

        var sound = new BakedSound
        {
            Name = file,
            Hash = hash,
            Source = source,
            SampleRate = pcm.SampleRate,
            LoopStart = pcm.LoopStart,
            Samples = pcm.Samples,
        };
        return new SoundLoad(Save(bakePath, sound), true, null);
    }

    /// <summary>Where a sound's bake goes: its path under the sound folder, with our extension in place of .wav.</summary>
    internal string BakePath(string file)
    {
        string bare = file.EndsWith(SoundNames.Extension, StringComparison.Ordinal) ? file[..^SoundNames.Extension.Length] : file;
        return Path.Combine(cacheFolder, "sounds", bare + Extension);
    }

    private static SoundLoad Refuse(string reason) => new(null, false, reason);

    /// <summary>Returns the bake if it's current. A stale or damaged one comes back null, to be baked again.</summary>
    private static BakedSound? TryRead(string path, string hash, string name)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            BakedSound? sound = SoundFile.Read(stream, hash);
            return sound?.Name == name ? sound : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Writes beside the old bake and then swaps, so a crash never leaves half a file. A failed save only means baking again next time.</summary>
    private static BakedSound Save(string path, BakedSound sound)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = $"{path}.{Environment.ProcessId}.tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                SoundFile.Write(stream, sound);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The sound still plays. It just bakes again next time.
        }

        return sound;
    }
}
