namespace GrudgeLap.Audio;

/// <summary>
/// The turbocharger's state as its sound needs it, once a frame: the boost (0 to 1 of the most the engine makes), the
/// throttle, the revs against the redline, the turbo's share of the full-throttle torque (which sets the most boost it
/// makes), what the boost does when the driver lifts (<see cref="TurboLift"/>), whether the listener is in the cabin, and
/// the corner of the low-pass the cabin hears the engine bay through (<see cref="EngineVoicing.CabinCutoff"/>).
/// </summary>
public readonly record struct TurboState(
    float Boost,
    float Throttle,
    float RevShare,
    float BoostShare,
    TurboLift Lift,
    bool Interior,
    float CabinCutoff = 2200f);

/// <summary>
/// What the turbo's sound does when the driver lifts on boost (see <see cref="TurboNoise"/>'s remarks): the game maps the
/// engine's own figure (the simulation's <c>EngineSpec.LiftOff</c>) onto it.
/// </summary>
public enum TurboLift
{
    /// <summary>A diverter valve vents the boost round the compressor as the throttle shuts: a short soft rush.</summary>
    Vent,

    /// <summary>No valve: the compressor surges against the shut throttle and flutters.</summary>
    Surge,

    /// <summary>No throttle shuts on the boost (a diesel): no vent and no surge, the shaft just runs down.</summary>
    SpoolDown,
}

/// <summary>
/// The turbocharger's sounds, synthesised (0.20, audit finding 11): the compressor's whistle following the shaft's speed,
/// the rush of the air it pushes, and what happens when the throttle shuts on boost, the vent of a blow-off valve or,
/// without one, the flutter of the compressor surging; on a diesel, whose turbo has no throttle shut on it, neither
/// (diesel6). Kept subtle: well under the engine. Allocates nothing per call; one thread.
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
/// as it comes on boost and as it runs down, while the shaft's own tone stays at one to two and a half kilohertz. That
/// whine is the tone that is heard on boost: the blade tone is then at 15 kHz, past what the game mixes (22.05 kHz), and
/// with the whine at a third of <see cref="ShaftGain"/> the turbo four on full boost was under its engine in every
/// third-octave band (0.20 review). It is now level with the engine in its own band, outside and in the cabin, a tone
/// over its masked threshold, and the whole turbo is still some 13 LU under the engine. (Set against the rally hatch's
/// run-up once the flutter in its recording was cut out of it: the voice had been held 1.5 LU under the common loudness
/// by that stretch's peaks, and at the common loudness the whine at 0.085 sat a decibel under the engine in its band.)
/// </para>
/// <para>
/// The cabin. The turbo sits in the engine bay and reaches the cabin through the same panels as the engine: low-passed
/// at the body's corner (<see cref="TurboState.CabinCutoff"/>) and at 0.6 of its level, crossfaded over 0.15 s when
/// the view changes. (Heard unfiltered, its rush stood 11 dB over the cabin's engine at 6.4 kHz.)
/// </para>
/// <para>
/// The shut throttle. With the throttle shut on boost the compressor pushes against a closed duct: its flow collapses and
/// reverses, refills and collapses again, a few to twenty times a second as the stored air bleeds away (deep surge, the
/// Helmholtz-like cycle of compressor and plenum; Greitzer, Surge and rotating stall in axial flow compressors, ASME J.
/// Eng. Power 98, 1976; measured on small automotive compressors by Galindo, Serrano, Climent and Tiseira, Experiments
/// and modelling of surge in small centrifugal compressor for automotive engines, Exp. Therm. Fluid Sci. 32, 2008): the
/// flutter, each cycle a chuff of air with the shaft stumbling. Production turbo cars fit a diverter valve that opens
/// on the manifold's vacuum and lets the boost round the compressor instead: a short soft rush (<see cref="TurboLift.Vent"/>,
/// <see cref="TurboLift.Surge"/> without one).
/// </para>
/// <para>
/// The diesel (diesel6). A diesel has no throttle plate: its load is the fuel injected, so lifting shuts nothing on the
/// boost. The fuel is cut, the air goes on through the cylinders and the turbine, the charge falls with the exhaust's
/// energy, and the turbo, fitted without a blow-off valve as diesels' are, spools down: its whine follows the shaft down
/// over the second it runs on, with no rush and no flutter (<see cref="TurboLift.SpoolDown"/>). The air drawn through
/// the compressor, which carries its tones out of the inlet and makes its rush, follows the falling boost rather than
/// the pedal, so the whine fades with the shaft instead of dropping when the foot comes off.
/// </para>
/// </remarks>
public sealed class TurboNoise
{
    /// <summary>The shaft's speed at the most boost, revolutions a second (150,000 rpm).</summary>
    public const float MaxShaftHz = 2500f;
    /// <summary>Full blades of the compressor wheel (splitter blades between them are not counted).</summary>
    public const int Blades = 6;
    /// <summary>Peak of the blade tone, of the shaft's own tone, RMS of the air's rush at full boost, and of the vent and the surge's chuffs.</summary>
    public const float WhistleGain = 0.012f, ShaftGain = 0.1f, RushGain = 0.02f, VentGain = 0.12f, SurgeGain = 0.3f;
    /// <summary>
    /// The turbo's share of the torque the tones' gains were set at: the rally hatch's (EngineSpec.RallyTurboFour), against
    /// whose run-up they were set (0.20 review). Its tones play at <see cref="WhistleGain"/> and <see cref="ShaftGain"/>
    /// exactly; a turbo working at another pressure ratio is scaled by <see cref="TonalGain"/>.
    /// </summary>
    public const float ReferenceBoostShare = 0.48f;

    private readonly float _rate;
    private uint _noise = 0x2545F491u;
    private Biquad _rush, _vent, _chuff, _ventLow, _cabin;
    private float _cabinCutoff, _interior;
    private bool _started;
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

    /// <summary>
    /// The level of the compressor's tones on full boost against the reference turbo's (<see cref="ReferenceBoostShare"/>),
    /// from the pressure ratio each makes (diesel6). The ratio is set by the blades' tip speed: the work done on the air
    /// grows as the tip speed squared (Euler's turbine equation, as in the remarks), so the tip speed goes as the square
    /// root of the work. A fan's or a compressor's sound power grows about as the fifth power of its tip speed (the fan
    /// laws' sound-power term, 50 log10 of the speed ratio at a fixed size, as AMCA Publication 301 applies it), its
    /// pressure as the tip speed to the 2.5, so as the work to the 1.25. The pressure ratios are the game's own, from each
    /// engine's torque without boost against with it (<see cref="TurboState.BoostShare"/>): 2.5 bar absolute for the fleet
    /// saloon's diesel against 1.9 for the rally hatch's, so its tones are 4.1 dB louder, as its clatter, broadband around
    /// the whine's 2.5 kHz, would otherwise mask them (1.7 dB under its run-up there outside, 3.8 in the cabin). The OM 651's
    /// 150 kW tune is described as running just under 3 bar (EngineSpec.DieselFour), which the same law would make 6.3 dB;
    /// the game's 2.5 is kept so that the sound's boost is the one its torque model makes (diesel6 review).
    /// </summary>
    public static float TonalGain(float boostShare) => MathF.Pow(Work(boostShare) / Work(ReferenceBoostShare), 1.25f);

    /// <summary>The compressor's work at the most boost a turbo with this share of the torque makes, as a share of c_p T.</summary>
    private static float Work(float boostShare)
    {
        var share = Math.Clamp(boostShare, 0.05f, 0.8f);
        // gamma = 1.4 for air: (gamma - 1) / gamma = 0.2857.
        return MathF.Pow(1f / (1f - share), 0.2857f) - 1f;
    }

    /// <summary>Adds the turbo's sounds for a frame of <paramref name="dt"/> seconds to <paramref name="output"/> (mixed into what is there).</summary>
    public void Render(Span<float> output, in TurboState state, float dt)
    {
        if (!_started)
        {
            _interior = state.Interior ? 1f : 0f;
            _started = true;
        }
        var target = ShaftTarget(state);
        // Spools behind the boost (which already lags the throttle) and runs on for about a second when it falls.
        _shaft += (target - _shaft) * Math.Clamp(dt / (target > _shaft ? 0.15f : 1.2f), 0f, 1f);
        // The throttle shuts on boost: the stored charge is vented or surges back through the compressor. A diesel's
        // lift shuts nothing on it, and the shaft only runs down (TurboLift.SpoolDown).
        if (state.Throttle < 0.15f && _lastThrottle >= 0.15f && state.Boost > 0.3f && !_venting && !_surging)
        {
            _stored = state.Boost;
            _venting = state.Lift == TurboLift.Vent;
            _surging = state.Lift == TurboLift.Surge;
            _surgePhase = 0f;
        }
        // Opening the throttle again closes the valve or ends the surge.
        if (state.Throttle > 0.4f) _venting = _surging = false;
        _lastThrottle = state.Throttle;

        if (state.CabinCutoff != _cabinCutoff)
        {
            _cabinCutoff = state.CabinCutoff;
            _cabin.SetLowPass(_cabinCutoff, 0.7f, _rate);
        }
        var cabinFollow = 1f - MathF.Exp(-1f / (0.15f * _rate));
        var whistleFrom = _whistleLevel;
        var shaftFrom = _shaftLevel;
        var rushFrom = _rushLevel;
        // The air through the compressor: a petrol engine's throttle sets it, so it collapses the moment the throttle shuts.
        // A diesel's lift shuts nothing (TurboLift.SpoolDown): the charge keeps flowing as it falls, so the flow follows
        // the boost down instead of the pedal. Held, the boost settles at most at the throttle (its target is the throttle
        // times the revs' share), so this matters on the lift and as the throttle eases: the whine had dropped 10.5 dB in
        // the first 50 ms while its pitch had barely moved, a step down where the spool-down should be heard (diesel6
        // review). The petrol turbos are unchanged.
        var throttle = Math.Clamp(state.Throttle, 0f, 1f);
        var flow = state.Lift == TurboLift.SpoolDown ? MathF.Max(throttle, Math.Clamp(state.Boost, 0f, 1f)) : throttle;
        // Both tones leave mostly through the compressor's inlet with the air it draws: fainter as that flow falls.
        var drawn = 0.3f + 0.7f * flow;
        // Louder for a turbo working at a higher pressure ratio than the reference (exactly 1 for the reference's).
        var tonal = TonalGain(state.BoostShare);
        _whistleLevel = WhistleGain * tonal * _shaft * drawn;
        _shaftLevel = ShaftGain * tonal * _shaft * _shaft * drawn;
        _rushLevel = RushGain * Math.Clamp(state.Boost, 0f, 1f) * flow;
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
                sample += _ventLow.Process(_vent.Process(noise)) * VentGain * _stored;
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
                sample += (_chuff.Process(noise) * 2.2f + 0.6f * thump) * chuff * SurgeGain * _stored;
                _stored -= _stored * 2.5f / _rate;
                if (_stored < 0.05f) _surging = false;
            }
            // Both paths run all the time, so a change of view crossfades without the filter starting cold.
            _interior += ((state.Interior ? 1f : 0f) - _interior) * cabinFollow;
            var cabin = _cabin.Process(sample) * 0.6f;
            output[i] += sample + (cabin - sample) * _interior;
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
