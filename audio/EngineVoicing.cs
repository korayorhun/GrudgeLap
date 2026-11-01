namespace GrudgeLap.Audio;

/// <summary>Where the engine sits in the car, as far as its sound in the cabin goes.</summary>
public enum EnginePlacement : byte
{
    /// <summary>Ahead of the cabin, behind the bulkhead under the bonnet; the exhaust runs under the floor to the tail.</summary>
    Front,
    /// <summary>Behind the seats, ahead of the rear axle: the engine and its intake a metre from the driver's head.</summary>
    Mid,
}

/// <summary>
/// How a car's body passes its engine's sound to the listener (0.20, audit finding 10): the cabin's transfer of the
/// engine from where it sits, and from outside the tailpipe's beam towards a camera behind the car. One recording per
/// engine carries intake and exhaust together; the voicing reshapes it per car and per viewpoint.
/// </summary>
/// <remarks>
/// <para>
/// The cabin. Airborne engine noise reaches the driver through the panels between them, whose transmission loss rises
/// with frequency (the mass law, about 6 dB per octave, steeper where an absorbing layer lines them; Beranek and Vér,
/// Noise and Vibration Control Engineering, 1992; Harrison, Vehicle Refinement, 2004): the cabin hears the engine
/// low-passed. A front engine sits behind the dash panel, the most heavily treated panel of a car, and its exhaust under
/// the floor reaches the cabin mostly as low-frequency boom: <see cref="CabinCutoff"/> 2.2 kHz. A mid engine sits a
/// metre behind the driver's head with a single bulkhead between, often glazed (the Countach's rear bulkhead window, the
/// 458's engine behind glass), and its intake plenum right there: the cabin hears far more of its top (4 kHz) and of its
/// induction roar, the firing harmonics and the intake's runner and box resonances a few hundred hertz up, which grows
/// with the air drawn (<see cref="CabinIntakeDb"/> at full throttle).
/// </para>
/// <para>
/// The boom: the cabin's first longitudinal acoustic mode, a half wavelength across its length, c / 2L (Nefske, Wolf
/// and Howell, Structural-acoustic finite element analysis of the automobile passenger compartment, J. Sound Vib. 80,
/// 1982). The two-seat cell of a mid-engined car (about 1.3 m) booms near 130 Hz, which was the one filter of 0.19; a
/// hatchback's or an estate's cabin, open to its load space, is twice as long and booms an octave lower, under the
/// firing frequency of a four at speed.
/// </para>
/// <para>
/// Outside. A tailpipe radiates evenly while its radius is small against the wavelength and beams along its axis above
/// ka = 1 (Levine and Schwinger, On the radiation of sound from an unflanged circular pipe, Phys. Rev. 73, 1948): a
/// camera behind the car, on that axis, hears the exhaust's top from about c / 2 pi a up (1.8 kHz for a 30 mm pipe)
/// some <see cref="TailpipeDb"/> louder than the side and the cabin do. A mid engine's intake opens on the rear deck and
/// is heard behind the car too, at half its cabin share.
/// </para>
/// </remarks>
public readonly record struct EngineVoicing(EnginePlacement Placement, float CabinLength, float TailpipeRadius = 0.03f)
{
    /// <summary>Speed of sound in the cabin's air, m/s (20 degrees C).</summary>
    public const float SpeedOfSound = 343f;
    /// <summary>The intake's roar: centre of its band, Hz.</summary>
    public const float IntakeHz = 380f;
    /// <summary>How much more of the exhaust's top a camera behind the car hears, dB.</summary>
    public const float TailpipeDb = 3f;

    /// <summary>A front-engined hatchback, saloon or estate: what a car is taken for when nothing else is known.</summary>
    public static EngineVoicing FrontEngined => new(EnginePlacement.Front, 2.2f);

    /// <summary>The corner of the cabin's low-pass, Hz.</summary>
    public float CabinCutoff => Placement == EnginePlacement.Mid ? 4000f : 2200f;
    /// <summary>The cabin's boom: its first longitudinal mode, Hz.</summary>
    public float BoomHz => SpeedOfSound / (2f * MathF.Max(CabinLength, 0.5f));
    /// <summary>The boom's peak, dB: the exhaust drives a front-engined car's floor; a mid engine's cabin is short and stiff.</summary>
    public float BoomDb => Placement == EnginePlacement.Mid ? 3.5f : 5f;
    /// <summary>The intake's roar in the cabin at full throttle, dB.</summary>
    public float CabinIntakeDb => Placement == EnginePlacement.Mid ? 5f : 1.5f;
    /// <summary>The intake's roar heard behind the car at full throttle, dB.</summary>
    public float OutsideIntakeDb => Placement == EnginePlacement.Mid ? 2.5f : 0f;
    /// <summary>Where the tailpipe starts to beam its sound along its axis, ka = 1, Hz.</summary>
    public float TailpipeHz => SpeedOfSound / (MathF.Tau * MathF.Max(TailpipeRadius, 0.01f));
}
