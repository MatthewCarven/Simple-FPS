using System.Collections.Generic;
using Godot;
using SimpleFps.Core.Objects;
using SysVector3 = System.Numerics.Vector3;

namespace SimpleFps.Game.World;

/// <summary>
/// Our own marks for teleporters, since the original pad model isn't ours: a glowing disc with a
/// small soft light at each way in, and a plain dim disc at each way out (Matthew, 2026-11-25).
/// The light casts no shadow, following the plan's cheap lighting. Display only.
/// </summary>
public partial class TeleporterPads : Node3D
{
    /// <summary>The old pad was 64 units across, so ours is too.</summary>
    private const float RadiusMapUnits = 32f;

    /// <summary>Sits this far above the floor, clear of it.</summary>
    private const float LiftMapUnits = 0.5f;

    private const float LightRangeMapUnits = 128f;
    private const float LightHeightMapUnits = 20f;
    private const float LightEnergy = 3f;

    private static readonly Color Glow = new(0.35f, 0.85f, 1f);

    // Drawn added onto whatever is under it, so it reads as light rather than paint. The ring and
    // the soft middle pulse gently on the way in; the way out is a faint ring that holds still.
    private const string DiscShader = """
        shader_type spatial;
        render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

        uniform vec3 colour : source_color;
        uniform float strength = 1.0;
        uniform float pulse = 0.0;

        void fragment() {
            float r = length(UV - vec2(0.5)) * 2.0;
            if (r > 1.0) {
                discard;
            }

            float ring = smoothstep(0.72, 0.88, r) * (1.0 - smoothstep(0.94, 1.0, r));
            float middle = (1.0 - r) * 0.45;
            float beat = 1.0 - pulse + pulse * (0.5 + 0.5 * sin(TIME * 3.0));
            ALBEDO = colour * (ring + middle * beat) * strength;
        }
        """;

    private static Shader? _shader;

    private readonly Dictionary<int, Node3D> _ways = new();

    public int WaysIn { get; private set; }

    public int WaysOut { get; private set; }

    public static TeleporterPads Build(MapObjects objects)
    {
        var pads = new TeleporterPads { Name = "TeleporterPads" };
        var exits = new HashSet<int>();
        foreach (Teleporter teleporter in objects.Teleporters)
        {
            if (teleporter.Pad is { } pad)
            {
                Node3D wayIn = pads.Disc(pad, strength: 1f, pulse: 0.35f);
                wayIn.AddChild(new OmniLight3D
                {
                    LightColor = Glow,
                    LightEnergy = LightEnergy,
                    OmniRange = LightRangeMapUnits / MapView.MapUnitsPerGodotUnit,
                    Position = new Vector3(0, LightHeightMapUnits / MapView.MapUnitsPerGodotUnit, 0),
                    ShadowEnabled = false,
                });
                pads._ways[teleporter.Entity] = wayIn;
                pads.WaysIn++;
            }

            if (teleporter.ExitPad is { } exitPad && exits.Add(teleporter.Exit!.Entity))
            {
                pads._ways[teleporter.Exit.Entity] = pads.Disc(exitPad, strength: 0.2f, pulse: 0f);
                pads.WaysOut++;
            }
        }

        return pads;
    }

    /// <summary>Takes a pad away when a killtarget removes its teleporter or exit.</summary>
    public void Handle(MapEvent e)
    {
        if (e.Kind == MapEventKind.Removed && _ways.Remove(e.Entity, out Node3D? way))
        {
            way.QueueFree();
        }
    }

    private Node3D Disc(SysVector3 floor, float strength, float pulse)
    {
        _shader ??= new Shader { Code = DiscShader };
        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("colour", Glow);
        material.SetShaderParameter("strength", strength);
        material.SetShaderParameter("pulse", pulse);

        float size = RadiusMapUnits * 2f / MapView.MapUnitsPerGodotUnit;
        var disc = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = MapView.ToGodot(floor + new SysVector3(0, 0, LiftMapUnits)),
        };
        AddChild(disc);
        return disc;
    }
}
