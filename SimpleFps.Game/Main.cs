using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using SimpleFps.Core;
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
/// tuning panel in that picture)  --smoke-test
/// </summary>
public partial class Main : Node
{
    private const string DefaultMap = "q2duel5";
    private const string TuningFile = "user://tuning.json";
    private const int FramesBeforeShot = 150;
    private const int SmokeTestTicks = 240;

    private const string Controls =
        "WASD move, Space jump (hold to hop), Ctrl crouch, left mouse test recoil\n" +
        "Tab tuning panel, R respawn, V fly (Q/E down/up, Shift fast), Esc frees the mouse";

    private static readonly Color SkyColour = new(0.33f, 0.42f, 0.55f);

    private readonly TuningPanel _panel = new();
    private readonly MoveSettings _settings = LoadSettings();
    private Label _status = null!;
    private LocalPlayer? _player;
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

        bool loaded = TryShowMap(Flag(args, "--map=") ?? DefaultMap, Flag(args, "--spawn="), out Simulation? sim);

        if (args.Contains("--smoke-test"))
        {
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
    }

    public override void _Process(double delta)
    {
        if (_mapTitle.Length > 0)
        {
            _status.Text = $"{_mapTitle}, {Engine.GetFramesPerSecond()} fps\n{_player?.Status}\n{Controls}";
        }
    }

    private static string? Flag(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    private static MoveSettings LoadSettings()
    {
        string path = ProjectSettings.GlobalizePath(TuningFile);
        return FileAccess.Exists(path) ? MoveSettings.FromJson(FileAccess.ReadAllText(path)) : new MoveSettings();
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

    private bool TryShowMap(string name, string? spawnFlag, out Simulation? sim)
    {
        sim = null;
        string content = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "content"));
        string path = Path.Combine(content, "maps", name + ".bsp");

        try
        {
            ulong started = Time.GetTicksMsec();
            Q2Bsp bsp = Q2Bsp.Load(path);
            var textures = new WorldTextures(content);
            MapView view = MapView.Build(bsp, textures);
            AddChild(view);

            sim = new Simulation(new BspCollision(bsp), _settings);
            (SysVector3 spawn, float yaw) = FindSpawn(bsp, spawnFlag);
            _player = new LocalPlayer();
            AddChild(_player);
            _player.Begin(sim, spawn, yaw);

            ulong taken = Time.GetTicksMsec() - started;
            _mapTitle = name;
            GD.Print($"map {name}: {bsp.Faces.Length} faces, {view.SurfaceCount} surfaces, {textures.LoadedCount} textures, {textures.Missing.Count} missing, {taken} ms");
            foreach (string missing in textures.Missing)
            {
                GD.Print($"  missing texture: {missing}");
            }

            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            GD.PrintErr($"Couldn't load {path}: {e.Message}");
            _status.Text = $"Couldn't load {name}:\n{e.Message}";
            return false;
        }
    }

    private static (SysVector3 Position, float Yaw) FindSpawn(Q2Bsp bsp, string? spawnFlag)
    {
        List<BspEntity> spawns = bsp.Entities.Where(e => e.ClassName == "info_player_deathmatch").ToList();
        if (spawns.Count == 0)
        {
            spawns = bsp.Entities.Where(e => e.ClassName == "info_player_start").ToList();
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
