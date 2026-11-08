namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// The torque-converter automatic's path through the drivetrain (0.76), taken in <see cref="TransmissionMode.Automatic"/>
/// by a car with its own automatic gearbox (<see cref="VehicleSpec.Automatic"/>; the BRZ's Aisin six-speed). The engine
/// drives the converter's pump, the turbine drives the gear set, and the converter's fluid passes torque between them by
/// their speeds (<see cref="TorqueConverter"/>): a stalled turbine gets the engine's torque multiplied, the car creeps at
/// idle in D, and the engine flares ahead of the wheels until the converter couples. The lock-up clutch closes in the upper
/// gears above a speed and then ties the engine to the gearbox as the manual's clutch does. Gears change under load:
/// the on-coming clutch's fill, the torque phase that hands the drive from one clutch to the other and the inertia phase
/// that brings the turbine to the new gear's speed; in a change down under power the inertia phase comes first, the
/// engine and the turbine flaring up before the drive is handed over (the textbook's four kinds of power shift, up or
/// down, under traction or on the overrun: Naunheimer et al., Automotive Transmissions).
/// </summary>
/// <remarks>
/// Only this path reads <see cref="AutomaticGearbox"/>; the clutch-shifted automatic of the other cars and the manual modes
/// of every car are the main file's, unchanged. <see cref="ClutchLocked"/> here means the lock-up clutch has closed;
/// <see cref="Engagement"/> is 1 with a gear in. The launch assist (<see cref="Launching"/>, <see cref="TractionTorque"/>)
/// has nothing to do: the converter's slip is the launch. In D the pedal is read as a drive at the wheels (0.76 review,
/// <see cref="DriveThrottle"/>), which this path alone does: the manual modes keep the pedal every car has.
/// </remarks>
public sealed partial class Drivetrain
{
    private readonly AutomaticGearbox? _automatic;
    /// <summary>The converter's capacity constant (N m per (rad/s)^2), from its stall speed (<see cref="TorqueConverter.StallCapacity"/>).</summary>
    private readonly float _converterCapacity;
    /// <summary>The engine's peak power (W) less the automatic's losses: the drive a full pedal asks for in D (<see cref="DriveDemand"/>).</summary>
    private readonly float _peakWheelPower;

    private enum ShiftPhase { None, Fill, Torque, Inertia }

    private ShiftPhase _phase;
    /// <summary>The gear a shift leaves; <see cref="Gear"/> is the one it goes to from its command on.</summary>
    private int _shiftFrom;
    /// <summary>A change down under power: the turbine runs up to the new gear before the drive is handed over.</summary>
    private bool _inertiaFirst;
    private float _phaseClock;
    private float _inertiaSeconds;
    /// <summary>The turbine's own speed (rad/s) while a change down under power lets it run free of both gears.</summary>
    private float _turbineSpeed;
    /// <summary>The lock-up clutch's pressure, 0 to 1.</summary>
    private float _lockUp;
    private float _sinceShift;
    private float _sinceBrake;
    /// <summary>The throttle as the shift schedule reads it (see <see cref="SelectConverterGear"/>).</summary>
    private float _scheduleThrottle;
    private float _scheduleSpeed;
    private float _scheduleAcceleration;
    /// <summary>The take-up of the forward or reverse clutch after the selector leaves N, 0 to 1.</summary>
    private float _rangeEngaged;

    /// <summary>The turbine's torque over the pump's in the last step: the converter's multiplication (1 through the closed lock-up clutch).</summary>
    internal float ConverterTorqueRatio { get; private set; } = 1f;
    /// <summary>The turbine's speed over the pump's in the last step.</summary>
    internal float ConverterSpeedRatio { get; private set; } = 1f;
    /// <summary>A change of gear is under way in the torque-converter automatic (from its command until the new gear drives).</summary>
    public bool Shifting => _phase != ShiftPhase.None;

    private float RatioOf(int gear) => _automatic!.RatioForGear(gear);

    private void ResetConverter()
    {
        _phase = ShiftPhase.None;
        _shiftFrom = 0;
        _inertiaFirst = false;
        _phaseClock = 0f;
        _inertiaSeconds = 0f;
        _turbineSpeed = 0f;
        _lockUp = 0f;
        _sinceShift = 10f;
        _sinceBrake = 10f;
        _scheduleThrottle = 0f;
        _scheduleSpeed = 0f;
        _scheduleAcceleration = 0f;
        _rangeEngaged = 0f;
        ConverterTorqueRatio = 1f;
        ConverterSpeedRatio = 1f;
    }

    /// <summary><see cref="ForceLocked"/> through the converter: the gear in, the lock-up clutch closed where the gear and the speed allow it.</summary>
    private void ForceConverterGear(int gear, float inputShaftSpeed)
    {
        var box = _automatic!;
        ResetConverter();
        Gear = Math.Clamp(gear, -1, box.ForwardGearCount);
        Engine.Start();
        Engine.AngularVelocity = MathF.Max(inputShaftSpeed, Engine.Spec.IdleRpm / EngineModel.RpmPerRadPerSec);
        _rangeEngaged = 1f;
        Engagement = 1f;
        var closes = Gear >= box.LockUpFromGear && inputShaftSpeed * EngineModel.RpmPerRadPerSec >= box.LockUpMinRpm;
        _lockUp = closes ? 1f : 0f;
        ClutchLocked = closes;
        _shiftTimer = 0f;
        // The schedule starts from the road speed the gear is put in at, not from rest.
        var ratio = MathF.Abs(box.RatioForGear(Gear) * box.FinalDrive);
        _scheduleSpeed = ratio > 0f ? MathF.Abs(inputShaftSpeed) / ratio * _spec.WheelRadius : 0f;
    }

    private void StepConverter(in DriverInputs input, float axleSpeed, float groundSpeed, float dt, float axleInertia, float axleLoadTorque)
    {
        var box = _automatic!;
        HandleIgnition(input);
        Launching = false;
        PreRevving = false;
        Synchronising = false;
        SelectConverterGear(box, input, axleSpeed, groundSpeed, dt);
        AdvanceShift(box, axleSpeed, dt);

        var finalDrive = box.FinalDrive;
        var (turbineRatio, outputRatio, ratioRate, freeTurbine) = ShiftRatios(box);
        var turbine = freeTurbine ? _turbineSpeed : axleSpeed * turbineRatio * finalDrive;

        // The traction control's cut and the speed limiter act on the throttle the pedal's drive asks for, as they act on
        // the pedal's own throttle in the other paths.
        var throttle = LimitSpeed(DriveThrottle(box, input.Throttle, groundSpeed, outputRatio, turbine) * MathUtil.Clamp01(TorqueCut), groundSpeed);
        // A gearbox's torque reduction: while an upshift's inertia phase brings the engine down to the new gear the
        // ignition is retarded, so the engine's own torque does not add a kick to the energy its flywheel gives up.
        var upshiftInertia = _phase == ShiftPhase.Inertia && !_inertiaFirst && RatioOf(Gear) < RatioOf(_shiftFrom);
        if (upshiftInertia && throttle > 0.5f) throttle *= 1f - box.UpshiftTorqueReduction;
        var engineTorque = Engine.Torque(throttle);
        var pumpInertia = box.PumpInertia;

        AxleTorque = 0f;
        ReflectedInertia = 0f;
        if (Gear == 0)
        {
            // Neutral: the turbine spins with the pump and loads the engine with nothing but its own churning.
            ClutchLocked = false;
            Engagement = 0f;
            ConverterTorqueRatio = 1f;
            ConverterSpeedRatio = 1f;
            Engine.AngularVelocity = MathF.Max(0f, Engine.AngularVelocity + engineTorque / pumpInertia * dt);
            Engine.Tick(dt);
            return;
        }
        Engagement = 1f;
        _rangeEngaged = MathF.Min(1f, _rangeEngaged + dt / RangeTakeUpSeconds);

        var efficiency = box.DrivelineEfficiency;

        // The lock-up clutch: pressure rising while the gearbox wants it closed, dumped at once otherwise.
        var lockUpWanted = LockUpWanted(box, groundSpeed, turbine);
        _lockUp = lockUpWanted ? MathF.Min(1f, _lockUp + dt / box.LockUpSeconds) : 0f;
        var lockUpCapacity = _lockUp * box.LockUpCapacity;
        var ratio = turbineRatio * finalDrive;

        if (ClutchLocked)
        {
            // Closed: the engine is tied to the gearbox as the manual's locked clutch ties it, and lets go as that one does
            // when its torque, or the wheels' change of speed, is more than the clutch holds (see the main path).
            var dragTorque = engineTorque - pumpInertia * (turbine - Engine.AngularVelocity) / dt;
            if (!lockUpWanted || MathF.Abs(engineTorque) > lockUpCapacity || MathF.Abs(dragTorque) > lockUpCapacity)
            {
                ClutchLocked = false;
            }
            else
            {
                AxleTorque = engineTorque * ratio * efficiency;
                ReflectedInertia = (pumpInertia + box.TurbineInertia) * ratio * ratio;
                Engine.AngularVelocity = MathF.Max(turbine, 0f);
                ConverterTorqueRatio = 1f;
                ConverterSpeedRatio = 1f;
                Engine.Tick(dt);
                return;
            }
        }

        var (pumpTorque, turbineTorque, pumpSlope) = TorqueConverter.Torques(box, _converterCapacity, Engine.AngularVelocity, turbine);
        var lockUpTorque = 0f;
        if (lockUpCapacity > 0f && !freeTurbine)
        {
            // The lock-up clutch slipping as its pressure rises: Coulomb torque towards one speed, never more than would
            // bring the engine and the turbine together within the step (the two-inertia balance of the main path's
            // clutch, with the axle's inertia through the gearing on the turbine's side).
            if (axleInertia <= 0f) axleInertia = 2f * _spec.WheelInertia + _spec.MassKg * _spec.WheelRadius * _spec.WheelRadius;
            var engineNext = Engine.AngularVelocity + (engineTorque - pumpTorque) * dt / pumpInertia;
            var turbineNext = ratio * (axleSpeed + (turbineTorque * ratio * efficiency - axleLoadTorque) * dt / axleInertia);
            var sync = (engineNext - turbineNext) / (dt * (1f / pumpInertia + ratio * ratio * efficiency / axleInertia));
            lockUpTorque = MathUtil.Clamp(sync, -lockUpCapacity, lockUpCapacity);
        }

        // The engine against the pump, semi-implicitly: the pump's torque rises steeply with the engine's speed (with the
        // square, and more as the turbine falls behind), which an explicit step would overshoot near the coupling range.
        var engineSpeed = Engine.AngularVelocity + dt * (engineTorque - pumpTorque - lockUpTorque) / (pumpInertia + dt * pumpSlope);
        Engine.AngularVelocity = MathF.Max(0f, engineSpeed);

        float passed;
        if (freeTurbine)
        {
            // A change down under power: the off-going clutch, its pressure lowered, keeps passing a share of the turbine's
            // torque on the old gear while the rest runs the turbine up, the engine flaring ahead of it through the fluid.
            passed = box.DownshiftClutchShare * MathF.Max(turbineTorque, 0f);
            _turbineSpeed += (turbineTorque - passed) * dt / box.TurbineInertia;
            var fromSpeed = axleSpeed * RatioOf(_shiftFrom) * finalDrive;
            var toSpeed = axleSpeed * RatioOf(Gear) * finalDrive;
            // Never back under the old gear's speed (its clutch would grip again), and at the new one within a second
            // whatever the engine does (the on-coming clutch pulling it up, as with the foot off the pedal).
            var floor = MathUtil.Lerp(fromSpeed, toSpeed, MathUtil.Clamp01(_phaseClock / FreeTurbineSeconds));
            _turbineSpeed = MathF.Max(_turbineSpeed, MathF.Max(fromSpeed, floor));
            if (_turbineSpeed >= toSpeed)
            {
                _phase = ShiftPhase.Torque;
                _phaseClock = 0f;
            }
        }
        else
        {
            // The turbine's own inertia, as the clutches change its speed in an upshift's (or a change down's on the
            // overrun) inertia phase, is passed on to the gearbox's output.
            passed = turbineTorque + lockUpTorque - box.TurbineInertia * axleSpeed * finalDrive * ratioRate;
            // Outside an inertia phase the turbine turns with the wheels through the gear set, and its inertia with them.
            if (_phase != ShiftPhase.Inertia) ReflectedInertia = box.TurbineInertia * ratio * ratio;
        }
        AxleTorque = passed * outputRatio * finalDrive * efficiency * _rangeEngaged;

        if (_lockUp >= 1f && _phase == ShiftPhase.None && MathF.Abs(engineTorque) <= box.LockUpCapacity
            && MathF.Abs(Engine.AngularVelocity - turbine) < 2f)
        {
            ClutchLocked = true;
            Engine.AngularVelocity = MathF.Max(turbine, 0f);
        }
        ConverterTorqueRatio = pumpTorque > 1e-3f ? turbineTorque / pumpTorque : 1f;
        ConverterSpeedRatio = Engine.AngularVelocity > 1f ? turbine / Engine.AngularVelocity : 0f;
        Engine.Tick(dt);
    }

    /// <summary>Seconds the forward or reverse clutch takes to take up after the selector leaves N (a game figure).</summary>
    private const float RangeTakeUpSeconds = 0.3f;
    /// <summary>The longest a change down under power may wait for the engine to flare before the on-coming clutch pulls the turbine up.</summary>
    private const float FreeTurbineSeconds = 1f;

    /// <summary>
    /// The throttle the engine is opened to in D (0.76 review): the larger of the driver's <paramref name="pedal"/>, read as
    /// every car reads it, and the throttle at which the engine gives the wheels the drive the pedal asks for
    /// (<see cref="DriveDemand"/>) through the converter's multiplication now and the gear the drive leaves by
    /// (<paramref name="outputRatio"/>; <paramref name="turbine"/> is the turbine's speed, rad/s). After an early upshift
    /// the engine is opened further at the lower revs, so the pull carries through the change as a drive-by-wire car's
    /// does; never less than the pedal, so a full pedal is full throttle in any gear and a pull-away through the
    /// converter, whose multiplication already gives the drive, is as before. N and R take the pedal as it is.
    /// </summary>
    private float DriveThrottle(AutomaticGearbox box, float pedal, float groundSpeed, float outputRatio, float turbine)
    {
        if (Gear < 1 || pedal <= 0f || !Engine.Running) return pedal;
        // A turbine turning backwards (rolling back in D) is taken as held, as TorqueConverter.Torques takes it.
        var speedRatio = ClutchLocked || Engine.AngularVelocity <= 1f ? 1f : turbine / Engine.AngularVelocity;
        var multiplication = speedRatio < 1f ? box.TorqueRatioAt(MathF.Max(speedRatio, 0f)) : 1f;
        var engineTorque = DriveDemand(box, pedal, groundSpeed) * _spec.WheelRadius
                           / (outputRatio * box.FinalDrive * box.DrivelineEfficiency * multiplication);
        return MathF.Max(pedal, Engine.ThrottleFor(engineTorque));
    }

    /// <summary>
    /// The drive (N at the driven wheels) the <paramref name="pedal"/> asks for in D at <paramref name="roadSpeed"/> (m/s,
    /// rolling back counting as a standstill): its share of the engine's peak power at the wheels
    /// (<see cref="AutomaticGearbox.PedalPowerExponent"/>), as a force at the road's speed, or under
    /// <see cref="AutomaticGearbox.PedalForceBelowKmh"/> at that speed.
    /// </summary>
    private float DriveDemand(AutomaticGearbox box, float pedal, float roadSpeed)
    {
        var share = MathF.Pow(MathUtil.Clamp01(pedal), box.PedalPowerExponent);
        return share * _peakWheelPower / MathF.Max(roadSpeed, box.PedalForceBelowKmh / MathUtil.KmhPerMps);
    }

    /// <summary>
    /// The drive (N at the driven wheels) <paramref name="gear"/> gives at full throttle at the schedule's speed, the
    /// turbine turning at <paramref name="perRatio"/> rpm per unit of ratio and the converter taken as coupled.
    /// </summary>
    private float FullThrottleDrive(AutomaticGearbox box, int gear, float perRatio)
    {
        var ratio = RatioOf(gear);
        return Engine.FullTorque(perRatio * ratio) * ratio * box.FinalDrive * box.DrivelineEfficiency / _spec.WheelRadius;
    }

    /// <summary>
    /// The gear set's ratio the turbine turns at, the one the drive leaves by, the turbine ratio's rate of change (1/s) and
    /// whether the turbine runs free (a change down under power's inertia phase), for the shift's phase.
    /// </summary>
    private (float Turbine, float Output, float Rate, bool Free) ShiftRatios(AutomaticGearbox box)
    {
        var to = RatioOf(Gear);
        if (_phase == ShiftPhase.None) return (to, to, 0f, false);
        var from = RatioOf(_shiftFrom);
        switch (_phase)
        {
            case ShiftPhase.Fill:
                return (from, from, 0f, false);
            case ShiftPhase.Torque:
                // The drive passes from one clutch to the other with the turbine at the speed of the gear the inertia phase
                // has not yet left (torque first) or has already reached (inertia first).
                var handed = MathUtil.SmoothStep(_phaseClock / box.TorquePhaseSeconds);
                return (_inertiaFirst ? to : from, MathUtil.Lerp(from, to, handed), 0f, false);
            default:
                if (_inertiaFirst) return (to, from, 0f, true);
                // The on-coming clutch brings the turbine from the old gear's speed to the new one's on an S-curve, the
                // drive leaving by the new gear meanwhile.
                var x = MathUtil.Clamp01(_phaseClock / _inertiaSeconds);
                var rate = (to - from) * 6f * x * (1f - x) / _inertiaSeconds;
                return (MathUtil.Lerp(from, to, MathUtil.SmoothStep(x)), to, rate, false);
        }
    }

    /// <summary>Moves a shift on through its phases.</summary>
    private void AdvanceShift(AutomaticGearbox box, float axleSpeed, float dt)
    {
        if (_phase == ShiftPhase.None) return;
        _phaseClock += dt;
        switch (_phase)
        {
            case ShiftPhase.Fill when _phaseClock >= box.FillSeconds:
                _phase = _inertiaFirst ? ShiftPhase.Inertia : ShiftPhase.Torque;
                _phaseClock = 0f;
                // A change down under power lets the turbine go from the old gear's speed.
                if (_inertiaFirst) _turbineSpeed = axleSpeed * RatioOf(_shiftFrom) * box.FinalDrive;
                break;
            case ShiftPhase.Torque when _phaseClock >= box.TorquePhaseSeconds:
                _phase = _inertiaFirst ? ShiftPhase.None : ShiftPhase.Inertia;
                _phaseClock = 0f;
                break;
            case ShiftPhase.Inertia when !_inertiaFirst && _phaseClock >= _inertiaSeconds:
                _phase = ShiftPhase.None;
                _phaseClock = 0f;
                break;
        }
    }

    /// <summary>
    /// Whether the lock-up clutch should close: in the upper gears (<see cref="AutomaticGearbox.LockUpFromGear"/>) above a
    /// road speed, with the turbine above the speed at which the engine would lug, and never during a shift or the
    /// take-up of a gear.
    /// </summary>
    private bool LockUpWanted(AutomaticGearbox box, float groundSpeed, float turbine)
    {
        if (_phase != ShiftPhase.None || Gear < box.LockUpFromGear || _rangeEngaged < 1f) return false;
        var kmh = MathF.Abs(groundSpeed) * MathUtil.KmhPerMps;
        // It opens again 5 km/h under the speed it closes at, so it does not chatter there.
        var from = box.LockUpFromKmh - (_lockUp > 0f ? 5f : 0f);
        return kmh >= from && turbine * EngineModel.RpmPerRadPerSec >= box.LockUpMinRpm;
    }

    /// <summary>
    /// The selector and the shift schedule of the D range. N, R and D as the clutch-shifted automatic has them (D takes
    /// first as soon as the engine runs; R at a standstill only). In D the gearbox changes up when the turbine, in the gear
    /// it is in, reaches <see cref="AutomaticGearbox.UpshiftRpmAt"/> for the pedal (looking ahead by the shift's fill and
    /// torque phase, so a change at full throttle is under way before the limiter) and the gear above can give the drive
    /// the pedal asks for within <see cref="AutomaticGearbox.UpshiftLoadLimit"/>, or, while the gear in cannot give it, at
    /// the line's full-pedal end; down to the highest gear whose next one down would not turn under
    /// <see cref="AutomaticGearbox.DownshiftShare"/> of that line (several at once, as coming to a stop); down when the
    /// gear in cannot give the pedal's drive, to the highest that can (<see cref="PowerDownshiftGear"/>); and on a
    /// kickdown to the gear that pulls hardest under <see cref="AutomaticGearbox.KickDownRpm"/>.
    /// </summary>
    /// <remarks>
    /// The schedule reads the gearbox's output, the driven wheels' speed, as a gearbox controller does, but no more than a
    /// little over the road's (a wheel spinning on a launch or in the air does not change gear), and only forwards: a car
    /// rolling back in D is at a standstill to it and holds first (0.76 review: read as a speed, rolling back down a 15.7 %
    /// grade at idle changed it up to second, and D taken while reversing at 66 km/h went straight to fifth, each change
    /// up dividing the converter's pull against the roll). It reads the pedal through a lag, quicker to a foot going down
    /// than to a lift, so a dab or a lift between two bends does not start a shift. A second shift waits
    /// <see cref="AutomaticGearbox.ShiftGapSeconds"/> unless it is a kickdown or the car is slowing (then the gearbox
    /// follows the speed down quickly), and there is no change up while the brake is on or for a second after. Between
    /// the upshift line and the downshift line beneath it a gear holds at a steady speed and pedal, and between the load a
    /// change up allows and the gear's full throttle it holds as the drive asked for varies.
    /// </remarks>
    private void SelectConverterGear(AutomaticGearbox box, in DriverInputs input, float axleSpeed, float groundSpeed, float dt)
    {
        _sinceShift += dt;
        _sinceBrake = input.Brake > 0f ? 0f : _sinceBrake + dt;
        var follow = input.Throttle > _scheduleThrottle ? ScheduleThrottleRise : ScheduleThrottleFall;
        _scheduleThrottle += (input.Throttle - _scheduleThrottle) * MathUtil.Clamp01(dt / follow);
        var road = MathF.Max(groundSpeed, 0f);
        var speed = MathF.Min(MathF.Max(axleSpeed, 0f) * _spec.WheelRadius, road * 1.1f + 1f);
        // The output's acceleration through a tenth of a second's filter, no more than a car's (a knock or a landing
        // jolts the wheels, not the schedule).
        var acceleration = MathUtil.Clamp((speed - _scheduleSpeed) / dt, -15f, 15f);
        _scheduleAcceleration += (acceleration - _scheduleAcceleration) * MathUtil.Clamp01(dt / 0.1f);
        _scheduleSpeed = speed;

        var target = Gear;
        if (Gear == 0 && Engine.Running) target = 1;
        if (input.Gear == -1 && groundSpeed < 1f) target = -1;
        if (Gear == -1 && input.Gear != -1 && input.Gear != DriverInputs.NoGearChange) target = input.Gear;
        target = Math.Clamp(target, -1, box.ForwardGearCount);
        if (target != Gear && (target <= 0 || Gear <= 0))
        {
            // Engaging reverse while rolling forward is refused, as the clutch-shifted automatic refuses it.
            if (target == -1 && groundSpeed > 1.5f) return;
            EngageRange(target);
            return;
        }
        if (Gear < 1 || _phase != ShiftPhase.None) return;

        // The turbine's speed (rpm) per unit of gear ratio at this road speed, and its rate of rise.
        var perRatio = speed / _spec.WheelRadius * box.FinalDrive * EngineModel.RpmPerRadPerSec;
        var perRatioRate = MathF.Max(_scheduleAcceleration, 0f) / _spec.WheelRadius * box.FinalDrive * EngineModel.RpmPerRadPerSec;
        var line = box.UpshiftRpmAt(_scheduleThrottle);

        if (input.Throttle >= box.KickDownThrottle && _sinceShift > KickDownGapSeconds && Gear > 1)
        {
            var current = Engine.Spec.CurveTorque(perRatio * RatioOf(Gear)) * RatioOf(Gear);
            var best = Gear;
            var bestPull = current;
            for (var g = Gear - 1; g >= 1 && perRatio * RatioOf(g) <= box.KickDownRpm; g--)
            {
                var pull = Engine.Spec.CurveTorque(perRatio * RatioOf(g)) * RatioOf(g);
                if (pull <= bestPull) continue;
                best = g;
                bestPull = pull;
            }
            if (best < Gear && bestPull > current * 1.1f)
            {
                StartShift(box, best, input.Throttle, perRatio);
                return;
            }
        }
        // Coming off the throttle or braking, the gearbox follows the falling speed down as quickly as its shifts allow,
        // so the car is in the gear for its speed when the driver pulls away again; otherwise shifts keep their gap.
        var coasting = _scheduleThrottle < 0.1f || input.Brake > 0f;
        if (_sinceShift < (coasting ? KickDownGapSeconds : box.ShiftGapSeconds)) return;

        // The drive the pedal asks for (0.76 review) against what the gears give: an early change up waits until the gear
        // above gives it with some in hand, so the engine is not left lugging at full throttle in a gear too tall for the
        // pull; while the gear in cannot give it at all, the driver wants more than the box can make early, and it changes
        // up where it would flat out.
        var demand = DriveDemand(box, _scheduleThrottle, speed);
        var shortOfDrive = demand > FullThrottleDrive(box, Gear, perRatio);
        bool Carries(int gear) => demand <= box.UpshiftLoadLimit * FullThrottleDrive(box, gear, perRatio);

        // No change up while braking or for a second after: the gearbox's controller holds the gear the car slows into a
        // bend in, so the driver's lift and dab of brake there is not followed by a change up and a kickdown out of it.
        var lookAhead = box.FillSeconds + box.TorquePhaseSeconds;
        var revs = (perRatio + perRatioRate * lookAhead) * RatioOf(Gear);
        if (Gear < box.ForwardGearCount && _sinceBrake > BrakeHoldSeconds
            && (revs > box.UpshiftRpmAt(1f) || (revs > line && !shortOfDrive && Carries(Gear + 1))))
        {
            var up = Gear + 1;
            // At a light pedal the gearbox goes straight to the gear the speed calls for (a skip shift on a lift).
            while (_scheduleThrottle < 0.3f && up < box.ForwardGearCount && perRatio * RatioOf(up) > line && Carries(up + 1)) up++;
            StartShift(box, up, input.Throttle, perRatio);
            return;
        }
        var down = Gear;
        while (down > 1 && perRatio * RatioOf(down - 1) < box.DownshiftShare * line) down--;
        // The gear the drive calls for is chosen for the pedal as it is now: the lagged reading only decides whether to go
        // (a dab does not), so a foot pressed to half from a cruise in sixth goes to the gear for half in one change.
        if (shortOfDrive) down = Math.Min(down, PowerDownshiftGear(box, MathF.Max(demand, DriveDemand(box, input.Throttle, speed)), perRatio));
        if (down < Gear) StartShift(box, down, input.Throttle, perRatio);
    }

    /// <summary>
    /// The gear a change down goes to when the gear in cannot give the drive the pedal asks for (0.76 review: half pedal at
    /// 80 km/h in sixth): the highest below it that gives <paramref name="demand"/> (N) within
    /// <see cref="AutomaticGearbox.UpshiftLoadLimit"/> under <see cref="AutomaticGearbox.KickDownRpm"/>, so that the box
    /// does not change straight back up; failing that, the one that pulls hardest there, if a tenth harder than the gear
    /// in. The gear in when neither.
    /// </summary>
    private int PowerDownshiftGear(AutomaticGearbox box, float demand, float perRatio)
    {
        var current = FullThrottleDrive(box, Gear, perRatio);
        var best = Gear;
        var bestDrive = current;
        for (var gear = Gear - 1; gear >= 1 && perRatio * RatioOf(gear) <= box.KickDownRpm; gear--)
        {
            var drive = FullThrottleDrive(box, gear, perRatio);
            if (demand <= box.UpshiftLoadLimit * drive) return gear;
            if (drive <= bestDrive) continue;
            best = gear;
            bestDrive = drive;
        }
        return bestDrive > 1.1f * current ? best : Gear;
    }

    /// <summary>The lag through which the schedule reads a foot going down (s): a kickdown's own switch needs none.</summary>
    private const float ScheduleThrottleRise = 0.15f;
    /// <summary>The lag through which the schedule reads a lift (s).</summary>
    private const float ScheduleThrottleFall = 0.6f;
    /// <summary>The least time after a shift before a kickdown (s).</summary>
    private const float KickDownGapSeconds = 0.3f;
    /// <summary>How long after the brake is let off the gearbox still holds its gear rather than change up (s, a game figure).</summary>
    private const float BrakeHoldSeconds = 1f;

    /// <summary>The selector moved between N, R and D: the range's clutch takes up from the converter's creep.</summary>
    private void EngageRange(int gear)
    {
        Gear = gear;
        _phase = ShiftPhase.None;
        _rangeEngaged = 0f;
        _lockUp = 0f;
        ClutchLocked = false;
        _sinceShift = 0f;
    }

    /// <summary>
    /// Starts a change from the gear in to <paramref name="gear"/>: the lock-up clutch opens, and a change down with the
    /// throttle open lets the turbine run up to the lower gear before the drive is handed over.
    /// </summary>
    private void StartShift(AutomaticGearbox box, int gear, float throttle, float perRatio)
    {
        var down = RatioOf(gear) > RatioOf(Gear);
        // Only on the move (0.76 review): at a standstill or rolling back there is no lower gear's speed to flare up to,
        // and the free turbine's floor, which reads the axle's speed, would push a backwards turbine on.
        _inertiaFirst = down && throttle > 0.1f && Engine.Running && perRatio > 0f;
        _shiftFrom = Gear;
        Gear = gear;
        _phase = ShiftPhase.Fill;
        _phaseClock = 0f;
        _sinceShift = 0f;
        _lockUp = 0f;
        ClutchLocked = false;
        var change = MathF.Abs(perRatio * (RatioOf(_shiftFrom) - RatioOf(gear)));
        _inertiaSeconds = box.InertiaPhaseSeconds + box.InertiaPhaseSecondsPer1000Rpm * change / 1000f;
    }
}
