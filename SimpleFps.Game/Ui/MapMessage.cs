using Godot;
using SimpleFps.Core.Objects;

namespace SimpleFps.Game.Ui;

/// <summary>
/// A map's words for the player, such as "You have found a secret area" or "2 more to go...",
/// shown in the middle of the screen for a few seconds and then faded (Matthew, 2026-11-25).
/// </summary>
public partial class MapMessage : CanvasLayer
{
    private const double ShowSeconds = 3.0;
    private const double FadeSeconds = 0.5;

    /// <summary>A little above the middle, clear of where you aim.</summary>
    private const float AboveMiddle = 0.3f;

    private readonly Label _label = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private double _left;

    /// <summary>Which player's messages to show: the one at this keyboard.</summary>
    public int Player { get; set; }

    public string Text => _label.Text;

    public void Build()
    {
        // Under the tuning panel and the Esc menu, over the world.
        Layer = 1;
        _label.AnchorLeft = 0f;
        _label.AnchorRight = 1f;
        _label.AnchorTop = AboveMiddle;
        _label.AnchorBottom = AboveMiddle;
        _label.AddThemeFontSizeOverride("font_size", 22);
        _label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _label.AddThemeConstantOverride("shadow_offset_x", 2);
        _label.AddThemeConstantOverride("shadow_offset_y", 2);
        _label.Visible = false;
        AddChild(_label);
    }

    public void Handle(MapEvent e)
    {
        if (e.Kind == MapEventKind.Message && e.Player == Player && e.Text is { Length: > 0 } text)
        {
            Say(text);
        }
    }

    public void Say(string text)
    {
        GD.Print($"map message: {text.Replace('\n', ' ')}");
        _label.Text = text;
        _label.Modulate = Colors.White;
        _label.Visible = true;
        _left = ShowSeconds + FadeSeconds;
    }

    public void Clear()
    {
        _left = 0;
        _label.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_left <= 0)
        {
            return;
        }

        _left -= delta;
        _label.Visible = _left > 0;
        _label.Modulate = new Color(1, 1, 1, (float)Mathf.Clamp(_left / FadeSeconds, 0, 1));
    }
}
