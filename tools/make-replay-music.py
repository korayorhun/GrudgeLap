"""Composes and renders the instant replay's music: "Grudge Replay", an original piece in the manner of late-1980s
arcade music (it is not a transcription of any existing tune).

Usage: python -I tools/make-replay-music.py [--stems <dir>]

Writes game/assets/audio/music/replay.wav (mono, 22050 Hz, 16-bit, for the Unreal version's minimal WAV reader),
replay.ogg (Vorbis, 44.1 kHz, two identical channels, for Godot) and game/src/ReplayScore.cs, the same score as the
note table the 1989 mode's synthesiser plays (EngineAudio.ReplaySong), so both modes play one tune from one source.
--stems writes each part on its own (22050 Hz, at its level in the mix) for checking the arrangement.

How it is made, and why:
- The arcade (Atari Games, 1989) played its sound on the "driver sound" board: a 68000 handing work to a TMS32010
  DSP that mixed sampled instruments in software and wrote one 12-bit word every 50 us (20 kHz) to an AM6012 DAC,
  mono (MAME, atari/harddriv_a.cpp). There was no FM chip on that board; the DSP played its sounds from ROM, and
  the studio sounds such samples were taken from in 1989 were FM synths (the DX/OPM brass, slap bass and leads), drum machines of the LinnDrum /
  SP-12 era with gated reverb, and the orchestra hit. So each instrument here is rebuilt the way those were made:
  four-operator FM with the YM2151's operator layout (feedback on the first operator, carriers and modulators in
  pairs, exponential attack and decays linear in decibels), drums from oscillators and noise with a gated room.
  The mix is then "sampled" as the board would have played it: band-limited and taken to 20 kHz, truncated to
  12 bits, held between samples as a DAC holds its output, and smoothed by the amplifier's low-pass.
- The tune: A mixolydian at 136 bpm with swung sixteenths. Bar 1 is the band's intro hit (the chord three times
  and a tom fill), bars 2-5 the hook (a syncopated lead over slap bass, horn stabs and a funk comp: A, G to D/F#,
  A, then F and G as band hits), and the button on the downbeat of bar 6: an A add-nine chord with the brass
  falling off. Five bars take 8.8 s and the chord rings to about 10.3 s, inside the replay's ten seconds plus the
  game's third-of-a-second fade.
- The level: peak -1 dBFS, loudness about that of the file it replaced (integrated -17 LUFS mono).
"""
import argparse
import math
import pathlib
import subprocess
import sys
import site
import tempfile
import wave

sys.path.append(site.getusersitepackages())
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parent.parent
MUSIC = ROOT / "game" / "assets" / "audio" / "music"

BPM = 136.0
STEP = 60.0 / BPM / 4.0          # one sixteenth, seconds
SWING = 0.56                     # where the second sixteenth of each eighth falls, as a fraction of the eighth
SR = 40000                       # the "studio" rate the instruments are rendered at, twice the board's
BOARD_SR = 20000                 # the DSP's output rate: one DAC word every 50 us
LENGTH = 10.45                   # seconds of audio, the last chord's ring included
DRIVE = 1.0                      # the mix's peak into the board, against the limiter's ceiling of 0.89
rng = np.random.default_rng(1989)


# --------------------------------------------------------------------------------------------------------------
# The score. Positions are (bar, sixteenth); bars count from 0. Pitches are MIDI numbers.
# --------------------------------------------------------------------------------------------------------------

def at(bar, step):
    """Seconds from the start for a sixteenth, swung: the off-sixteenths are pushed late, as a funk drummer plays."""
    eighth = math.floor(step / 2.0)
    within = step - eighth * 2.0
    swung = eighth * 2.0 + (within * 2.0 * SWING if within <= 1.0 else 2.0 * SWING + (within - 1.0) * (2.0 - 2.0 * SWING))
    return (bar * 16 + swung) * STEP


A_ADD9 = [57, 64, 69, 71, 73, 76]        # the intro hits and the button: A3 E4 A4 B4 C#5 E5
CHORDS = {                                # the horns' close triads, in the trumpets' middle
    "A": [64, 69, 73], "G": [62, 67, 71], "D/F#": [66, 69, 74], "F": [65, 69, 72], "Gh": [67, 71, 74],
}
SHELLS = {"A": [61, 67], "G": [59, 64], "D/F#": [62, 66]}   # the comp: A7 shell (third, seventh), G6, D


def score():
    """Every note of the piece as (time s, length s, part, pitch, velocity, extras)."""
    notes = []

    def add(bar, step, steps, part, pitch, vel, **extra):
        notes.append((at(bar, step), steps * STEP, part, pitch, vel, extra))

    # Bar 0: the intro hit. The band together on one, the and of two and three; a tom fill on four.
    for step, steps, vel in [(0, 5, 1.0), (6, 1.5, 0.85), (8, 3, 0.95)]:
        for p in A_ADD9:
            add(0, step, steps, "brass", p, vel, fall=step == 8)
        add(0, step, steps, "orch", 69, vel)
        add(0, step, steps, "bass", 33, vel, kind="T")
        add(0, step, steps, "bass", 45, vel * 0.7, kind="P")
        add(0, step, 1, "kick", 0, vel)
        add(0, step, 1, "snare", 0, vel * (0.6 if step == 0 else 0.9))
    add(0, 0, 16, "crash", 0, 1.0)
    add(0, 12, 1, "snare", 0, 0.75)
    for step, pitch in [(13, 50), (14, 45), (15, 41)]:
        add(0, step, 1, "tom", pitch, 0.85)

    # The lead: (bar, step, sixteenths, pitch, velocity, bend from (semitones below) or None, vibrato)
    lead = [
        # Bar 1, over A: the hook.
        (1, 0, 2, 76, 0.95, None), (1, 2, 1, 79, 0.8, None), (1, 3, 3, 81, 1.0, 2), (1, 7, 1, 79, 0.75, None),
        (1, 8, 2, 76, 0.9, None), (1, 10, 1, 74, 0.75, None), (1, 11, 4, 76, 0.95, 1),
        # Bar 2, over G then D/F#: the answer, coming down and turning up to the hook again.
        (2, 0, 2, 74, 0.9, None), (2, 2, 1, 76, 0.75, None), (2, 3, 3, 79, 1.0, 1), (2, 7, 1, 78, 0.75, None),
        (2, 8, 2, 74, 0.9, None), (2, 10, 1, 71, 0.75, None), (2, 11, 2, 69, 0.85, None), (2, 13, 1, 71, 0.75, None),
        (2, 14, 2, 73, 0.9, None),
        # Bar 3, over A: the hook, its end lifted a sixth.
        (3, 0, 2, 76, 0.95, None), (3, 2, 1, 79, 0.8, None), (3, 3, 3, 81, 1.0, 2), (3, 7, 1, 79, 0.75, None),
        (3, 8, 2, 85, 1.0, None), (3, 10, 1, 83, 0.8, None), (3, 11, 4, 81, 1.0, 1),
        # Bar 4, F then G with the band's hits; a pickup to the button.
        (4, 0, 2, 84, 1.0, None), (4, 2, 1, 81, 0.8, None), (4, 3, 3, 84, 1.0, None),
        (4, 8, 2, 86, 1.0, None), (4, 10, 1, 83, 0.8, None), (4, 11, 2, 86, 1.0, None),
        (4, 14, 1, 83, 0.85, None), (4, 15, 1, 86, 0.9, None),
        # Bar 5: the button, held and falling off.
        (5, 0, 7, 88, 1.0, None),
    ]
    for bar, step, steps, pitch, vel, bend in lead:
        add(bar, step, steps, "lead", pitch, vel, bend=bend, fall=bar == 5)

    # The horns: stabs off the beat, the chord of the next bar pushed in a sixteenth early.
    stabs = [
        (1, 6, 1, "A", 0.75), (1, 15, 2, "G", 0.85),
        (2, 8, 2, "D/F#", 0.8), (2, 15, 2, "A", 0.85),
        (3, 6, 1, "A", 0.75), (3, 14, 1, "A", 0.7),
        (4, 0, 2, "F", 1.0), (4, 3, 3, "F", 0.95), (4, 8, 2, "Gh", 1.0), (4, 11, 2, "Gh", 0.95), (4, 15, 1, "Gh", 0.9),
    ]
    for bar, step, steps, chord, vel in stabs:
        for p in CHORDS[chord]:
            add(bar, step, steps, "brass", p, vel)
    for p in A_ADD9:
        add(5, 0, 7, "brass", p, 1.0, fall=True)
    add(5, 0, 7, "orch", 69, 1.0)

    # The funk comp: dominant shells on the off-sixteenths, short and dry, as a muted guitar or a clav plays them.
    comp_steps = [(2, 0.8), (6, 0.6), (9, 0.55), (10, 0.75), (14, 0.65)]
    for bar, first, second in [(1, "A", "A"), (2, "G", "D/F#"), (3, "A", "A")]:
        for step, vel in comp_steps:
            for p in SHELLS[first if step < 8 else second]:
                add(bar, step, 0.6, "comp", p, vel)

    # Slap bass: T thumb, P popped octave, G dead (ghost) note. (step, kind, interval, sixteenths, velocity)
    groove = [(0, "T", 0, 2, 1.0), (2, "P", 12, 1, 0.8), (3, "G", 0, 1, 0.35), (5, "T", 0, 1, 0.7),
              (6, "P", 12, 1, 0.85), (7, "G", 0, 1, 0.3), (8, "T", 0, 1.5, 0.95), (10, "P", 10, 1, 0.8),
              (11, "T", 7, 1, 0.7), (12, "P", 12, 1, 0.85), (13, "G", 0, 1, 0.3), (14, "T", 3, 1, 0.7),
              (15, "T", 4, 1, 0.75)]
    half = [(0, "T", 0, 2, 1.0), (2, "P", 12, 1, 0.8), (3, "G", 0, 1, 0.35), (5, "T", 0, 1, 0.7),
            (6, "P", 12, 1, 0.85), (7, "G", 0, 1, 0.3)]
    for step, kind, interval, steps, vel in groove:
        add(1, step, steps, "bass", 33 + interval, vel, kind=kind)
        add(3, step, steps, "bass", 33 + interval, vel, kind=kind)
    for step, kind, interval, steps, vel in half:
        add(2, step, steps, "bass", 31 + interval, vel, kind=kind)
        if step < 7:
            add(2, step + 8, steps, "bass", 30 + interval, vel, kind=kind)
    add(2, 15, 1, "bass", 32, 0.75, kind="T")                       # G# leading into the A of bar 3
    for step, kind, pitch, steps, vel in [(0, "T", 29, 2, 1.0), (2, "P", 41, 1, 0.8), (3, "T", 29, 3, 0.95),
                                          (6, "P", 41, 1, 0.7), (7, "G", 29, 1, 0.3), (8, "T", 31, 2, 1.0),
                                          (10, "P", 43, 1, 0.8), (11, "T", 31, 2, 0.95)]:
        add(4, step, steps, "bass", pitch, vel, kind=kind)
    add(4, 15, 1, "bass", 31, 0.9, kind="T", slide=2)               # G sliding up into the button's A
    add(5, 0, 6, "bass", 33, 1.0, kind="T")
    add(5, 0, 6, "bass", 45, 0.75, kind="P")

    # Drums.
    kicks = {1: [0, 6, 10], 2: [0, 3, 6, 10], 3: [0, 6, 10, 14], 4: [0, 3, 8, 11]}
    for bar, steps in kicks.items():
        for step in steps:
            add(bar, step, 1, "kick", 0, 1.0 if step % 4 == 0 else 0.85)
    for bar in (1, 2, 3):
        add(bar, 4, 1, "snare", 0, 1.0)
        add(bar, 12, 1, "snare", 0, 1.0)
        for step, vel in [(7, 0.22), (9, 0.18), (15, 0.28)]:
            add(bar, step, 1, "snare", 0, vel, ghost=True)
        for step in range(16):
            if bar != 2 and step == 14:
                add(bar, step, 2, "ohat", 0, 0.7)
            elif not (bar != 2 and step == 15):
                add(bar, step, 1, "hat", 0, 0.85 if step % 4 == 0 else (0.6 if step % 2 == 0 else 0.4))
    add(1, 0, 16, "crash", 0, 0.85)
    add(4, 0, 8, "crash", 0, 0.6)
    for step in (0, 2, 4, 6, 8, 10):
        add(4, step, 1, "hat", 0, 0.7)
    add(4, 4, 1, "snare", 0, 1.0)
    add(4, 11, 1, "snare", 0, 0.8)
    add(4, 12, 1, "snare", 0, 1.0)
    add(4, 13, 1, "tom", 50, 0.85)
    add(4, 14, 1, "tom", 45, 0.9)
    add(4, 15, 1, "snare", 0, 0.9)
    add(5, 0, 1, "kick", 0, 1.0)
    add(5, 0, 1, "snare", 0, 1.0)
    add(5, 0, 24, "crash", 0, 1.0)
    add(5, 0, 1, "tom", 41, 0.7)
    return notes


# --------------------------------------------------------------------------------------------------------------
# Instruments
# --------------------------------------------------------------------------------------------------------------

def hz(midi):
    return 440.0 * 2.0 ** ((midi - 69) / 12.0)


def envelope(n, gate, attack, decay1, level1, decay2, release):
    """An OPM-style operator envelope as linear gain: an exponential attack, then attenuation rising linearly in
    decibels (decay1 dB/s down to level1 dB, then decay2 dB/s) and, from the gate's end, release dB/s."""
    t = np.arange(n) / SR
    rise = 1.0 - np.exp(-np.minimum(t / max(attack, 1e-4), 12.0) * 5.0)
    rise /= 1.0 - math.exp(-5.0) if attack > 0 else 1.0
    rise = np.minimum(rise, 1.0)
    after = np.maximum(t - attack, 0.0)
    knee = level1 / decay1 if decay1 > 0 else np.inf
    db = np.where(after < knee, after * decay1, level1 + (after - knee) * decay2)
    if gate < t[-1]:
        g = min(int(gate * SR), n - 1)
        db = np.where(t > gate, db[g] + (t - gate) * release, db)
    return rise * 10.0 ** (-np.minimum(db, 120.0) / 20.0)


def phase_of(freq):
    """Running phase in radians of a frequency track (Hz per sample)."""
    return np.cumsum(freq) * (2.0 * math.pi / SR)


def feedback_op(phase, beta):
    """The YM2151's first operator: a sine modulating itself with the mean of its last two outputs. beta is the
    feedback in radians per sample (an array, so it can follow an envelope); about 1.5 gives a sawtooth."""
    out = np.empty(len(phase))
    y1 = y2 = 0.0
    sin = math.sin
    for i, (p, b) in enumerate(zip(phase.tolist(), beta.tolist())):
        y = sin(p + b * (y1 + y2) * 0.5)
        out[i] = y
        y2, y1 = y1, y
    return out


def pitch_track(n, f0, bend=None, fall=False, vibrato=0.0, gate=0.0, scoop=0.0):
    """Frequency per sample: an optional bend up from below (semitones, over 70 ms), a scoop (a brass player's
    slide into the note), a delayed vibrato and a fall-off at the end of a held final note."""
    t = np.arange(n) / SR
    semis = np.zeros(n)
    if bend:
        semis -= bend * np.exp(-t / 0.035)
    if scoop:
        semis -= scoop * np.exp(-t / 0.018)
    if vibrato:
        depth = vibrato * np.clip((t - 0.16) / 0.2, 0.0, 1.0)
        semis += depth * np.sin(2.0 * math.pi * 5.6 * t)
    if fall:
        start = max(gate - 0.12, 0.05)
        semis -= 5.0 * np.clip((t - start) / 0.45, 0.0, 1.0) ** 2
    return f0 * 2.0 ** (semis / 12.0)


def lead(pitch, length, vel, bend=None, fall=False):
    """The lead: algorithm 4 of the YM2151 (two modulator-carrier pairs). Pair 1 runs its modulator on heavy
    feedback for a sawtooth's buzz; pair 2 an octave-up modulator for a hollow, square-ish body, its carrier
    detuned a few cents for the chorus of a doubled synth. A delayed vibrato on held notes."""
    tail = 0.6 if fall else 0.15
    n = int((length + tail) * SR)
    gate = length * (1.0 if fall else 0.92)
    f = pitch_track(n, hz(pitch), bend=bend, fall=fall, vibrato=0.3 if length > 0.2 else 0.0, gate=gate)
    ph = phase_of(f)
    release = 60.0 if fall else 160.0
    env_c = envelope(n, gate, 0.004, 4.0, 4.0, 2.0, release)
    env_m = envelope(n, gate, 0.012, 20.0, 6.0, 3.0, release)
    m1 = feedback_op(ph, np.full(n, 1.45)) * env_m * (0.9 + 0.5 * vel)
    c1 = np.sin(ph + m1)
    m2 = np.sin(2.0 * ph) * env_m * 1.1
    c2 = np.sin(phase_of(f * 1.0035) + m2)
    return (0.55 * c1 + 0.45 * c2) * env_c * vel


def brass(pitch, length, vel, fall=False):
    """Brass section: algorithm 4 with feedback, the modulators' attack slower than the carriers' so each note
    opens from a soft blat to its full brightness, as the OPM brass patches did; a scoop into the pitch, and
    each player a few cents off the others."""
    tail = 0.55 if fall else 0.15
    n = int((length + tail) * SR)
    gate = length * (1.0 if fall else 0.85)
    cents = rng.normal(0.0, 4.0)
    f = pitch_track(n, hz(pitch) * 2.0 ** (cents / 1200.0), scoop=0.45, fall=fall, gate=gate,
                    vibrato=0.12 if length > 0.4 else 0.0)
    ph = phase_of(f)
    release = 55.0 if fall else 170.0
    env_c = envelope(n, gate, 0.01, 8.0, 3.0, 2.0, release)
    env_m = envelope(n, gate, 0.04, 10.0, 4.0, 4.0, release * 0.8)
    bright = 0.8 + 0.9 * vel
    m1 = feedback_op(ph, env_m * 1.3) * env_m * bright
    c1 = np.sin(ph + m1)
    m2 = np.sin(phase_of(f * 1.004)) * env_m * bright * 0.8
    c2 = np.sin(phase_of(f * 0.997) + m2)
    return (0.5 * c1 + 0.5 * c2) * env_c * vel


def orch(pitch, length, vel):
    """The orchestra hit of the late eighties (a sampled tutti chord): a cluster of detuned bright voices over
    octaves with a breath of noise, a fast attack and a short decay, as the sample was cut."""
    n = int((min(length, 0.5) + 0.35) * SR)
    t = np.arange(n) / SR
    env = envelope(n, min(length, 0.45), 0.003, 18.0, 30.0, 25.0, 60.0)
    out = np.zeros(n)
    for semis, level in [(-12, 0.6), (0, 0.8), (4, 0.45), (7, 0.55), (12, 0.6), (19, 0.3), (24, 0.2)]:
        for detune in (-0.006, 0.006):
            ph = phase_of(np.full(n, hz(pitch + semis) * (1.0 + detune)))
            out += level * np.sin(ph + 1.6 * np.exp(-t / 0.15) * np.sin(ph))
    noise = rng.standard_normal(n) * np.exp(-t / 0.06)
    out = out / 5.0 + 0.25 * np.diff(noise, prepend=0.0)
    return out * env * vel


def comp(pitch, length, vel):
    """The comp: a clav-like FM pluck (modulator at three times the pitch, its index dropping fast), choked short
    as a muted funk guitar is."""
    n = int((length + 0.08) * SR)
    t = np.arange(n) / SR
    ph = phase_of(np.full(n, hz(pitch)))
    index = 2.6 * np.exp(-t / 0.03) + 0.4
    out = np.sin(ph + index * np.sin(3.0 * ph))
    env = envelope(n, length, 0.001, 30.0, 40.0, 10.0, 320.0)
    return out * env * vel


def bass(pitch, length, vel, kind="T", slide=0):
    """Slap bass in the DX manner: a thumb (T) is a carrier with a low modulator whose index bursts and settles,
    plus a short high-ratio modulator for the string's snap on the frets; a pop (P) snaps harder and brighter; a
    dead note (G) is the thud and the rattle of a muted string, no pitch to speak of."""
    if kind == "G":
        n = int(0.06 * SR)
        t = np.arange(n) / SR
        noise = np.diff(rng.standard_normal(n), prepend=0.0)
        thud = np.sin(phase_of(np.full(n, hz(pitch)))) * np.exp(-t / 0.012)
        return (0.35 * noise * np.exp(-t / 0.008) + 0.6 * thud) * vel
    n = int((length + 0.08) * SR)
    t = np.arange(n) / SR
    f = np.full(n, hz(pitch))
    if slide:
        f = hz(pitch) * 2.0 ** (slide * np.clip((t - 0.05) / 0.09, 0.0, 1.0) / 12.0)
    ph = phase_of(f)
    pop = kind == "P"
    index = (3.2 if pop else 1.9) * vel * np.exp(-t / (0.05 if pop else 0.07)) + 0.45
    snap = (2.6 if pop else 1.5) * np.exp(-t / 0.006)
    out = np.sin(ph + index * np.sin(ph) + snap * np.sin(7.0 * ph))
    out += 0.35 * np.sin(ph)                                     # the body's fundamental, under the bright layer
    env = envelope(n, length * 0.9, 0.001, 5.0, 12.0, 4.0, 260.0)
    return out * env * vel * (0.8 if pop else 1.0)


def gated_room(x, seconds=0.2, wet=0.55):
    """The gated reverb of the eighties' snares and toms: a dense room cut off sharply after a fixed time."""
    n = int(seconds * SR)
    t = np.arange(n) / SR
    ir = rng.standard_normal(n) * np.exp(-t / 0.5) * np.where(t > seconds - 0.015, (seconds - t) / 0.015, 1.0)
    ir = np.convolve(ir, np.ones(6) / 6.0, mode="same")          # a darker room than white noise
    ir *= wet / math.sqrt(np.sum(ir * ir))
    return x + fft_convolve(x, ir)[: len(x)]


def kick(vel):
    n = int(0.4 * SR)
    t = np.arange(n) / SR
    f = 48.0 + 115.0 * np.exp(-t / 0.03)
    body = np.sin(phase_of(f)) * np.exp(-t / 0.14) * np.minimum(t / 0.001, 1.0)
    click = np.diff(rng.standard_normal(n), prepend=0.0) * np.exp(-t / 0.003) * 0.25
    return np.tanh(1.8 * (body + click)) * vel


def snare(vel, ghost=False):
    n = int(0.32 * SR)
    t = np.arange(n) / SR
    tone = (np.sin(phase_of(np.full(n, 185.0))) + 0.5 * np.sin(phase_of(np.full(n, 332.0)))) * np.exp(-t / 0.05)
    noise = np.diff(rng.standard_normal(n), prepend=0.0) * np.exp(-t / (0.06 if ghost else 0.11))
    dry = (0.6 * tone + 0.55 * noise) * vel
    return dry if ghost else gated_room(dry, 0.22, 0.7)


def tom(pitch, vel):
    n = int(0.45 * SR)
    t = np.arange(n) / SR
    f0 = hz(pitch)
    body = np.sin(phase_of(f0 * (1.0 + 0.5 * np.exp(-t / 0.04)))) * np.exp(-t / 0.22)
    noise = np.diff(rng.standard_normal(n), prepend=0.0) * np.exp(-t / 0.02) * 0.2
    return gated_room((body + noise) * vel, 0.26, 0.5)


# The 808's six square oscillators: metal without a pitch.
METAL = [205.3, 304.4, 369.6, 522.7, 540.0, 800.0]


def metallic(n, scale):
    t = np.arange(n) / SR
    out = np.zeros(n)
    for f in METAL:
        out += np.sign(np.sin(2.0 * math.pi * f * scale * t + rng.uniform(0, 2 * math.pi)))
    return out


def highpass_fft(x, cutoff, order=4):
    """A zero-phase Butterworth-magnitude high-pass: the cymbals keep only their sizzle, as the drum machines'
    filtered metal did."""
    spectrum = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1.0 / SR)
    spectrum *= 1.0 / np.sqrt(1.0 + (cutoff / np.maximum(f, 1e-3)) ** (2 * order))
    return np.fft.irfft(spectrum, len(x))


def hat(vel, open_=False):
    n = int((0.3 if open_ else 0.08) * SR)
    t = np.arange(n) / SR
    decay = 0.16 if open_ else 0.028
    raw = metallic(n, 1.6) * 0.5 + rng.standard_normal(n)
    return highpass_fft(raw * np.exp(-t / decay) * np.minimum(t / 0.0005, 1.0), 6000.0) * vel * 0.25


def crash(length, vel):
    n = int(min(length + 0.8, 2.6) * SR)
    t = np.arange(n) / SR
    raw = metallic(n, 2.3) * 0.4 + rng.standard_normal(n)
    env = np.minimum(t / 0.002, 1.0) * np.exp(-t / 0.75)
    return highpass_fft(raw * env, 3500.0, 2) * vel * 0.2


# --------------------------------------------------------------------------------------------------------------
# Mixing and the board
# --------------------------------------------------------------------------------------------------------------

def fft_convolve(a, b):
    n = len(a) + len(b) - 1
    size = 1 << (n - 1).bit_length()
    return np.fft.irfft(np.fft.rfft(a, size) * np.fft.rfft(b, size), size)[:n]


def lowpass_fft(x, sr, cutoff, order=2):
    """A zero-phase low-pass with a Butterworth magnitude, applied in the frequency domain."""
    spectrum = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1.0 / sr)
    spectrum /= np.sqrt(1.0 + (f / cutoff) ** (2 * order))
    return np.fft.irfft(spectrum, len(x))


def resample(x, sr_in, sr_out):
    """Band-limited resampling by the discrete Fourier transform (the length is padded to a whole number of
    both rates' periods so the ratio is exact)."""
    g = math.gcd(sr_in, sr_out)
    block = sr_in // g
    pad = (-len(x)) % block
    x = np.concatenate([x, np.zeros(pad)])
    n_out = len(x) * sr_out // sr_in
    spectrum = np.fft.rfft(x)
    keep = n_out // 2 + 1
    out = np.zeros(keep, dtype=complex)
    m = min(keep, len(spectrum))
    out[:m] = spectrum[:m]
    return np.fft.irfft(out, n_out) * (n_out / len(x))


def room(x, seconds=1.1, decay=0.3):
    """A small hall on the melodic parts, as the studio's sampled instruments carried: exponentially decaying
    noise, darker as it fades."""
    n = int(seconds * SR)
    t = np.arange(n) / SR
    bright = rng.standard_normal(n) * np.exp(-t / (decay * 0.5))
    dark = np.convolve(rng.standard_normal(n), np.ones(12) / 12.0, mode="same") * np.exp(-t / decay)
    ir = bright + 2.5 * dark
    ir[: int(0.012 * SR)] = 0.0                                   # the pre-delay of a room's first reflections
    ir /= math.sqrt(np.sum(ir * ir))
    return fft_convolve(x, ir)[: len(x)]


def sliding(x, width, how):
    pad = width // 2
    padded = np.pad(x, (pad, width - 1 - pad), mode="edge")
    view = np.lib.stride_tricks.sliding_window_view(padded, width)
    return view.min(axis=1) if how == "min" else view.mean(axis=1)


def glue(x, sr, threshold_db=-14.0, ratio=2.5):
    """A gentle bus compressor (an RMS detector over 12 ms, its gain smoothed over 25 ms): it holds the band
    together under the hits without pumping. Written without recursion so numpy can do it in one pass."""
    rms = np.sqrt(sliding(x * x, int(0.012 * sr), "mean") + 1e-12)
    level = 20.0 * np.log10(rms)
    over = np.maximum(level - threshold_db, 0.0)
    gain = 10.0 ** (-over * (1.0 - 1.0 / ratio) / 20.0)
    return x * sliding(gain, int(0.025 * sr), "mean")


def limit(x, sr, ceiling):
    """A look-ahead peak limiter: the gain each sample needs, its minimum held over 3 ms either side and then
    averaged over 3 ms, never lets a peak through and never moves the gain faster than a few milliseconds."""
    need = np.minimum(1.0, ceiling / np.maximum(np.abs(x), 1e-9))
    width = int(0.003 * sr) | 1
    held = sliding(need, 2 * width + 1, "min")
    return x * sliding(held, width, "mean")


def render(notes, stems=None):
    length = int(LENGTH * SR)
    parts = {}
    for start, dur, part, pitch, vel, extra in notes:
        # A player's timing and touch: a few milliseconds and a few percent either way, the same every build.
        start = max(start + rng.normal(0.0, 0.003 if part not in ("kick", "crash") else 0.0015), 0.0)
        vel = vel * (1.0 + rng.normal(0.0, 0.04))
        if part == "lead":
            sound = lead(pitch, dur, vel, bend=extra.get("bend"), fall=extra.get("fall", False))
        elif part == "brass":
            sound = brass(pitch, dur, vel, fall=extra.get("fall", False))
        elif part == "orch":
            sound = orch(pitch, dur, vel)
        elif part == "comp":
            sound = comp(pitch, dur, vel)
        elif part == "bass":
            sound = bass(pitch, dur, vel, extra.get("kind", "T"), extra.get("slide", 0))
        elif part == "kick":
            sound = kick(vel)
        elif part == "snare":
            sound = snare(vel, extra.get("ghost", False))
        elif part == "tom":
            sound = tom(pitch, vel)
        elif part == "hat":
            sound = hat(vel)
        elif part == "ohat":
            sound = hat(vel, open_=True)
        else:
            sound = crash(dur, vel)
        ramp = min(int(0.008 * SR), len(sound))                   # no sound may stop on a cliff
        sound[-ramp:] *= np.linspace(1.0, 0.0, ramp)
        i = int(start * SR)
        track = parts.setdefault(part, np.zeros(length))
        end = min(i + len(sound), length)
        track[i:end] += sound[: end - i]

    # The balance: drums and bass up front, the lead over the horns, the comp and the cymbals underneath.
    gains = {"kick": 0.4, "snare": 0.42, "tom": 0.28, "hat": 0.9, "ohat": 0.6, "crash": 0.6, "bass": 0.3,
             "lead": 0.32, "brass": 0.17, "orch": 0.5, "comp": 0.15}
    sends = {"lead": 0.3, "brass": 0.35, "orch": 0.35, "comp": 0.2, "snare": 0.08, "tom": 0.08}
    mix = np.zeros(length)
    send = np.zeros(length)
    for part, track in parts.items():
        mix += gains[part] * track
        send += gains[part] * sends.get(part, 0.0) * track
    mix += 0.6 * room(send)

    # The band into the board at a set level, so the compressor and the limiter work the same whatever the
    # balance above: the hits peak a little over the limiter's ceiling and only they are caught.
    scale = DRIVE / np.max(np.abs(mix))
    mix *= scale
    if stems:
        stems.mkdir(parents=True, exist_ok=True)
        for part, track in parts.items():
            write_wav(stems / f"{part}.wav", resample(gains[part] * track * scale, SR, 22050), 22050, 1)

    # Into the board: the sampler's anti-alias filter and the 20 kHz rate, the band glued and limited to the
    # level the DSP's 16-bit sum would have been scaled to, then the 12-bit word the AM6012 is given (the DSP
    # drops the four low bits: truncation, not rounding).
    board = resample(lowpass_fft(mix, SR, 8500.0, 4), SR, BOARD_SR)
    board = glue(board, BOARD_SR)
    board = limit(board, BOARD_SR, 0.89)
    board = np.floor(board * 2048.0) / 2048.0
    # Out of the DAC: each word held for its 50 us (a sample-and-hold's sin(x)/x droop and its images), then the
    # amplifier's low-pass, then the rates the game plays.
    held = np.repeat(board, 4)
    analog = lowpass_fft(held, BOARD_SR * 4, 9000.0, 2)
    return analog - np.mean(analog), BOARD_SR * 4


def write_wav(path, x, sr, channels):
    pcm = np.clip(np.round(x * 32767.0), -32768, 32767).astype("<i2")
    if channels == 2:
        pcm = np.repeat(pcm, 2)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())


def finish(x, sr_in, sr_out, peak_db=-1.0):
    """At the game's rate: faded out over the last 150 ms so the ring cannot end in a click, the peak set."""
    y = resample(x, sr_in, sr_out)[: int(LENGTH * sr_out)]
    fade = int(0.15 * sr_out)
    y[-fade:] *= 0.5 + 0.5 * np.cos(np.linspace(0.0, math.pi, fade))
    return y * (10.0 ** (peak_db / 20.0) / np.max(np.abs(y)))


def csharp(notes):
    """The score as the 1989 synthesiser's table (beat, beats, MIDI note, voice, level), as C# source. That synth
    has no bends, so a bend becomes a grace note a third of a sixteenth long; it plays the notes as written, on the
    beat, without this render's human timing. The horns' six-note chords are thinned to four and the orchestra hit
    is left to them."""
    voice = {"lead": "Pulse", "brass": "Brass", "bass": "Bass", "kick": "Kick", "snare": "Snare", "hat": "Hat",
             "ohat": "Cymbal", "crash": "Cymbal", "tom": "Tom", "comp": "Lead"}
    level = {"lead": 0.42, "brass": 0.15, "bass": 0.46, "kick": 0.6, "snare": 0.34, "hat": 0.16, "ohat": 0.12,
             "crash": 0.16, "tom": 0.45, "comp": 0.12}
    beat = 60.0 / BPM
    horns_at = {}
    for start, _, part, *_ in notes:
        if part == "brass":
            horns_at[start] = horns_at.get(start, 0) + 1
    rows = []
    for start, dur, part, pitch, vel, extra in notes:
        if part == "orch" or (part == "brass" and pitch in (57, 71) and horns_at[start] > 4):
            continue
        b, length = start / beat, dur / beat
        if part == "bass" and extra.get("kind") == "G":
            length, vel = 0.08, vel * 0.8
        if part == "ohat":
            length = 0.35
        if part == "lead" and extra.get("bend"):
            grace = STEP / 3.0 / beat
            rows.append((b, grace, pitch - extra["bend"], "Pulse", level["lead"] * vel * 0.8))
            b, length = b + grace, length - grace
        rows.append((b, length, pitch, voice[part], level[part] * vel))
    rows.sort(key=lambda r: r[0])
    body = "\n".join(f"        ({b:.3f}f, {n:.3f}f, {m}, {v}, {lv:.2f}f)," for b, n, m, v, lv in rows)
    return f"""// <auto-generated>
// Written by tools/make-replay-music.py from its score: change the tune there and run it again, which also renders
// the recording (assets/audio/music/replay.*) so that both modes keep playing the same music.
// </auto-generated>
namespace GrudgeLap.Game;

internal sealed partial class EngineAudio
{{
    private const float ReplayBpm = {BPM:g}f;

    /// <summary>The instant replay's tune for the 1989 synthesiser: beat, length in beats, MIDI note, voice, level.</summary>
    /// <remarks>A method, not a static field: static initialisers in two files of a partial class run in no set order,
    /// and <c>ReplaySong</c> is built from this in its own.</remarks>
    private static (float Beat, float Beats, int Midi, int Voice, float Level)[] ReplayScore() =>
    [
{body}
    ];
}}
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--stems", type=pathlib.Path, help="also write each part to this folder")
    args = parser.parse_args()
    notes = score()
    # The C# sources of this repository use CRLF line ends.
    (ROOT / "game" / "src" / "ReplayScore.cs").write_text(csharp(notes), encoding="utf-8", newline="\r\n")
    analog, sr = render(notes, args.stems)
    MUSIC.mkdir(parents=True, exist_ok=True)
    wav = finish(analog, sr, 22050)
    write_wav(MUSIC / "replay.wav", wav, 22050, 1)
    ogg_source = finish(analog, sr, 44100)
    with tempfile.TemporaryDirectory() as tmp:
        source = pathlib.Path(tmp) / "replay44.wav"
        write_wav(source, ogg_source, 44100, 2)
        # Bit-exact, so a re-run gives the same file (the Ogg stream serial number is random otherwise).
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(source), "-c:a", "libvorbis", "-b:a", "160k",
                        "-fflags", "+bitexact", "-flags:a", "+bitexact",
                        "-map_metadata", "-1", str(MUSIC / "replay.ogg")], check=True)
    rms = 20.0 * math.log10(math.sqrt(np.mean(wav * wav)))
    peak = 20.0 * math.log10(np.max(np.abs(wav)))
    print(f"replay.wav: {len(wav) / 22050:.2f} s, peak {peak:.1f} dBFS, rms {rms:.1f} dBFS; replay.ogg written")


if __name__ == "__main__":
    main()
