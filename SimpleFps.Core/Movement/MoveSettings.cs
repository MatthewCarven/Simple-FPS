using System.Text.Json;

namespace SimpleFps.Core.Movement;

/// <summary>Everything about how movement feels. Speeds are in map units a second.</summary>
public sealed class MoveSettings
{
    public float Gravity { get; set; } = 800f;
    public float RunSpeed { get; set; } = 300f;
    public float CrouchSpeed { get; set; } = 100f;
    public float Accelerate { get; set; } = 10f;
    public float Friction { get; set; } = 6f;
    public float StopSpeed { get; set; } = 100f;
    public float JumpSpeed { get; set; } = 270f;
    public float StepHeight { get; set; } = 18f;

    /// <summary>How far your heading can drift in the air, in degrees a second. Never adds speed.</summary>
    public float AirSteer { get; set; } = 30f;

    /// <summary>The most speed your feet can give you in mid-air, enough to nudge onto a ledge.</summary>
    public float AirNudgeSpeed { get; set; } = 30f;

    /// <summary>Ticks on the ground between landing and rebounding while jump is held.</summary>
    public float HopTouchTicks { get; set; } = 3f;

    /// <summary>Most you can turn on each tick of that touch, in degrees.</summary>
    public float HopTurnPerTick { get; set; } = 15f;

    /// <summary>Share of your speed lost by turning a full 180 degrees. Smaller turns cost less.</summary>
    public float HopTurnCost { get; set; } = 0.6f;

    /// <summary>How far sideways you can be eased round a corner you've clipped.</summary>
    public float CornerSlideReach { get; set; } = 8f;

    /// <summary>How fast that easing happens, in units a second.</summary>
    public float CornerSlideSpeed { get; set; } = 300f;

    /// <summary>How many units narrower each side of your body can get.</summary>
    public float SqueezeAmount { get; set; } = 4f;

    /// <summary>How fast your body narrows and springs back, in units a second.</summary>
    public float SqueezeSpeed { get; set; } = 60f;

    /// <summary>Lets you crouch in mid-air to fit low openings. Off, as agreed in the plan.</summary>
    public bool AllowCrouchJump { get; set; }

    /// <summary>Recoil can't push you past this speed in the direction it's pushing.</summary>
    public float RecoilTopSpeed { get; set; } = 700f;

    /// <summary>Speed one shot of the test gun adds, until real weapons arrive.</summary>
    public float TestRecoilKick { get; set; } = 40f;

    /// <summary>Ticks between shots of the test gun.</summary>
    public float TestFireTicks { get; set; } = 6f;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Reads saved settings, falling back to the defaults if they're unreadable.</summary>
    public static MoveSettings FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<MoveSettings>(json) ?? new MoveSettings();
        }
        catch (JsonException)
        {
            return new MoveSettings();
        }
    }

    /// <summary>The settings shown on the tuning panel.</summary>
    public static IReadOnlyList<Tunable> Tunables { get; } =
    [
        new("Hopping", "Air steering", s => s.AirSteer, (s, v) => s.AirSteer = v, 0, 180, 1),
        new("Hopping", "Touch length (ticks)", s => s.HopTouchTicks, (s, v) => s.HopTouchTicks = v, 0, 12, 1),
        new("Hopping", "Turn per tick", s => s.HopTurnPerTick, (s, v) => s.HopTurnPerTick = v, 0, 60, 1),
        new("Hopping", "Turn speed cost", s => s.HopTurnCost, (s, v) => s.HopTurnCost = v, 0, 1, 0.05f),
        new("Wall grace", "Corner slide reach", s => s.CornerSlideReach, (s, v) => s.CornerSlideReach = v, 0, 24, 1),
        new("Wall grace", "Corner slide speed", s => s.CornerSlideSpeed, (s, v) => s.CornerSlideSpeed = v, 50, 900, 10),
        new("Wall grace", "Squeeze amount", s => s.SqueezeAmount, (s, v) => s.SqueezeAmount = v, 0, 10, 0.5f),
        new("Wall grace", "Squeeze speed", s => s.SqueezeSpeed, (s, v) => s.SqueezeSpeed = v, 10, 300, 5),
        new("Recoil test (left mouse)", "Kick per shot", s => s.TestRecoilKick, (s, v) => s.TestRecoilKick = v, 0, 200, 5),
        new("Recoil test (left mouse)", "Ticks between shots", s => s.TestFireTicks, (s, v) => s.TestFireTicks = v, 1, 30, 1),
        new("Recoil test (left mouse)", "Recoil top speed", s => s.RecoilTopSpeed, (s, v) => s.RecoilTopSpeed = v, 100, 2000, 25),
        new("Running", "Run speed", s => s.RunSpeed, (s, v) => s.RunSpeed = v, 100, 600, 10),
        new("Running", "Acceleration", s => s.Accelerate, (s, v) => s.Accelerate = v, 1, 30, 0.5f),
        new("Running", "Friction", s => s.Friction, (s, v) => s.Friction = v, 0, 12, 0.25f),
        new("Running", "Jump speed", s => s.JumpSpeed, (s, v) => s.JumpSpeed = v, 100, 500, 5),
        new("Running", "Gravity", s => s.Gravity, (s, v) => s.Gravity = v, 200, 1600, 10),
    ];
}

/// <summary>One slider on the tuning panel.</summary>
public sealed record Tunable(
    string Group,
    string Label,
    Func<MoveSettings, float> Get,
    Action<MoveSettings, float> Set,
    float Min,
    float Max,
    float Step);
