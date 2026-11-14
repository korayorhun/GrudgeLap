using System.Globalization;

namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// What the showroom and the title screen print about a car (0.79), worked out from its <see cref="VehicleSpec"/> alone,
/// so a car added to the catalogue appears there with its facts and nothing else to write. The Godot game's Showroom and
/// Hud held this code (0.25, 0.13); it lives in the core now so that the Unreal game reads the same lines through the C
/// interface (glp_car_facts) and the figures cannot drift from the simulation they describe.
/// </summary>
/// <remarks>
/// The figures are the simulation's and the showroom says so: the mass carries a driver and fuel
/// (<see cref="VehicleSpec.MassKg"/>), and the power and torque are the peaks of the full-throttle curve, which can sit a
/// little off a brochure's.
/// </remarks>
public static class CarFacts
{
    /// <summary>The separator between the parts of a line: a middle dot with three spaces either side.</summary>
    private const string Separator = "   ·   ";

    /// <summary>The car's year, from the '84 that ends its name; empty when the name carries none.</summary>
    public static string Year(VehicleSpec spec)
    {
        var at = spec.Name.LastIndexOf('\'');
        if (at < 0 || at + 3 > spec.Name.Length || !int.TryParse(spec.Name.AsSpan(at + 1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var two)) return "";
        // Two digits: the catalogue runs from the 1980s to the 2010s, so anything under 40 is this century.
        return (two < 40 ? 2000 + two : 1900 + two).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The layout line: the year, where the engine sits, its cylinders and its induction, the driven wheels and the
    /// gearbox. The engine's place is read from the weight the spec puts on each axle (the centre of mass against the
    /// axles): a car carrying more than half its weight on the rear axle has its engine behind the seats (the
    /// flat-twelve 40/60, the V8 42/58), a front-engined car more than half on the front; the spec has no other word
    /// for it.
    /// </summary>
    public static string LayoutLine(VehicleSpec spec)
    {
        var front = FrontWeightShare(spec);
        var engine = front >= 0.5f ? "FRONT ENGINE" : "ENGINE BEHIND THE SEATS";
        var induction = spec.Engine.Governor ? "TURBO-DIESEL" : spec.Engine.BoostShare > 0f ? "TURBO" : "";
        var cylinders = $"{spec.Engine.Cylinders} CYLINDERS{(induction == "" ? "" : "  " + induction)}";
        var drive = spec.FourWheelDrive is not null ? "FOUR-WHEEL DRIVE" : spec.FrontWheelDrive ? "FRONT-WHEEL DRIVE" : "REAR-WHEEL DRIVE";
        var year = Year(spec);
        var weight = string.Create(CultureInfo.InvariantCulture, $"{front * 100f:0} / {100f - front * 100f:0} FRONT / REAR");
        return string.Join(Separator, new[] { year, engine, cylinders, drive, Gearbox(spec), weight }.Where(p => p != ""));
    }

    /// <summary>
    /// The gearbox as the layout line names it. A car sold with a torque-converter automatic (0.76: the boxer coupe's
    /// six-speed, <see cref="VehicleSpec.Automatic"/>) is named by that box, which is what its AUTOMATIC drives through;
    /// otherwise a gear change under an eighth of a second is a dual-clutch box handing over, and a hand on a gate takes
    /// a quarter.
    /// </summary>
    public static string Gearbox(VehicleSpec spec)
    {
        if (spec.Automatic is { } automatic) return $"{automatic.ForwardGearCount}-SPEED AUTOMATIC";
        return $"{spec.ForwardGearCount}-SPEED {(spec.ShiftSeconds <= 0.125f ? "DUAL-CLUTCH" : "MANUAL")}";
    }

    /// <summary>
    /// The figures line: peak power in metric horsepower (the period brochures' unit, as the title screen prints it) and
    /// peak torque, each with its revs, from the full-throttle torque curve; the mass; the power to weight; the tyres.
    /// </summary>
    public static string FiguresLine(VehicleSpec spec)
    {
        var curve = spec.Engine.FullThrottleCurve;
        var peakPower = curve.MaxBy(p => p.Rpm * p.Torque);
        var peakTorque = curve.MaxBy(p => p.Torque);
        var hp = peakPower.Rpm * peakPower.Torque / 7023f;
        var tyres = spec.FrontTire == spec.RearTire ? Tyre(spec.FrontTire) : $"{Tyre(spec.FrontTire)} FRONT, {Tyre(spec.RearTire)} REAR";
        return string.Create(CultureInfo.InvariantCulture,
            $"{hp:0} HP AT {peakPower.Rpm:0} RPM{Separator}{peakTorque.Torque:0} NM AT {peakTorque.Rpm:0} RPM{Separator}{spec.MassKg:0} KG{Separator}{hp / spec.MassKg * 1000f:0} HP PER TONNE{Separator}TYRES {tyres}");
    }

    /// <summary>
    /// The title screen's line under the car's name (the Godot game's Hud.CarFigures, 0.13): power, mass, the driven
    /// wheels and the number of gears, short enough to be read at a glance while stepping through the cars.
    /// </summary>
    public static string TitleLine(VehicleSpec spec)
    {
        var power = spec.Engine.FullThrottleCurve.Max(p => p.Rpm * p.Torque) / 7023f;
        var drive = spec.FourWheelDrive is not null ? "FOUR-WHEEL DRIVE" : spec.FrontWheelDrive ? "FRONT-WHEEL DRIVE" : "REAR-WHEEL DRIVE";
        var turbo = spec.Engine.BoostShare > 0f ? "  TURBO" : "";
        return string.Create(CultureInfo.InvariantCulture, $"{power:0} HP{turbo}   {spec.MassKg:0} KG   {drive}   {spec.ForwardGearCount} GEARS");
    }

    private static string Tyre(TireSize tyre) => string.Create(CultureInfo.InvariantCulture, $"{tyre.WidthMm:0}/{tyre.AspectPercent:0} R{tyre.RimInches:0}");

    /// <summary>The share of the car's weight on its front axle at rest, from where the spec puts the centre of mass between the axles.</summary>
    public static float FrontWeightShare(VehicleSpec spec) => (spec.RearAxle + spec.CentreOfMassOffset.Z) / (spec.FrontAxle + spec.RearAxle);
}
