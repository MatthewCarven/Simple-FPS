using Godot;

namespace SimpleFps.Game.Player;

/// <summary>A free-flying view for looking round maps, until real movement arrives in milestone 2.</summary>
public partial class FlyCamera : Camera3D
{
    private const float DegreesPerPixel = 0.1f;
    private const float PitchLimit = 89f;
    private const float Speed = 12f;
    private const float FastMultiplier = 4f;

    private float _yaw;
    private float _pitch;

    public void Place(Vector3 position, float yawDegrees)
    {
        Position = position;
        _yaw = yawDegrees;
        _pitch = 0f;
        ApplyLook();
    }

    public override void _Ready()
    {
        Near = 0.05f;
        Far = 400f;
        Fov = 90f;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                _yaw -= motion.Relative.X * DegreesPerPixel;
                _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * DegreesPerPixel, -PitchLimit, PitchLimit);
                ApplyLook();
                break;

            case InputEventMouseButton { Pressed: true }:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;

            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                break;
        }
    }

    public override void _Process(double delta)
    {
        var wish = new Vector3(
            Axis(Key.D, Key.A),
            Axis(Key.E, Key.Q),
            Axis(Key.S, Key.W));

        if (wish == Vector3.Zero)
        {
            return;
        }

        float speed = Input.IsPhysicalKeyPressed(Key.Shift) ? Speed * FastMultiplier : Speed;
        Position += Basis * wish.Normalized() * speed * (float)delta;
    }

    private static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);

    private void ApplyLook() =>
        RotationDegrees = new Vector3(_pitch, _yaw, 0f);
}
