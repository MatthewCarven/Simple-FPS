using System.Numerics;
using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Tests;

public class BspReadingTests
{
    [Fact]
    public void ReadsTheLumpsOfASmallMap()
    {
        Q2Bsp bsp = Q2Bsp.Read(TinyBsp.Build());

        Assert.Equal(4, bsp.Vertices.Length);
        Assert.Equal(new Vector3(32, 16, 0), bsp.Vertices[2]);
        Assert.Equal(Vector3.UnitZ, Assert.Single(bsp.Planes).Normal);
        Assert.Equal(4, Assert.Single(bsp.Faces).EdgeCount);
        Assert.Equal(1, Assert.Single(bsp.Models).FaceCount);
        Assert.Equal("worldspawn", Assert.Single(bsp.Entities).ClassName);
    }

    [Fact]
    public void TextureNamesAreLowerCaseWithForwardSlashes()
    {
        Q2Bsp bsp = Q2Bsp.Read(TinyBsp.Build(texture: "E1U1\\Floor1"));

        Assert.Equal("e1u1/floor1", Assert.Single(bsp.TexInfos).Texture);
    }

    [Fact]
    public void AQuake1MapIsRefusedWithAClearMessage()
    {
        byte[] quake1 = BitConverter.GetBytes(29L);

        var error = Assert.Throws<InvalidDataException>(() => Q2Bsp.Read(quake1));
        Assert.Contains("Quake1", error.Message);
    }

    [Fact]
    public void ACutOffFileIsRefused()
    {
        byte[] whole = TinyBsp.Build();

        Assert.Throws<InvalidDataException>(() => Q2Bsp.Read(whole[..40]));
        Assert.Throws<InvalidDataException>(() => Q2Bsp.Read(whole[..(whole.Length - 8)]));
    }
}
