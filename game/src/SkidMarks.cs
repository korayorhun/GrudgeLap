using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Race;
using GrudgeLap.Physics.Vehicle;
using SN = System.Numerics;

namespace GrudgeLap.Game;

/// <summary>
/// The rubber a sliding tyre leaves on the road (0.42): a wheel that slides on the asphalt (locked under the brakes,
/// spinning under power, or drifting sideways past its grip) lays a dark strip its own width behind it, darker the
/// harder it slides, and the strips stay for the rest of the session, as a road keeps them. Until 0.42 a stop from
/// 160 km/h or a spin left the road as clean as it was.
/// </summary>
/// <remarks>
/// One multimesh of unit quads, each stretched from the wheel's last contact to its present one, in a ring of
/// <see cref="Capacity"/> strips (the oldest give way); 1.5 cm over the road against fighting it for depth, alpha blended,
/// matte (0.70). The marks start at <see cref="Threshold"/> of the wheel's slip intensity (the squeal's measure), so a
/// tyre at the edge of its grip squeals before it marks. None in the 1989 look.
/// </remarks>
internal sealed partial class SkidMarks : MultiMeshInstance3D
{
    private const int Capacity = 6000;
    private const float Threshold = 0.32f;
    private const float Lift = 0.015f;
    private readonly Vector3?[] _last = new Vector3?[4];
    private int _next;
    private int _count;

    /// <summary>Automation (--pose-log): every 50th strip laid, in the log.</summary>
    public static bool Log { get; set; }

    public SkidMarks()
    {
        Name = "SkidMarks";
        CastShadow = ShadowCastingSetting.Off;
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new PlaneMesh { Size = Vector2.One },
            InstanceCount = Capacity,
            VisibleInstanceCount = 0,
        };
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.025f, 0.024f, 0.023f),
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            // Matte, reflecting nothing (0.70): 0.42 made them a little glossy (roughness 0.55 at the default specular),
            // and at a grazing angle they mirrored the sky and the sun, brighter than the rough road round them, so
            // from the seat they shone and at times read as white streaks. With no specular reflectance the Fresnel
            // term has nothing to raise either (Godot's f90 follows f0): the strips only darken the road.
            Roughness = 1f,
            MetallicSpecular = 0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Back,
        };
    }

    /// <summary>The strips of a new session's road: none.</summary>
    public void Clear()
    {
        _next = 0;
        _count = 0;
        Multimesh.VisibleInstanceCount = 0;
        System.Array.Fill(_last, null);
    }

    /// <summary>Once a frame: a strip from each sliding wheel's last contact to its present one.</summary>
    public void Update(RaceSession session, bool retro)
    {
        Visible = !retro;
        var car = session.Car;
        // Also through the wreck's slide after a crash (the replay phase's first seconds, simulated); the replay itself moves no wheel.
        // Not on the loop's open steel grating (0.72): a strip would hang over its holes.
        var marking = !retro && !(car.BodyHit is { HasSurface: true } hit && WorldBuilder.IsGrating(hit.Frame));
        for (var i = 0; i < car.Wheels.Length && i < _last.Length; i++)
        {
            var w = car.Wheels[i];
            var intensity = w.SlipIntensity;
            if (!marking || !w.InContact || w.Surface != SurfaceType.Road || intensity < Threshold)
            {
                _last[i] = null;
                continue;
            }
            var normal = Convert.ToGodot(w.ContactNormal).Normalized();
            var point = Convert.ToGodot(w.ContactPoint) + normal * Lift;
            if (_last[i] is not { } from)
            {
                _last[i] = point;
                continue;
            }
            var step = point - from;
            var length = step.Length();
            if (length < 0.2f) continue;
            if (length > 4f)
            {
                // A jump in the contact (the wheel left the road between frames, or the car was put back): start again.
                _last[i] = point;
                continue;
            }
            var tyre = w.IsFront ? car.Spec.FrontTire : car.Spec.RearTire;
            var width = tyre.WidthMm / 1000f * 0.9f;
            var alpha = MathUtil.Clamp01((intensity - Threshold) / (1f - Threshold)) * 0.75f + 0.12f;
            Add(from, point, normal, width, alpha);
            _last[i] = point;
        }
    }

    private void Add(Vector3 from, Vector3 to, Vector3 normal, float width, float alpha)
    {
        var along = to - from;
        var length = along.Length();
        var direction = along / length;
        var side = normal.Cross(direction).Normalized();
        var up = direction.Cross(side).Normalized();
        // The quad is a unit square in its x-z plane: x across the strip, z along it (a little over, so strips meet).
        var basis = new Basis(side * width, up, direction * (length + 0.04f));
        Multimesh.SetInstanceTransform(_next, new Transform3D(basis, (from + to) * 0.5f));
        Multimesh.SetInstanceColor(_next, new Color(1f, 1f, 1f, alpha));
        _next = (_next + 1) % Capacity;
        _count = System.Math.Min(_count + 1, Capacity);
        if (Log && _count % 50 == 0) GD.Print($"skid marks: {_count} strips, the last at {to.X:0}, {to.Z:0}");
        Multimesh.VisibleInstanceCount = _count;
    }
}
