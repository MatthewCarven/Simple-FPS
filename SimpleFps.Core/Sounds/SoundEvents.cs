using System.Numerics;

namespace SimpleFps.Core.Sounds;

/// <summary>A sound to play once, from a point in the map.</summary>
/// <param name="Sound">Its file under the sound folder, as <see cref="SoundNames.ToFile"/> gives it, such as <c>world/amb10.wav</c>.</param>
/// <param name="Origin">Where it plays from, in map units.</param>
/// <param name="Volume">0 to 1.</param>
/// <param name="Attenuation">How fast it fades with distance, as in <see cref="SoundFalloff"/>.</param>
/// <param name="Emitter">
/// Who made it, or 0 for nobody in particular. A new sound from the same emitter cuts off the one
/// before, as a door's stop sound cuts off its start sound.
/// </param>
public readonly record struct SoundEvent(string Sound, Vector3 Origin, float Volume = 1f, float Attenuation = SoundFalloff.Normal, int Emitter = 0);

/// <summary>A sound that plays over and over from a point while it's on, such as a machine's hum.</summary>
public readonly record struct LoopingSound(string Sound, Vector3 Origin, bool On);

/// <summary>
/// What the simulation wants heard. One-off sounds wait here until the game takes them, and
/// loops are kept as state that the game reads every frame. The simulation only says what
/// plays where; whether a listener can hear it and how loud is the game's business.
/// </summary>
public sealed class SoundEvents
{
    /// <summary>One-off sounds kept when nothing takes them, as on a server with no one listening. The oldest go first.</summary>
    public const int MaxWaiting = 256;

    private readonly List<SoundEvent> _waiting = [];
    private readonly List<LoopingSound> _loops = [];

    /// <summary>Every loop there is, on or off. A loop's number is its place in this list.</summary>
    public IReadOnlyList<LoopingSound> Loops => _loops;

    public int Waiting => _waiting.Count;

    public void Play(SoundEvent sound)
    {
        if (_waiting.Count == MaxWaiting)
        {
            _waiting.RemoveAt(0);
        }

        _waiting.Add(sound);
    }

    /// <summary>Hands over every one-off sound played since the last call, oldest first.</summary>
    public void TakeWaiting(List<SoundEvent> into)
    {
        into.AddRange(_waiting);
        _waiting.Clear();
    }

    /// <summary>Adds a loop and returns its number.</summary>
    public int AddLoop(string sound, Vector3 origin, bool on)
    {
        _loops.Add(new LoopingSound(sound, origin, on));
        return _loops.Count - 1;
    }

    public void SetLoop(int loop, bool on) => _loops[loop] = _loops[loop] with { On = on };

    public void MoveLoop(int loop, Vector3 origin) => _loops[loop] = _loops[loop] with { Origin = origin };
}
