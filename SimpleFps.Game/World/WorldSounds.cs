using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using SimpleFps.Core.Bake;
using SimpleFps.Core.Sounds;
using SysVector3 = System.Numerics.Vector3;

namespace SimpleFps.Game.World;

/// <summary>
/// Plays the simulation's sounds in the world, from our baked files. Loops play while the camera
/// can hear them and stop when it can't, picking up where every copy of that sound has got to, as
/// the original kept them in step. One-off sounds play once and fade as the camera moves. Which
/// sounds reach the camera, and how loud, is worked out in Core; Godot only places and pans them.
/// </summary>
public partial class WorldSounds : Node3D
{
    /// <summary>The original's default volume.</summary>
    private const float MasterVolume = 0.7f;

    private const float Silent = -80f;

    private readonly Dictionary<string, BakedSound?> _sounds = new();
    private readonly Dictionary<string, AudioStreamWav> _loopStreams = new();
    private readonly Dictionary<string, AudioStreamWav> _onceStreams = new();
    private readonly List<AudioStreamPlayer3D?> _loopPlayers = new();
    private readonly List<OneOff> _oneOffs = new();
    private readonly List<SoundEvent> _taken = new();
    private readonly List<string> _refused = new();
    private SoundCache _cache = null!;
    private SoundEvents _events = null!;
    private Hearing _hearing = null!;
    private LoopMix _mix = null!;
    private float[] _gains = [];
    private double _clock;

    /// <summary>Sounds that wouldn't load, each with its reason.</summary>
    public IReadOnlyList<string> Refused => _refused;

    /// <summary>Files that baked with something wrong in them.</summary>
    public IReadOnlyList<string> Problems => _cache.Problems;

    /// <summary>Different sounds asked for so far, loaded or not.</summary>
    public int AskedCount => _sounds.Count;

    public int BakedCount { get; private set; }

    /// <summary>Time spent loading and baking sounds.</summary>
    public TimeSpan LoadTime { get; private set; }

    public int PlayingLoops => _loopPlayers.Count(p => p is { Playing: true });

    public int PlayingOneOffs => _oneOffs.Count;

    /// <param name="preload">Every sound the map might play, loaded now so nothing waits for a bake mid-game.</param>
    public static WorldSounds Build(string contentRoot, string cacheRoot, BakedMap map, SoundEvents events, IEnumerable<string> preload)
    {
        var sounds = new WorldSounds
        {
            Name = "Sounds",
            _cache = new SoundCache(contentRoot, cacheRoot),
            _events = events,
        };
        sounds._hearing = new Hearing(map.Collision, map.Visibility);
        sounds._mix = new LoopMix(sounds._hearing);

        var clock = Stopwatch.StartNew();
        foreach (string name in preload.Concat(events.Loops.Select(l => l.Sound)))
        {
            sounds.Get(name);
        }

        sounds.LoadTime = clock.Elapsed;
        return sounds;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (GetViewport().GetCamera3D() is { } camera)
        {
            Update(MapView.ToMap(camera.GlobalPosition));
        }
    }

    /// <summary>Moves the listener's ear, then starts, stops and sets the volume of every sound to match.</summary>
    public void Update(SysVector3 ear)
    {
        _hearing.Listen(ear);
        UpdateLoops();
        PlayWaiting();
        UpdateOneOffs(ear);
    }

    /// <summary>
    /// For the smoke test: listens from a point, then plays the map's first sound there once
    /// through the simulation's events, and reports what happened.
    /// </summary>
    public (bool Ok, string Report) Check(SysVector3 ear)
    {
        Update(ear);
        int loops = PlayingLoops;
        string? test = _sounds.Where(s => s.Value is not null).Select(s => s.Key).Order(StringComparer.Ordinal).FirstOrDefault();
        if (test is not null)
        {
            _events.Play(new SoundEvent(test, ear));
            Update(ear);
        }

        bool ok = test is null || PlayingOneOffs == 1;
        string played = test is null ? "no sound to test with" : $"{test} {(ok ? "played" : "didn't play")} once";
        return (ok, $"sounds: {loops} of {_events.Loops.Count} loops playing where you stand, {played}");
    }

    /// <summary>A sound by its file name, loaded once per map. Null if it's refused.</summary>
    private BakedSound? Get(string name)
    {
        if (_sounds.TryGetValue(name, out BakedSound? known))
        {
            return known;
        }

        SoundLoad load;
        try
        {
            load = _cache.Load(name);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            load = new SoundLoad(null, false, $"sound/{name}: {e.Message}");
        }

        if (load.Refused is { } reason)
        {
            _refused.Add(reason);
        }

        BakedCount += load.Baked ? 1 : 0;
        _sounds[name] = load.Sound;
        return load.Sound;
    }

    private void UpdateLoops()
    {
        IReadOnlyList<LoopingSound> loops = _events.Loops;
        if (_gains.Length < loops.Count)
        {
            Array.Resize(ref _gains, loops.Count);
            while (_loopPlayers.Count < loops.Count)
            {
                _loopPlayers.Add(null);
            }
        }

        _mix.Mix(loops, _gains);
        for (int i = 0; i < loops.Count; i++)
        {
            AudioStreamPlayer3D? player = _loopPlayers[i];
            if (_gains[i] <= 0f)
            {
                if (player is { Playing: true })
                {
                    player.Stop();
                }

                continue;
            }

            if (Get(loops[i].Sound) is not { } sound)
            {
                continue;
            }

            if (player is null)
            {
                player = Player3D(Stream(_loopStreams, sound, loop: true));
                _loopPlayers[i] = player;
            }

            player.Position = MapView.ToGodot(loops[i].Origin);
            player.VolumeDb = Decibels(_gains[i]);
            if (!player.Playing)
            {
                player.Play((float)(_clock % sound.Seconds));
            }
        }
    }

    private void PlayWaiting()
    {
        _taken.Clear();
        _events.TakeWaiting(_taken);
        foreach (SoundEvent wanted in _taken)
        {
            if (Get(wanted.Sound) is not { } sound || !_hearing.CanHearOnce(wanted.Origin, wanted.Attenuation))
            {
                continue;
            }

            if (wanted.Emitter != 0)
            {
                foreach (OneOff old in _oneOffs.Where(o => o.Event.Emitter == wanted.Emitter).ToList())
                {
                    old.Player.QueueFree();
                    _oneOffs.Remove(old);
                }
            }

            AudioStreamWav stream = Stream(_onceStreams, sound, loop: false);

            // Heard everywhere means at full volume in both ears, as in the original, so it isn't placed at all.
            Node player = wanted.Attenuation == SoundFalloff.None ? PlayerEverywhere(stream) : Player3D(stream, MapView.ToGodot(wanted.Origin));
            _oneOffs.Add(new OneOff(wanted, player));
            SetVolume(player, Decibels(OneOffGain(wanted, _hearing.Listener)));
            Play(player);
        }
    }

    private void UpdateOneOffs(SysVector3 ear)
    {
        for (int i = _oneOffs.Count - 1; i >= 0; i--)
        {
            OneOff oneOff = _oneOffs[i];
            if (!IsPlaying(oneOff.Player))
            {
                oneOff.Player.QueueFree();
                _oneOffs.RemoveAt(i);
                continue;
            }

            SetVolume(oneOff.Player, Decibels(OneOffGain(oneOff.Event, ear)));
        }
    }

    private static float OneOffGain(SoundEvent sound, SysVector3 ear) =>
        sound.Volume * SoundFalloff.Gain(SysVector3.Distance(sound.Origin, ear), sound.Attenuation);

    private static bool IsPlaying(Node player) => player switch
    {
        AudioStreamPlayer3D placed => placed.Playing,
        AudioStreamPlayer everywhere => everywhere.Playing,
        _ => false,
    };

    private static void Play(Node player)
    {
        if (player is AudioStreamPlayer3D placed)
        {
            placed.Play();
        }
        else if (player is AudioStreamPlayer everywhere)
        {
            everywhere.Play();
        }
    }

    private static void SetVolume(Node player, float decibels)
    {
        if (player is AudioStreamPlayer3D placed)
        {
            placed.VolumeDb = decibels;
        }
        else if (player is AudioStreamPlayer everywhere)
        {
            everywhere.VolumeDb = decibels;
        }
    }

    private static float Decibels(float gain) =>
        gain > 0f ? Mathf.Max(Silent, Mathf.LinearToDb(gain * MasterVolume)) : Silent;

    /// <summary>A player that Godot pans but doesn't fade, since the fade is worked out in Core.</summary>
    private AudioStreamPlayer3D Player3D(AudioStreamWav stream, Vector3 position = default)
    {
        var player = new AudioStreamPlayer3D
        {
            Stream = stream,
            Position = position,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
            AttenuationFilterDb = 0f,
            MaxDistance = 0f,
        };
        AddChild(player);
        return player;
    }

    private AudioStreamPlayer PlayerEverywhere(AudioStreamWav stream)
    {
        var player = new AudioStreamPlayer { Stream = stream };
        AddChild(player);
        return player;
    }

    private static AudioStreamWav Stream(Dictionary<string, AudioStreamWav> streams, BakedSound sound, bool loop)
    {
        if (streams.TryGetValue(sound.Name, out AudioStreamWav? known))
        {
            return known;
        }

        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = sound.SampleRate,
            Stereo = false,
            Data = MemoryMarshal.AsBytes(sound.Samples.AsSpan()).ToArray(),
        };

        // A loop goes back to the very start, whatever the file's cue point says, as the original's loops did.
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = sound.Samples.Length;
        }

        streams[sound.Name] = stream;
        return stream;
    }

    private sealed record OneOff(SoundEvent Event, Node Player);
}
