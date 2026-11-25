using System.Collections.Generic;
using System.Linq;
using Godot;
using SimpleFps.Core.Bake;
using SimpleFps.Core.Bsp;
using SysVector3 = System.Numerics.Vector3;

namespace SimpleFps.Game.World;

/// <summary>Draws a map. Everything here is display only; the game itself stays in map units.</summary>
public partial class MapView : Node3D
{
    /// <summary>32 map units to the metre keeps Godot's numbers at a human scale.</summary>
    public const float MapUnitsPerGodotUnit = 32f;

    private const float Trans33Opacity = 0.33f;
    private const float Trans66Opacity = 0.66f;

    private readonly List<(ShaderMaterial Material, string Texture)> _walls = new();
    private ShaderMaterial? _skyMaterial;

    public int SurfaceCount { get; private set; }

    /// <summary>The sky the map asks for, and its spin.</summary>
    public MapSky Sky { get; private set; } = null!;

    /// <summary>Where the sky's pictures came from, or null if they're missing or the map shows no sky.</summary>
    public string? SkySource { get; private set; }

    /// <summary>Quake 2 has Z up and Y to the left; Godot has Y up and -Z ahead.</summary>
    public static Vector3 ToGodotDirection(SysVector3 p) => new(p.X, p.Z, -p.Y);

    public static Vector3 ToGodot(SysVector3 p) => ToGodotDirection(p) / MapUnitsPerGodotUnit;

    public static SysVector3 ToMap(Vector3 p) => new SysVector3(p.X, -p.Z, p.Y) * MapUnitsPerGodotUnit;

    public static MapView Build(BakedMap map, WorldTextures textures)
    {
        MapGeometry geometry = map.Geometry;
        LightmapAtlas atlas = geometry.Lightmap;
        Image lightImage = Image.CreateFromData(atlas.Width, atlas.Height, false, Image.Format.Rgb8, atlas.Rgb);
        ImageTexture lightmap = ImageTexture.CreateFromImage(lightImage);

        Dictionary<int, SysVector3> origins = ModelOrigins(map.Entities);
        var view = new MapView { Sky = MapSky.From(map.Entities) };
        for (int model = 0; model < geometry.Models.Count; model++)
        {
            var holder = new Node3D { Name = model == 0 ? "World" : $"Model{model}" };
            if (origins.TryGetValue(model, out SysVector3 origin))
            {
                holder.Position = ToGodot(origin);
            }

            view.AddChild(holder);
            foreach (MeshBatch batch in geometry.Models[model].Where(b => b.Indices.Count > 0))
            {
                holder.AddChild(view.MakeSurface(batch, textures, lightmap));
                view.SurfaceCount++;
            }
        }

        view.Retexture(textures);
        return view;
    }

    /// <summary>
    /// Points every surface at the textures and sky of the set <paramref name="textures"/> is on now.
    /// Called again when the set changes. Texture coordinates stay as they are, because a high-res
    /// picture keeps its .wal's size.
    /// </summary>
    public void Retexture(WorldTextures textures)
    {
        foreach ((ShaderMaterial material, string texture) in _walls)
        {
            material.SetShaderParameter("albedo_map", textures.Get(texture).Texture);
        }

        if (_skyMaterial is null)
        {
            return;
        }

        (Cubemap Box, string Source)? sky = textures.Sky(Sky.Name);
        SkySource = sky?.Source;
        _skyMaterial.SetShaderParameter("has_box", sky is not null);
        if (sky is { } found)
        {
            _skyMaterial.SetShaderParameter("sky_box", found.Box);
        }
    }

    /// <summary>Doors and lifts are drawn relative to their entity's origin, when it has one.</summary>
    private static Dictionary<int, SysVector3> ModelOrigins(IEnumerable<BspEntity> entities)
    {
        var origins = new Dictionary<int, SysVector3>();
        foreach (BspEntity entity in entities)
        {
            string? model = entity["model"];
            if (model is ['*', ..] && int.TryParse(model[1..], out int index) && entity.GetVector("origin") is { } origin)
            {
                origins[index] = origin;
            }
        }

        return origins;
    }

    private MeshInstance3D MakeSurface(MeshBatch batch, WorldTextures textures, Texture2D lightmap)
    {
        bool sky = (batch.Flags & SurfaceFlags.Sky) != 0;

        // The sky doesn't use texture coordinates, so its pictures needn't be loaded here.
        Vector2 size = sky ? Vector2.One : textures.Get(batch.Texture).Size;

        int count = batch.Vertices.Count;
        var positions = new Vector3[count];
        var normals = new Vector3[count];
        var texCoords = new Vector2[count];
        var lightCoords = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            MapVertex v = batch.Vertices[i];
            positions[i] = ToGodot(v.Position);
            normals[i] = ToGodotDirection(v.Normal);
            texCoords[i] = new Vector2(v.TexCoord.X, v.TexCoord.Y) / size;
            lightCoords[i] = new Vector2(v.LightCoord.X, v.LightCoord.Y);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = texCoords;
        arrays[(int)Mesh.ArrayType.TexUV2] = lightCoords;
        arrays[(int)Mesh.ArrayType.Index] = batch.Indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, sky ? SkyMaterial() : WallMaterial(batch, lightmap));

        return new MeshInstance3D
        {
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private ShaderMaterial WallMaterial(MeshBatch batch, Texture2D lightmap)
    {
        bool glass = (batch.Flags & (SurfaceFlags.Trans33 | SurfaceFlags.Trans66)) != 0;
        var material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>(glass ? "res://World/world_glass.gdshader" : "res://World/world.gdshader"),
        };
        material.SetShaderParameter("light_map", lightmap);
        if (glass)
        {
            material.SetShaderParameter("opacity", (batch.Flags & SurfaceFlags.Trans33) != 0 ? Trans33Opacity : Trans66Opacity);
        }

        _walls.Add((material, batch.Texture));
        return material;
    }

    /// <summary>One material for every sky surface, since they all show the same box.</summary>
    private ShaderMaterial SkyMaterial()
    {
        if (_skyMaterial is null)
        {
            _skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/world_sky.gdshader") };
            if (Sky.Spins)
            {
                _skyMaterial.SetShaderParameter("spin_axis", ToGodotDirection(SysVector3.Normalize(Sky.Axis)));
                _skyMaterial.SetShaderParameter("spin_degrees_per_second", Sky.DegreesPerSecond);
            }
        }

        return _skyMaterial;
    }
}
