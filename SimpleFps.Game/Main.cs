using System;
using Godot;
using SimpleFps.Core;

namespace SimpleFps.Game;

/// <summary>Entry scene. Milestone 1 replaces the label with a map.</summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        string engine = Engine.GetVersionInfo()["string"].AsString();
        GD.Print($"{GameInfo.Title}: Godot {engine}, {GameInfo.TickRate} ticks a second");

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0)
        {
            GD.Print("smoke test ok");
            GetTree().Quit();
            return;
        }

        AddChild(new Label
        {
            Text = $"{GameInfo.Title}: setup works",
            Position = new Vector2(24, 24),
        });
    }
}
