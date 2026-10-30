using Godot;

namespace GrudgeLap.Game;

/// <summary>
/// The particles of the knocks the car survives: a burst of sparks off a hard one, and steam from under the bonnet
/// once the car is badly knocked about (past 60 % of a write-off), thickening as it gets worse, until the car is
/// repaired. The crash and its replay have their own (<see cref="CrashScene"/>).
/// </summary>
internal sealed partial class KnockSmoke : Node3D
{
    private readonly List<(CpuParticles3D Particles, double Until)> _bursts = [];
    private CpuParticles3D? _steam;
    private StandardMaterial3D? _spark;

    /// <summary>A burst of sparks at <paramref name="at"/> (world), thrown back along <paramref name="away"/>.</summary>
    public void Sparks(Vector3 at, Vector3 away, float speed)
    {
        _spark ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.72f, 0.35f),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        var particles = new CpuParticles3D
        {
            Name = "KnockSparks",
            Amount = Math.Clamp((int)(12f + speed * 3f), 12, 60),
            Lifetime = 0.35f,
            OneShot = true,
            Explosiveness = 0.95f,
            Mesh = new QuadMesh { Size = new Vector2(0.025f, 0.025f), Material = _spark },
            Direction = (away.Normalized() + Vector3.Up * 0.5f).Normalized(),
            Spread = 60f,
            InitialVelocityMin = 2f,
            InitialVelocityMax = 3f + speed * 0.3f,
            Gravity = new Vector3(0f, -9.8f, 0f),
        };
        AddChild(particles);
        particles.GlobalPosition = at;
        particles.Emitting = true;
        _bursts.Add((particles, Time.GetTicksMsec() / 1000.0 + 1.0));
    }

    /// <summary>Once a frame: the steam follows the car's bonnet while <paramref name="damage"/> is high; old bursts are freed.</summary>
    public void Follow(CarView car, float damage, bool muted)
    {
        var now = Time.GetTicksMsec() / 1000.0;
        for (var i = _bursts.Count - 1; i >= 0; i--)
        {
            if (now < _bursts[i].Until) continue;
            _bursts[i].Particles.QueueFree();
            _bursts.RemoveAt(i);
        }
        var steaming = !muted && damage > 0.6f;
        if (steaming && _steam is null) _steam = MakeSteam();
        if (_steam is null) return;
        _steam.Emitting = steaming;
        if (!steaming) return;
        // Out of the gap between the bonnet and the windscreen's foot, a little ahead of the centre of gravity.
        var (nose, _, _, _) = car.Extent;
        _steam.GlobalPosition = car.GlobalTransform * new Vector3(0f, 0.25f, nose * 0.55f);
        _steam.Amount = Math.Clamp((int)(10f + (damage - 0.6f) * 60f), 10, 34);
    }

    private CpuParticles3D MakeSteam()
    {
        // A soft round puff: white at the middle fading to nothing at the rim.
        var fade = new Gradient();
        fade.SetColor(0, new Color(1f, 1f, 1f, 1f));
        fade.SetColor(1, new Color(1f, 1f, 1f, 0f));
        var puff = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.93f, 0.94f, 0.96f, 0.12f),
            AlbedoTexture = new GradientTexture2D { Gradient = fade, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 64, Height = 64 },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        var grow = new Curve();
        grow.AddPoint(new Vector2(0f, 0.4f));
        grow.AddPoint(new Vector2(1f, 1.6f));
        var steam = new CpuParticles3D
        {
            Name = "KnockSteam",
            Amount = 12,
            Lifetime = 1.4f,
            Mesh = new QuadMesh { Size = new Vector2(0.55f, 0.55f), Material = puff },
            Direction = Vector3.Up,
            Spread = 25f,
            InitialVelocityMin = 0.6f,
            InitialVelocityMax = 1.4f,
            Gravity = new Vector3(0f, 0.6f, 0f),
            ScaleAmountCurve = grow,
            LocalCoords = false,
        };
        AddChild(steam);
        return steam;
    }
}
