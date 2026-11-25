using System.Text;

namespace SimpleFps.Core.Tests;

/// <summary>Writes small .wav files in code, so the reader is tested without any game content.</summary>
internal static class TinyWavs
{
    /// <summary>A whole file: the RIFF header, then the chunks given, each padded to an even length.</summary>
    public static byte[] File(params (string Id, byte[] Body)[] chunks)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(0);
        w.Write("WAVE"u8);
        foreach ((string id, byte[] body) in chunks)
        {
            w.Write(Encoding.ASCII.GetBytes(id));
            w.Write(body.Length);
            w.Write(body);
            if (body.Length % 2 == 1)
            {
                w.Write((byte)0);
            }
        }

        w.Flush();
        stream.Position = 4;
        w.Write((int)stream.Length - 8);
        return stream.ToArray();
    }

    public static (string, byte[]) Fmt(int format, int channels, int rate, int bits) =>
        ("fmt ", Bytes(w =>
        {
            w.Write((short)format);
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * channels * bits / 8);
            w.Write((short)(channels * bits / 8));
            w.Write((short)bits);
        }));

    /// <summary>A WAVE_FORMAT_EXTENSIBLE header whose sub-format is <paramref name="subFormat"/>.</summary>
    public static (string, byte[]) ExtensibleFmt(int subFormat, int channels, int rate, int bits) =>
        ("fmt ", Bytes(w =>
        {
            w.Write(Fmt(0xFFFE, channels, rate, bits).Item2);
            w.Write((short)22);
            w.Write((short)bits);
            w.Write(0);
            w.Write((short)subFormat);
            w.Write(new byte[14]);
        }));

    public static (string, byte[]) Data(byte[] pcm) => ("data", pcm);

    public static byte[] Pcm16(params short[] samples) => Bytes(w =>
    {
        foreach (short s in samples)
        {
            w.Write(s);
        }
    });

    /// <summary>A cue chunk with one cue point at <paramref name="sample"/>.</summary>
    public static (string, byte[]) Cue(int sample) =>
        ("cue ", Bytes(w =>
        {
            w.Write(1);
            w.Write(1);
            w.Write(0);
            w.Write("data"u8);
            w.Write(0);
            w.Write(0);
            w.Write(sample);
        }));

    /// <summary>A LIST chunk holding a label of <paramref name="length"/> samples, marked as a loop the way Cool Edit wrote them.</summary>
    public static (string, byte[]) LoopLength(int length) =>
        ("LIST", Bytes(w =>
        {
            w.Write("adtl"u8);
            w.Write("ltxt"u8);
            w.Write(20);
            w.Write(1);
            w.Write(length);
            w.Write("mark"u8);
            w.Write(new byte[8]);
        }));

    /// <summary>A plain 16-bit mono file.</summary>
    public static byte[] Mono16(int rate, params short[] samples) =>
        File(Fmt(1, 1, rate, 16), Data(Pcm16(samples)));

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        write(w);
        w.Flush();
        return stream.ToArray();
    }
}
