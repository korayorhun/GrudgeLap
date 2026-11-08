namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// A car's own automatic gearbox (0.76): a torque converter with a lock-up clutch in front of a planetary gearbox whose
/// clutches change gear under load, with its own ratios, final drive and shift schedule. A car that has one drives through
/// it in <see cref="TransmissionMode.Automatic"/> (<see cref="Drivetrain"/>'s converter path) and keeps its manual gearbox
/// and clutch in the manual modes; a car without one (<see cref="VehicleSpec.Automatic"/> null) has its manual gearbox
/// shifted for the driver in the automatic mode, as every car had before 0.76.
/// </summary>
/// <remarks>
/// The converter is the usual three-element unit: an impeller (the pump, turned by the engine), a turbine (driving the
/// gearbox) and a stator on a one-way clutch. The pump absorbs a torque that grows with the square of its speed and falls
/// as the turbine catches it up (the capacity, <see cref="CapacityCurve"/>); the turbine gets that torque multiplied, by
/// <see cref="StallTorqueRatio"/> with the turbine held and falling to one at <see cref="CouplingPoint"/>, past which the
/// stator freewheels and the converter is a fluid coupling. The capacity is scaled so that at full throttle with the
/// turbine held the engine settles at <see cref="StallRpm"/>, the stall test of a workshop manual. The defaults are a
/// passenger car's converter (a game figure where the car's own is not published).
/// <para>
/// In D the pedal asks for a drive at the wheels rather than a share of the engine's torque (0.76 review,
/// <see cref="PedalPowerExponent"/>): the engine is opened as far as that drive needs in whatever gear the box holds, and
/// the box changes up early only into a gear that can still give it (<see cref="UpshiftLoadLimit"/>), down when the gear
/// in cannot. Read as every other car reads it, as a share of the torque between engine braking and full throttle, the
/// pedal left the engine 20-45 % of its torque at the 1,300-2,300 rpm the early upshifts keep it at: from rest 0-50
/// km/h took 47.7 s at 0.3 pedal and 12.4 s at 0.5, against 18.7 and 8.4 s for the manual changing up at 4,000, and at
/// 0.5 pedal the car never reached 100 km/h nor changed down from sixth at 80.
/// </para>
/// </remarks>
public sealed record AutomaticGearbox
{
    /// <summary>Ratios as <see cref="VehicleSpec.GearRatios"/> has them: index = gear + 1 (reverse, neutral, 1..n).</summary>
    public required float[] GearRatios { get; init; }
    public required float FinalDrive { get; init; }
    /// <summary>
    /// The gearbox and final drive's efficiency with the converter locked, as <see cref="VehicleSpec.DrivelineEfficiency"/>:
    /// a planetary automatic's gear sets and its oil pump, driven at engine speed, cost a few per cent more than a manual
    /// (0.85 against the manuals' 0.9 here).
    /// </summary>
    public float DrivelineEfficiency { get; init; } = 0.85f;

    /// <summary>Engine speed (rpm) at full throttle with the turbine held: the converter's stall speed.</summary>
    public float StallRpm { get; init; } = 2500f;
    /// <summary>Turbine torque over pump torque with the turbine held (the stall torque ratio).</summary>
    public float StallTorqueRatio { get; init; } = 1.9f;
    /// <summary>Turbine over pump speed at which the multiplication has fallen to one (the coupling point).</summary>
    public float CouplingPoint { get; init; } = 0.85f;
    /// <summary>
    /// The pump's capacity against the speed ratio, as a share of its capacity with the turbine held (1 at 0, 0 at 1): the
    /// inverse square of a converter's K-factor curve, flat to about half the pump's speed and falling steeply into the
    /// coupling range, so that the turbine slips 5-10 % behind the pump under a light load and 3-4 % at full throttle high
    /// in the revs (passenger-car converter curves, as SAE J643 tests plot them; a game figure for the shape).
    /// </summary>
    public (float SpeedRatio, float Capacity)[] CapacityCurve { get; init; } =
    [
        (0f, 1f), (0.5f, 1f), (0.6f, 0.961f), (0.7f, 0.873f), (0.8f, 0.694f), (0.85f, 0.574f), (0.9f, 0.416f), (0.95f, 0.227f),
        (0.98f, 0.098f), (1f, 0f),
    ];
    /// <summary>
    /// The capacity when the turbine drives the pump (engine braking with the converter open), as a share of the forward
    /// one at the same speed ratio: the oil meets the blades from behind and the stator does nothing for it (a game figure).
    /// </summary>
    public float OverrunCapacityShare { get; init; } = 0.5f;
    /// <summary>
    /// Engine, flexplate, impeller and the oil in the converter (kg m^2): the automatic has no flywheel and no clutch, and
    /// this replaces <see cref="VehicleSpec.EngineInertia"/> on its path.
    /// </summary>
    public float PumpInertia { get; init; } = 0.17f;
    /// <summary>The turbine, the input shaft and the clutch drums that turn with it (kg m^2).</summary>
    public float TurbineInertia { get; init; } = 0.05f;

    /// <summary>The lowest gear in which the lock-up clutch closes in D.</summary>
    public int LockUpFromGear { get; init; } = 4;
    /// <summary>The road speed (km/h) above which the lock-up clutch closes; it opens again 5 km/h under it.</summary>
    public float LockUpFromKmh { get; init; } = 45f;
    /// <summary>The turbine's speed (rpm) under which the lock-up clutch opens, so the engine never lugs or stalls through it.</summary>
    public float LockUpMinRpm { get; init; } = 1200f;
    /// <summary>The torque (N m) the lock-up clutch holds fully applied.</summary>
    public float LockUpCapacity { get; init; } = 450f;
    /// <summary>Seconds the lock-up clutch's pressure takes to rise, slipping the engine down onto the turbine as it does.</summary>
    public float LockUpSeconds { get; init; } = 0.6f;

    /// <summary>Seconds from a shift's command to the on-coming clutch's first bite (its fill), the old gear driving meanwhile.</summary>
    public float FillSeconds { get; init; } = 0.1f;
    /// <summary>Seconds the drive takes to pass from the off-going clutch to the on-coming one (the torque phase).</summary>
    public float TorquePhaseSeconds { get; init; } = 0.08f;
    /// <summary>
    /// Seconds the on-coming clutch takes to bring the turbine to the new gear's speed (the inertia phase of a change up,
    /// or of a change down off the throttle; a change down under power waits for the engine to flare instead)...
    /// </summary>
    public float InertiaPhaseSeconds { get; init; } = 0.3f;
    /// <summary>...and the seconds added for each 1000 rpm the turbine's speed has to change.</summary>
    public float InertiaPhaseSecondsPer1000Rpm { get; init; } = 0.05f;
    /// <summary>
    /// The share of the engine's torque taken away while an upshift's inertia phase brings the engine down (the ignition
    /// retard of a gearbox's torque reduction), with the throttle past half: it takes back much of what the engine's slowing
    /// flywheel gives the wheels then, so the drive passes to the new gear's with a swell rather than a kick (a game
    /// figure; retards of a third to a half of the torque are usual).
    /// </summary>
    public float UpshiftTorqueReduction { get; init; } = 0.5f;
    /// <summary>
    /// The share of the turbine's torque the off-going clutch keeps passing while a change down under power lets the engine
    /// and the turbine run up to the lower gear's speed: the rest speeds the turbine up, so the engine flares to the new
    /// gear, and the drive sags meanwhile (the delay a driver feels before a kickdown pulls).
    /// </summary>
    public float DownshiftClutchShare { get; init; } = 0.5f;

    /// <summary>
    /// The turbine's speed (rpm) in the gear it is in at which the gearbox changes up, against the pedal (ascending): the
    /// shift map of the D range, early at a light pedal and near the redline flat out. A change down to the next gear
    /// comes when that gear would turn at less than <see cref="DownshiftShare"/> of the same line (the downshift lines lie
    /// under the upshift ones, so a gear chosen holds at a steady speed and pedal). Since the 0.76 review the line is the
    /// earliest a change up comes: it waits until the gear above can give the pedal's drive
    /// (<see cref="UpshiftLoadLimit"/>), and while the gear in cannot give it, until the line's full-pedal end.
    /// </summary>
    public required (float Throttle, float Rpm)[] UpshiftRpm { get; init; }
    public float DownshiftShare { get; init; } = 0.8f;
    /// <summary>Seconds between two shifts the schedule asks for (a kickdown may come sooner).</summary>
    public float ShiftGapSeconds { get; init; } = 1f;
    /// <summary>The throttle past which the pedal's travel is the kickdown: the gearbox changes down as far as pulls hardest.</summary>
    public float KickDownThrottle { get; init; } = 0.95f;
    /// <summary>
    /// The highest turbine speed (rpm) a kickdown, or a change down for a drive the gear in cannot give, may land on, so the
    /// engine still has room to pull before the next upshift.
    /// </summary>
    public float KickDownRpm { get; init; } = 6000f;

    /// <summary>
    /// How the D range reads the pedal (0.76 review): as a share of the engine's peak power at the wheels, the pedal's
    /// travel raised to this power, so a light foot asks for little and the share grows towards a full one as the pedal
    /// goes down (1.75: a tenth of the power at 0.27, a fifth at 0.4, 30 % at 0.5, 54 % at 0.7). The engine is opened
    /// as far as that drive needs through the converter and the gear the box holds, never less than the pedal itself;
    /// torque-based engine management turns the pedal into a torque demand the same way, the FA20's from the pedal's
    /// angle and the engine's speed (EcuTek's ProECU tuning guide for the BRZ, FR-S and GT86, "Accel Pedal to Desired
    /// Torque"; the maps' figures are not published, so this exponent is a game figure). From rest to 50 km/h the BRZ
    /// takes 16.4 s at 0.3 pedal and 6.8 s at 0.5, against 17.9 and 8.1 s in the manual for a driver changing up at 4,000
    /// rpm, and 3.3 s floored; from a cruise at 80 km/h in sixth, 0.5 pedal changes down to fourth and reaches 100 km/h in
    /// 7.7 s (the manual held in fourth 11.3 s).
    /// </summary>
    public float PedalPowerExponent { get; init; } = 1.75f;
    /// <summary>
    /// The road speed (km/h) under which the pedal asks for the force its share of the power gives at this speed rather
    /// than at the road's: a power asks for a boundless force at a standstill (a game figure, setting a pull-away's pull).
    /// </summary>
    public float PedalForceBelowKmh { get; init; } = 36f;
    /// <summary>
    /// The most of its full-throttle torque the engine may need, in the gear the box would change up into, to give the
    /// pedal's drive there (a game figure, a tenth in hand): an early change up waits for it, so a part-throttle pull
    /// does not end with the engine lugging at full throttle in a gear too tall for it.
    /// </summary>
    public float UpshiftLoadLimit { get; init; } = 0.9f;

    public int ForwardGearCount => GearRatios.Length - 2;
    public float RatioForGear(int gear) => GearRatios[gear + 1];

    /// <summary>The turbine's speed (rpm) at which the gearbox changes up at <paramref name="throttle"/> (see <see cref="UpshiftRpm"/>).</summary>
    internal float UpshiftRpmAt(float throttle) => Interpolate(UpshiftRpm, MathUtil.Clamp01(throttle));

    /// <summary>The pump's capacity at <paramref name="speedRatio"/> (0..1) as a share of its stall capacity, and its slope against the speed ratio.</summary>
    internal (float Capacity, float Slope) CapacityAt(float speedRatio)
    {
        var curve = CapacityCurve;
        speedRatio = MathUtil.Clamp01(speedRatio);
        for (var i = 1; i < curve.Length; i++)
        {
            if (speedRatio > curve[i].SpeedRatio) continue;
            var (x0, y0) = curve[i - 1];
            var (x1, y1) = curve[i];
            var slope = (y1 - y0) / (x1 - x0);
            return (y0 + slope * (speedRatio - x0), slope);
        }
        return (curve[^1].Capacity, 0f);
    }

    /// <summary>The turbine's torque over the pump's at <paramref name="speedRatio"/>: the stall ratio falling to one at the coupling point.</summary>
    internal float TorqueRatioAt(float speedRatio) =>
        MathUtil.Lerp(StallTorqueRatio, 1f, MathUtil.Clamp01(speedRatio / CouplingPoint));

    private static float Interpolate((float X, float Y)[] table, float x)
    {
        if (x <= table[0].X) return table[0].Y;
        for (var i = 1; i < table.Length; i++)
        {
            if (x > table[i].X) continue;
            var (x0, y0) = table[i - 1];
            var (x1, y1) = table[i];
            return MathUtil.Lerp(y0, y1, (x - x0) / (x1 - x0));
        }
        return table[^1].Y;
    }
}

/// <summary>The torque converter's fluid path: what the pump absorbs from the engine and what the turbine passes on.</summary>
internal static class TorqueConverter
{
    /// <summary>
    /// The pump's capacity constant (N m per (rad/s)^2 with the turbine held): the engine's full-throttle torque at the
    /// stall speed over the square of that speed, so that the engine settles there at full throttle with the turbine held.
    /// </summary>
    public static float StallCapacity(EngineSpec engine, AutomaticGearbox gearbox)
    {
        var stall = gearbox.StallRpm / EngineModel.RpmPerRadPerSec;
        return engine.CurveTorque(gearbox.StallRpm) / (stall * stall);
    }

    /// <summary>
    /// The pump's torque (the load on the engine), the turbine's torque (the drive into the gearbox) and the pump torque's
    /// slope against the engine's speed with the turbine's speed held (N m per rad/s, never negative), at pump speed
    /// <paramref name="pump"/> and turbine speed <paramref name="turbine"/> (rad/s). With the turbine ahead of the pump
    /// (the wheels driving the engine) both torques are negative and the converter is a weaker coupling.
    /// </summary>
    public static (float Pump, float Turbine, float PumpSlope) Torques(AutomaticGearbox gearbox, float capacity, float pump, float turbine)
    {
        pump = MathF.Max(pump, 0f);
        if (turbine <= pump)
        {
            if (pump <= 0f) return (0f, 0f, 0f);
            // A turbine turning backwards (the car rolling back in D) is taken as held: the stator's multiplication is at
            // its most there.
            var ratio = MathF.Max(turbine, 0f) / pump;
            var (share, slope) = gearbox.CapacityAt(ratio);
            var absorbed = capacity * share * pump * pump;
            // d/d(pump) of capacity * share(turbine / pump) * pump^2 with the turbine held.
            var pumpSlope = capacity * pump * (2f * share - ratio * slope);
            return (absorbed, gearbox.TorqueRatioAt(ratio) * absorbed, MathF.Max(pumpSlope, 0f));
        }
        else
        {
            var ratio = pump / turbine;
            var (share, slope) = gearbox.CapacityAt(ratio);
            var k = gearbox.OverrunCapacityShare * capacity;
            var passed = -k * share * turbine * turbine;
            // d/d(pump) of -k * share(pump / turbine) * turbine^2: -k * slope * turbine, positive as the slope never is.
            return (passed, passed, MathF.Max(-k * slope * turbine, 0f));
        }
    }
}
