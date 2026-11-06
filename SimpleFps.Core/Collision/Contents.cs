namespace SimpleFps.Core.Collision;

/// <summary>What a brush or a point in space is made of. Values match the Quake 2 map format.</summary>
[Flags]
public enum Contents
{
    None = 0,
    Solid = 0x1,
    Window = 0x2,
    Lava = 0x8,
    Slime = 0x10,
    Water = 0x20,
    PlayerClip = 0x10000,
    Ladder = 0x20000000,

    /// <summary>Everything a player can't walk through.</summary>
    PlayerSolid = Solid | Window | PlayerClip,

    Liquid = Lava | Slime | Water,
}
