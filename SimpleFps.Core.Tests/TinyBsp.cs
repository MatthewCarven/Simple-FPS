using System.Numerics;
using System.Text;
using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Tests;

/// <summary>
/// Writes a real Quake 2 map file holding one 32 x 16 floor quad, so tests don't need any game
/// content. The quad faces up and is lit by four rows of known bytes.
/// </summary>
internal static class TinyBsp
{
    private const int LumpCount = 19;

    /// <summary>A 32 x 16 face at 16 units a luxel gives a 3 x 2 lightmap.</summary>
    public static readonly byte[] Light =
    [
        10, 11, 12, 20, 21, 22, 30, 31, 32,
        40, 41, 42, 50, 51, 52, 60, 61, 62,
    ];

    public static byte[] Build(
        SurfaceFlags flags = SurfaceFlags.None,
        string texture = "base/floor",
        int lightOffset = 0,
        string entities = """{ "classname" "worldspawn" }""",
        Vector3[]? outline = null,
        bool solidBox = false,
        byte[]? visibility = null)
    {
        var lumps = new byte[LumpCount][];
        for (int i = 0; i < LumpCount; i++)
        {
            lumps[i] = [];
        }

        lumps[0] = Encoding.Latin1.GetBytes(entities);
        lumps[3] = visibility ?? [];

        // One plane: facing up, through the origin.
        lumps[1] = Bytes(w =>
        {
            w.Write(0f);
            w.Write(0f);
            w.Write(1f);
            w.Write(0f);
            w.Write(2);
        });

        // Corners clockwise as seen from above, which is how the format winds its faces.
        Vector3[] corners = outline ?? [new(0, 0, 0), new(0, 16, 0), new(32, 16, 0), new(32, 0, 0)];
        lumps[2] = Bytes(w =>
        {
            foreach (Vector3 c in corners)
            {
                w.Write(c.X);
                w.Write(c.Y);
                w.Write(c.Z);
            }
        });

        // Texture axes map X to S and Y to T, one texel a unit.
        lumps[5] = Bytes(w =>
        {
            foreach (float f in new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f })
            {
                w.Write(f);
            }

            w.Write((int)flags);
            w.Write(0);
            byte[] name = new byte[32];
            Encoding.Latin1.GetBytes(texture).CopyTo(name, 0);
            w.Write(name);
            w.Write(-1);
        });

        lumps[6] = Bytes(w =>
        {
            w.Write((ushort)0);
            w.Write((short)0);
            w.Write(0);
            w.Write((short)corners.Length);
            w.Write((short)0);
            w.Write(new byte[] { 0, 255, 255, 255 });
            w.Write(lightOffset);
        });

        lumps[7] = Light;

        // Edge 0 is a dummy: the format can't express a negative index of zero.
        lumps[11] = Bytes(w =>
        {
            w.Write((ushort)0);
            w.Write((ushort)0);
            for (int i = 0; i < corners.Length; i++)
            {
                w.Write((ushort)i);
                w.Write((ushort)((i + 1) % corners.Length));
            }
        });

        lumps[12] = Bytes(w =>
        {
            for (int i = 1; i <= corners.Length; i++)
            {
                w.Write(i);
            }
        });

        lumps[13] = Bytes(w =>
        {
            for (int i = 0; i < 9; i++)
            {
                w.Write(0f);
            }

            w.Write(0);
            w.Write(0);
            w.Write(1);
        });

        if (solidBox)
        {
            AddSolidBox(lumps);
        }

        return Assemble(lumps);
    }

    /// <summary>The solid box under the floor quad: from (0, 0, -16) to (32, 16, 0).</summary>
    public static readonly (Vector3 Min, Vector3 Max) Box = (new Vector3(0, 0, -16), new Vector3(32, 16, 0));

    /// <summary>
    /// Fills the box under the quad with one solid brush, and gives the world a tree of one node:
    /// the floor's plane, with an empty leaf in front and the brush's leaf behind.
    /// </summary>
    private static void AddSolidBox(byte[][] lumps)
    {
        (Vector3 min, Vector3 max) = Box;
        (Vector3 Normal, float Distance)[] sides =
        [
            (Vector3.UnitX, max.X), (-Vector3.UnitX, -min.X),
            (Vector3.UnitY, max.Y), (-Vector3.UnitY, -min.Y),
            (Vector3.UnitZ, max.Z), (-Vector3.UnitZ, -min.Z),
        ];

        // Plane 0 stays the floor's, and the brush's sides follow it.
        lumps[1] = [.. lumps[1], .. Bytes(w =>
        {
            foreach ((Vector3 n, float d) in sides)
            {
                w.Write(n.X);
                w.Write(n.Y);
                w.Write(n.Z);
                w.Write(d);
                w.Write(0);
            }
        })];

        lumps[4] = Bytes(w =>
        {
            w.Write(0);
            w.Write(-1);
            w.Write(-2);
            w.Write(new byte[12]);
            w.Write((ushort)0);
            w.Write((ushort)1);
        });

        lumps[8] = Bytes(w =>
        {
            WriteLeaf(w, contents: 0, firstBrush: 0, brushCount: 0);
            WriteLeaf(w, contents: 1, firstBrush: 0, brushCount: 1);
        });

        lumps[10] = Bytes(w => w.Write((ushort)0));

        lumps[14] = Bytes(w =>
        {
            w.Write(0);
            w.Write(sides.Length);
            w.Write(1);
        });

        lumps[15] = Bytes(w =>
        {
            for (int i = 0; i < sides.Length; i++)
            {
                w.Write((ushort)(i + 1));
                w.Write((short)0);
            }
        });

        static void WriteLeaf(BinaryWriter w, int contents, ushort firstBrush, ushort brushCount)
        {
            w.Write(contents);
            w.Write((short)0);
            w.Write((short)0);
            w.Write(new byte[12]);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write(firstBrush);
            w.Write(brushCount);
        }
    }

    /// <summary>Writes a .bsp.override file, with a map path, an entity list, or both.</summary>
    public static byte[] Override(string? mapPath, string? entities) => Bytes(w =>
    {
        w.Write((mapPath is null ? 0 : 1) | (entities is null ? 0 : 4));
        if (mapPath is not null)
        {
            byte[] name = new byte[64];
            Encoding.Latin1.GetBytes(mapPath).CopyTo(name, 0);
            w.Write(name);
        }

        if (entities is not null)
        {
            byte[] text = Encoding.Latin1.GetBytes(entities);
            w.Write(text.Length);
            w.Write(text);
        }
    });

    private static byte[] Assemble(byte[][] lumps)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("IBSP"));
        writer.Write(38);

        int offset = 8 + LumpCount * 8;
        foreach (byte[] lump in lumps)
        {
            writer.Write(offset);
            writer.Write(lump.Length);
            offset += lump.Length;
        }

        foreach (byte[] lump in lumps)
        {
            writer.Write(lump);
        }

        return stream.ToArray();
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return stream.ToArray();
    }
}
