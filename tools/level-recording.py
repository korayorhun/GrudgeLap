"""Takes the slow rise of level out of a run-up recording before its banks are cut (make-engine-bank.py).

Usage: python level-recording.py <in> <out.wav> <from s> <to s> [window s]

Decodes with ffmpeg to mono 44.1 kHz and divides the stretch [from, to) by its own RMS over a sliding window (one
second by default, Hann-weighted), so the level no longer climbs with the revs; the window is long against a firing
period (60 pulses or more at 1800 rpm on a four), so the pulses' levels against their neighbours, the cylinders' own
beat, stay. The rest of the file is copied as it is, and the stretch joins it smoothly (the gain eases over a window
at either end). Writes a 16-bit WAV peaking at -1 dBFS (the bank tool levels each segment again). Nothing is moved in
time, so the firing pulses the bank tool marks stay where they were recorded.

Why (cars13): the voice levels a run-up's grains onto a trend of level against the revs whose slope is the one measured
within each recording, up to 3 dB an octave (GrainBank.LevelTrend), and levels the idle's grains onto the same trend.
A dynamometer run of the BRZ's FA20 (Freesound #183654) rises some 8 dB an octave from 2,000 to 7,500 rpm in fourth gear
(the rollers' load and the room the microphones were in), where every other bank's run-up keeps its level within a
recording (their trends' slopes are 0): cut as it was, the trend fell 6 dB from the run-up to the idle's revs, the
idle's grains were held 6 dB down pulling (the most GrainBank.LevelGains moves a grain), and opening the throttle at
the idle revs raised the level by under 1 LU where every other car rises 3 LU or more. Levelled, the boxer's run-up
rises with the revs as the others do, by the voice's own law.
"""
import site
import subprocess
import sys
import wave

sys.path.append(site.getusersitepackages())
import numpy as np
from scipy.signal import fftconvolve

SR = 44100


def main():
    src, out, start, end = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4])
    window = float(sys.argv[5]) if len(sys.argv) > 5 else 1.0
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", src, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    x = np.frombuffer(raw, dtype=np.float32).astype(np.float64)
    x = x - x.mean()
    n = int(window * SR)
    w = np.hanning(n)
    w /= w.sum()
    # The mean square around every sample over the window (zero-padded at the file's ends, then corrected for it).
    power = fftconvolve(x * x, w, mode="same")
    cover = fftconvolve(np.ones_like(x), w, mode="same")
    rms = np.sqrt(np.maximum(power, 0.0) / np.maximum(cover, 1e-9))
    a, b = int(start * SR), int(end * SR)
    reference = float(np.sqrt(np.mean(x[a:b] ** 2)))
    gain = np.ones_like(x)
    gain[a:b] = reference / np.maximum(rms[a:b], 1e-9)
    # Eased in and out over a window at either end of the stretch, so it joins the rest of the recording smoothly.
    ramp = np.clip(np.minimum(np.arange(len(x)) - a, b - np.arange(len(x))) / n, 0.0, 1.0)
    ramp = 0.5 - 0.5 * np.cos(np.pi * ramp)
    y = x * (1.0 + (gain - 1.0) * ramp)
    y *= 0.89 / max(np.max(np.abs(y)), 1e-12)
    with wave.open(out, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes((y * 32767).astype(np.int16).tobytes())
    print(f"{src}: {start:.1f}-{end:.1f} s levelled over {window:.1f} s windows -> {out}")


main()
