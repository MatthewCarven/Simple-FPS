namespace SimpleFps.Core.Objects;

public enum MapEventKind
{
    /// <summary>A player went through a teleporter. The entity is the teleporter.</summary>
    Teleported,

    /// <summary>A push zone caught a player, at most once every second and a half for each player.</summary>
    Pushed,

    /// <summary>A trigger or counter has words for the player. The text is in <see cref="MapEvent.Text"/>.</summary>
    Message,

    /// <summary>
    /// Something with no map object yet was set off, such as a door or a sound spot. Whatever handles
    /// that class can listen for this until it becomes a map object of its own.
    /// </summary>
    Used,

    /// <summary>A killtarget took something out of the map.</summary>
    Removed,
}

/// <summary>Something that happened on one tick, for the game to show or play.</summary>
/// <param name="Entity">The entity's place in the map's entity list.</param>
/// <param name="Player">The player it happened to, or -1 for nobody in particular.</param>
public readonly record struct MapEvent(MapEventKind Kind, int Entity, int Player, string? Text = null);
