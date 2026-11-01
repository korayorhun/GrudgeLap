namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// Engine + clutch + gearbox feeding the driven axle (the rear, or the front on a front-wheel-drive car). The clutch is a lock/slip state machine:
/// while slipping it transmits Coulomb torque limited by the pedal, when the speeds meet it locks and the
/// engine inertia is lumped onto the wheels. That is numerically stable at the 240 Hz step and still gives
/// the two behaviours the original cabinet was known for: a launch you can fluff, and a stall.
/// </summary>
public sealed class Drivetrain
{
    private readonly VehicleSpec _spec;
    private float _shiftTimer;
    private float _autoShiftCooldown;

    public EngineModel Engine { get; }
    public int Gear { get; private set; }                       // -1, 0, 1..4
    public bool ClutchLocked { get; private set; }
    /// <summary>Effective clutch engagement after driver assists, 0 = open, 1 = fully engaged.</summary>
    public float Engagement { get; private set; }
    public TransmissionMode Mode { get; set; } = TransmissionMode.ManualAutoClutch;
    /// <summary>Torque delivered to the driven axle (sum of both wheels) during the last step.</summary>
    public float AxleTorque { get; private set; }
    /// <summary>
    /// Share of the throttle the engine is allowed (1 = all of it): traction control's cut, applied to the engine only,
    /// so the clutch and the gearbox go by the driver's pedal.
    /// </summary>
    public float TorqueCut { get; set; } = 1f;
    /// <summary>Extra inertia the driven wheels carry while the clutch is locked, already reflected through the gearing.</summary>
    public float ReflectedInertia { get; private set; }
    /// <summary>
    /// Torque (N m, both driven wheels together) the driven tyres can put down now short of spinning up past their peak,
    /// set by the car each step from the tyres' loads and grip (see VehicleSim.LaunchTraction); infinite when unknown.
    /// The launch assist slips the clutch so that no more than this reaches the wheels.
    /// </summary>
    public float TractionTorque { get; set; } = float.PositiveInfinity;
    /// <summary>
    /// True during a standing start in the assisted modes (0.19): from the first bite of the clutch until it locks, the
    /// engine is held near <see cref="EngineSpec.LaunchRpm"/> and the clutch passes what the tyres can take. With the brake
    /// held as well, the clutch stays open and the engine is held there ready (<see cref="PreRevving"/>).
    /// </summary>
    public bool Launching { get; private set; }
    /// <summary>Brake and throttle together at a standstill in the assisted modes: the engine held at the launch revs, the clutch open.</summary>
    public bool PreRevving { get; private set; }
    /// <summary>The assisted clutch taking up a gear on the move (after a shift), not yet locked.</summary>
    public bool Synchronising { get; private set; }

    public Drivetrain(VehicleSpec spec)
    {
        _spec = spec;
        Engine = new EngineModel(spec.Engine);
    }

    public float Ratio => _spec.RatioForGear(Gear) * _spec.FinalDrive;

    public void Reset()
    {
        Gear = 0;
        ClutchLocked = false;
        Engagement = 0f;
        AxleTorque = 0f;
        ReflectedInertia = 0f;
        TorqueCut = 1f;
        TractionTorque = float.PositiveInfinity;
        Launching = false;
        PreRevving = false;
        Synchronising = false;
        Engine.Stop();
    }

    /// <summary>Test/debug helper: engine running, given gear engaged and clutch locked at the given input-shaft speed.</summary>
    public void ForceLocked(int gear, float inputShaftSpeed)
    {
        Gear = Math.Clamp(gear, -1, _spec.ForwardGearCount);
        Engine.Start();
        Engine.AngularVelocity = MathF.Max(inputShaftSpeed, Engine.Spec.IdleRpm / EngineModel.RpmPerRadPerSec);
        ClutchLocked = true;
        Engagement = 1f;
        _shiftTimer = 0f;
    }

    /// <summary>
    /// Steps the drivetrain. <paramref name="axleSpeed"/> is the mean angular velocity of the driven wheels (rad/s).
    /// <paramref name="axleInertia"/> is the inertia (kg m^2) the driven axle presents to the clutch over one step: its
    /// wheels plus what their tyres hold them to (see VehicleSim.IntegrateWheelSpin); zero or less means unknown, and
    /// the wheels are then taken as tied to the car through the tyres. <paramref name="axleLoadTorque"/> is the torque the
    /// road puts on the driven wheels through the tyres (N m, positive when it holds them back), so the clutch knows what
    /// the wheels have to work against to speed up.
    /// Returns nothing; read <see cref="AxleTorque"/> and <see cref="ReflectedInertia"/> afterwards.
    /// </summary>
    public void Step(in DriverInputs input, float axleSpeed, float groundSpeed, float dt, float axleInertia = 0f, float axleLoadTorque = 0f)
    {
        HandleIgnition(input);
        SelectGear(input, groundSpeed, dt);
        var pedal = ResolveClutchPedal(input, groundSpeed, dt);
        Engagement = 1f - pedal;

        var throttle = input.Throttle * MathUtil.Clamp01(TorqueCut);
        if (Mode != TransmissionMode.ManualClutch && _shiftTimer > 0f) throttle = 0f; // lift fully during assisted shifts
        if (Launching || PreRevving) throttle = MathF.Min(throttle, LaunchGovernor(input.Throttle));
        else if (Synchronising)
        {
            // Taking up the next gear: the engine is brought down to the input shaft's speed with the throttle eased (the
            // driver's lifted foot, a dual-clutch box's ignition cut) instead of being fought by the clutch.
            var shaftRpm = MathF.Abs(axleSpeed * Ratio) * EngineModel.RpmPerRadPerSec;
            // Aimed a little under the shaft's speed: held at or above it, the engine only balanced the clutch there, a few
            // rpm short of the shaft, and the clutch never locked.
            throttle = MathF.Min(throttle, MathUtil.Clamp01(1f - (Engine.Rpm - shaftRpm + 100f) / 200f));
        }
        var engineTorque = Engine.Torque(throttle);
        var ratio = Ratio;
        var capacity = Engagement * _spec.ClutchMaxTorque;

        AxleTorque = 0f;
        ReflectedInertia = 0f;

        if (Gear == 0 || capacity < 1f || ratio == 0f)
        {
            ClutchLocked = false;
            IntegrateFreeEngine(engineTorque, dt);
        }
        else
        {
            var inputShaftSpeed = axleSpeed * ratio;
            var slip = Engine.AngularVelocity - inputShaftSpeed;

            if (ClutchLocked)
            {
                // Rigid coupling: the engine torque goes straight to the axle and the engine simply follows the wheels.
                // The coupling breaks if the pedal cannot hold the torque, if the driver opens the clutch, or if the
                // wheels change speed faster than the clutch can drag the flywheel along (brakes locking the wheels in
                // the air, for instance) — otherwise a braked axle would stall the engine through a rigid link.
                var dragTorque = engineTorque - _spec.EngineInertia * (inputShaftSpeed - Engine.AngularVelocity) / dt;
                if (MathF.Abs(engineTorque) > capacity || MathF.Abs(dragTorque) > capacity || Engagement < 0.35f)
                {
                    ClutchLocked = false;
                }
                else
                {
                    AxleTorque = engineTorque * ratio * _spec.DrivelineEfficiency;
                    ReflectedInertia = _spec.EngineInertia * ratio * ratio;
                    Engine.AngularVelocity = MathF.Max(inputShaftSpeed, 0f);
                }
            }

            if (!ClutchLocked)
            {
                // Slipping: Coulomb torque toward synchronisation, but never more than what would carry the
                // engine past the input shaft within this step. Without that cap the flywheel's small momentum
                // is delivered as a hammer blow to the wheels and the engine can never stall.
                // Two-inertia synchronisation: the torque that would bring engine and input shaft to the same
                // speed by the end of this step (axle inertia seen through the gearing).
                // The axle side carries the wheels and what the tyres tie them to. The caller knows that from the tyres'
                // stiffness: a gripping tyre makes the wheel heavy to turn within a step, a spinning one leaves it light.
                // Without that, the axle was taken as the wheels plus the whole car (m r^2), which is right for a
                // gripping tyre and badly wrong for a spinning one: once the tyre's force fell past its peak (0.18),
                // the clutch overshot every step and hammered the wheels back and forth.
                if (axleInertia <= 0f) axleInertia = 2f * _spec.WheelInertia + _spec.MassKg * _spec.WheelRadius * _spec.WheelRadius;
                var syncTorque = (slip + engineTorque * dt / _spec.EngineInertia + ratio * axleLoadTorque * dt / axleInertia)
                                 / (dt * (1f / _spec.EngineInertia + ratio * ratio * _spec.DrivelineEfficiency / axleInertia));
                var clutchTorque = MathUtil.Clamp(syncTorque, -capacity, capacity);
                var engineAccel = (engineTorque - clutchTorque) / _spec.EngineInertia;
                Engine.AngularVelocity = MathF.Max(0f, Engine.AngularVelocity + engineAccel * dt);
                AxleTorque = clutchTorque * ratio * _spec.DrivelineEfficiency;

                var newSlip = Engine.AngularVelocity - inputShaftSpeed;
                // A launch ends when the input shaft has caught the held engine up: the clutch locks there however lightly
                // the assist was holding it, as a driver lets the pedal up once the car has the revs.
                // In the assisted modes the clutch also locks once it can carry what the engine makes, however lightly it
                // is held: a take-up limited to the tyres' grip (Synchronising) may never reach the pedal's 0.6.
                var holds = Engagement > 0.6f || (Mode != TransmissionMode.ManualClutch && (Launching || capacity >= MathF.Abs(engineTorque)));
                if (holds && MathF.Abs(newSlip) < 2f)
                {
                    ClutchLocked = true;
                    Launching = false;
                    Engine.AngularVelocity = MathF.Max(inputShaftSpeed, 0f);
                }
            }
        }

        Engine.Tick(dt);
    }

    private void HandleIgnition(in DriverInputs input)
    {
        if (!input.Ignition || Engine.Running) return;
        // The cabinet lets you crank with the clutch down or in neutral; with assisted clutch we always allow it.
        var canStart = Mode != TransmissionMode.ManualClutch || Gear == 0 || input.Clutch > 0.7f;
        if (canStart) Engine.Start();
    }

    private void SelectGear(in DriverInputs input, float groundSpeed, float dt)
    {
        if (_shiftTimer > 0f) _shiftTimer -= dt;
        if (_autoShiftCooldown > 0f) _autoShiftCooldown -= dt;

        var target = Gear;
        if (Mode == TransmissionMode.Automatic)
        {
            if (Gear == 0 && Engine.Running) target = 1;
            if (Gear >= 1 && _autoShiftCooldown <= 0f)
            {
                var rpm = Engine.Rpm;
                // Shift points scale with the engine: up near the redline, down at a third of it.
                var redline = Engine.Spec.RedlineRpm;
                if (rpm > redline * 0.885f && Gear < _spec.ForwardGearCount) target = Gear + 1;
                else if (rpm < redline * 0.333f && Gear > 1 && input.Throttle < 0.9f) target = Gear - 1;
                else if (rpm < redline * 0.246f && Gear > 1) target = Gear - 1;
            }
            if (input.Gear == -1 && groundSpeed < 1f) target = -1;        // reverse is allowed at standstill only
            if (Gear == -1 && input.Gear != -1 && input.Gear != DriverInputs.NoGearChange) target = input.Gear;
        }
        else
        {
            // Sequential requests are ignored while a shift is in progress, so a held key cannot skip gears.
            if (input.Gear != DriverInputs.NoGearChange) target = input.Gear;
            else if (input.ShiftUp && _shiftTimer <= 0f) target = Gear + 1;
            else if (input.ShiftDown && _shiftTimer <= 0f) target = Gear - 1;
        }

        target = Math.Clamp(target, -1, _spec.ForwardGearCount);
        if (target == Gear) return;

        // Engaging reverse while rolling forward is refused, like the shifter gate on the cabinet.
        if (target == -1 && groundSpeed > 1.5f) return;

        Gear = target;
        ClutchLocked = false;
        _shiftTimer = _spec.ShiftSeconds;
        _autoShiftCooldown = 0.9f;
    }

    private float ResolveClutchPedal(in DriverInputs input, float groundSpeed, float dt)
    {
        PreRevving = false;
        Synchronising = false;
        if (Mode == TransmissionMode.ManualClutch) Launching = false;
        switch (Mode)
        {
            case TransmissionMode.ManualClutch:
                return MathUtil.Clamp01(input.Clutch);

            default:
                // Assisted clutch: open while shifting, open when the engine would otherwise die, and feed in
                // progressively on launch so the revs stay in the fat part of the curve.
                if (!Engine.Running || _shiftTimer > 0f || Gear == 0 || ClutchLocked || input.Throttle < 0.05f) Launching = false;
                if (!Engine.Running) return 1f;
                if (_shiftTimer > 0f) return 1f;
                if (Gear == 0) return 0f;
                // Rolling with the revs collapsing (hard braking in gear, wheels locked): open the clutch like a
                // torque converter would, so the assisted modes can never stall.
                var idle = Engine.Spec.IdleRpm;
                if (Engine.Rpm < idle * 1.58f && groundSpeed > 5f && input.Throttle < 0.3f) return 1f;
                // Brake and throttle at a standstill: the clutch waits open with the engine held at the launch revs (the
                // two-footed start of a driver, the brake-throttle-release of a launch control).
                if (input.Brake > 0.3f && input.Throttle > 0.3f && MathF.Abs(groundSpeed) < 0.5f && !ClutchLocked)
                {
                    PreRevving = true;
                    return 1f;
                }
                if (!ClutchLocked && input.Throttle >= 0.05f && MathF.Abs(groundSpeed) < 3f) Launching = true;
                if (Launching) return 1f - LaunchEngagement(input.Throttle);
                // Slip the clutch the way a driver launches a sports car: feed it in between 2200 and 4000 rpm.
                var launch = MathUtil.Clamp01((groundSpeed - 0.5f) / 6f);
                var revBased = MathUtil.Clamp01((Engine.Rpm - idle * 2.3f) / (idle * 1.9f));
                // Launch control of sorts: until the car rolls, the clutch only bites enough to let the engine hold
                // its revs against it, instead of locking at once and dragging the engine down to a lugging start.
                var engaged = MathF.Min(MathF.Max(launch, revBased), 0.35f + 0.65f * launch);
                if (input.Throttle < 0.05f && groundSpeed < 2.5f) engaged = 0f; // creep-free idle at standstill
                // Taking up a gear on the move (after a shift): the clutch passes no more than the tyres can put down, so the
                // flywheel's revs go into the road instead of into wheelspin; up to 0.18 it bit with its whole capacity, and a
                // V8 changing into second spun its rear tyres past their peak and lost a tenth of a second there.
                Synchronising = !ClutchLocked && MathF.Abs(groundSpeed) >= 3f;
                if (Synchronising) engaged = MathF.Min(engaged, TractionEngagement());
                return 1f - engaged;
        }
    }

    /// <summary>
    /// Revs a standing start holds for the driver's <paramref name="pedal"/>: the launch revs at full throttle, a gentle
    /// 1.6 times idle when only creeping off.
    /// </summary>
    private float LaunchTarget(float pedal)
    {
        var low = Engine.Spec.IdleRpm * 1.6f;
        return low + (Engine.Spec.LaunchRpm - low) * MathUtil.Clamp01(pedal);
    }

    /// <summary>
    /// Clutch engagement during a standing start: the clutch takes the engine's torque in proportion to how far the revs
    /// stand above a band below the target (none at its foot, all of it at the target, more above it), so the engine
    /// settles at the target with the clutch passing what it makes; and never more than the tyres can put down
    /// (<see cref="TractionTorque"/>), so the wheels do not spin away the drive. That is what a driver's left foot does on
    /// a good launch and what a launch control's clutch does on its own.
    /// </summary>
    private float LaunchEngagement(float pedal)
    {
        var target = LaunchTarget(pedal);
        var band = 0.3f * target;
        var share = MathUtil.Clamp((Engine.Rpm - (target - band)) / band, 0f, 2f);
        var capacity = Engine.FullTorque(Engine.Rpm) * MathUtil.Clamp01(pedal) * share;
        var ratio = MathF.Abs(Ratio);
        if (ratio > 0f && float.IsFinite(TractionTorque)) capacity = MathF.Min(capacity, TractionTorque / (ratio * _spec.DrivelineEfficiency));
        return MathUtil.Clamp01(capacity / _spec.ClutchMaxTorque);
    }

    /// <summary>Clutch engagement at which the clutch passes what the tyres can take (<see cref="TractionTorque"/>); 1 when that is unknown.</summary>
    private float TractionEngagement()
    {
        var ratio = MathF.Abs(Ratio);
        if (ratio <= 0f || !float.IsFinite(TractionTorque)) return 1f;
        return MathUtil.Clamp01(TractionTorque / (ratio * _spec.DrivelineEfficiency) / _spec.ClutchMaxTorque);
    }

    /// <summary>
    /// Throttle allowed while launching or held ready: eased as the revs pass the target so the engine waits there instead
    /// of running to the limiter while the clutch slips (or is open); the driver's own pedal is never exceeded.
    /// </summary>
    private float LaunchGovernor(float pedal) => MathUtil.Clamp01(1f - (Engine.Rpm - LaunchTarget(pedal) - 150f) / 300f);

    private void IntegrateFreeEngine(float engineTorque, float dt)
    {
        var accel = engineTorque / _spec.EngineInertia;
        Engine.AngularVelocity = MathF.Max(0f, Engine.AngularVelocity + accel * dt);
    }
}
