using Godot;
using GrudgeLap.Physics;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// Turns the player's keys and pad into control positions. Held keys ramp: steering faster at low speed and
/// returning to centre on its own, pedals over a fraction of a second, which keeps keyboard play from being a
/// bang-bang affair and mimics how fast hands move a real wheel. A pad's stick and triggers are positions
/// already and are taken as they are, the stick through a gentle curve so that small corrections stay small;
/// whichever the player is moving wins (<see cref="InputSetup"/> has the layout).
/// </summary>
internal sealed class KeyboardDriver
{
    private float _steer;
    private float _throttle;
    private float _brake;
    private float _clutch;

    public float Steer => _steer;

    public DriverInputs Sample(float dt, float speedMps)
    {
        var stick = Input.GetJoyAxis(InputSetup.Pad, JoyAxis.LeftX);
        if (MathF.Abs(stick) > InputSetup.StickDeadzone)
        {
            // The stick is the wheel: its travel past the dead zone, bent a little for finesse near the centre,
            // reached quickly enough that a flick is a flick but without the stick's jitter.
            var travel = (MathF.Abs(stick) - InputSetup.StickDeadzone) / (1f - InputSetup.StickDeadzone);
            var target = MathF.Sign(stick) * MathF.Pow(travel, 1.6f);
            _steer = MathUtil.MoveToward(_steer, target, 8f * dt);
        }
        else
        {
            var left = Input.IsActionPressed(InputSetup.SteerLeft);
            var right = Input.IsActionPressed(InputSetup.SteerRight);
            var target = (right ? 1f : 0f) - (left ? 1f : 0f);

            // Full lock takes ~0.35 s when parked and ~0.7 s at speed; centring is quicker, like a self-aligning wheel.
            var speedFactor = MathUtil.Clamp01(speedMps / 50f);
            var rate = target != 0f ? MathUtil.Lerp(2.8f, 1.4f, speedFactor) : 4.5f;
            _steer = MathUtil.MoveToward(_steer, target, rate * dt);
        }

        _throttle = Pedal(_throttle, JoyAxis.TriggerRight, InputSetup.Throttle, dt, 0.18f, 0.12f);
        _brake = Pedal(_brake, JoyAxis.TriggerLeft, InputSetup.Brake, dt, 0.15f, 0.10f);
        _clutch = Ramp(_clutch, Input.IsActionPressed(InputSetup.Clutch), dt, 0.12f, 0.25f);

        var gear = DriverInputs.NoGearChange;
        if (Input.IsActionJustPressed(InputSetup.Gear1)) gear = 1;
        else if (Input.IsActionJustPressed(InputSetup.Gear2)) gear = 2;
        else if (Input.IsActionJustPressed(InputSetup.Gear3)) gear = 3;
        else if (Input.IsActionJustPressed(InputSetup.Gear4)) gear = 4;
        else if (Input.IsActionJustPressed(InputSetup.Gear5)) gear = 5;
        else if (Input.IsActionJustPressed(InputSetup.Gear6)) gear = 6;
        else if (Input.IsActionJustPressed(InputSetup.Gear7)) gear = 7;
        else if (Input.IsActionJustPressed(InputSetup.Neutral)) gear = 0;
        else if (Input.IsActionJustPressed(InputSetup.Reverse)) gear = -1;

        return new DriverInputs
        {
            Steer = _steer,
            Throttle = _throttle,
            Brake = _brake,
            Clutch = _clutch,
            Gear = gear,
            ShiftUp = Input.IsActionJustPressed(InputSetup.ShiftUp),
            ShiftDown = Input.IsActionJustPressed(InputSetup.ShiftDown),
            Ignition = Input.IsActionJustPressed(InputSetup.Ignition),
        };
    }

    public void Reset()
    {
        _steer = 0f;
        _throttle = 0f;
        _brake = 0f;
        _clutch = 0f;
    }

    /// <summary>A pedal: the trigger's position while it is pressed at all, the key's ramp otherwise.</summary>
    private static float Pedal(float current, JoyAxis trigger, string action, float dt, float riseSeconds, float fallSeconds)
    {
        var pressed = Input.GetJoyAxis(InputSetup.Pad, trigger);
        if (pressed > 0.02f) return MathUtil.Clamp01(pressed);
        return Ramp(current, Input.IsActionPressed(action), dt, riseSeconds, fallSeconds);
    }

    private static float Ramp(float current, bool pressed, float dt, float riseSeconds, float fallSeconds)
    {
        var target = pressed ? 1f : 0f;
        var rate = pressed ? 1f / riseSeconds : 1f / fallSeconds;
        return MathUtil.MoveToward(current, target, rate * dt);
    }
}
