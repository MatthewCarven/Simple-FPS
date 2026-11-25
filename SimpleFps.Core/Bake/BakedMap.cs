using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Bake;

/// <param name="Name">The map's name, which is its file name without <c>.bsp</c>, in lower case.</param>
/// <param name="Hash">SHA-256 of the files it was baked from: the .bsp, then the override file if there is one.</param>
/// <param name="Bsp">The .bsp it was built from. Only differs from the name when an override names another map.</param>
public sealed record MapInfo(string Name, string Hash, string Bsp, string? Title, string? Author);

/// <summary>A map in our own form, holding everything the game needs, so the .bsp is never read to play it.</summary>
public sealed class BakedMap
{
    public required MapInfo Info { get; init; }

    /// <summary>From the override file when there is one, otherwise from the map.</summary>
    public required IReadOnlyList<BspEntity> Entities { get; init; }

    public required MapGeometry Geometry { get; init; }
    public required CollisionData Collision { get; init; }

    /// <summary>Which clusters can see and hear which, for letting sounds through. Leaves' clusters are in <see cref="Collision"/>.</summary>
    public required MapVisibility Visibility { get; init; }
}
