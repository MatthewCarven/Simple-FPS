using SimpleFps.Core.Sounds;
using static SimpleFps.Core.Tests.TinyWavs;

namespace SimpleFps.Core.Tests;

public class WavReaderTests
{
    [Fact]
    public void SixteenBitMonoReadsAsItIs()
    {
        PcmSound sound = WavReader.Read(Mono16(22050, 0, 1000, -1000, short.MaxValue, short.MinValue));

        Assert.Equal(22050, sound.SampleRate);
        Assert.Equal(new short[] { 0, 1000, -1000, short.MaxValue, short.MinValue }, sound.Samples);
        Assert.Equal(-1, sound.LoopStart);
        Assert.False(sound.CutShort);
    }

    [Fact]
    public void EightBitIsUnsignedAndWidensToSixteen()
    {
        byte[] file = File(Fmt(1, 1, 11025, 8), Data([128, 255, 0, 129, 127]));

        PcmSound sound = WavReader.Read(file);

        Assert.Equal(11025, sound.SampleRate);
        Assert.Equal(new short[] { 0, 127 << 8, -128 << 8, 1 << 8, -1 << 8 }, sound.Samples);
    }

    [Fact]
    public void StereoIsMixedDownToMono()
    {
        byte[] file = File(Fmt(1, 2, 44100, 16), Data(Pcm16(1000, 3000, -100, -301, short.MaxValue, short.MaxValue)));

        PcmSound sound = WavReader.Read(file);

        Assert.Equal(new short[] { 2000, -200, short.MaxValue }, sound.Samples);
    }

    [Fact]
    public void TwentyFourAndThirtyTwoBitKeepTheirTopSixteenBits()
    {
        byte[] pcm24 = [0xFF, 0x34, 0x12, 0x00, 0x00, 0x80];
        byte[] pcm32 = [0xAA, 0xBB, 0x34, 0x12, 0x00, 0x00, 0x00, 0x80];

        Assert.Equal(new short[] { 0x1234, short.MinValue }, WavReader.Read(File(Fmt(1, 1, 48000, 24), Data(pcm24))).Samples);
        Assert.Equal(new short[] { 0x1234, short.MinValue }, WavReader.Read(File(Fmt(1, 1, 48000, 32), Data(pcm32))).Samples);
    }

    [Fact]
    public void FloatIsScaledAndClipped()
    {
        byte[] pcm = [.. BitConverter.GetBytes(0f), .. BitConverter.GetBytes(0.5f), .. BitConverter.GetBytes(-2f), .. BitConverter.GetBytes(float.NaN)];

        PcmSound sound = WavReader.Read(File(Fmt(3, 1, 22050, 32), Data(pcm)));

        Assert.Equal(new short[] { 0, 16384, -short.MaxValue, 0 }, sound.Samples);
    }

    [Fact]
    public void AnExtensibleHeaderIsReadByItsSubFormat()
    {
        PcmSound sound = WavReader.Read(File(ExtensibleFmt(1, 1, 22050, 16), Data(Pcm16(5, -5))));

        Assert.Equal(new short[] { 5, -5 }, sound.Samples);
        var e = Assert.Throws<InvalidDataException>(() => WavReader.Read(File(ExtensibleFmt(85, 1, 22050, 16), Data(Pcm16(5)))));
        Assert.Contains("MP3", e.Message);
    }

    [Fact]
    public void OtherChunksAnywhereAreSkippedEvenWithOddLengths()
    {
        byte[] file = File(("LIST", [1, 2, 3]), ("junk", []), Data(Pcm16(7, 8)), ("smpl", new byte[9]), Fmt(1, 1, 8000, 16));

        Assert.Equal(new short[] { 7, 8 }, WavReader.Read(file).Samples);
    }

    [Fact]
    public void ACuePointIsWhereALoopStarts()
    {
        PcmSound sound = WavReader.Read(File(Fmt(1, 1, 22050, 16), Data(Pcm16(1, 2, 3, 4, 5)), Cue(2)));

        Assert.Equal(2, sound.LoopStart);
        Assert.Equal(5, sound.Samples.Length);
    }

    [Fact]
    public void AMarkedLoopLengthEndsTheSoundThere()
    {
        PcmSound sound = WavReader.Read(File(Fmt(1, 1, 22050, 16), Data(Pcm16(1, 2, 3, 4, 5, 6)), Cue(1), LoopLength(3)));

        Assert.Equal(1, sound.LoopStart);
        Assert.Equal(new short[] { 1, 2, 3, 4 }, sound.Samples);
    }

    [Fact]
    public void ALoopThatDoesntFitIsLeftOut()
    {
        byte[] data = Pcm16(1, 2, 3);
        PcmSound tooLong = WavReader.Read(File(Fmt(1, 1, 22050, 16), Data(data), Cue(1), LoopLength(5)));
        PcmSound startsPastTheEnd = WavReader.Read(File(Fmt(1, 1, 22050, 16), Data(data), Cue(3)));

        Assert.Equal((1, 3), (tooLong.LoopStart, tooLong.Samples.Length));
        Assert.Equal((-1, 3), (startsPastTheEnd.LoopStart, startsPastTheEnd.Samples.Length));
    }

    [Fact]
    public void ALabelBeforeTheCueChunkIsntALoopLength()
    {
        PcmSound sound = WavReader.Read(File(Fmt(1, 1, 22050, 16), LoopLength(1), Data(Pcm16(1, 2, 3, 4)), Cue(0)));

        Assert.Equal((0, 4), (sound.LoopStart, sound.Samples.Length));
    }

    [Fact]
    public void DataRunningPastTheEndKeepsTheWholeSamplesThere()
    {
        byte[] file = Mono16(22050, 1, 2, 3, 4);
        byte[] cut = file[..^3];

        PcmSound sound = WavReader.Read(cut);

        Assert.True(sound.CutShort);
        Assert.Equal(new short[] { 1, 2 }, sound.Samples);
    }

    [Theory]
    [InlineData(17, 4, "IMA ADPCM")]
    [InlineData(2, 4, "Microsoft ADPCM")]
    [InlineData(1, 12, "12-bit PCM")]
    [InlineData(3, 64, "64-bit floating point")]
    public void FormatsWeDontReadAreRefusedWithTheReason(int format, int bits, string reason)
    {
        var e = Assert.Throws<InvalidDataException>(() => WavReader.Read(File(Fmt(format, 1, 22050, bits), Data(new byte[16]))));

        Assert.Contains(reason, e.Message);
    }

    [Fact]
    public void BrokenFilesAreRefusedWithTheReason()
    {
        Assert.Contains("isn't a WAV", Refusal("set sw_stipple 1\n"u8.ToArray()));
        Assert.Contains("no fmt", Refusal(File(Data(Pcm16(1)))));
        Assert.Contains("no data", Refusal(File(Fmt(1, 1, 22050, 16))));
        Assert.Contains("no samples", Refusal(File(Fmt(1, 1, 22050, 16), Data([]))));
        Assert.Contains("no channels", Refusal(File(Fmt(1, 0, 22050, 16), Data(Pcm16(1)))));
        Assert.Contains("sample rate", Refusal(File(Fmt(1, 1, 0, 16), Data(Pcm16(1)))));
        Assert.Contains("fmt chunk is only", Refusal(File(("fmt ", new byte[10]), Data(Pcm16(1)))));
    }

    private static string Refusal(byte[] file) => Assert.Throws<InvalidDataException>(() => WavReader.Read(file)).Message;
}
