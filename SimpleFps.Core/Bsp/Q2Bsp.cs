using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SimpleFps.Core.Bsp;

/// <summary>A Quake 2 map file (IBSP version 38), read into plain arrays.</summary>
public sealed class Q2Bsp
{
    private const int HeaderSize = 8;
    private const int LumpEntrySize = 8;
    private const int LumpCount = 19;
    private const int TexInfoSize = 76;
    private const int TexNameSize = 32;

    private const int LumpEntities = 0;
    private const int LumpPlanes = 1;
    private const int LumpVertices = 2;
    private const int LumpNodes = 4;
    private const int LumpTexInfo = 5;
    private const int LumpFaces = 6;
    private const int LumpLighting = 7;
    private const int LumpLeaves = 8;
    private const int LumpLeafBrushes = 10;
    private const int LumpEdges = 11;
    private const int LumpSurfEdges = 12;
    private const int LumpModels = 13;
    private const int LumpBrushes = 14;
    private const int LumpBrushSides = 15;

    public required BspPlane[] Planes { get; init; }
    public required Vector3[] Vertices { get; init; }
    public required BspNode[] Nodes { get; init; }
    public required BspTexInfo[] TexInfos { get; init; }
    public required BspFace[] Faces { get; init; }
    public required byte[] Lighting { get; init; }
    public required BspLeaf[] Leaves { get; init; }
    public required ushort[] LeafBrushes { get; init; }
    public required BspEdge[] Edges { get; init; }
    public required int[] SurfEdges { get; init; }
    public required BspModel[] Models { get; init; }
    public required BspBrush[] Brushes { get; init; }
    public required BspBrushSide[] BrushSides { get; init; }
    public required List<BspEntity> Entities { get; init; }

    public static Q2Bsp Load(string path) => Read(File.ReadAllBytes(path));

    /// <exception cref="InvalidDataException">The data isn't a readable Quake 2 map.</exception>
    public static Q2Bsp Read(byte[] data)
    {
        BspKind kind = BspFormat.Identify(data);
        if (kind != BspKind.Quake2)
        {
            throw new InvalidDataException($"Not a Quake 2 map (looks like {kind}).");
        }

        if (data.Length < HeaderSize + LumpCount * LumpEntrySize)
        {
            throw new InvalidDataException("File is too short to hold a map header.");
        }

        return new Q2Bsp
        {
            Planes = Lump<BspPlane>(data, LumpPlanes),
            Vertices = Lump<Vector3>(data, LumpVertices),
            Nodes = Lump<BspNode>(data, LumpNodes),
            TexInfos = ReadTexInfos(LumpBytes(data, LumpTexInfo)),
            Faces = Lump<BspFace>(data, LumpFaces),
            Lighting = LumpBytes(data, LumpLighting).ToArray(),
            Leaves = Lump<BspLeaf>(data, LumpLeaves),
            LeafBrushes = Lump<ushort>(data, LumpLeafBrushes),
            Edges = Lump<BspEdge>(data, LumpEdges),
            SurfEdges = Lump<int>(data, LumpSurfEdges),
            Models = Lump<BspModel>(data, LumpModels),
            Brushes = Lump<BspBrush>(data, LumpBrushes),
            BrushSides = Lump<BspBrushSide>(data, LumpBrushSides),
            Entities = BspEntity.Parse(Encoding.Latin1.GetString(LumpBytes(data, LumpEntities))),
        };
    }

    private static ReadOnlySpan<byte> LumpBytes(byte[] data, int lump)
    {
        ReadOnlySpan<byte> entry = data.AsSpan(HeaderSize + lump * LumpEntrySize, LumpEntrySize);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(entry);
        int length = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);

        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException($"Lump {lump} runs outside the file.");
        }

        return data.AsSpan(offset, length);
    }

    private static T[] Lump<T>(byte[] data, int lump)
        where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = LumpBytes(data, lump);
        int whole = bytes.Length - bytes.Length % Unsafe.SizeOf<T>();
        return MemoryMarshal.Cast<byte, T>(bytes[..whole]).ToArray();
    }

    private static BspTexInfo[] ReadTexInfos(ReadOnlySpan<byte> bytes)
    {
        var result = new BspTexInfo[bytes.Length / TexInfoSize];
        for (int i = 0; i < result.Length; i++)
        {
            ReadOnlySpan<byte> entry = bytes.Slice(i * TexInfoSize, TexInfoSize);
            ReadOnlySpan<float> axes = MemoryMarshal.Cast<byte, float>(entry[..32]);
            result[i] = new BspTexInfo(
                new Vector4(axes[0], axes[1], axes[2], axes[3]),
                new Vector4(axes[4], axes[5], axes[6], axes[7]),
                (SurfaceFlags)BinaryPrimitives.ReadInt32LittleEndian(entry[32..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[36..]),
                ReadName(entry.Slice(40, TexNameSize)),
                BinaryPrimitives.ReadInt32LittleEndian(entry[72..]));
        }

        return result;
    }

    private static string ReadName(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        ReadOnlySpan<byte> name = end < 0 ? field : field[..end];
        return Encoding.Latin1.GetString(name).Replace('\\', '/').ToLowerInvariant();
    }
}
