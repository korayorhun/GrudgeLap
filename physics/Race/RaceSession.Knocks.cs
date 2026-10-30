using System.Numerics;
using GrudgeLap.Physics.Track;

namespace GrudgeLap.Physics.Race;

/// <summary>
/// The knocks: the car's plan against the solid props', the drones' and the cows'. A knock below the crash speed
/// pushes the car out of what it hit and bounces it off with an impulse (a little restitution, friction along the
/// face), dents it and adds to its damage; a knock above it, or the one that takes the damage to a write-off, is a
/// crash with its replay. Until 0.9.1 every touch of a solid prop or a drone was a crash and the car drove through cows.
/// </summary>
public sealed partial class RaceSession
{
    /// <summary>Knocks closing slower than this (m/s) are scrapes: they push the car out and leave no mark.</summary>
    private const float ScrapeSpeed = 1.2f;
    /// <summary>Damage from a knock just under the crash speed; it grows with the square of the speed (the energy).</summary>
    private const float DamageAtCrashSpeed = 0.4f;
    /// <summary>Friction between the body and what it scrapes along (painted steel on bark, brick, another car).</summary>
    private const float KnockFriction = 0.35f;
    /// <summary>A cow moos at most this often when the car keeps shoving it (s).</summary>
    private const float CowMooInterval = 2.5f;

    private float _lastKnockAt = float.NegativeInfinity;
    private float _lastKnockSpeed;

    /// <summary>
    /// How knocked about the car is, from 0 (sound) to 1 (a write-off). Past a third of the way it loses power (up to
    /// 30 %); at 1 it is a crash. A crash repairs it, when the car is set down after the replay.
    /// </summary>
    public float Damage { get; private set; }

    /// <summary>The last knock the car took and survived (<see cref="RaceEvent.Knocked"/>).</summary>
    public KnockInfo LastKnock { get; private set; }

    private void Repair()
    {
        Damage = 0f;
        Car.Drivetrain.Engine.PowerFactor = 1f;
    }

    /// <summary>A landing past <see cref="RaceOptions.LandingDamageSpeed"/> bottoms the car out; true when that wrecks it.</summary>
    private bool LandedHard()
    {
        var impact = Car.LandingImpact;
        if (impact <= Options.LandingDamageSpeed) return false;
        var share = (impact - Options.LandingDamageSpeed) / (Options.LandingCrashSpeed - Options.LandingDamageSpeed);
        // The front valance and the sump take it, low under the nose.
        var nose = Car.Position + Car.Forward * (Car.Spec.BodyFront * 0.8f) - Car.Up * 0.3f;
        return Take(0.5f * share, impact, nose, -Car.Up, KnockKind.Landing, -1);
    }

    /// <summary>Every knock of this step, solid props first; the crash it ends in, or none.</summary>
    private CrashReason Knocks()
    {
        var car = Car;
        // Upside down in the loop, or rolling: no plan on the ground, and nothing stands there.
        if (car.Up.Y < 0.5f) return CrashReason.None;
        var spec = car.Spec;
        var reach = MathF.Max(spec.BodyFront, spec.BodyRear) + 0.5f;
        Footprint Plan() => Footprint.Of(car.Position, car.Forward, spec.BodyFront, spec.BodyRear, spec.BodyHalfWidth);

        var props = Track.Props;
        for (var i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            if (!prop.Solid) continue;
            // Over it (on a bridge deck above a pier), or well below it.
            if (car.Position.Y > prop.Position.Y + prop.Size.Y || car.Position.Y < prop.Position.Y - 3f) continue;
            var dx = prop.Position.X - car.Position.X;
            var dz = prop.Position.Z - car.Position.Z;
            var near = reach + 0.71f * MathF.Max(prop.Size.X, prop.Size.Z);
            if (dx * dx + dz * dz > near * near) continue;
            if (!Plan().Overlaps(Footprint.OfProp(prop.Position, prop.YawDeg, prop.Size.X, prop.Size.Z), out var normal, out var depth, out var at)) continue;
            if (Knock(normal, depth, at, Vector3.Zero, 0f, 1f, Options.ImpactCrashSpeed, KnockKind.Solid, i, out _, out _))
            {
                return prop.Kind switch
                {
                    PropKind.Tree => CrashReason.HitTree,
                    PropKind.Train => CrashReason.HitTrain,
                    _ => CrashReason.HitBuilding,
                };
            }
        }

        if (Traffic is not null)
        {
            for (var i = 0; i < Traffic.Cars.Count; i++)
            {
                var drone = Traffic.Cars[i];
                var d = drone.Position - car.Position;
                if (d.LengthSquared() > 49f || MathF.Abs(Vector3.Dot(d, car.Up)) > 1.6f) continue;
                var plan = Footprint.Of(drone.Position, drone.Forward, Traffic.HalfLength, Traffic.HalfLength, Traffic.HalfWidth);
                if (!Plan().Overlaps(plan, out var normal, out var depth, out var at)) continue;
                // The drone keeps to its lane: the car takes all of the overlap, the drone only the change of speed.
                if (Knock(normal, depth, at, drone.Forward * drone.Speed, 1f / Traffic.Mass, 1f, Options.ImpactCrashSpeed, KnockKind.Drone, i, out var onDrone, out _))
                {
                    return CrashReason.HitTraffic;
                }
                Traffic.Knock(drone, onDrone);
            }
        }

        var carInverseMass = 1f / spec.MassKg;
        for (var i = 0; i < Herd.Cows.Count; i++)
        {
            var cow = Herd.Cows[i];
            var d = cow.Position - car.Position;
            if (d.X * d.X + d.Z * d.Z > (reach + 1.5f) * (reach + 1.5f) || MathF.Abs(d.Y) > 2.5f) continue;
            if (!Plan().Overlaps(Herd.Plan(cow), out var normal, out var depth, out var at)) continue;
            // The overlap is shared by mass: the car is pushed back less than the cow is shoved on.
            var carShare = carInverseMass / (carInverseMass + 1f / Herd.CowMass);
            var cowVelocity = new Vector3(cow.Velocity.X, 0f, cow.Velocity.Y);
            if (Knock(normal, depth, at, cowVelocity, 1f / Herd.CowMass, carShare, Options.CowCrashSpeed, KnockKind.Cow, i, out var onCow, out var closing))
            {
                return CrashReason.HitCow;
            }
            Herd.Displace(cow, new Vector3(normal.X, 0f, normal.Y) * (depth * (1f - carShare)));
            if (onCow.LengthSquared() > 1f) Herd.Knock(cow, onCow, new Vector3(at.X, 0f, at.Y), Elapsed);
            if (closing > 0.8f && !(_softHitTimes.TryGetValue(cow.Prop, out var last) && Elapsed - last < CowMooInterval))
            {
                _softHitTimes[cow.Prop] = Elapsed;
                LastSoftHit = cow.Prop;
                _events.Add(RaceEvent.CowHit);
            }
        }
        return CrashReason.None;
    }

    /// <summary>
    /// One knock between the car and something it overlaps on the ground. <paramref name="normal"/> points from the car
    /// into it, <paramref name="at"/> is where they touch; the other moves at <paramref name="otherVelocity"/> with
    /// <paramref name="otherInverseMass"/> (0 when it does not give), and the car takes <paramref name="carShare"/> of the
    /// overlap. Returns true when the knock wrecks the car: closing faster than <paramref name="crashSpeed"/>, or the
    /// damage it adds a write-off; the car is then left as it was for the crash and its replay. Otherwise the car is
    /// pushed out and bounced off, and <paramref name="onOther"/> is the impulse the other takes back (N s).
    /// </summary>
    private bool Knock(Vector2 normal, float depth, Vector2 at, Vector3 otherVelocity, float otherInverseMass, float carShare,
        float crashSpeed, KnockKind kind, int index, out Vector3 onOther, out float closing)
    {
        var car = Car;
        var n = new Vector3(normal.X, 0f, normal.Y);
        // At the height of the centre of gravity: the blow turns the car about its vertical axis, it does not trip it.
        var point = new Vector3(at.X, car.Position.Y, at.Y);
        var relative = car.VelocityAt(point) - otherVelocity;
        closing = Vector3.Dot(relative, n);
        onOther = Vector3.Zero;
        if (closing > crashSpeed) return true;
        var ratio = closing / crashSpeed;
        var harm = closing > ScrapeSpeed ? DamageAtCrashSpeed * ratio * ratio : 0f;
        if (Damage + harm >= 1f) return true;

        car.Displace(-n * (depth * carShare + 0.002f));
        if (closing <= 0f) return false;
        // A car's body crumples rather than bounces: some restitution at a parking knock, little at speed.
        var restitution = MathUtil.Lerp(0.3f, 0.08f, MathUtil.Clamp01(closing / 12f));
        var push = (1f + restitution) * closing / (car.InverseMassAlong(point, n) + otherInverseMass);
        var impulse = -n * push;
        var slide = relative - n * closing;
        slide.Y = 0f;
        var slideSpeed = slide.Length();
        if (slideSpeed > 1e-3f)
        {
            // Friction along the face, never more than stops the sliding.
            var t = slide / slideSpeed;
            impulse -= t * MathF.Min(KnockFriction * push, slideSpeed / (car.InverseMassAlong(point, t) + otherInverseMass));
        }
        car.ApplyImpulse(point, impulse);
        onOther = -impulse;
        if (harm > 0f) Take(harm, closing, point, n, kind, index);
        return false;
    }

    /// <summary>
    /// Adds <paramref name="harm"/> to the damage and reports the knock (at most every fifth of a second, unless a
    /// harder one follows). Returns true when the car is now a write-off.
    /// </summary>
    private bool Take(float harm, float speed, Vector3 point, Vector3 normal, KnockKind kind, int index)
    {
        Damage = MathF.Min(Damage + harm, 1f);
        // Past a third of the way to a write-off the engine loses power, up to 30 % (a cracked radiator, a bent intake).
        Car.Drivetrain.Engine.PowerFactor = 1f - 0.3f * MathUtil.Clamp01((Damage - 0.3f) / 0.7f);
        // A knock too light to show (a landing just past the limit) is not reported.
        if (harm >= 0.01f && (Elapsed - _lastKnockAt > 0.2f || speed > _lastKnockSpeed + 2f))
        {
            var inverse = Quaternion.Inverse(Car.Orientation);
            LastKnock = new KnockInfo(Vector3.Transform(point - Car.Position, inverse), Vector3.Transform(normal, inverse), speed, kind, index);
            _events.Add(RaceEvent.Knocked);
            _lastKnockAt = Elapsed;
            _lastKnockSpeed = speed;
        }
        return Damage >= 1f;
    }
}

/// <summary>What the car knocked into.</summary>
public enum KnockKind
{
    /// <summary>A solid prop: <see cref="KnockInfo.Index"/> is into <c>Track.Props</c>.</summary>
    Solid,
    /// <summary>A drone: the index is into <c>Traffic.Cars</c>.</summary>
    Drone,
    /// <summary>A cow: the index is into <c>Herd.Cows</c>.</summary>
    Cow,
    /// <summary>The ground, landing a jump hard: no index.</summary>
    Landing,
}

/// <summary>
/// A knock the car survived, in the car's own frame (x right, y up, z forward, origin at the centre of gravity):
/// where it was struck, the way the blow pushed in (from the car towards what it hit), and the closing speed (m/s).
/// </summary>
public readonly record struct KnockInfo(Vector3 Point, Vector3 Normal, float Speed, KnockKind Kind, int Index);
