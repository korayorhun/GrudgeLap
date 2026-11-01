namespace GrudgeLap.Audio;

/// <summary>
/// The state of the car that its synthesised noises follow, once a frame: road speed, where the listener is, the
/// tyres' spray and the anti-lock modulator (<see cref="TyreMix"/>), and the gearbox: the ratio of the gear in (signed,
/// negative in reverse, 0 in neutral), the speed of its output shaft (the wheels' revolutions a second times the final
/// drive) and the share of the engine's peak torque going through the gears either way (driving or on the overrun).
/// </summary>
public readonly record struct BodyState(
    float Speed,
    bool Interior,
    float Spray,
    float Abs,
    float AbsCycle,
    float GearRatio,
    float OutputShaftHz,
    float Torque);

/// <summary>
/// The car's noises that no recording carries, synthesised per sample (0.19): the wind, the spray of a wet road, the
/// whine of the gears and the final drive, and the anti-lock modulator's pump. Allocates nothing per call; one thread.
/// </summary>
/// <remarks>
/// <para>
/// Wind: the turbulence round the pillars, mirrors and seals is broadband noise whose level grows as the square of the
/// speed (12 dB per doubling; measured cabin wind noise grows by 40 to 60 log10 v, so this is the gentle end of it) and
/// whose spectrum moves up with the speed (vortex shedding scales with v, Strouhal), with slow gusts of buffeting.
/// Calibrated against the tyres' roar (<see cref="TyreMix.RollGain"/> on the road recording) to take over from it at
/// about 110 km/h in the cabin, where the wind takes over in real cars; above about 200 km/h it grows no more, so it
/// never buries the engine. Up to 0.18 the wind was silent below 43 km/h and some 20 dB under the roar at 110 km/h.
/// </para>
/// <para>
/// Gear whine: a gearbox sings at its meshing frequencies, a shaft's revolutions a second times its teeth. A layout of
/// the usual countershaft box is assumed: a constant-mesh pair of 20 and 30 teeth and gear pairs of 50 teeth together,
/// so the selected pair of ratio p (overall ratio over the constant-mesh 1.5) meshes at the output shaft's speed times
/// 50 p / (1 + p) and the constant-mesh pair at the input shaft's speed times 20. The load on the teeth grows with the
/// ratio, so first gear sings loudest; a straight-cut reverse (no helix) whines three times as loud; a direct top gear
/// (1:1) locks input to output and carries no load through the teeth. The hypoid final drive adds its own tone at the
/// output shaft's speed times its pinion's ten teeth, rising with road speed. Each tone is modulated by its shaft's
/// runout. Through the transmission tunnel the cabin hears them better than the outside.
/// </para>
/// <para>
/// The anti-lock pump: while the modulator holds pressure back its return pump's motor runs, a growl of pressure pulses
/// at about 45 Hz rich in harmonics, pulsing with the valves' cycle; heard and felt in the cabin, little outside.
/// </para>
/// </remarks>
public sealed class BodyNoise
{
    /// <summary>The wind's RMS level at <see cref="TyreMix.RollReferenceSpeed"/> in the cabin, on the scale of the tyres' loops.</summary>
    public const float WindGain = 0.0456f;
    /// <summary>The wet road's spray at <see cref="TyreMix.RollReferenceSpeed"/> on a streaming road, RMS.</summary>
    public const float SprayGain = 0.05f;
    /// <summary>First gear's whine at full torque in the cabin, peak.</summary>
    public const float WhineGain = 0.016f;
    /// <summary>The final drive's whine at full torque, high speed, in the cabin, peak.</summary>
    public const float FinalDriveGain = 0.006f;
    /// <summary>The anti-lock pump's growl in the cabin, peak.</summary>
    public const float AbsGain = 0.05f;
    /// <summary>Teeth of the input shaft's constant-mesh gear and of a gear pair together (see the remarks).</summary>
    public const int InputTeeth = 20, PairTeeth = 50, PinionTeeth = 10;
    public const float ConstantMeshRatio = 1.5f;
    /// <summary>Centre of the wind's band at speed v: 150 Hz plus 10 Hz per m/s (450 Hz at 110 km/h).</summary>
    private const float WindBase = 150f, WindPerMps = 10f;
    private const float WindTopSpeed = 55f;

    private readonly float _rate;
    private uint _noise = 0x9E3779B9u;
    private Biquad _windBand, _windLow, _spray, _absTone;
    private bool _started;
    private float _gust = 1f, _gustTarget = 1f;
    private int _gustCountdown;
    private double _pairPhase, _meshPhase, _finalPhase, _shaftPhase, _pumpPhase;
    private float _wind, _sprayLevel, _whine, _finalLevel, _abs, _windCentre = WindBase;
    private float _pairHz, _meshHz, _finalHz, _shaftHz;

    public BodyNoise(int sampleRate)
    {
        _rate = sampleRate;
        _spray.SetBandPass(2600f, 0.6f, _rate);
        _windLow.SetLowPass(140f, 0.7f, _rate);
        _absTone.SetLowPass(900f, 0.7f, _rate);
        _windBand.SetBandPass(WindBase, 0.6f, _rate);
    }

    public int SampleRate => (int)_rate;

    /// <summary>
    /// Adds the noises for <paramref name="state"/> to <paramref name="output"/> (it is mixed into what is there), gliding
    /// from the previous call's state across the block.
    /// </summary>
    public void Render(Span<float> output, in BodyState state)
    {
        if (!_started)
        {
            _started = true;
        }
        if (output.IsEmpty) return;
        var n = output.Length;
        // Levels and frequencies are taken at the block's ends and glided linearly per sample; the filters are retuned
        // every 32 samples, which keeps the cost at a few multiplies a sample.
        var windFrom = _wind;
        var windTo = WindLevel(state);
        var sprayFrom = _sprayLevel;
        var sprayTo = SprayGain * state.Spray * (state.Interior ? 0.8f : 1f);
        var (whineTo, pairTo, meshTo, finalTo, finalHzTo, shaftTo) = Gears(state);
        var whineFrom = _whine;
        var finalFrom = _finalLevel;
        var pairFrom = _pairHz == 0f ? pairTo : _pairHz;
        var meshFrom = _meshHz == 0f ? meshTo : _meshHz;
        var finalHzFrom = _finalHz == 0f ? finalHzTo : _finalHz;
        var shaftFrom = _shaftHz == 0f ? shaftTo : _shaftHz;
        var absFrom = _abs;
        var absTo = AbsGain * state.Abs * (0.55f + 0.45f * state.AbsCycle) * (state.Interior ? 1f : 0.35f);
        var centreTo = WindBase + WindPerMps * MathF.Min(MathF.Abs(state.Speed), WindTopSpeed);
        var centreFrom = _windCentre;
        var nyquist = 0.45f * _rate;

        for (var i = 0; i < n; i++)
        {
            var t = (i + 1f) / n;
            var sample = 0f;
            var wind = windFrom + (windTo - windFrom) * t;
            if ((i & 31) == 0)
            {
                _windCentre = centreFrom + (centreTo - centreFrom) * t;
                _windBand.SetBandPass(_windCentre, 0.6f, _rate);
                if (--_gustCountdown <= 0)
                {
                    // A new gust every 0.3 to 1 s: the buffeting of turbulent air on the body.
                    _gustTarget = 0.75f + 0.5f * Uniform();
                    _gustCountdown = (int)((0.3f + 0.7f * Uniform()) * _rate / 32f);
                }
            }
            if (wind > 1e-5f)
            {
                _gust += (_gustTarget - _gust) * 0.0004f;
                var white = Noise();
                // The band-pass's RMS grows as the square root of its width, which grows with its centre: normalised
                // so the level follows WindLevel alone. White noise of unit RMS through it, plus a low rumble.
                var band = _windBand.Process(white) * MathF.Sqrt(WindBase / _windCentre) * 5.5f;
                var rumble = _windLow.Process(white) * 4f;
                sample += (band + rumble * (_gust - 0.6f)) * wind * _gust;
            }
            var spray = sprayFrom + (sprayTo - sprayFrom) * t;
            if (spray > 1e-5f) sample += _spray.Process(Noise()) * 1.68f * spray;

            var whine = whineFrom + (whineTo - whineFrom) * t;
            var finalLevel = finalFrom + (finalTo - finalFrom) * t;
            if (whine > 1e-6f || finalLevel > 1e-6f)
            {
                var pairHz = pairFrom + (pairTo - pairFrom) * t;
                var meshHz = meshFrom + (meshTo - meshFrom) * t;
                var finalHz = finalHzFrom + (finalHzTo - finalHzFrom) * t;
                var shaftHz = shaftFrom + (shaftTo - shaftFrom) * t;
                _pairPhase = Wrap(_pairPhase + pairHz / _rate);
                _meshPhase = Wrap(_meshPhase + meshHz / _rate);
                _finalPhase = Wrap(_finalPhase + finalHz / _rate);
                _shaftPhase = Wrap(_shaftPhase + shaftHz / _rate);
                // Runout of the shaft: the tooth contact breathes once a revolution, which puts sidebands on the tone.
                var runout = 1f + 0.3f * MathF.Sin(MathF.Tau * (float)_shaftPhase);
                var tone = 0f;
                if (pairHz < nyquist) tone += MathF.Sin(MathF.Tau * (float)_pairPhase);
                if (meshHz < nyquist) tone += 0.5f * MathF.Sin(MathF.Tau * (float)_meshPhase);
                sample += tone * whine * runout;
                if (finalHz < nyquist) sample += MathF.Sin(MathF.Tau * (float)_finalPhase) * finalLevel * runout;
            }

            var abs = absFrom + (absTo - absFrom) * t;
            if (abs > 1e-5f)
            {
                _pumpPhase = Wrap(_pumpPhase + 45.0 / _rate);
                // The pump's pistons: sharp pressure pulses once a turn, softened by the pipes; and the motor's hum.
                var s = MathF.Max(MathF.Sin(MathF.Tau * (float)_pumpPhase), 0f);
                var pulse = s * s * s * s * s * s * 2.2f - 0.35f;
                sample += _absTone.Process(pulse + 0.15f * Noise()) * abs;
            }
            output[i] += sample;
        }
        _wind = windTo;
        _sprayLevel = sprayTo;
        _whine = whineTo;
        _finalLevel = finalTo;
        _pairHz = pairTo;
        _meshHz = meshTo;
        _finalHz = finalHzTo;
        _shaftHz = shaftTo;
        _abs = absTo;
    }

    /// <summary>The wind's RMS level at the state's speed: as v squared up to <see cref="WindTopSpeed"/>; a little less from outside.</summary>
    public static float WindLevel(in BodyState state)
    {
        var pace = MathF.Min(MathF.Abs(state.Speed), WindTopSpeed) / TyreMix.RollReferenceSpeed;
        return WindGain * pace * pace * (state.Interior ? 1f : 0.7f);
    }

    /// <summary>The whine's level and the meshing frequencies for a state (see the remarks).</summary>
    private static (float Whine, float PairHz, float MeshHz, float Final, float FinalHz, float ShaftHz) Gears(in BodyState state)
    {
        var ratio = MathF.Abs(state.GearRatio);
        var output = MathF.Abs(state.OutputShaftHz);
        var place = state.Interior ? 1f : 0.4f;
        var torque = Math.Clamp(state.Torque, 0f, 1f);
        // Below a few revolutions a second the tones are too low and too faint to hear; they fade in over 2-8 rev/s.
        var turning = Math.Clamp((output - 2f) / 6f, 0f, 1f);
        var finalLevel = FinalDriveGain * Math.Clamp(output / 50f, 0f, 1f) * (0.3f + 0.7f * torque) * place * turning;
        if (ratio < 0.01f) return (0f, 0f, 0f, finalLevel, output * PinionTeeth, output);
        var pair = ratio / ConstantMeshRatio;
        var pairHz = output * PairTeeth * pair / (1f + pair);
        var meshHz = output * ratio * InputTeeth;
        var direct = MathF.Abs(ratio - 1f) < 0.03f ? 0.15f : 1f;
        var cut = state.GearRatio < 0f ? 3f : 1f;
        var whine = WhineGain * MathF.Pow(ratio / 3.1f, 1.5f) * (0.25f + 0.75f * torque) * direct * cut * place * turning;
        return (whine, pairHz, meshHz, finalLevel, output * PinionTeeth, output * ratio);
    }

    private static double Wrap(double phase) => phase >= 1.0 ? phase - 1.0 : phase;

    /// <summary>White noise of unit RMS (uniform on ±√3), xorshift: cheap and the same on every run.</summary>
    private float Noise() => (Uniform() * 2f - 1f) * 1.7320508f;

    private float Uniform()
    {
        _noise ^= _noise << 13;
        _noise ^= _noise >> 17;
        _noise ^= _noise << 5;
        return (_noise >> 8) * (1f / 16777216f);
    }
}
