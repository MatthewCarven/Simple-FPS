using System.Security.Cryptography;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Bake;

/// <summary>Builds a <see cref="BakedMap"/> from a map's own files. Everything slow happens here, once.</summary>
public static class MapBaker
{
    /// <param name="name">The map's name, which can differ from the .bsp's when an override names another map.</param>
    /// <param name="bspName">The .bsp's name, without the extension.</param>
    /// <param name="bsp">The .bsp file's bytes.</param>
    /// <param name="overrideFile">The override file's bytes, or null if the map has none.</param>
    /// <exception cref="InvalidDataException">The map or override file can't be read.</exception>
    public static BakedMap Bake(string name, string bspName, byte[] bsp, byte[]? overrideFile)
    {
        Q2Bsp map = Q2Bsp.Read(bsp);
        List<BspEntity> entities = map.Entities;
        if (overrideFile is not null && MapOverride.Read(overrideFile).Entities is { } replacement)
        {
            entities = BspEntity.Parse(replacement);
        }

        (string? title, string? author) = MapCredits.From(entities);
        return new BakedMap
        {
            Info = new MapInfo(name, Hash(bsp, overrideFile), bspName, title, author),
            Entities = entities,
            Geometry = MapMesher.Build(map),
            Collision = CollisionData.From(map),
            Visibility = MapVisibility.Read(map.Visibility),
        };
    }

    /// <summary>SHA-256 of the .bsp followed by the override file, in lower-case hex.</summary>
    public static string Hash(byte[] bsp, byte[]? overrideFile)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(bsp);
        if (overrideFile is not null)
        {
            hash.AppendData(overrideFile);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
