using System.Numerics;
using SimpleFps.Core.Textures;

namespace SimpleFps.Core.Bsp;

/// <summary>
/// The sky a map asks for in its worldspawn: the pictures' name (<c>sky</c>), and an optional
/// slow spin, <c>skyrotate</c> degrees a second about <c>skyaxis</c>. 46 maps in the collection spin
/// their sky. A map that names no sky gets Quake 2's default.
/// </summary>
/// <param name="Axis">In the map's frame. Zero when the map gives none, which means no spin.</param>
public sealed record MapSky(string Name, Vector3 Axis, float DegreesPerSecond)
{
    public bool Spins => DegreesPerSecond != 0 && Axis.LengthSquared() > 0;

    public static MapSky From(IEnumerable<BspEntity> entities)
    {
        BspEntity? world = entities.FirstOrDefault(e => e.ClassName == "worldspawn");
        string name = (world?["sky"] ?? "").Trim().Replace('\\', '/').ToLowerInvariant();
        return new MapSky(
            name.Length > 0 ? name : SkyBox.DefaultName,
            world?.GetVector("skyaxis") ?? Vector3.Zero,
            world?.GetFloat("skyrotate") ?? 0f);
    }
}
