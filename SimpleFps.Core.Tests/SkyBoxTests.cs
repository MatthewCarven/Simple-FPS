using System.Numerics;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Textures;

namespace SimpleFps.Core.Tests;

public class SkyBoxTests
{
    private static string SideAt(float x, float y, float z) => SkyBox.Sides[SkyBox.Lookup(new Vector3(x, y, z)).Side];

    [Theory]
    [InlineData(1, 0, 0, "rt")]
    [InlineData(0, 1, 0, "bk")]
    [InlineData(-1, 0, 0, "lf")]
    [InlineData(0, -1, 0, "ft")]
    [InlineData(0, 0, 1, "up")]
    [InlineData(0, 0, -1, "dn")]
    public void EachPictureFacesItsOwnWay(float x, float y, float z, string side)
    {
        (int found, float u, float v) = SkyBox.Lookup(new Vector3(x, y, z));

        Assert.Equal(side, SkyBox.Sides[found]);
        Assert.Equal(0.5f, u, 5);
        Assert.Equal(0.5f, v, 5);
    }

    [Fact]
    public void TheWallsAreUpright()
    {
        // Looking east, up is toward the top of the picture and south is to the right.
        Assert.Equal("rt", SideAt(1, 0, 0.5f));
        Assert.True(SkyBox.Lookup(new Vector3(1, 0, 0.5f)).V < 0.5f);
        Assert.True(SkyBox.Lookup(new Vector3(1, -0.5f, 0)).U > 0.5f);

        // Looking north, east is to the right.
        Assert.True(SkyBox.Lookup(new Vector3(0.5f, 1, 0)).U > 0.5f);
        Assert.True(SkyBox.Lookup(new Vector3(0, 1, 0.5f)).V < 0.5f);
    }

    [Fact]
    public void TheTopIsSeenFacingEastWithYourHeadTippedBack()
    {
        // Tipping back from facing east, the top of your view points west and your right is still south.
        Assert.Equal("up", SideAt(-0.5f, 0, 1));
        Assert.True(SkyBox.Lookup(new Vector3(-0.5f, 0, 1)).V < 0.5f);
        Assert.True(SkyBox.Lookup(new Vector3(0, -0.5f, 1)).U > 0.5f);
    }

    [Fact]
    public void TheBottomIsSeenFacingEastLookingDown()
    {
        Assert.Equal("dn", SideAt(0.5f, 0, -1));
        Assert.True(SkyBox.Lookup(new Vector3(0.5f, 0, -1)).V < 0.5f);
        Assert.True(SkyBox.Lookup(new Vector3(0, -0.5f, -1)).U > 0.5f);
    }

    [Fact]
    public void LookingUpAPointOnAPictureFindsThatPoint()
    {
        var random = new Random(7);
        for (int i = 0; i < 1000; i++)
        {
            int side = random.Next(6);
            float u = 0.01f + random.NextSingle() * 0.98f;
            float v = 0.01f + random.NextSingle() * 0.98f;

            (int found, float u2, float v2) = SkyBox.Lookup(SkyBox.PictureDirection(side, u, v) * (1 + random.NextSingle()));

            Assert.Equal(side, found);
            Assert.Equal(u, u2, 4);
            Assert.Equal(v, v2, 4);
        }
    }

    [Fact]
    public void CubeFacesMeetEdgeToEdge()
    {
        // Every point on a face's edge must also be on another face's edge, or a face is turned or flipped.
        var edges = new List<(int Face, Vector3 Direction)>();
        for (int face = 0; face < 6; face++)
        {
            for (int k = 0; k <= 8; k++)
            {
                float along = k / 4f - 1;
                foreach ((float s, float t) in new[] { (-1f, along), (1f, along), (along, -1f), (along, 1f) })
                {
                    edges.Add((face, Vector3.Normalize(SkyBox.CubeDirection(face, s, t))));
                }
            }
        }

        foreach ((int face, Vector3 direction) in edges)
        {
            Assert.Contains(edges, e => e.Face != face && Vector3.Distance(e.Direction, direction) < 1e-5f);
        }
    }

    [Fact]
    public void EachPictureLandsOnTheCubeFaceItFaces()
    {
        // Side i is filled with red 10 * (i + 1).
        RgbaImage[] pictures = Enumerable.Range(0, 6).Select(i => Solid(8, (byte)(10 * (i + 1)))).ToArray();

        RgbaImage[] faces = SkyBox.ToCubeFaces(pictures);

        // Cube faces +X, -X, +Y, -Y, +Z, -Z are the map's east, west, up, down, south and north.
        string[] expected = ["rt", "lf", "up", "dn", "ft", "bk"];
        for (int face = 0; face < 6; face++)
        {
            int side = SkyBox.Sides.ToList().IndexOf(expected[face]);
            Assert.All(Enumerable.Range(0, 64), p => Assert.Equal(10 * (side + 1), faces[face].Pixels[p * 4]));
        }
    }

    [Fact]
    public void ASkyPaintedByDirectionComesOutTheSameOnTheCube()
    {
        const int size = 32;
        RgbaImage[] pictures = Enumerable.Range(0, 6).Select(side => Painted(size, (u, v) => SkyBox.PictureDirection(side, u, v))).ToArray();

        RgbaImage[] faces = SkyBox.ToCubeFaces(pictures);

        for (int face = 0; face < 6; face++)
        {
            RgbaImage expected = Painted(size, (u, v) => SkyBox.CubeToMap(SkyBox.CubeDirection(face, u * 2 - 1, v * 2 - 1)));
            for (int i = 0; i < expected.Pixels.Length; i++)
            {
                Assert.InRange(faces[face].Pixels[i] - expected.Pixels[i], -1, 1);
            }
        }

        (double across, double inside) = SkySeams.Measure(faces);
        Assert.True(across < inside * 1.5 + 1, $"{across} across the edges, {inside} inside");
    }

    [Fact]
    public void PicturesOfAnotherSizeAreStretchedToTheLargest()
    {
        RgbaImage[] pictures = [Solid(16, 1), Solid(32, 2), Solid(32, 3), Solid(32, 4), Solid(32, 5), Solid(8, 6)];

        RgbaImage[] faces = SkyBox.ToCubeFaces(pictures);

        Assert.All(faces, f => Assert.Equal((32, 32), (f.Width, f.Height)));
        Assert.All(Enumerable.Range(0, 32 * 32), p => Assert.Equal(1, faces[0].Pixels[p * 4]));
    }

    [Fact]
    public void AMapWithNoSkyGetsTheDefaultAndNoSpin()
    {
        MapSky sky = MapSky.From(BspEntity.Parse("{ \"classname\" \"worldspawn\" }"));

        Assert.Equal(SkyBox.DefaultName, sky.Name);
        Assert.False(sky.Spins);
    }

    [Fact]
    public void AMapsSkyNameAndSpinAreRead()
    {
        MapSky sky = MapSky.From(BspEntity.Parse(
            "{ \"classname\" \"worldspawn\" \"sky\" \" Space1\" \"skyaxis\" \"0 0 1\" \"skyrotate\" \"10\" }"));

        Assert.Equal("space1", sky.Name);
        Assert.Equal(Vector3.UnitZ, sky.Axis);
        Assert.Equal(10f, sky.DegreesPerSecond);
        Assert.True(sky.Spins);
    }

    [Fact]
    public void ASpinWithoutAnAxisDoesNothing()
    {
        // q2duel5 gives its axis as a bare "0".
        MapSky sky = MapSky.From(BspEntity.Parse("{ \"classname\" \"worldspawn\" \"sky\" \"unit5_\" \"skyaxis\" \"0\" \"skyrotate\" \"20\" }"));

        Assert.False(sky.Spins);
    }

    private static RgbaImage Solid(int size, byte red)
    {
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = red;
            pixels[i + 3] = 255;
        }

        return new RgbaImage(size, size, pixels);
    }

    /// <summary>Each pixel coloured by the direction at its centre, so neighbouring directions get neighbouring colours.</summary>
    private static RgbaImage Painted(int size, Func<float, float, Vector3> direction)
    {
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector3 d = Vector3.Normalize(direction((x + 0.5f) / size, (y + 0.5f) / size));
                int at = (y * size + x) * 4;
                pixels[at] = (byte)MathF.Round((d.X + 1) * 127.5f);
                pixels[at + 1] = (byte)MathF.Round((d.Y + 1) * 127.5f);
                pixels[at + 2] = (byte)MathF.Round((d.Z + 1) * 127.5f);
                pixels[at + 3] = 255;
            }
        }

        return new RgbaImage(size, size, pixels);
    }
}
