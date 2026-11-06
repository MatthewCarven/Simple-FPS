using System.Text;
using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Tests;

public class BspFormatTests
{
    private static byte[] Header(string magic, int version)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes(magic));
        bytes.AddRange(BitConverter.GetBytes(version));
        return bytes.ToArray();
    }

    [Fact]
    public void Quake2MapIsRecognised()
    {
        Assert.Equal(BspKind.Quake2, BspFormat.Identify(Header("IBSP", 38)));
    }

    [Fact]
    public void Quake1MapIsRecognisedFromItsLeadingVersion()
    {
        Assert.Equal(BspKind.Quake1, BspFormat.Identify(BitConverter.GetBytes(29L)));
    }

    [Fact]
    public void Quake3MapIsNotMistakenForQuake2()
    {
        // Same magic word as Quake 2, different version.
        Assert.Equal(BspKind.Unknown, BspFormat.Identify(Header("IBSP", 46)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    public void TruncatedHeaderIsUnknownRatherThanACrash(int length)
    {
        Assert.Equal(BspKind.Unknown, BspFormat.Identify(new byte[length]));
    }

    [Fact]
    public void AnythingElseIsUnknown()
    {
        Assert.Equal(BspKind.Unknown, BspFormat.Identify(Encoding.ASCII.GetBytes("not a map file")));
    }
}
