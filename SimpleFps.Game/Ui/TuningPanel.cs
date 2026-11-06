using System.Collections.Generic;
using System.Globalization;
using Godot;
using SimpleFps.Core.Movement;

namespace SimpleFps.Game.Ui;

/// <summary>Live sliders for everything about how movement feels.</summary>
public partial class TuningPanel : CanvasLayer
{
    private const int PanelWidth = 430;
    private const int Margin = 12;
    private const int LabelWidth = 170;
    private const int ValueWidth = 56;

    private readonly PanelContainer _panel = new();
    private readonly List<(Tunable Tunable, HSlider Slider, Label Value)> _rows = new();
    private readonly MoveSettings _defaults = new();
    private MoveSettings _settings = null!;
    private CheckBox _crouchJump = null!;

    public bool IsOpen => _panel.Visible;

    public void Build(MoveSettings settings)
    {
        _settings = settings;

        _panel.Visible = false;
        _panel.AnchorLeft = 1f;
        _panel.AnchorRight = 1f;
        _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = -(PanelWidth + Margin);
        _panel.OffsetRight = -Margin;
        _panel.OffsetTop = Margin;
        _panel.OffsetBottom = -Margin;
        AddChild(_panel);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _panel.AddChild(scroll);

        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(rows);

        string group = "";
        foreach (Tunable tunable in MoveSettings.Tunables)
        {
            if (tunable.Group != group)
            {
                group = tunable.Group;
                rows.AddChild(new Label { Text = group, ThemeTypeVariation = "HeaderSmall" });
            }

            rows.AddChild(MakeRow(tunable));
        }

        _crouchJump = new CheckBox { Text = "Allow crouching in mid-air", ButtonPressed = settings.AllowCrouchJump };
        _crouchJump.Toggled += on => _settings.AllowCrouchJump = on;
        rows.AddChild(_crouchJump);

        var reset = new Button { Text = "Reset all to defaults" };
        reset.Pressed += Reset;
        rows.AddChild(reset);
    }

    public void Toggle() => _panel.Visible = !_panel.Visible;

    private static string Show(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private HBoxContainer MakeRow(Tunable tunable)
    {
        float current = tunable.Get(_settings);
        var label = new Label { Text = tunable.Label, CustomMinimumSize = new Vector2(LabelWidth, 0) };
        var value = new Label { Text = Show(current), CustomMinimumSize = new Vector2(ValueWidth, 0) };
        var slider = new HSlider
        {
            MinValue = tunable.Min,
            MaxValue = tunable.Max,
            Step = tunable.Step,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        slider.SetValueNoSignal(current);
        slider.ValueChanged += v =>
        {
            tunable.Set(_settings, (float)v);
            value.Text = Show((float)v);
        };

        _rows.Add((tunable, slider, value));

        var row = new HBoxContainer();
        row.AddChild(label);
        row.AddChild(slider);
        row.AddChild(value);
        return row;
    }

    private void Reset()
    {
        foreach ((Tunable tunable, HSlider slider, Label value) in _rows)
        {
            float original = tunable.Get(_defaults);
            tunable.Set(_settings, original);
            slider.SetValueNoSignal(original);
            value.Text = Show(original);
        }

        _settings.AllowCrouchJump = _defaults.AllowCrouchJump;
        _crouchJump.SetPressedNoSignal(_defaults.AllowCrouchJump);
    }
}
