using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The windscreen as the driver sees it (0.46, assets/shaders/windscreen.glsl): the rain's drops on the glass and the
/// wipers sweeping them off, a film of water between the sweeps, the drops driven up the glass at speed; on a dry day the
/// glass's dust and the wipers' fine arcs lit into a haze with the sun ahead. Drawn over the view by the camera's
/// compositor after the transparent pass (<see cref="WindscreenGlass"/>, 0.77; a quad in that pass until then, which saw
/// the scene as copied before it and so not the loop's grating); the cabin is left alone by its distance. The cockpit view
/// only: nothing in the chase and replay views or in the 1989 look.
/// </summary>
/// <remarks>
/// Until 0.46 the glass was not drawn from the seat at all (CarView hides it), and the rain fell in the air before a
/// windscreen that stayed dry. The camera's motion blur runs before the transparent pass since 0.46, so the drops on
/// the glass stay sharp as the road behind them streams by; since 0.77 what a drop shows of the road is blurred as the
/// road behind it is (Godot's screen texture of 0.46-0.76 was copied before the blur).
/// </remarks>
internal sealed partial class Windscreen : Node3D
{
    private WindscreenGlass? _distance;
    private WindscreenGlass? _glass;
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
    /// The glass's own render layer (0.56), which the rear-view mirror's camera and the car's reflection probe leave out:
    /// the mirror showed the drops, the blades and the arcs. Since 0.77 the glass is drawn by the cockpit camera's
    /// compositor alone, which neither has; nothing is on the layer.
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
        // The camera's compositor (CameraRig: the motion blur's) takes the glass's two passes after its own.
        if (GetParent() is Camera3D { Compositor: { } compositor })
        {
            _distance = WindscreenGlass.DistancePass();
            _glass = new WindscreenGlass();
            compositor.CompositorEffects = [.. compositor.CompositorEffects, _distance, _glass];
        }
        Visible = false;
        ShowPasses();
    }

    public override void _Notification(int what)
    {
        // The camera rig hides the glass in the chase and replay views without an update: the passes go with it.
        if (what == NotificationVisibilityChanged) ShowPasses();
    }

    public override void _ExitTree()
    {
        // The passes and their GPU objects go with the glass (WindscreenGlass.Release).
        if (GetParent() is Camera3D { Compositor: { } compositor })
        {
            var kept = new Godot.Collections.Array<CompositorEffect>();
            foreach (var effect in compositor.CompositorEffects)
            {
                if (effect != _distance && effect != _glass) kept.Add(effect);
            }
            compositor.CompositorEffects = kept;
        }
        _distance?.Release();
        _glass?.Release();
        _distance = null;
        _glass = null;
    }

    private void ShowPasses()
    {
        if (_distance is not null) _distance.Enabled = Visible;
        if (_glass is not null) _glass.Enabled = Visible;
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
        if (_glass is null) return;
        _glass.Rain = Rain;
        _glass.Wet = Wet;
        // Most of the glass beads in the rain; a wet day leaves a few drops of spray.
        _glass.DropCover = Rain > 0f ? 0.85f : 0.12f;
        _glass.Wipers = wiping;
        _glass.WiperClock = _wiperClock;
        _glass.WiperSweep = sweep;
        _glass.WiperPeriod = period;
        _glass.Speed = speed;
        _glass.SunView = sunInView;
        _glass.Glare = glare;
        _glass.Glint = WorldBuilder.GlassGlint;
        _glass.CabinLight = WorldBuilder.CabinLight;
    }
}
