using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using SimpleFps.Core;
using SimpleFps.Core.Bsp;
using SimpleFps.Game.Player;
using SimpleFps.Game.World;

namespace SimpleFps.Game;

/// <summary>
/// Entry scene. For now it shows one map from a fly camera. Game flags go after a bare "--":
/// --map=name  --spawn=number  --shot=file.png (save a picture and quit)  --smoke-test
/// </summary>
public partial class Main : Node
{
    private const string DefaultMap = "q2duel5";
    private const float EyeHeight = 22f;
    private const int FramesBeforeShot = 150;

    private static readonly Color SkyColour = new(0.33f, 0.42f, 0.55f);

    private Label _status = null!;
    private string _mapTitle = "";

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string engine = Engine.GetVersionInfo()["string"].AsString();
        GD.Print($"{GameInfo.Title}: Godot {engine}, {GameInfo.TickRate} ticks a second");

        RenderingServer.SetDefaultClearColor(SkyColour);
        _status = new Label { Position = new Vector2(16, 12) };
        var overlay = new CanvasLayer();
        overlay.AddChild(_status);
        AddChild(overlay);

        bool loaded = TryShowMap(Flag(args, "--map=") ?? DefaultMap, Flag(args, "--spawn="));

        if (args.Contains("--smoke-test"))
        {
            GD.Print(loaded ? "smoke test ok" : "smoke test failed");
            GetTree().Quit(loaded ? 0 : 1);
            return;
        }

        if (Flag(args, "--shot=") is { } shot)
        {
            SaveShot(shot);
        }
    }

    public override void _Process(double delta)
    {
        if (_mapTitle.Length > 0)
        {
            _status.Text = $"{_mapTitle}\n{Engine.GetFramesPerSecond()} fps\nClick to look, WASD fly, Q/E down/up, Shift fast, Esc frees the mouse";
        }
    }

    private static string? Flag(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    private bool TryShowMap(string name, string? spawnFlag)
    {
        string content = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "content"));
        string path = Path.Combine(content, "maps", name + ".bsp");

        try
        {
            ulong started = Time.GetTicksMsec();
            Q2Bsp bsp = Q2Bsp.Load(path);
            var textures = new WorldTextures(content);
            MapView view = MapView.Build(bsp, textures);
            AddChild(view);

            var camera = new FlyCamera();
            AddChild(camera);
            PlaceAtSpawn(camera, bsp, spawnFlag);

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

    private static void PlaceAtSpawn(FlyCamera camera, Q2Bsp bsp, string? spawnFlag)
    {
        List<BspEntity> spawns = bsp.Entities.Where(e => e.ClassName == "info_player_deathmatch").ToList();
        if (spawns.Count == 0)
        {
            spawns = bsp.Entities.Where(e => e.ClassName == "info_player_start").ToList();
        }

        if (spawns.Count == 0)
        {
            return;
        }

        int index = int.TryParse(spawnFlag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int wanted)
            ? Math.Clamp(wanted, 0, spawns.Count - 1)
            : 0;

        BspEntity spawn = spawns[index];
        System.Numerics.Vector3 origin = spawn.GetVector("origin") ?? System.Numerics.Vector3.Zero;
        origin.Z += EyeHeight;

        // A map's angle 0 faces +X, while a Godot camera with no rotation faces -Z.
        camera.Place(MapView.ToGodot(origin), (spawn.GetFloat("angle") ?? 0f) - 90f);
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
