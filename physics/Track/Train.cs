using System.Numerics;

namespace GrudgeLap.Physics.Track;

/// <summary>
/// The train of the original: a locomotive and a few wagons that cross the speed route at a level crossing
/// every so often. Kinematic: it runs along a straight line through the crossing at a steady speed, pauses out
/// of sight and comes round again. Solid to the car. The 1989 game drew no rails under it; since 0.9 the view
/// lays a railway along the line (<see cref="LineFrom"/> to <see cref="LineTo"/>).
/// </summary>
public sealed class Train
{
    public const float CarLength = 14f;
    public const float CarSpacing = 15.5f;
    public const float CarHalfWidth = 1.6f;
    public const float CarHeight = 4.2f;

    private readonly float _halfLength;
    private readonly float _pause;
    private float _clock;

    public Train(Vector3 crossing, Vector3 direction, float halfLength, float speed, int cars = 4, float pause = 18f, float phase = 0f)
    {
        Crossing = crossing;
        Direction = Vector3.Normalize(new Vector3(direction.X, 0f, direction.Z));
        _halfLength = halfLength;
        Speed = speed;
        _pause = pause;
        Cars = new Vector3[cars];
        _clock = phase;
        Step(0f);
    }

    /// <summary>Where the line meets the road.</summary>
    public Vector3 Crossing { get; }
    /// <summary>The stretch of the line the train is ever on, metres along <see cref="Direction"/> from the crossing: from the far end of the parked train to the locomotive at the end of its run.</summary>
    public float LineFrom => -_halfLength - (2 * Cars.Length - 1) * CarSpacing - CarLength;
    public float LineTo => _halfLength + CarLength;
    /// <summary>Unit direction of travel.</summary>
    public Vector3 Direction { get; }
    public float Speed { get; }
    /// <summary>Centre of each car, locomotive first; y is the ground level at the crossing (the view puts them on the terrain).</summary>
    public Vector3[] Cars { get; }
    /// <summary>Seconds of one run plus the pause.</summary>
    public float Cycle => (2f * _halfLength + Cars.Length * CarSpacing) / Speed + _pause;
    /// <summary>True while the train is on its run (not parked out of sight).</summary>
    public bool Running { get; private set; }
    /// <summary>Distance of the locomotive from the crossing along the line, negative while approaching.</summary>
    public float HeadOffset { get; private set; }

    public void Step(float dt)
    {
        _clock = (_clock + dt) % Cycle;
        var runSeconds = (2f * _halfLength + Cars.Length * CarSpacing) / Speed;
        Running = _clock < runSeconds;
        var head = -_halfLength - Cars.Length * CarSpacing + MathF.Min(_clock, runSeconds) * Speed;
        if (!Running) head = -_halfLength - Cars.Length * CarSpacing;   // parked beyond the start of the line
        HeadOffset = head;
        for (var i = 0; i < Cars.Length; i++)
        {
            Cars[i] = Crossing + Direction * (head - i * CarSpacing - CarLength * 0.5f);
        }
    }

    /// <summary>Seconds until the locomotive reaches the crossing on the current run (or the next one).</summary>
    public float SecondsToCrossing()
    {
        var runSeconds = (2f * _halfLength + Cars.Length * CarSpacing) / Speed;
        var arrival = (_halfLength + Cars.Length * CarSpacing) / Speed;
        if (_clock < arrival) return arrival - _clock;
        return Cycle - _clock + arrival;
    }

    /// <summary>True when a point (the car's centre, probed ahead and behind) is inside one of the wagons.</summary>
    public bool Hits(Vector3 point, float groundY)
    {
        if (!Running) return false;
        var right = new Vector3(-Direction.Z, 0f, Direction.X);
        foreach (var centre in Cars)
        {
            var d = point - centre;
            var along = Vector3.Dot(d, Direction);
            var across = Vector3.Dot(d, right);
            var up = point.Y - groundY;
            if (MathF.Abs(along) <= CarLength * 0.5f + 1.2f && MathF.Abs(across) <= CarHalfWidth + 0.9f && up > -1f && up < CarHeight) return true;
        }
        return false;
    }
}
