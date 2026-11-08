using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The windscreen as the driver sees it (0.46, assets/shaders/windscreen.gdshader): the rain's drops on the glass and
/// the wipers sweeping them off, a film of water between the sweeps, the drops driven up the glass at speed; on a dry
/// day the glass's dust and the wipers' fine arcs lit into a haze with the sun ahead. A quad over the view, the
/// camera's child, drawn after everything else; the cabin is left alone by its depth. The cockpit view only: nothing in
/// the chase and replay views or in the 1989 look.
/// </summary>
/// <remarks>
/// Until 0.46 the glass was not drawn from the seat at all (CarView hides it), and the rain fell in the air before a
/// windscreen that stayed dry. The camera's motion blur runs before the transparent pass since 0.46, so the drops on
/// the glass stay sharp as the road behind them streams by.
/// </remarks>
internal sealed partial class Windscreen : MeshInstance3D
{
    private ShaderMaterial _material = null!;
    private float _wiperClock;
    private bool _wiping;
    private float _wiperPeriod = WiperSweep;

    /// <summary>A blade's sweep up and back, seconds; the wipers' sound (WorldAudio, tools/make-rain.py) is cut to it.</summary>
    public const float WiperSweep = 1.1f;

    /// <summary>
    /// The wipers now, for their sound (0.54, WorldAudio): whether they run before the driver, seconds since they started,
    /// and seconds from one sweep's start to the next. Off whenever the glass is not drawn (the chase and replay views,
    /// the 1989 look).
    /// </summary>
    public (bool On, float Clock, float Period) Wipers => (Visible && _wiping, _wiperClock, _wiperPeriod);

    /// <summary>
    /// The glass's own render layer (0.56): the quad covers the view of whatever camera draws it, so the rear-view mirror's
    /// camera and the car's reflection probe leave this layer out; the mirror showed the drops, the blades and the arcs.
    /// </summary>
    public const uint Layer = 1u << 10;

    /// <summary>Automation (--windscreen=0): no glass drawn before the driver, for comparisons.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>How hard it rains, 0 to 1 (the drops' rate; the wipers run while it rains).</summary>
    public float Rain { get; set; }

    /// <summary>The water left on the glass on a wet day without rain, 0 to 1: a few drops, no wipers.</summary>
    public float Wet { get; set; }

    public override void _Ready()
    {
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/windscreen.gdshader"), RenderPriority = 127 };
        Mesh = new QuadMesh { Size = Vector2.One };
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;
        Layers = Layer;
        // The shader puts the quad over the whole view; its bounds must never let the culling drop it.
        CustomAabb = new Aabb(new Vector3(-1e4f, -1e4f, -1e4f), new Vector3(2e4f, 2e4f, 2e4f));
        Position = new Vector3(0f, 0f, -0.5f);
        Visible = false;
    }

    /// <summary>
    /// Once a frame from the camera: whether the view is the driver's, the car's speed (m/s), the direction to the sun in
    /// the camera's frame and how much of the sun reaches the glass (0 under cloud or overcast).
    /// </summary>
    public void Update(bool cockpit, float speed, Vector3 sunInView, float sunShare, float dt)
    {
        // The sun more than 50 degrees off the view lights nothing on the glass the driver sees through.
        var glare = sunInView.Normalized().Z < -0.64f ? sunShare * 0.9f : 0f;
        Visible = Enabled && cockpit && (Rain > 0f || Wet > 0f || glare > 0.01f);
        if (!Visible) return;
        // The wipers run while it rains: steadily in a downpour, every few seconds in a light rain. Switched on, they
        // start with a sweep.
        var wiping = Rain > 0.02f;
        if (wiping && !_wiping) _wiperClock = 0f;
        _wiping = wiping;
        if (wiping) _wiperClock += dt;
        const float sweep = WiperSweep;
        var period = Rain >= 0.6f ? sweep : Mathf.Lerp(4.5f, sweep, Rain / 0.6f);
        _wiperPeriod = period;
        _material.SetShaderParameter("rain", Rain);
        _material.SetShaderParameter("wet", Wet);
        // Most of the glass beads in the rain; a wet day leaves a few drops of spray.
        _material.SetShaderParameter("drop_cover", Rain > 0f ? 0.85f : 0.12f);
        _material.SetShaderParameter("wipers", wiping);
        _material.SetShaderParameter("wiper_clock", _wiperClock);
        _material.SetShaderParameter("wiper_sweep", sweep);
        _material.SetShaderParameter("wiper_period", period);
        _material.SetShaderParameter("speed", speed);
        _material.SetShaderParameter("sun_view", sunInView);
        _material.SetShaderParameter("glare", glare);
        _material.SetShaderParameter("glint", WorldBuilder.GlassGlint);
        _material.SetShaderParameter("cabin_light", WorldBuilder.CabinLight);
    }
}
