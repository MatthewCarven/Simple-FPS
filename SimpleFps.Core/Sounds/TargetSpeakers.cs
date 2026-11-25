using System.Globalization;
using System.Numerics;
using SimpleFps.Core.Bsp;

namespace SimpleFps.Core.Sounds;

/// <summary>A map's <c>target_speaker</c>: a spot that plays a sound, looping or each time it's triggered.</summary>
/// <param name="Entity">Its place in the map's entity list. Its one-off sounds come from emitter <c>Entity + 1</c>, so each cuts off the one before.</param>
/// <param name="Sound">Its file under the sound folder, from the <c>noise</c> key.</param>
/// <param name="Loop">Its number in <see cref="SoundEvents.Loops"/>, or -1 if it plays once each time it's triggered.</param>
public sealed record TargetSpeaker(int Entity, string Sound, Vector3 Origin, float Volume, float Attenuation, int SpawnFlags, string? TargetName, int Loop)
{
    public bool Loops => Loop >= 0;
}

/// <summary>
/// The maps' sound spots, 4,123 of them in 199 of the 375 maps. Most loop from the start (spawn flag 1).
/// Loops that start off (flag 2) and speakers that play once wait to be triggered, which is
/// milestone 4b's job: it calls <see cref="Use"/>, as the original's triggers did.
/// </summary>
public static class TargetSpeakers
{
    public const int LoopedOn = 1;
    public const int LoopedOff = 2;

    /// <summary>The original removes anything with this flag from deathmatch games.</summary>
    public const int NotInDeathmatch = 0x800;

    public const string ClassName = "target_speaker";

    /// <summary>
    /// Finds the map's speakers and adds their loops to <paramref name="sounds"/>, on if they start
    /// on. A speaker with no <c>noise</c> key, or one kept out of deathmatch, is left out, as in
    /// the original.
    /// </summary>
    public static List<TargetSpeaker> Start(IEnumerable<BspEntity> entities, SoundEvents sounds)
    {
        var speakers = new List<TargetSpeaker>();
        int index = -1;
        foreach (BspEntity entity in entities)
        {
            index++;
            if (entity.ClassName != ClassName || entity["noise"] is not { } noise)
            {
                continue;
            }

            int flags = int.TryParse(entity["spawnflags"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int f) ? f : 0;
            if ((flags & NotInDeathmatch) != 0)
            {
                continue;
            }

            string sound = SoundNames.ToFile(noise);
            Vector3 origin = entity.GetVector("origin") ?? Vector3.Zero;
            int loop = (flags & (LoopedOn | LoopedOff)) != 0 ? sounds.AddLoop(sound, origin, on: (flags & LoopedOn) != 0) : -1;
            speakers.Add(new TargetSpeaker(
                index,
                sound,
                origin,
                SoundFalloff.VolumeFromKey(entity.GetFloat("volume")),
                SoundFalloff.FromKey(entity.GetFloat("attenuation")),
                flags,
                entity["targetname"],
                loop));
        }

        return speakers;
    }

    /// <summary>
    /// What a speaker does when triggered: a loop turns on or off, and any other speaker plays its
    /// sound once, with the map's volume and attenuation.
    /// </summary>
    public static void Use(TargetSpeaker speaker, SoundEvents sounds)
    {
        if (speaker.Loops)
        {
            sounds.SetLoop(speaker.Loop, !sounds.Loops[speaker.Loop].On);
            return;
        }

        sounds.Play(new SoundEvent(speaker.Sound, speaker.Origin, speaker.Volume, speaker.Attenuation, Emitter: speaker.Entity + 1));
    }
}
