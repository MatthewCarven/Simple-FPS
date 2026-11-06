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
        Vector3[]? outline = null)
    {
        var lumps = new byte[LumpCount][];
        for (int i = 0; i < LumpCount; i++)
        {
            lumps[i] = [];
        }

        lumps[0] = Encoding.Latin1.GetBytes(entities);

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

        return Assemble(lumps);
    }

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
