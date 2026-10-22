namespace GrudgeLap.Physics.Vehicle;

/// <summary>
/// Engine + clutch + four-speed gearbox feeding the rear axle. The clutch is a lock/slip state machine:
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
    /// <summary>Torque delivered to the rear axle (sum of both wheels) during the last step.</summary>
    public float AxleTorque { get; private set; }
    /// <summary>Extra inertia the rear wheels carry while the clutch is locked, already reflected through the gearing.</summary>
    public float ReflectedInertia { get; private set; }

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
    /// Steps the drivetrain. <paramref name="axleSpeed"/> is the mean rear wheel angular velocity (rad/s).
    /// Returns nothing; read <see cref="AxleTorque"/> and <see cref="ReflectedInertia"/> afterwards.
    /// </summary>
    public void Step(in DriverInputs input, float axleSpeed, float groundSpeed, float dt)
    {
        HandleIgnition(input);
        SelectGear(input, groundSpeed, dt);
        var pedal = ResolveClutchPedal(input, groundSpeed, dt);
        Engagement = 1f - pedal;

        var throttle = input.Throttle;
        if (Mode != TransmissionMode.ManualClutch && _shiftTimer > 0f) throttle = 0f; // lift fully during assisted shifts
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
                // The axle side carries the wheels and, through the tyres, the car itself (m r^2); without the car
                // the estimate is tiny, the clutch transmits almost nothing and slips for seconds.
                var axleInertia = 2f * _spec.WheelInertia + _spec.MassKg * _spec.WheelRadius * _spec.WheelRadius;
                var syncTorque = (slip + engineTorque * dt / _spec.EngineInertia)
                                 / (dt * (1f / _spec.EngineInertia + ratio * ratio * _spec.DrivelineEfficiency / axleInertia));
                var clutchTorque = MathUtil.Clamp(syncTorque, -capacity, capacity);
                var engineAccel = (engineTorque - clutchTorque) / _spec.EngineInertia;
                Engine.AngularVelocity = MathF.Max(0f, Engine.AngularVelocity + engineAccel * dt);
                AxleTorque = clutchTorque * ratio * _spec.DrivelineEfficiency;

                var newSlip = Engine.AngularVelocity - inputShaftSpeed;
                if (Engagement > 0.6f && MathF.Abs(newSlip) < 2f)
                {
                    ClutchLocked = true;
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
        _shiftTimer = 0.28f;
        _autoShiftCooldown = 0.9f;
    }

    private float ResolveClutchPedal(in DriverInputs input, float groundSpeed, float dt)
    {
        switch (Mode)
        {
            case TransmissionMode.ManualClutch:
                return MathUtil.Clamp01(input.Clutch);

            default:
                // Assisted clutch: open while shifting, open when the engine would otherwise die, and feed in
                // progressively on launch so the revs stay in the fat part of the curve.
                if (!Engine.Running) return 1f;
                if (_shiftTimer > 0f) return 1f;
                if (Gear == 0) return 0f;
                // Rolling with the revs collapsing (hard braking in gear, wheels locked): open the clutch like a
                // torque converter would, so the assisted modes can never stall.
                var idle = Engine.Spec.IdleRpm;
                if (Engine.Rpm < idle * 1.58f && groundSpeed > 5f && input.Throttle < 0.3f) return 1f;
                // Slip the clutch the way a driver launches a sports car: feed it in between 2200 and 4000 rpm.
                var launch = MathUtil.Clamp01((groundSpeed - 0.5f) / 6f);
                var revBased = MathUtil.Clamp01((Engine.Rpm - idle * 2.3f) / (idle * 1.9f));
                // Launch control of sorts: until the car rolls, the clutch only bites enough to let the engine hold
                // its revs against it, instead of locking at once and dragging the engine down to a lugging start.
                var engaged = MathF.Min(MathF.Max(launch, revBased), 0.35f + 0.65f * launch);
                if (input.Throttle < 0.05f && groundSpeed < 2.5f) engaged = 0f; // creep-free idle at standstill
                return 1f - engaged;
        }
    }

    private void IntegrateFreeEngine(float engineTorque, float dt)
    {
        var accel = engineTorque / _spec.EngineInertia;
        Engine.AngularVelocity = MathF.Max(0f, Engine.AngularVelocity + accel * dt);
    }
}
