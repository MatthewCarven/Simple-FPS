using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SimpleFps.Core;
using SimpleFps.Core.Bake;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;
using SimpleFps.Core.Movement;
using SimpleFps.Core.Sounds;
using SimpleFps.Game.Player;
using SimpleFps.Game.Ui;
using SimpleFps.Game.World;
using FileAccess = System.IO.File;
using SysVector3 = System.Numerics.Vector3;

namespace SimpleFps.Game;

/// <summary>
/// Entry scene: the map list, then a map with you in it. Game flags go after a bare "--":
/// --map=name (skip the list and play it)  --spawn=number  --smoke-test  --shot=file.png (save a
/// picture and quit)  --panel (open the tuning panel in that picture)  --menu (open the Esc menu in
/// that picture)  --list (open the map list even with --smoke-test or --shot)
/// --textures=original|highres (for this run only, not saved)  --pitch=degrees (start looking down,
/// or up if negative).
/// The smoke test and --shot skip the list and play ringbase, unless --map names another map. With
/// --list, --shot takes a picture of the list, and the smoke test picks the map from the list, goes
/// back to the list through the Esc menu, and picks it again before hopping.
/// </summary>
public partial class Main : Node
{
    private const string DefaultMap = "ringbase";
    private const string TuningFile = "user://tuning.json";
    private const string OptionsFile = "user://options.json";
    private const int FramesBeforeShot = 150;
    private const int SmokeTestTicks = 240;

    /// <summary>The longest quitting waits for the audio thread to let go of the sounds.</summary>
    private const ulong QuitBackstopMs = 3000;

    private const string Controls =
        "WASD move, Space jump (hold to hop), Ctrl crouch, left mouse test recoil\n" +
        "Tab tuning panel, R respawn, V fly (Q/E down/up, Shift fast), Esc menu (Resume, Map list, Quit)\n" +
        "T flips between high-res and original textures";

    private static readonly Color SkyColour = new(0.33f, 0.42f, 0.55f);

    private readonly TuningPanel _panel = new();
    private readonly MapMenu _menu = new();
    private readonly PauseMenu _pause = new();
    private readonly MoveSettings _settings = LoadSettings();
    private readonly PlayerOptions _options = LoadOptions();
    private Label _status = null!;
    private MapCache _cache = null!;
    private string _content = "";
    private string _cacheFolder = "";
    private TextureSet _textureSet;

    private MapView? _view;
    private WorldTextures? _textures;
    private WorldSounds? _sounds;
    private bool _quitting;
    private LocalPlayer? _player;
    private BakedMap? _map;
    private SysVector3 _spawn;
    private string _mapTitle = "";
    private string? _lastMap;
    private Task<bool> _loading = Task.FromResult(false);

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string engine = Engine.GetVersionInfo()["string"].AsString();
        GD.Print($"{GameInfo.Title}: Godot {engine}, {GameInfo.TickRate} ticks a second");

        Engine.PhysicsTicksPerSecond = GameInfo.TickRate;
        GetTree().AutoAcceptQuit = false;
        RenderingServer.SetDefaultClearColor(SkyColour);

        _status = new Label { Position = new Vector2(16, 12) };
        var overlay = new CanvasLayer();
        overlay.AddChild(_status);
        AddChild(overlay);

        string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        _content = FindContent(root);
        _cacheFolder = Path.Combine(root, "cache");
        _cache = new MapCache(Path.Combine(_content, "maps"), Path.Combine(_cacheFolder, "maps"));
        _textureSet = Flag(args, "--textures=") switch
        {
            "original" => TextureSet.Original,
            "highres" => TextureSet.HighRes,
            _ => _options.HighResTextures ? TextureSet.HighRes : TextureSet.Original,
        };

        _panel.Build(_settings);
        AddChild(_panel);

        _menu.Build();
        _menu.MapChosen += name =>
        {
            if (_loading.IsCompleted)
            {
                _loading = PlayFromList(name);
            }
        };
        AddChild(_menu);

        _pause.Build();
        _pause.ResumeChosen += Resume;
        _pause.MapListChosen += BackToList;
        _pause.QuitChosen += () => QuitGame();
        AddChild(_pause);

        string? mapFlag = Flag(args, "--map=");
        string? shot = Flag(args, "--shot=");
        bool smokeTest = args.Contains("--smoke-test");

        if (args.Contains("--list") || (mapFlag is null && shot is null && !smokeTest))
        {
            OpenMapList();
            if (smokeTest)
            {
                RunListSmokeTest(mapFlag ?? DefaultMap);
            }
            else if (shot is not null)
            {
                SaveShot(shot);
            }

            return;
        }

        string? error = ShowMap(mapFlag ?? DefaultMap, Flag(args, "--spawn="));

        if (smokeTest)
        {
            if (error is null)
            {
                // Both ways, so the second flip times the set that's already loaded.
                FlipTextures(save: false);
                FlipTextures(save: false);
            }

            GD.Print(error is null && RunSmokeTest() && CheckSounds() ? "smoke test ok" : "smoke test failed");
            QuitGame(error is null ? 0 : 1);
            return;
        }

        if (shot is not null)
        {
            _status.Text = error ?? "";
            if (args.Contains("--panel"))
            {
                _panel.Toggle();
            }

            if (args.Contains("--menu") && error is null)
            {
                OpenPauseMenu();
            }

            SaveShot(shot);
            return;
        }

        if (error is null)
        {
            // Grabbed from the start, so looking around works without a click first.
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else
        {
            OpenMapList(error);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            QuitGame();
        }
    }

    public override void _Input(InputEvent @event)
    {
        // With no map, the map list handles its own keys.
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || _player is null)
        {
            return;
        }

        // Picked up here, before the controls see them, because a focused slider would otherwise swallow Tab.
        switch (key.PhysicalKeycode)
        {
            case Key.Tab when !_pause.IsOpen:
            case Key.Escape when _panel.IsOpen:
                ToggleTuning();
                break;

            case Key.Escape when _pause.IsOpen:
                Resume();
                break;

            case Key.Escape:
                OpenPauseMenu();
                break;

            case Key.T when !_pause.IsOpen:
                FlipTextures(save: true);
                break;

            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_mapTitle.Length > 0)
        {
            string textures = _textureSet == TextureSet.HighRes ? "high-res" : "original";
            _status.Text = $"{_mapTitle}, {Engine.GetFramesPerSecond()} fps, {textures} textures\n{_player?.Status}\n{Controls}";
        }
    }

    /// <summary>
    /// Quits, stopping the map's sounds first and waiting until the audio thread has let go of
    /// them. Godot keeps a stopped sound in the audio server's list until the audio thread next
    /// mixes (every 93 ms headless), and any still in that list at exit are reported as leaked:
    /// a WAV stream and its playback for each sound playing. So this waits for two mixes to start
    /// after the stop, which means one whole mix has run since, then one more frame for the main
    /// thread to free what the mix let go. A sound let go but not yet freed is freed with the audio
    /// server at exit. The timer is only a backstop, for a machine with no audio running at all.
    /// </summary>
    private async void QuitGame(int exitCode = 0)
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        if (_sounds is not null)
        {
            List<ulong> held = _sounds.StopForQuit();
            _sounds.QueueFree();
            _sounds = null;

            ulong started = Time.GetTicksMsec();
            int frames = 0, mixes = 0;
            double sinceMix = AudioServer.GetTimeSinceLastMix();
            while (mixes < 2 && held.Any(GodotObject.IsInstanceIdValid) && Time.GetTicksMsec() - started < QuitBackstopMs)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frames++;

                // The time since the last mix only ever goes down when a new mix starts.
                double now = AudioServer.GetTimeSinceLastMix();
                mixes += now < sinceMix ? 1 : 0;
                sinceMix = now;
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            int left = held.Count(GodotObject.IsInstanceIdValid);
            string how = mixes >= 2 || left == 0 ? "let go" : "NOT let go, gave up";
            GD.Print($"quit: {held.Count} sound objects {how} after {mixes} mixes, {frames + 1} frames, {Time.GetTicksMsec() - started} ms; {held.Count - left} freed, {left} to be freed at exit");
        }

        GetTree().Quit(exitCode);
    }

    private static string? Flag(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    private static MoveSettings LoadSettings()
    {
        string path = ProjectSettings.GlobalizePath(TuningFile);
        return FileAccess.Exists(path) ? MoveSettings.FromJson(FileAccess.ReadAllText(path)) : new MoveSettings();
    }

    private static PlayerOptions LoadOptions()
    {
        string path = ProjectSettings.GlobalizePath(OptionsFile);
        return FileAccess.Exists(path) ? PlayerOptions.FromJson(FileAccess.ReadAllText(path)) : new PlayerOptions();
    }

    /// <summary>
    /// Swaps every surface between the high-res and original textures. The other set loads the
    /// first time it's shown. Saving keeps the choice for the next run.
    /// </summary>
    private void FlipTextures(bool save)
    {
        if (_textures is null || _view is null)
        {
            return;
        }

        ulong started = Time.GetTicksMsec();
        int bakedBefore = _textures.BakedCount;
        _textureSet = _textureSet == TextureSet.HighRes ? TextureSet.Original : TextureSet.HighRes;
        _textures.Set = _textureSet;
        _view.Retexture(_textures);
        bool highRes = _textureSet == TextureSet.HighRes;
        GD.Print($"textures: {(highRes ? "high-res" : "original")} in {Time.GetTicksMsec() - started} ms, {_textures.BakedCount - bakedBefore} baked, sky {_view.SkySource ?? "missing"}");

        if (save)
        {
            _options.HighResTextures = highRes;
            FileAccess.WriteAllText(ProjectSettings.GlobalizePath(OptionsFile), _options.ToJson());
        }
    }

    private void ToggleTuning()
    {
        _panel.Toggle();
        if (_player is not null)
        {
            _player.Paused = _panel.IsOpen;
        }

        Input.MouseMode = _panel.IsOpen ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (!_panel.IsOpen)
        {
            FileAccess.WriteAllText(ProjectSettings.GlobalizePath(TuningFile), _settings.ToJson());
        }
    }

    /// <summary>
    /// Reads every map's title afresh, so maps dropped in since are listed, and shows the list with
    /// the last map played highlighted.
    /// </summary>
    private void OpenMapList(string? message = null)
    {
        IReadOnlyList<MapListEntry> shown = [];
        try
        {
            ulong started = Time.GetTicksMsec();
            MapList list = MapList.Read(_cache);
            shown = list.Shown;
            GD.Print($"map list: {list.All.Count} maps read in {Time.GetTicksMsec() - started} ms, {shown.Count} shown");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"Couldn't read the maps: {e.Message}");
            message ??= $"Couldn't read the maps folder: {e.Message}";
        }

        Input.MouseMode = Input.MouseModeEnum.Visible;
        _menu.SetMaps(shown);
        _menu.Open(_lastMap, message);
    }

    private async Task<bool> PlayFromList(string name)
    {
        _menu.ShowMessage($"Loading {name}...");

        // Two frames, so the message is on screen before loading holds everything up.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (ShowMap(name, null) is { } error)
        {
            _menu.ShowMessage(error.Replace('\n', ' '));
            return false;
        }

        _menu.Close();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        return true;
    }

    private void OpenPauseMenu()
    {
        _player!.Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _pause.Open(_mapTitle);
    }

    private void Resume()
    {
        _pause.Close();
        if (_player is not null)
        {
            _player.Paused = false;
        }

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void BackToList()
    {
        _pause.Close();
        UnloadMap();
        OpenMapList();
    }

    /// <summary>Loads a map and puts you in it. Returns null, or what went wrong.</summary>
    private string? ShowMap(string name, string? spawnFlag)
    {
        try
        {
            ulong started = Time.GetTicksMsec();
            (BakedMap map, bool baked) = _cache.Load(name);
            ulong mapTaken = Time.GetTicksMsec() - started;

            ulong texturesStarted = Time.GetTicksMsec();
            var textures = new WorldTextures(_content, _cacheFolder, _textureSet);
            _view = MapView.Build(map, textures);
            ulong texturesTaken = Time.GetTicksMsec() - texturesStarted;
            AddChild(_view);
            _textures = textures;

            var sim = new Simulation(new BspCollision(map.Collision), _settings);
            (SysVector3 spawn, float yaw) = FindSpawn(map.Entities, spawnFlag);
            _player = new LocalPlayer();
            AddChild(_player);
            float pitch = float.TryParse(Flag(OS.GetCmdlineUserArgs(), "--pitch="), NumberStyles.Float, CultureInfo.InvariantCulture, out float wanted) ? wanted : 0f;
            _player.Begin(sim, spawn, yaw, pitch);
            var speakers = TargetSpeakers.Start(map.Entities, sim.Sounds);
            _sounds = WorldSounds.Build(_content, _cacheFolder, map, sim.Sounds, speakers.Select(s => s.Sound));
            AddChild(_sounds);

            _map = map;
            _spawn = spawn;
            _lastMap = name;
            _mapTitle = map.Info.Title is { } title ? $"{name}: {title}" : name;

            ulong taken = Time.GetTicksMsec() - started;
            string from = baked ? "baked" : "loaded from cache";
            string set = _textureSet == TextureSet.HighRes ? $"high-res ({textures.HighResCount} from the pack)" : "original";
            GD.Print($"map {name}: {from} in {mapTaken} ms, {_view.SurfaceCount} surfaces, {textures.LoadedCount} textures {set}, {textures.Missing.Count} missing, {textures.BakedCount} baked, textures and meshes {texturesTaken} ms (bakes {textures.CacheTime.TotalMilliseconds:F0}, to Godot {textures.UploadTime.TotalMilliseconds:F0}), {taken} ms in all");
            GD.Print($"  sky {_view.Sky.Name}: {_view.SkySource ?? "pictures missing, flat colour"}{(_view.Sky.Spins ? $", spinning {_view.Sky.DegreesPerSecond} degrees a second" : "")}");
            foreach (string missing in textures.Missing)
            {
                GD.Print($"  missing texture: {missing}");
            }

            foreach (string problem in textures.Problems.Concat(textures.OddShapes).Concat(textures.UnlikeSkies))
            {
                GD.Print($"  {problem}");
            }

            GD.Print($"  sounds: {speakers.Count} speakers, {sim.Sounds.Loops.Count(l => l.On)} of {sim.Sounds.Loops.Count} loops on, {_sounds.AskedCount} sounds, {_sounds.BakedCount} baked, {_sounds.Refused.Count} refused, {_sounds.LoadTime.TotalMilliseconds:F0} ms");
            foreach (string problem in _sounds.Refused.Concat(_sounds.Problems))
            {
                GD.Print($"  sound refused: {problem}");
            }

            return null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            UnloadMap();
            GD.PrintErr($"Couldn't load {name}: {e.Message}");
            return $"Couldn't load {name}:\n{e.Message}";
        }
    }

    private void UnloadMap()
    {
        _view?.QueueFree();
        _player?.QueueFree();
        _sounds?.QueueFree();
        _sounds = null;
        _view = null;
        _textures = null;
        _player = null;
        _map = null;
        _mapTitle = "";
        _status.Text = "";
    }

    /// <summary>
    /// The content folder beside the project, or the nearest one further up. A git worktree in
    /// <c>.claude/worktrees</c> has none of its own, because content isn't in git, so it uses the
    /// main folder's. Bakes still go in the worktree's own <c>cache/</c>.
    /// </summary>
    private static string FindContent(string root)
    {
        for (DirectoryInfo? dir = new(root); dir is not null; dir = dir.Parent)
        {
            string content = Path.Combine(dir.FullName, "content");
            if (Directory.Exists(Path.Combine(content, "maps")))
            {
                return content;
            }
        }

        return Path.Combine(root, "content");
    }

    private static (SysVector3 Position, float Yaw) FindSpawn(IReadOnlyList<BspEntity> entities, string? spawnFlag)
    {
        List<BspEntity> spawns = entities.Where(e => e.ClassName == "info_player_deathmatch").ToList();
        if (spawns.Count == 0)
        {
            spawns = entities.Where(e => e.ClassName == "info_player_start").ToList();
        }

        if (spawns.Count == 0)
        {
            return (SysVector3.Zero, 0f);
        }

        int index = int.TryParse(spawnFlag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int wanted)
            ? Math.Clamp(wanted, 0, spawns.Count - 1)
            : 0;

        BspEntity spawn = spawns[index];
        return (spawn.GetVector("origin") ?? SysVector3.Zero, spawn.GetFloat("angle") ?? 0f);
    }

    /// <summary>
    /// Proves input, simulation and map are joined up, with no window needed. Hops in a simulation
    /// of its own, built from the loaded map just as the player's was, so frames that pass while the
    /// map list is in use don't move the start.
    /// </summary>
    private bool RunSmokeTest()
    {
        var sim = new Simulation(new BspCollision(_map!.Collision), _settings);
        sim.AddPlayer(_spawn);
        for (int i = 0; i < SmokeTestTicks; i++)
        {
            sim.Tick([new MoveCommand(i * 0.5f, 0, 1, 0, Jump: true)]);
        }

        PlayerState s = sim.Players[0];
        GD.Print($"after {SmokeTestTicks} ticks of hopping: speed {s.FlatSpeed:F0}, at {s.Position}");
        return s.FlatSpeed > 1f;
    }

    /// <summary>Listens from the spawn, and plays one sound through the simulation's events, which needs no window or speakers.</summary>
    private bool CheckSounds()
    {
        (bool ok, string report) = _sounds!.Check(_spawn + (SysVector3.UnitZ * (PlayerHull.SpawnLift + PlayerHull.StandingEye)));
        GD.Print(report);
        return ok;
    }

    /// <summary>The smoke test the long way round: pick from the list, Esc menu, Map list, pick again.</summary>
    private async void RunListSmokeTest(string name)
    {
        bool ok = await PickFromList(name) && RunSmokeTest();
        if (ok)
        {
            OpenPauseMenu();
            BackToList();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ok = _menu.IsOpen && _player is null && await PickFromList(name) && RunSmokeTest();
        }

        GD.Print(ok ? "smoke test ok" : "smoke test failed");
        QuitGame(ok ? 0 : 1);
    }

    private async Task<bool> PickFromList(string name)
    {
        _menu.Search(name, select: name);
        GD.Print($"map list: searched for {name}, highlighted {_menu.Selected ?? "nothing"}");
        if (_menu.Selected != name)
        {
            return false;
        }

        _menu.ChooseSelected();
        return await _loading;
    }

    private async void SaveShot(string path)
    {
        for (int i = 0; i < FramesBeforeShot; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path} at {Engine.GetFramesPerSecond()} fps");
        QuitGame();
    }
}
