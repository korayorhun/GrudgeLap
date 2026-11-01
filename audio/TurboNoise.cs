namespace GrudgeLap.Audio;

/// <summary>
/// The turbocharger's state as its sound needs it, once a frame: the boost (0 to 1 of the most the engine makes), the
/// throttle, the revs against the redline, the turbo's share of the full-throttle torque (which sets the most boost it
/// makes), whether a valve vents the boost when the throttle shuts, and whether the listener is in the cabin.
/// </summary>
public readonly record struct TurboState(
    float Boost,
    float Throttle,
    float RevShare,
    float BoostShare,
    bool BlowOffValve,
    bool Interior);

/// <summary>
/// The turbocharger's sounds, synthesised (0.20, audit finding 11): the compressor's whistle following the shaft's speed,
/// the rush of the air it pushes, and what happens when the throttle shuts on boost, the vent of a blow-off valve or,
/// without one, the flutter of the compressor surging. Kept subtle: well under the engine. Allocates nothing per call;
/// one thread.
/// </summary>
/// <remarks>
/// <para>
/// The shaft. A centrifugal compressor's pressure ratio comes from its tip speed: the work it does on the air,
/// c_p T (PR^((gamma - 1) / gamma) - 1), is a fixed share of the tip speed squared (Euler's turbine equation), so the
/// shaft turns as the square root of that work. The most boost is taken from the engine's own figures, PR = 1 / (1 -
/// <see cref="TurboState.BoostShare"/>) (its torque without boost against with it: 1.8 for the turbo four, 0.8 bar),
/// reached at <see cref="MaxShaftHz"/> (150,000 rpm, a small passenger-car turbo's). Off boost the exhaust still turns the
/// turbine, up to about a third of that with the revs and the throttle. The shaft spools up behind the boost and runs on
/// for about a second when the throttle shuts.
/// </para>
/// <para>
/// The whistle. A compressor sings at its blade-passing frequency, the shaft's revolutions a second times its blades,
/// and more faintly at the shaft's own frequency from the rotor's residual unbalance (Raitor and Neise, Sound generation
/// in centrifugal compressors, J. Sound Vib. 314, 2008). With six full blades the blade tone climbs through the top of
/// hearing as the turbo spools (5 kHz at a third of its speed) and leaves it on full boost, which is why a turbo whistles
/// as it comes on boost and as it runs down, while the shaft's own tone stays at one or two kilohertz.
/// </para>
/// <para>
/// The shut throttle. With the throttle shut on boost the compressor pushes against a closed duct: its flow collapses and
/// reverses, refills and collapses again, a few to twenty times a second as the stored air bleeds away (deep surge, the
/// Helmholtz-like cycle of compressor and plenum; Greitzer, Surge and rotating stall in axial flow compressors, ASME J.
/// Eng. Power 98, 1976; measured on small automotive compressors by Galindo, Serrano, Climent and Tiseira, Experiments
/// and modelling of surge in small centrifugal compressor for automotive engines, Exp. Therm. Fluid Sci. 32, 2008): the
/// flutter, each cycle a chuff of air with the shaft stumbling. Production turbo cars fit a diverter valve that opens
/// on the manifold's vacuum and lets the boost round the compressor instead: a short soft rush (<see cref="TurboState.BlowOffValve"/>).
/// </para>
/// </remarks>
public sealed class TurboNoise
{
    /// <summary>The shaft's speed at the most boost, revolutions a second (150,000 rpm).</summary>
    public const float MaxShaftHz = 2500f;
    /// <summary>Full blades of the compressor wheel (splitter blades between them are not counted).</summary>
    public const int Blades = 6;
    /// <summary>Peak of the blade tone, of the shaft's own tone, RMS of the air's rush at full boost, and of the vent and the surge's chuffs.</summary>
    public const float WhistleGain = 0.012f, ShaftGain = 0.004f, RushGain = 0.02f, VentGain = 0.12f, SurgeGain = 0.3f;

    private readonly float _rate;
    private uint _noise = 0x2545F491u;
    private Biquad _rush, _vent, _chuff, _ventLow;
    private double _bladePhase, _shaftPhase, _thumpPhase;
    private float _shaft, _lastThrottle, _stored, _surgePhase;
    private bool _venting, _surging;
    private float _whistleLevel, _shaftLevel, _rushLevel;

    public TurboNoise(int sampleRate)
    {
        _rate = sampleRate;
        _rush.SetBandPass(3500f, 0.7f, _rate);
        _vent.SetHighPass(1200f, 0.7f, _rate);
        _ventLow.SetLowPass(6000f, 0.7f, _rate);
        _chuff.SetBandPass(1600f, 1.2f, _rate);
    }

    /// <summary>The shaft's speed, 0 to 1 of <see cref="MaxShaftHz"/>.</summary>
    public float Shaft => _shaft;
    /// <summary>The compressor is surging against a shut throttle.</summary>
    public bool Surging => _surging;
    /// <summary>The valve vents the boost.</summary>
    public bool Venting => _venting;

    /// <summary>
    /// The shaft's speed for a state, 0 to 1 (see the remarks): the square root of the compressor's work at the boost,
    /// against its work at the most boost, or what the exhaust alone turns it at, whichever is higher.
    /// </summary>
    public static float ShaftTarget(in TurboState state)
    {
        var share = Math.Clamp(state.BoostShare, 0.05f, 0.8f);
        var most = 1f / (1f - share);
        var ratio = 1f + Math.Clamp(state.Boost, 0f, 1f) * (most - 1f);
        // gamma = 1.4 for air: (gamma - 1) / gamma = 0.2857.
        var work = (MathF.Pow(ratio, 0.2857f) - 1f) / (MathF.Pow(most, 0.2857f) - 1f);
        var exhaust = 0.35f * Math.Clamp(state.RevShare, 0f, 1f) * (0.4f + 0.6f * Math.Clamp(state.Throttle, 0f, 1f));
        return MathF.Max(MathF.Sqrt(MathF.Max(work, 0f)), exhaust);
    }

    /// <summary>Adds the turbo's sounds for a frame of <paramref name="dt"/> seconds to <paramref name="output"/> (mixed into what is there).</summary>
    public void Render(Span<float> output, in TurboState state, float dt)
    {
        var target = ShaftTarget(state);
        // Spools behind the boost (which already lags the throttle) and runs on for about a second when it falls.
        _shaft += (target - _shaft) * Math.Clamp(dt / (target > _shaft ? 0.15f : 1.2f), 0f, 1f);
        // The throttle shuts on boost: the stored charge is vented or surges back through the compressor.
        if (state.Throttle < 0.15f && _lastThrottle >= 0.15f && state.Boost > 0.3f && !_venting && !_surging)
        {
            _stored = state.Boost;
            _venting = state.BlowOffValve;
            _surging = !state.BlowOffValve;
            _surgePhase = 0f;
        }
        // Opening the throttle again closes the valve or ends the surge.
        if (state.Throttle > 0.4f) _venting = _surging = false;
        _lastThrottle = state.Throttle;

        var place = state.Interior ? 0.6f : 1f;
        var whistleFrom = _whistleLevel;
        var shaftFrom = _shaftLevel;
        var rushFrom = _rushLevel;
        _whistleLevel = WhistleGain * _shaft * (0.3f + 0.7f * Math.Clamp(state.Throttle, 0f, 1f)) * place;
        _shaftLevel = ShaftGain * _shaft * _shaft * place;
        _rushLevel = RushGain * Math.Clamp(state.Boost, 0f, 1f) * Math.Clamp(state.Throttle, 0f, 1f) * place;
        var nyquist = 0.5f * _rate;
        var n = output.Length;
        for (var i = 0; i < n; i++)
        {
            var t = (i + 1f) / n;
            var shaftHz = MaxShaftHz * _shaft;
            if (_surging)
            {
                // Each surge cycle the shaft stumbles: its speed sags and recovers with the flow.
                shaftHz *= 1f - 0.05f * _stored * MathF.Exp(-_surgePhase * 6f);
            }
            var bladeHz = shaftHz * Blades;
            _bladePhase += bladeHz / _rate;
            if (_bladePhase >= 1.0) _bladePhase -= Math.Floor(_bladePhase);
            _shaftPhase += shaftHz / _rate;
            if (_shaftPhase >= 1.0) _shaftPhase -= Math.Floor(_shaftPhase);
            // The blade tone fades out as it nears the top of the band the voice is rendered at.
            var audible = Math.Clamp((0.9f * nyquist - bladeHz) / (0.15f * nyquist), 0f, 1f);
            var sample = MathF.Sin(MathF.Tau * (float)_bladePhase) * (whistleFrom + (_whistleLevel - whistleFrom) * t) * audible;
            sample += MathF.Sin(MathF.Tau * (float)_shaftPhase) * (shaftFrom + (_shaftLevel - shaftFrom) * t);
            var noise = Noise();
            sample += _rush.Process(noise) * 1.6f * (rushFrom + (_rushLevel - rushFrom) * t);

            if (_venting)
            {
                // The valve's rush: high-passed noise as the charge escapes, gone in about half a second.
                sample += _ventLow.Process(_vent.Process(noise)) * VentGain * _stored * place;
                _stored -= _stored * 4.5f / _rate;
                if (_stored < 0.01f) _venting = false;
            }
            else if (_surging)
            {
                // The cycle quickens with the stored pressure: about 14 a second at full boost, 7 as it runs out.
                var cycleHz = 6f + 8f * _stored;
                _surgePhase += cycleHz / _rate;
                if (_surgePhase >= 1f) _surgePhase -= 1f;
                // A chuff of air at the start of each cycle (20 ms), with a thump of pressure under it.
                var chuff = MathF.Exp(-_surgePhase / cycleHz / 0.02f);
                _thumpPhase += 110.0 / _rate;
                if (_thumpPhase >= 1.0) _thumpPhase -= 1.0;
                var thump = MathF.Sin(MathF.Tau * (float)_thumpPhase) * MathF.Exp(-_surgePhase / cycleHz / 0.012f);
                sample += (_chuff.Process(noise) * 2.2f + 0.6f * thump) * chuff * SurgeGain * _stored * place;
                _stored -= _stored * 2.5f / _rate;
                if (_stored < 0.05f) _surging = false;
            }
            output[i] += sample;
        }
    }

    /// <summary>White noise of unit RMS (uniform on ±√3), xorshift: cheap and the same on every run.</summary>
    private float Noise()
    {
        _noise ^= _noise << 13;
        _noise ^= _noise >> 17;
        _noise ^= _noise << 5;
        return ((_noise >> 8) * (1f / 16777216f) * 2f - 1f) * 1.7320508f;
    }
}
