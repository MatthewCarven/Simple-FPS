using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using SimpleFps.Core;
using SimpleFps.Core.Bake;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;
using SimpleFps.Core.Movement;
using SimpleFps.Game.Player;
using SimpleFps.Game.Ui;
using SimpleFps.Game.World;
using FileAccess = System.IO.File;
using SysVector3 = System.Numerics.Vector3;

namespace SimpleFps.Game;

/// <summary>
/// Entry scene: one map, with you in it. Game flags go after a bare "--":
/// --map=name  --spawn=number  --shot=file.png (save a picture and quit)  --panel (open the
/// tuning panel in that picture)  --textures=original|highres (for this run only, not saved)
/// --smoke-test
/// </summary>
public partial class Main : Node
{
    private const string DefaultMap = "ringbase";
    private const string TuningFile = "user://tuning.json";
    private const string OptionsFile = "user://options.json";
    private const int FramesBeforeShot = 150;
    private const int SmokeTestTicks = 240;

    private const string Controls =
        "WASD move, Space jump (hold to hop), Ctrl crouch, left mouse test recoil\n" +
        "Tab tuning panel, R respawn, V fly (Q/E down/up, Shift fast), Esc frees the mouse (click to grab it again)\n" +
        "T flips between high-res and original textures";

    private static readonly Color SkyColour = new(0.33f, 0.42f, 0.55f);

    private readonly TuningPanel _panel = new();
    private readonly MoveSettings _settings = LoadSettings();
    private readonly PlayerOptions _options = LoadOptions();
    private Label _status = null!;
    private LocalPlayer? _player;
    private WorldTextures? _textures;
    private MapView? _view;
    private string _mapTitle = "";

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string engine = Engine.GetVersionInfo()["string"].AsString();
        GD.Print($"{GameInfo.Title}: Godot {engine}, {GameInfo.TickRate} ticks a second");

        Engine.PhysicsTicksPerSecond = GameInfo.TickRate;
        RenderingServer.SetDefaultClearColor(SkyColour);

        _status = new Label { Position = new Vector2(16, 12) };
        var overlay = new CanvasLayer();
        overlay.AddChild(_status);
        AddChild(overlay);

        _panel.Build(_settings);
        AddChild(_panel);

        TextureSet textureSet = Flag(args, "--textures=") switch
        {
            "original" => TextureSet.Original,
            "highres" => TextureSet.HighRes,
            _ => _options.HighResTextures ? TextureSet.HighRes : TextureSet.Original,
        };

        bool loaded = TryShowMap(Flag(args, "--map=") ?? DefaultMap, Flag(args, "--spawn="), textureSet, out Simulation? sim);

        if (args.Contains("--smoke-test"))
        {
            if (loaded)
            {
                // Both ways, so the second flip times the set that's already loaded.
                FlipTextures(save: false);
                FlipTextures(save: false);
            }

            GD.Print(loaded && RunSmokeTest(sim!) ? "smoke test ok" : "smoke test failed");
            GetTree().Quit(loaded ? 0 : 1);
            return;
        }

        if (Flag(args, "--shot=") is { } shot)
        {
            if (args.Contains("--panel"))
            {
                _panel.Toggle();
            }

            SaveShot(shot);
            return;
        }

        if (loaded)
        {
            // Grabbed from the start, so looking around works without a click first.
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Picked up here because a focused slider would otherwise swallow Tab.
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Tab })
        {
            ToggleTuning();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.T })
        {
            FlipTextures(save: true);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_mapTitle.Length > 0)
        {
            string textures = _textures?.Set == TextureSet.HighRes ? "high-res" : "original";
            _status.Text = $"{_mapTitle}, {Engine.GetFramesPerSecond()} fps, {textures} textures\n{_player?.Status}\n{Controls}";
        }
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
        _textures.Set = _textures.Set == TextureSet.HighRes ? TextureSet.Original : TextureSet.HighRes;
        _view.Retexture(_textures);
        bool highRes = _textures.Set == TextureSet.HighRes;
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

    private bool TryShowMap(string name, string? spawnFlag, TextureSet textureSet, out Simulation? sim)
    {
        sim = null;
        string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        string content = FindContent(root);
        var cache = new MapCache(Path.Combine(content, "maps"), Path.Combine(root, "cache", "maps"));

        try
        {
            ulong started = Time.GetTicksMsec();
            (BakedMap map, bool baked) = cache.Load(name);
            ulong mapTaken = Time.GetTicksMsec() - started;

            ulong texturesStarted = Time.GetTicksMsec();
            var textures = new WorldTextures(content, Path.Combine(root, "cache"), textureSet);
            MapView view = MapView.Build(map, textures);
            ulong texturesTaken = Time.GetTicksMsec() - texturesStarted;
            AddChild(view);
            _textures = textures;
            _view = view;

            sim = new Simulation(new BspCollision(map.Collision), _settings);
            (SysVector3 spawn, float yaw) = FindSpawn(map.Entities, spawnFlag);
            _player = new LocalPlayer();
            AddChild(_player);
            _player.Begin(sim, spawn, yaw);

            ulong taken = Time.GetTicksMsec() - started;
            _mapTitle = map.Info.Title is { } title ? $"{name}: {title}" : name;
            string from = baked ? "baked" : "loaded from cache";
            string set = textureSet == TextureSet.HighRes ? $"high-res ({textures.HighResCount} from the pack)" : "original";
            GD.Print($"map {name}: {from} in {mapTaken} ms, {view.SurfaceCount} surfaces, {textures.LoadedCount} textures {set}, {textures.Missing.Count} missing, {textures.BakedCount} baked, textures and meshes {texturesTaken} ms, {taken} ms in all");
            GD.Print($"  sky {view.Sky.Name}: {view.SkySource ?? "pictures missing, flat colour"}{(view.Sky.Spins ? $", spinning {view.Sky.DegreesPerSecond} degrees a second" : "")}");
            foreach (string missing in textures.Missing)
            {
                GD.Print($"  missing texture: {missing}");
            }

            foreach (string problem in textures.Problems.Concat(textures.OddShapes))
            {
                GD.Print($"  {problem}");
            }

            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            GD.PrintErr($"Couldn't load {name}: {e.Message}");
            _status.Text = $"Couldn't load {name}:\n{e.Message}";
            return false;
        }
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

    /// <summary>Proves input, simulation and map are joined up, with no window needed.</summary>
    private static bool RunSmokeTest(Simulation sim)
    {
        for (int i = 0; i < SmokeTestTicks; i++)
        {
            sim.Tick([new MoveCommand(i * 0.5f, 0, 1, 0, Jump: true)]);
        }

        PlayerState s = sim.Players[0];
        GD.Print($"after {SmokeTestTicks} ticks of hopping: speed {s.FlatSpeed:F0}, at {s.Position}");
        return s.FlatSpeed > 1f;
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
        GetTree().Quit();
    }
}
