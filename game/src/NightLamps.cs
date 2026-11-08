using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The lamps of the night (0.58): the glow a lit lamp shows (assets/shaders/lamp_glow.gdshader) and the farms' yard lamps.
/// The cars' lamps are CarView.Headlights, the train's TrainView.Night. Everything here is made hidden and shown only at
/// night (<see cref="Night"/>); the world's export leaves it out (glTF carries no shader: the glows went out as white cards).
/// </summary>
internal static class NightLamps
{
    private static Shader? _glow;
    private static readonly Dictionary<(Color, float), ShaderMaterial> _materials = [];
    private static readonly List<Node3D> _yardLit = [];
    private static StandardMaterial3D? _pole;
    private static StandardMaterial3D? _head;
    private static bool _night;

    /// <summary>The sodium lamp's colour: the 1980s farm's dusk-to-dawn lamp, orange (about 2000 K).</summary>
    public static readonly Color Sodium = new(1f, 0.62f, 0.28f);

    /// <summary>
    /// A lamp's glow: a card <paramref name="size"/> metres across, turned to the camera, shining the way the node's -Z
    /// faces, <paramref name="energy"/> times <paramref name="colour"/> at its heart (past 1 it blooms).
    /// </summary>
    public static MeshInstance3D Glow(Color colour, float energy, float size)
    {
        _glow ??= GD.Load<Shader>("res://assets/shaders/lamp_glow.gdshader");
        // One material for each colour and strength: forty-odd lamps at night share a handful.
        if (!_materials.TryGetValue((colour, energy), out var material))
        {
            material = new ShaderMaterial { Shader = _glow };
            material.SetShaderParameter("colour", colour);
            material.SetShaderParameter("energy", energy);
            _materials[(colour, energy)] = material;
        }
        return new MeshInstance3D
        {
            Name = "Glow",
            Mesh = new QuadMesh { Size = new Vector2(size, size) },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
    }

    /// <summary>
    /// A farm's yard lamp at <paramref name="at"/> (the building's frame): a wooden pole 7 m high with a lamp head on an
    /// arm, standing by day too; at night a sodium lamp lights the yard 30 m round, without shadows (a lamp over a farmyard
    /// lights the barn's face and the ground; a shadow cube a lamp cost too much for what it shows).
    /// </summary>
    public static Node3D YardLamp(Vector3 at)
    {
        _pole ??= new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.25f, 0.20f), Roughness = 0.9f };
        _head ??= new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.56f, 0.58f), Roughness = 0.5f, Metallic = 0.6f };
        var lamp = new Node3D { Name = "YardLamp", Position = at };
        lamp.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.12f, Height = 7f, Material = _pole }, Position = new Vector3(0f, 3.5f, 0f) });
        lamp.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, 1.1f), Material = _head }, Position = new Vector3(0f, 6.7f, 0.5f) });
        lamp.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.18f, 0.45f), Material = _head }, Position = new Vector3(0f, 6.6f, 1.05f) });
        if (WorldBuilder.ExportMode) return lamp;
        var lit = new Node3D { Name = "Lit", Visible = _night };
        // The refractor glows all round: a material of its own, not the cars' shared ones.
        var glow = Glow(Sodium, 7f, 1.3f);
        var refractor = (ShaderMaterial)glow.MaterialOverride.Duplicate();
        refractor.SetShaderParameter("directional", 0f);
        glow.MaterialOverride = refractor;
        glow.Position = new Vector3(0f, 6.45f, 1.05f);
        lit.AddChild(glow);
        lit.AddChild(new OmniLight3D
        {
            Name = "Light",
            Position = new Vector3(0f, 6.3f, 1.05f),
            LightColor = Sodium,
            LightEnergy = 3f,
            OmniRange = 30f,
            OmniAttenuation = 1.2f,
            ShadowEnabled = false,
        });
        lamp.AddChild(lit);
        _yardLit.Add(lit);
        return lamp;
    }

    /// <summary>Night or day for the yard lamps: lit at night, the 1989 look aside.</summary>
    public static bool Night
    {
        get => _night;
        set
        {
            if (value == _night) return;
            _night = value;
            foreach (var lit in _yardLit)
            {
                if (GodotObject.IsInstanceValid(lit)) lit.Visible = value;
            }
        }
    }

    /// <summary>Drops the lamps and the shared shader and materials (a new world, or the game quits).</summary>
    public static void ReleaseShared()
    {
        _yardLit.Clear();
        _materials.Clear();
        _glow = null;
        _pole = null;
        _head = null;
        _night = false;
    }
}
