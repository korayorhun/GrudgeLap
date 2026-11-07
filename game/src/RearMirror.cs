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
    private bool _active = true;
    private bool _skipFrame;
    private readonly List<MeshInstance3D> _housing = [];
    private QuadMesh? _glassMesh;
    private Material? _picture;

    /// <summary>
    /// The glass seen from outside the car (SetActive false): silvered glass reflecting the car's probe. The camera's
    /// frozen picture is unshaded, and from the chase view, through the rear window, it glared as a white block on the
    /// tail on a dark, rainy day.
    /// </summary>
    private static readonly StandardMaterial3D Silvered = new()
    {
        AlbedoColor = new Color(0.55f, 0.56f, 0.58f),
        Metallic = 1f,
        Roughness = 0.06f,
    };

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
        _housing.Add(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.008f, Height = 0.07f, Material = housing }, Position = new Vector3(0f, 0.045f, -0.01f) });
        _housing.Add(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(GlassWidth + 0.02f, glassHeight + 0.02f, 0.03f), Material = housing } });
        foreach (var part in _housing) AddChild(part);

        _viewport = new SubViewport
        {
            Name = "MirrorView",
            Size = GlassPixels,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        AddChild(_viewport);
        _camera = new Camera3D { Fov = 26f, Near = 2.7f, Far = 65000f, Current = true, Environment = WorldBuilder.MirrorEnvironment(), CullMask = 0xFFFFFu & ~Windscreen.Layer };
        _viewport.AddChild(_camera);

        // A mirror shows the scene left-right reversed: the glass samples the camera picture with u flipped.
        var glass = new StandardMaterial3D
        {
            AlbedoTexture = _viewport.GetTexture(),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Uv1Scale = new Vector3(-1f, 1f, 1f),
            Uv1Offset = new Vector3(1f, 0f, 0f),
        };
        _picture = glass;
        _glassMesh = new QuadMesh { Size = new Vector2(GlassWidth, glassHeight), Material = glass };
        AddChild(new MeshInstance3D
        {
            Mesh = _glassMesh,
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
        // Every other frame: the mirror renders the world a second time, 2.5-3.5 ms of GPU a frame on the development
        // laptop, and a picture this small does not need the full frame rate.
        if (!_active) return;
        _skipFrame = !_skipFrame;
        if (!_skipFrame) _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    /// <summary>
    /// The glass made the size of a real car model's own mirror glass, which it covers (CarView.MountMirror); the
    /// picture keeps its pixel width and takes the glass's shape, so the road behind is not stretched.
    /// </summary>
    public void FitGlass(Vector2 size)
    {
        if (_glassMesh is null || size.X <= 0.01f || size.Y <= 0.005f) return;
        _glassMesh.Size = size;
        _viewport.Size = new Vector2I(GlassPixels.X, Math.Clamp((int)MathF.Round(GlassPixels.X * size.Y / size.X), 64, 256));
    }

    /// <summary>Only the glass: a real car model's own housing and stalk hold it (CarView.MountMirror).</summary>
    public void HideHousing()
    {
        foreach (var part in _housing) part.Visible = false;
    }

    /// <summary>Runs the mirror's camera only while the driver can look into it; from outside the glass is plain silvered glass.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        _viewport.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled;
        // Called every frame: only a change of view swaps the glass's material.
        var glass = active ? _picture : Silvered;
        if (_glassMesh is not null && _glassMesh.Material != glass) _glassMesh.Material = glass;
    }
}
