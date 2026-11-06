using System;
using Godot;
using SimpleFps.Core.Movement;
using SimpleFps.Game.World;
using SysVector3 = System.Numerics.Vector3;

namespace SimpleFps.Game.Player;

/// <summary>
/// The person at the keyboard. Turns input into a command each tick, hands it to the simulation,
/// and shows the result. Nothing here decides how the player moves.
/// </summary>
public partial class LocalPlayer : Node
{
    private const int PlayerIndex = 0;
    private const float DegreesPerPixel = 0.1f;
    private const float PitchLimit = 89f;

    private const float StepSettleRate = 14f;
    private const float CrouchSettleRate = 14f;

    private const float FlySpeed = 12f;
    private const float FlyFastMultiplier = 4f;

    private readonly Camera3D _camera = new() { Near = 0.05f, Far = 400f, Fov = 90f, Current = true };

    private Simulation _sim = null!;
    private SysVector3 _spawn;
    private float _spawnYaw;
    private float _yaw;
    private float _pitch;
    private PlayerState _before;
    private PlayerState _after;
    private float _stepOffset;
    private float _eye = PlayerHull.StandingEye;
    private bool _flying;

    /// <summary>Set while the tuning panel is open, so typing there doesn't move the player.</summary>
    public bool Paused { get; set; }

    private PlayerState Current => _after;

    public string Status
    {
        get
        {
            if (_flying)
            {
                return "flying (V to drop back in)";
            }

            PlayerState s = Current;
            string place = s.OnLadder ? "ladder" : s.WaterLevel >= 2 ? "swimming" : s.OnGround ? "ground" : "air";
            string squeeze = s.HalfWidth < PlayerHull.FullHalfWidth ? $", squeezed {PlayerHull.FullHalfWidth - s.HalfWidth:F1}" : "";
            return $"speed {s.FlatSpeed:F0}, {place}{squeeze}";
        }
    }

    public void Begin(Simulation sim, SysVector3 spawn, float yaw)
    {
        _sim = sim;
        _spawn = spawn;
        _spawnYaw = yaw;
        _yaw = yaw;
        sim.AddPlayer(spawn);
        _before = _after = sim.Players[PlayerIndex];
        AddChild(_camera);
        UpdateCamera(0f);
    }

    public void Respawn()
    {
        _sim.Teleport(PlayerIndex, _spawn + (SysVector3.UnitZ * PlayerHull.SpawnLift));
        _before = _after = _sim.Players[PlayerIndex];
        _yaw = _spawnYaw;
        _pitch = 0f;
        _flying = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                _yaw -= motion.Relative.X * DegreesPerPixel;
                _pitch = Mathf.Clamp(_pitch + (motion.Relative.Y * DegreesPerPixel), -PitchLimit, PitchLimit);
                break;

            case InputEventMouseButton { Pressed: true } when !Paused && Input.MouseMode != Input.MouseModeEnum.Captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;

            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape }:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                break;

            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.R }:
                Respawn();
                break;

            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.V }:
                ToggleFlying();
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_flying)
        {
            return;
        }

        _before = _after;
        _sim.Tick([ReadCommand()]);
        _after = _sim.Players[PlayerIndex];

        // Stairs lift the body in jumps. The camera catches up smoothly instead.
        float rise = _after.Position.Z - _before.Position.Z;
        if (_before.OnGround && _after.OnGround && MathF.Abs(rise) <= _sim.Settings.StepHeight)
        {
            _stepOffset -= rise;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_flying)
        {
            Fly(dt);
            ApplyLook();
            return;
        }

        _stepOffset *= MathF.Exp(-StepSettleRate * dt);
        _eye += (_after.EyeHeight - _eye) * (1f - MathF.Exp(-CrouchSettleRate * dt));
        UpdateCamera((float)Engine.GetPhysicsInterpolationFraction());
    }

    private static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);

    private MoveCommand ReadCommand()
    {
        if (Paused)
        {
            return new MoveCommand(_yaw, _pitch, 0, 0);
        }

        return new MoveCommand(
            _yaw,
            _pitch,
            Axis(Key.W, Key.S),
            Axis(Key.D, Key.A),
            Jump: Input.IsPhysicalKeyPressed(Key.Space),
            Crouch: Input.IsPhysicalKeyPressed(Key.Ctrl) || Input.IsPhysicalKeyPressed(Key.C),
            Attack: Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsMouseButtonPressed(MouseButton.Left));
    }

    private void UpdateCamera(float blend)
    {
        SysVector3 position = SysVector3.Lerp(_before.Position, _after.Position, blend);
        position.Z += _eye + _stepOffset;
        _camera.Position = MapView.ToGodot(position);
        ApplyLook();
    }

    /// <summary>A map's yaw 0 faces +X, while a Godot camera with no rotation faces -Z.</summary>
    private void ApplyLook() =>
        _camera.RotationDegrees = new Vector3(-_pitch, _yaw - 90f, 0f);

    private void ToggleFlying()
    {
        _flying = !_flying;
        if (!_flying)
        {
            SysVector3 position = MapView.ToMap(_camera.Position);
            position.Z -= PlayerHull.StandingEye;
            _sim.Teleport(PlayerIndex, position);
            _before = _after = _sim.Players[PlayerIndex];
        }
    }

    private void Fly(float dt)
    {
        if (Paused)
        {
            return;
        }

        var wish = new Vector3(Axis(Key.D, Key.A), Axis(Key.E, Key.Q), Axis(Key.S, Key.W));
        if (wish != Vector3.Zero)
        {
            float speed = Input.IsPhysicalKeyPressed(Key.Shift) ? FlySpeed * FlyFastMultiplier : FlySpeed;
            _camera.Position += _camera.Basis * wish.Normalized() * speed * dt;
        }
    }
}
