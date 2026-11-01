using System.Numerics;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Audio.Tests;

/// <summary>
/// The tyres' sound follows the simulated tyres (0.19): a squeal that grows as a tyre nears its force peak and gives
/// way to a howl or a scrub past it, a locked wheel's scrub against the chattering squeal of anti-lock brakes on the
/// simulation's own stops, water that silences the squeal, a roar that grows by 30 dB a decade of speed.
/// </summary>
public class TyreMixTests(ITestOutputHelper output)
{
    private const float Dt = 1f / 60f;

    /// <summary>Four tyres alike, held for two seconds so the levels settle.</summary>
    private static TyreMix Hold(TyreContact wheel, float wetness = 0f, float seconds = 2f)
    {
        var mix = new TyreMix();
        var wheels = new[] { wheel, wheel, wheel, wheel };
        for (var t = 0f; t < seconds; t += Dt) mix.Update(wheels, wetness, Dt);
        return mix;
    }

    /// <summary>A tyre at <paramref name="toPeak"/> of its peak slip, sliding sideways (cornering) at 25 m/s.</summary>
    private static TyreContact Cornering(float toPeak) =>
        TyreContact.Of(TyreGround.Asphalt, toPeak, 0f, 25f * 0.07f * toPeak, 25f, 1f, 1f);

    [Fact]
    public void Squeal_grows_to_the_tyres_limit_and_gives_way_to_a_howl_past_it()
    {
        var grip = Hold(Cornering(0.4f));
        var near = Hold(Cornering(0.8f));
        var limit = Hold(Cornering(1.05f));
        var sliding = Hold(Cornering(4f));
        output.WriteLine($"squeal 0.4: {grip.Squeal.Level:0.00}, 0.8: {near.Squeal.Level:0.00}, 1.05: {limit.Squeal.Level:0.00}, 4: {sliding.Squeal.Level:0.00}; howl 1.05: {limit.Howl.Level:0.00}, 4: {sliding.Howl.Level:0.00}");
        Assert.True(grip.Squeal.Level < 0.02f, "a gripping tyre squeals");
        Assert.InRange(near.Squeal.Level, 0.2f, limit.Squeal.Level * 0.95f);
        Assert.True(limit.Squeal.Level > 0.9f, "a tyre at its limit is quiet");
        Assert.True(limit.Howl.Level < 0.05f, "the howl starts before the tyre slides");
        // Sliding sideways far past the peak: the howl has taken over, the tonal squeal has fallen away.
        Assert.True(sliding.Howl.Level > sliding.Squeal.Level * 2f);
        Assert.True(sliding.Squeal.Level < limit.Squeal.Level * 0.3f);
        Assert.True(sliding.Scrub.Level < 0.05f, "a sideways slide is not a locked wheel's scrub");
        Assert.True(limit.Squeal.Pitch > near.Squeal.Pitch, "the squeal climbs as the slip deepens");
    }

    /// <summary>
    /// The simulation's own stops from 100 km/h, pedal hard down. The estate has no anti-lock brakes: its wheels lock
    /// (the front pair at once) and scrub, and the squeal is gone. The V8 has them: the modulator holds every tyre at its
    /// peak, so it squeals instead of scrubbing, chattering with the valves, and the pump runs.
    /// </summary>
    [Fact]
    public void Locked_wheels_scrub_where_anti_lock_brakes_squeal_and_chatter()
    {
        var locked = Stop(VehicleSpec.Estate, Weather.Dry);
        var abs = Stop(VehicleSpec.V8Evo, Weather.Dry);
        output.WriteLine($"locked: scrub {locked.Scrub:0.00} squeal {locked.Squeal:0.00} abs {locked.Abs:0.00}; ABS: scrub {abs.Scrub:0.00} squeal {abs.Squeal:0.00} (chatter {abs.SquealLow:0.00}-{abs.SquealHigh:0.00}) abs {abs.Abs:0.00}");
        Assert.True(locked.Scrub > 0.6f, "locked wheels hardly scrub");
        Assert.True(locked.Squeal < locked.Scrub * 0.3f, "a locked wheel still squeals");
        Assert.Equal(0f, locked.Abs);
        Assert.True(abs.Squeal > 0.5f, "tyres held at their peak are quiet");
        Assert.True(abs.Scrub < abs.Squeal * 0.3f, "the anti-lock stop scrubs like a locked one");
        Assert.True(abs.Abs > 0.9f, "the modulator's pump does not run");
        Assert.True(abs.SquealHigh > abs.SquealLow * 1.8f, "the squeal does not chatter with the valves");
    }

    [Fact]
    public void A_wet_road_silences_the_squeal_and_softens_the_scrub()
    {
        var dry = Stop(VehicleSpec.V8Evo, Weather.Dry);
        var wet = Stop(VehicleSpec.V8Evo, Weather.Rain);
        var lockedDry = Stop(VehicleSpec.Estate, Weather.Dry);
        var lockedWet = Stop(VehicleSpec.Estate, Weather.Rain);
        output.WriteLine($"squeal dry {dry.Squeal:0.00} wet {wet.Squeal:0.00}; scrub dry {lockedDry.Scrub:0.00} wet {lockedWet.Scrub:0.00}, spray {lockedWet.Spray:0.00}");
        Assert.True(wet.Squeal < dry.Squeal * 0.3f);
        Assert.InRange(lockedWet.Scrub, lockedDry.Scrub * 0.3f, lockedDry.Scrub * 0.7f);
        Assert.True(lockedWet.Spray > 0.2f, "a wet road throws no spray");
        Assert.Equal(0f, lockedDry.Spray);
    }

    [Fact]
    public void The_roar_grows_thirty_decibels_a_decade_of_speed_and_stops_in_the_air()
    {
        var slow = Hold(TyreContact.Of(TyreGround.Asphalt, 0.1f, 0f, 0f, 15f, 1f, 1f), seconds: 4f);
        var fast = Hold(TyreContact.Of(TyreGround.Asphalt, 0.1f, 0f, 0f, 30f, 1f, 1f), seconds: 4f);
        var air = Hold(TyreContact.Of(TyreGround.Air, 0f, 0f, 0f, 30f, 0f, 1f), seconds: 4f);
        var db = Spectrum.Db(fast.Roll.Level / slow.Roll.Level);
        output.WriteLine($"roar 15 m/s {slow.Roll.Level:0.000}, 30 m/s {fast.Roll.Level:0.000}: {db:0.0} dB a doubling");
        Assert.InRange(db, 8.6, 9.5);
        Assert.InRange(fast.Roll.Level, TyreMix.RollGain * 0.98f, TyreMix.RollGain * 1.02f);
        Assert.True(air.Roll.Level < 1e-3f);
        Assert.True(fast.Roll.Pitch > slow.Roll.Pitch);
    }

    /// <summary>Tyres sound as independent sources: an axle at its limit is 3 dB over one tyre, all four 3 dB more.</summary>
    [Fact]
    public void Tyres_add_as_independent_sources()
    {
        var limit = Cornering(1.05f);
        var quiet = Cornering(0.2f);
        float Level(int squealing)
        {
            var mix = new TyreMix();
            var wheels = Enumerable.Range(0, 4).Select(i => i < squealing ? limit : quiet).ToArray();
            for (var t = 0f; t < 2f; t += Dt) mix.Update(wheels, 0f, Dt);
            return mix.Squeal.Level;
        }
        var one = Level(1);
        var two = Level(2);
        Assert.InRange(Spectrum.Db(two / one), 2.7, 3.3);
        Assert.True(Level(4) > two);
    }

    [Fact]
    public void Scrub_climbs_with_the_speed_of_the_sliding_rubber()
    {
        var slow = Hold(TyreContact.Of(TyreGround.Asphalt, 9f, -1f, 0f, 8f, 1f, 1f));
        var fast = Hold(TyreContact.Of(TyreGround.Asphalt, 9f, -1f, 0f, 25f, 1f, 1f));
        Assert.True(fast.Scrub.Pitch > slow.Scrub.Pitch + 0.2f);
        // Wheelspin from rest: the patch barely travels but the rubber slides fast, and it screeches.
        var spin = Hold(TyreContact.Of(TyreGround.Asphalt, 9f, 3f, 0f, 1f, 1f, 1f));
        Assert.True(spin.Scrub.Level > 0.4f, "wheelspin is silent");
    }

    private readonly record struct StopSound(float Squeal, float Scrub, float Abs, float Spray, float SquealLow, float SquealHigh);

    /// <summary>
    /// A stop from 100 km/h with the pedal hard down, the tyres heard as the game hears them (TyreAudio's mapping of the
    /// wheels); the levels are averaged from 0.4 s to 1.4 s in, and the squeal's range over that time is its chatter.
    /// </summary>
    private static StopSound Stop(VehicleSpec spec, Weather weather)
    {
        var car = new VehicleSim(spec, Proving()) { Weather = weather };
        car.LaunchAt(100f / MathUtil.KmhPerMps);
        var mix = new TyreMix();
        var contacts = new TyreContact[4];
        const int perFrame = 4;
        float squeal = 0f, scrub = 0f, abs = 0f, spray = 0f, low = float.MaxValue, high = 0f;
        var frames = 0;
        for (var frame = 0; frame < 90; frame++)
        {
            for (var i = 0; i < perFrame; i++) car.Step(new DriverInputs { Brake = 1f, Gear = 0 }, Dt / perFrame);
            Contacts(car, contacts);
            mix.Update(contacts, WeatherModel.Wetness(weather), Dt);
            if (frame < 24) continue;
            squeal += mix.Squeal.Level;
            scrub += mix.Scrub.Level;
            abs += mix.Abs;
            spray += mix.Spray;
            low = MathF.Min(low, mix.Squeal.Level);
            high = MathF.Max(high, mix.Squeal.Level);
            frames++;
        }
        return new StopSound(squeal / frames, scrub / frames, abs / frames, spray / frames, low, high);
    }

    /// <summary>The wheels as TyreAudio reads them (game/src/TyreAudio.cs, kept in step with it).</summary>
    private static void Contacts(VehicleSim car, TyreContact[] contacts)
    {
        var spec = car.Spec;
        var staticLoad = spec.MassKg * MathUtil.Gravity / 4f;
        for (var i = 0; i < 4; i++)
        {
            var w = car.Wheels[i];
            var ground = !w.InContact ? TyreGround.Air : w.Surface is SurfaceType.Road or SurfaceType.Pad ? TyreGround.Asphalt : TyreGround.Loose;
            var tyre = w.IsFront ? spec.FrontTire : spec.RearTire;
            var toPeak = ground == TyreGround.Air ? 0f : TireModel.SlipToPeak(w.SlipRatio, w.SlipAngle, w.Load, w.Friction, tyre);
            contacts[i] = TyreContact.Of(ground, toPeak, w.SlipRatio, w.LateralSpeed, w.GroundSpeed, w.Load / staticLoad, w.BrakePressure);
        }
    }

    /// <summary>A long flat straight, as the physics tests' proving ground.</summary>
    private static TrackGeometry Proving()
    {
        var common = new TrackBuilder(PathKind.Common, Vector3.Zero, 0f).Straight(3000f);
        var speed = TrackBuilder.ContinueFrom(common.Path, PathKind.Speed).Straight(50f);
        var stunt = TrackBuilder.ContinueFrom(common.Path, PathKind.Stunt).Straight(50f);
        var final = TrackBuilder.ContinueFrom(speed.Path, PathKind.Final).Straight(50f);
        return new TrackGeometry(common.Path, speed.Path, stunt.Path, final.Path, [], rollingGround: 0f);
    }

    /// <summary>
    /// A tyre sliding near broadside howls as loud as one sliding at 80 degrees: the sideways slide is the contact
    /// patch's own speed across the wheel. Rebuilt from the speed along the wheel and the slip angle, it collapsed as the
    /// slide neared 90 degrees and the howl went quiet past 85 (0.19 review).
    /// </summary>
    [Fact]
    public void A_slide_near_broadside_keeps_its_howl()
    {
        float HowlAt(float angleDeg)
        {
            var a = angleDeg * MathF.PI / 180f;
            const float speed = 15f;
            var mix = Hold(TyreContact.Of(TyreGround.Asphalt, 8f, 0f, speed * MathF.Sin(a), speed * MathF.Cos(a), 1f, 1f));
            return mix.Howl.Level;
        }
        var at80 = HowlAt(80f);
        var at88 = HowlAt(88f);
        var at90 = HowlAt(90f);
        output.WriteLine($"howl at 80 deg {at80:0.00}, 88 deg {at88:0.00}, 90 deg {at90:0.00}");
        Assert.True(at80 > 0.5f);
        Assert.True(at88 > 0.85f * at80 && at90 > 0.85f * at80, "the howl should hold as the slide nears broadside");
    }
}
