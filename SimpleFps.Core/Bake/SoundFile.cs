using System.Text;
using static SimpleFps.Core.Bake.BakeSections;

namespace SimpleFps.Core.Bake;

/// <summary>
/// Reads and writes our baked sound files. A file starts with "SFBS", the version and the hash of
/// the file it was baked from, then the sections described in <see cref="BakeSections"/>: INFO
/// (name, source, sample rate, loop start) and PCM (the samples).
/// </summary>
public static class SoundFile
{
    /// <summary>Bump this whenever what a baked sound holds or how it's built changes, and every sound re-bakes.</summary>
    public const int Version = 1;

    private static readonly byte[] Magic = "SFBS"u8.ToArray();

    private static readonly uint InfoTag = Tag("INFO");
    private static readonly uint SamplesTag = Tag("PCM ");

    public static void Write(Stream stream, BakedSound sound)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(sound.Hash);

        Section(w, InfoTag, () =>
        {
            w.Write(sound.Name);
            w.Write(sound.Source);
            w.Write(sound.SampleRate);
            w.Write(sound.LoopStart);
        });

        Section(w, SamplesTag, () => WriteArray<short>(w, sound.Samples));
        Section(w, EndTag, () => { });
    }

    /// <summary>
    /// Reads a baked sound, or returns null if it's from another version or was baked from a file
    /// with a different hash, either of which means it needs baking again.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is damaged or isn't a baked sound.</exception>
    public static BakedSound? Read(Stream stream, string expectedHash)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            if (!r.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
            {
                throw new InvalidDataException("Not a baked sound.");
            }

            if (r.ReadInt32() != Version || r.ReadString() is not { } hash || hash != expectedHash)
            {
                return null;
            }

            (string Name, string Source, int Rate, int LoopStart)? info = null;
            short[]? samples = null;
            while (true)
            {
                (uint tag, long end) = ReadSectionStart(r);
                if (tag == EndTag)
                {
                    break;
                }

                if (tag == InfoTag)
                {
                    info = (r.ReadString(), r.ReadString(), r.ReadInt32(), r.ReadInt32());
                }
                else if (tag == SamplesTag)
                {
                    samples = ReadArray<short>(r);
                }

                EndSection(r, end);
            }

            (string name, string source, int rate, int loopStart) = info ?? throw Missing("INFO");
            if (samples is null)
            {
                throw Missing("PCM");
            }

            if (rate <= 0 || samples.Length == 0 || loopStart < -1 || loopStart >= samples.Length)
            {
                throw new InvalidDataException($"Baked sound has a bad rate ({rate}), length ({samples.Length}) or loop ({loopStart}).");
            }

            return new BakedSound
            {
                Name = name,
                Hash = hash,
                Source = source,
                SampleRate = rate,
                LoopStart = loopStart,
                Samples = samples,
            };
        }
        catch (Exception e) when (IsDamage(e))
        {
            throw new InvalidDataException($"Baked sound is damaged: {e.Message}", e);
        }
    }

    private static InvalidDataException Missing(string tag) => new($"Baked sound has no {tag} section.");
}
