using System.Buffers.Binary;

namespace SimpleFps.Core.Bsp;

/// <summary>
/// A map's visibility data. The map's leaves are grouped into clusters, and for each cluster the
/// map compiler worked out which clusters could possibly be seen from it (the PVS) and which could
/// be seen from any of those (the PHS, "hearable"). The original game sent looping sounds only to
/// players who could see the sound's spot, and one-off sounds only to players who could hear it.
/// </summary>
/// <remarks>
/// The lump starts with the cluster count, then two offsets for each cluster, PVS and PHS, each
/// pointing at a row of one bit per cluster, with runs of zero bytes squeezed to a zero and a count.
/// A map with no data sees everything from everywhere, as in the original; 8 of the 363 maps have
/// none. A row that would run long is cut at its end, as the original did. All the rows are
/// unpacked once when a map loads: 4.4 MB for the largest map, <c>risc2</c>, with 4,169 clusters.
/// </remarks>
public sealed class MapVisibility
{
    public static readonly MapVisibility None = new([], 0, [], []);

    private readonly byte[] _pvs;
    private readonly byte[] _phs;

    private MapVisibility(byte[] lump, int clusterCount, byte[] pvs, byte[] phs)
    {
        Lump = lump;
        ClusterCount = clusterCount;
        _pvs = pvs;
        _phs = phs;
    }

    /// <summary>The data as it is in the map, which is what the bake keeps.</summary>
    public byte[] Lump { get; }

    /// <summary>How many clusters the data covers, or 0 if there's none and everything is visible.</summary>
    public int ClusterCount { get; }

    /// <summary>Bytes in one row: a bit for each cluster.</summary>
    public int RowBytes => (ClusterCount + 7) >> 3;

    /// <summary>
    /// Unpacks the map's visibility lump. Data whose header doesn't hold together counts as none,
    /// so the map still plays, with every sound let through.
    /// </summary>
    public static MapVisibility Read(byte[] lump)
    {
        if (lump.Length < 4)
        {
            return None;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(lump);
        if (count <= 0 || count > (lump.Length - 4) / 8)
        {
            return None;
        }

        int rowBytes = (count + 7) >> 3;
        var pvs = new byte[count * rowBytes];
        var phs = new byte[count * rowBytes];
        for (int cluster = 0; cluster < count; cluster++)
        {
            int pvsAt = BinaryPrimitives.ReadInt32LittleEndian(lump.AsSpan(4 + (cluster * 8)));
            int phsAt = BinaryPrimitives.ReadInt32LittleEndian(lump.AsSpan(8 + (cluster * 8)));
            if (!Unpack(lump, pvsAt, pvs.AsSpan(cluster * rowBytes, rowBytes))
                || !Unpack(lump, phsAt, phs.AsSpan(cluster * rowBytes, rowBytes)))
            {
                return None;
            }
        }

        return new MapVisibility(lump, count, pvs, phs);
    }

    /// <summary>True if <paramref name="to"/> could be seen from <paramref name="from"/>. Cluster -1, inside solid, sees and is seen by nothing.</summary>
    public bool CanSee(int from, int to) => Has(_pvs, from, to);

    /// <summary>True if a sound in <paramref name="from"/> could be heard in <paramref name="to"/>.</summary>
    public bool CanHear(int from, int to) => Has(_phs, from, to);

    /// <summary>
    /// ORs a cluster's PVS row into <paramref name="row"/>, which is <see cref="RowBytes"/> long.
    /// Cluster -1 adds nothing.
    /// </summary>
    public void AddVisibleFrom(int cluster, Span<byte> row)
    {
        if (cluster < 0 || cluster >= ClusterCount)
        {
            return;
        }

        ReadOnlySpan<byte> source = _pvs.AsSpan(cluster * RowBytes, RowBytes);
        for (int i = 0; i < source.Length; i++)
        {
            row[i] |= source[i];
        }
    }

    private bool Has(byte[] rows, int from, int to)
    {
        if (from < 0 || to < 0)
        {
            return false;
        }

        if (ClusterCount == 0)
        {
            return true;
        }

        return from < ClusterCount && to < ClusterCount && (rows[(from * RowBytes) + (to >> 3)] & (1 << (to & 7))) != 0;
    }

    /// <summary>Unpacks one row. False if it starts outside the lump; a row that runs off the end keeps zeros for the rest.</summary>
    private static bool Unpack(byte[] lump, int at, Span<byte> row)
    {
        if (at < 0 || at >= lump.Length)
        {
            return false;
        }

        int written = 0;
        while (written < row.Length && at < lump.Length)
        {
            byte value = lump[at++];
            if (value != 0)
            {
                row[written++] = value;
                continue;
            }

            if (at >= lump.Length)
            {
                break;
            }

            // A run of zero bytes. The row is already zero, so only the position moves.
            written = Math.Min(row.Length, written + lump[at++]);
        }

        return true;
    }
}
