using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The rear-view mirror at the top of the windscreen: a housing on a stalk and a glass that shows the road
/// behind, rendered by a small camera of its own (the 1989 cabinet had one too). Lives on the player's car only.
/// </summary>
/// <remarks>
/// The mirror camera starts 2.7 m out, past the car's own tail, because from the driver's seat the shell is
/// not drawn and the cabin lining would otherwise fill the glass. It renders with a lighter environment
/// (<see cref="WorldBuilder.MirrorEnvironment"/>): no global illumination, screen-space effects or fog, which
/// keeps a second view of the world cheap at this size.
/// </remarks>
internal sealed partial class RearMirror : Node3D
{
    private static readonly Vector2I GlassPixels = new(512, 144);
    private const float GlassWidth = 0.21f;
    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;

    public static RearMirror Create(Material housing)
    {
        var mirror = new RearMirror { Name = "RearMirror" };
        mirror.Build(housing);
        return mirror;
    }

    private void Build(Material housing)
    {
        var glassHeight = GlassWidth * GlassPixels.Y / GlassPixels.X;
        // Stalk up to the roof, the housing, and the glass on its rear face (towards the driver, +z).
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.008f, Height = 0.07f, Material = housing }, Position = new Vector3(0f, 0.045f, -0.01f) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(GlassWidth + 0.02f, glassHeight + 0.02f, 0.03f), Material = housing } });

        _viewport = new SubViewport
        {
            Name = "MirrorView",
            Size = GlassPixels,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa2X,
        };
        AddChild(_viewport);
        _camera = new Camera3D { Fov = 26f, Near = 2.7f, Far = 1500f, Current = true, Environment = WorldBuilder.MirrorEnvironment() };
        _viewport.AddChild(_camera);

        // A mirror shows the scene left-right reversed: the glass samples the camera picture with u flipped.
        var glass = new StandardMaterial3D
        {
            AlbedoTexture = _viewport.GetTexture(),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Uv1Scale = new Vector3(-1f, 1f, 1f),
            Uv1Offset = new Vector3(1f, 0f, 0f),
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(GlassWidth, glassHeight), Material = glass },
            Position = new Vector3(0f, 0f, 0.016f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public override void _Process(double delta)
    {
        // Looking straight back along the car (+z in its frame), from the mirror's place, a touch down to the
        // road: the car's basis, not the mirror's, which is turned towards the driver.
        var car = GetParentNode3D()?.GlobalBasis ?? GlobalBasis;
        var basis = car * new Basis(Vector3.Up, Mathf.Pi) * new Basis(Vector3.Right, -0.03f);
        _camera.GlobalTransform = new Transform3D(basis, GlobalPosition);
    }

    /// <summary>Runs the mirror's camera only while the driver can look into it; from outside the glass keeps its last picture.</summary>
    public void SetActive(bool active)
    {
        _viewport.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
    }
}
