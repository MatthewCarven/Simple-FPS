using System;
using Godot;

namespace SimpleFps.Game.Ui;

/// <summary>
/// The Esc menu in a map: Resume, Map list, Quit. Opening and closing it, and what each button
/// does, is up to <see cref="Main"/>.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private const int ButtonWidth = 240;

    private static readonly Color Shade = new(0f, 0f, 0f, 0.55f);

    private readonly Label _heading = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private Button _resume = null!;

    public event Action? ResumeChosen;
    public event Action? MapListChosen;
    public event Action? QuitChosen;

    public bool IsOpen => Visible;

    public void Build()
    {
        // Above the tuning panel and the status text.
        Layer = 2;
        Visible = false;

        var shade = new ColorRect { Color = Shade, MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);

        var panel = new PanelContainer();
        centre.AddChild(panel);

        var margin = new MarginContainer();
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            margin.AddThemeConstantOverride(side, 20);
        }

        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        _heading.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(_heading);
        _resume = AddButton(column, "Resume", () => ResumeChosen?.Invoke());
        AddButton(column, "Map list", () => MapListChosen?.Invoke());
        AddButton(column, "Quit", () => QuitChosen?.Invoke());
    }

    /// <summary>Shows the menu headed with the map's title, with Resume ready for Enter.</summary>
    public void Open(string heading)
    {
        _heading.Text = heading;
        Visible = true;
        _resume.GrabFocus();
    }

    public void Close() => Visible = false;

    private static Button AddButton(Container column, string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(ButtonWidth, 0) };
        button.Pressed += pressed;
        column.AddChild(button);
        return button;
    }
}
