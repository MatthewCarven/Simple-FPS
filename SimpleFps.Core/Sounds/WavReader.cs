using System.Buffers.Binary;

namespace SimpleFps.Core.Sounds;

/// <summary>
/// A sound decoded from a .wav file: one channel of 16-bit signed samples at the file's own rate.
/// </summary>
/// <param name="LoopStart">
/// The sample a looping play goes back to, from the file's first cue point, or -1 if it has none.
/// </param>
/// <param name="CutShort">
/// The file's data runs past its end, so only the samples that are there were read.
/// </param>
public sealed record PcmSound(int SampleRate, short[] Samples, int LoopStart, bool CutShort);

/// <summary>
/// Reads .wav files. The collection holds 8- and 16-bit mono PCM almost entirely (2,860 of
/// 2,877 files), with a few in stereo, 24-bit or 32-bit float, all of which are read. Stereo is
/// mixed down to mono, since every sound is played from a point. Compressed formats are refused
/// with the reason, and so is anything that isn't a WAV file at all.
/// </summary>
/// <remarks>
/// Loop points follow the original game: the first cue point's sample is where a loop starts, and
/// if the next LIST chunk after the cue chunk is a "mark" label, its length ends the sound there.
/// Unlike the original, a loop that would run past the end is ignored rather than refused.
/// </remarks>
public static class WavReader
{
    private const int PcmFormat = 1;
    private const int FloatFormat = 3;
    private const int ExtensibleFormat = 0xFFFE;
    private const int MinRate = 1000;
    private const int MaxRate = 384_000;

    /// <exception cref="InvalidDataException">The data isn't a WAV file, or is in a format we don't read.</exception>
    public static PcmSound Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("It isn't a WAV file.");
        }

        Chunks chunks = FindChunks(data);
        if (chunks.Format is not { } fmtAt)
        {
            throw new InvalidDataException("It has no fmt chunk, so its format isn't known.");
        }

        if (chunks.Data is not { } dataAt)
        {
            throw new InvalidDataException("It has no data chunk.");
        }

        (int format, int channels, int rate, int bits) = ReadFormat(data.Slice(fmtAt.Start, fmtAt.Length));
        int frameSize = channels * (bits / 8);
        ReadOnlySpan<byte> pcm = data.Slice(dataAt.Start, dataAt.Length);
        int count = pcm.Length / frameSize;
        if (count == 0)
        {
            throw new InvalidDataException("It holds no samples.");
        }

        (int loopStart, int loopEnd) = ReadLoop(data, chunks, count);
        if (loopEnd < count)
        {
            count = loopEnd;
        }

        var samples = new short[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> frame = pcm.Slice(i * frameSize, frameSize);
            int sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += ToShort(frame.Slice(c * (bits / 8), bits / 8), format);
            }

            samples[i] = (short)(sum / channels);
        }

        return new PcmSound(rate, samples, loopStart, chunks.DataCutShort);
    }

    private readonly record struct Chunk(int Start, int Length);

    private sealed class Chunks
    {
        public Chunk? Format;
        public Chunk? Data;
        public Chunk? Cue;

        /// <summary>The first LIST chunk after the cue chunk, where a loop length can be.</summary>
        public Chunk? ListAfterCue;

        public bool DataCutShort;
    }

    /// <summary>
    /// Walks the chunks after the header, padded to even lengths. The RIFF length is ignored, as
    /// the original did, since some files get it wrong. A data chunk running past the end keeps
    /// what's there; any other chunk that does ends the walk.
    /// </summary>
    private static Chunks FindChunks(ReadOnlySpan<byte> data)
    {
        var found = new Chunks();
        int at = 12;
        while (at <= data.Length - 8)
        {
            ReadOnlySpan<byte> id = data.Slice(at, 4);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
            int start = at + 8;
            bool fits = length <= (uint)(data.Length - start);
            if (id.SequenceEqual("data"u8) && found.Data is null)
            {
                found.Data = new Chunk(start, fits ? (int)length : data.Length - start);
                found.DataCutShort = !fits;
            }

            if (!fits)
            {
                break;
            }

            var chunk = new Chunk(start, (int)length);
            if (id.SequenceEqual("fmt "u8))
            {
                found.Format ??= chunk;
            }
            else if (id.SequenceEqual("cue "u8))
            {
                found.Cue ??= chunk;
            }
            else if (id.SequenceEqual("LIST"u8) && found.Cue is not null)
            {
                found.ListAfterCue ??= chunk;
            }

            at = start + (int)length + (int)(length & 1);
        }

        return found;
    }

    private static (int Format, int Channels, int Rate, int Bits) ReadFormat(ReadOnlySpan<byte> fmt)
    {
        if (fmt.Length < 16)
        {
            throw new InvalidDataException($"Its fmt chunk is only {fmt.Length} bytes long.");
        }

        int format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);

        // An extensible header names the real format in the first two bytes of its sub-format.
        if (format == ExtensibleFormat)
        {
            format = fmt.Length >= 26 ? BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]) : 0;
        }

        if (format is not (PcmFormat or FloatFormat))
        {
            throw new InvalidDataException($"It's compressed as {FormatName(format)}, which isn't supported.");
        }

        bool known = format == PcmFormat ? bits is 8 or 16 or 24 or 32 : bits == 32;
        if (!known)
        {
            string kind = format == PcmFormat ? "PCM" : "floating point";
            throw new InvalidDataException($"It's {bits}-bit {kind}, which isn't supported.");
        }

        if (channels == 0)
        {
            throw new InvalidDataException("It says it has no channels.");
        }

        if (rate is < MinRate or > MaxRate)
        {
            throw new InvalidDataException($"Its sample rate of {rate} a second can't be right.");
        }

        return (format, channels, (int)rate, bits);
    }

    private static string FormatName(int format) => format switch
    {
        2 => "Microsoft ADPCM (format 2)",
        6 => "A-law (format 6)",
        7 => "mu-law (format 7)",
        17 => "IMA ADPCM (format 17)",
        85 => "MP3 (format 85)",
        _ => $"format {format}",
    };

    /// <summary>
    /// The loop's start and end, in samples. The end is the sound's length unless a "mark" label
    /// says otherwise. A loop that starts or ends past the samples there are is dropped.
    /// </summary>
    private static (int Start, int End) ReadLoop(ReadOnlySpan<byte> data, Chunks chunks, int count)
    {
        if (chunks.Cue is not { } cue || cue.Length < 28 || BinaryPrimitives.ReadUInt32LittleEndian(data[cue.Start..]) == 0)
        {
            return (-1, count);
        }

        // The first cue point's sample offset, 24 bytes in: after the count, its ID, position, chunk ID, chunk start and block start.
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(data[(cue.Start + 24)..]);
        if (start >= (uint)count)
        {
            return (-1, count);
        }

        // A LIST holding "adtl", then an "ltxt" label: its cue ID, sample length, then purpose, which is "mark" for a loop.
        if (chunks.ListAfterCue is { Length: >= 24 } list && data.Slice(list.Start + 20, 4).SequenceEqual("mark"u8))
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[(list.Start + 16)..]);
            if ((ulong)start + length <= (ulong)count)
            {
                return ((int)start, (int)(start + length));
            }
        }

        return ((int)start, count);
    }

    private static int ToShort(ReadOnlySpan<byte> sample, int format)
    {
        if (format == FloatFormat)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(sample);
            return float.IsNaN(value) ? 0 : (int)MathF.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue);
        }

        // 8-bit is unsigned. Wider samples keep their top 16 bits.
        return sample.Length switch
        {
            1 => (sample[0] - 128) << 8,
            _ => BinaryPrimitives.ReadInt16LittleEndian(sample[^2..]),
        };
    }
}
