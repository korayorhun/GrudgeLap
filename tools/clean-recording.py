"""Takes the steady hiss out of a quiet engine recording before its banks are cut (make-engine-bank.py).

Usage: python clean-recording.py <in> <out.wav> [from Hz] [low-pass Hz] [notch from-to Hz]

Decodes with ffmpeg to mono 44.1 kHz, measures the recording's noise floor in every frequency bin as the 10th
percentile of its power over the whole file, subtracts it (a Wiener-style gain per bin, twice the floor taken away,
never below -20 dB, smoothed over three frames and three bins), only from `from Hz` up (700 by default), low-passes at
`low-pass Hz` (6000 by default) and writes a 16-bit WAV peaking at -1 dBFS (the bank tool levels each segment again).
Nothing is pitch-shifted or moved in time, so the firing pulses the bank tool marks stay where they were recorded.

Why (diesel6): the CC0 recording of a TDI's engine bay the diesel's banks are cut from (Freesound #243773 and #243783,
fastson) was made at -55 to -60 dBFS, and above 4 kHz it is its recorder's floor: those bands move by under 3 dB
between the idle and the run-up. The bank tool brings every segment to one loudness, 40 dB up, and the floor with it:
the grains' sharpest steps stood 2 to 3.6 times their RMS where every other bank's stand at 0.1 to 0.7, hiss the voice
would have played at every firing pulse. Below 700 Hz the engine is always far over the floor, and the percentile
there measures the engine, not the floor, so nothing is taken away.

The notch (optional, say 159-166): a zero-phase band-stop (a second-order Butterworth each way) for a steady tone
that does not follow the revs. The same drives carry one at 161-163 Hz (an auxiliary of the car's: 162.7 Hz idling
at 780 rpm, 161.3 at about 980), the strongest line of the idle, as loud as its second firing harmonic; cut into
grains and laid at the firing rate it is chopped into a buzz at that rate, and the voice idled with it 7 to 9 dB over
its firing line (diesel6 review). It is taken out only of the copy the idle is cut from: through a run-up every
firing harmonic passes it in turn, and a notch over the whole drive would cut each as it went by.
"""
import site
import subprocess
import sys
import wave

sys.path.append(site.getusersitepackages())
import numpy as np
from scipy.ndimage import uniform_filter
from scipy.signal import butter, sosfiltfilt

SR = 44100
N = 2048
HOP = N // 4


def main():
    src, out = sys.argv[1], sys.argv[2]
    start_hz = float(sys.argv[3]) if len(sys.argv) > 3 else 700.0
    lowpass_hz = float(sys.argv[4]) if len(sys.argv) > 4 else 6000.0
    notch = [float(v) for v in sys.argv[5].split("-")] if len(sys.argv) > 5 else None
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", src, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    x = np.frombuffer(raw, dtype=np.float32).astype(np.float64)
    pad = np.concatenate([np.zeros(N), x, np.zeros(N)])
    # A square-root Hann window both ways: at a quarter frame's hop the squared windows sum to 2, divided out below.
    w = np.sqrt(np.hanning(N + 1)[:N])
    starts = range(0, len(pad) - N, HOP)
    spectra = np.array([np.fft.rfft(pad[i:i + N] * w) for i in starts])
    power = np.abs(spectra) ** 2
    floor = np.percentile(power, 10, axis=0)
    gain = np.sqrt(np.clip(1.0 - 2.0 * floor / (power + 1e-30), 0.01, 1.0))
    # Smoothed, so a bin's gain does not flicker from frame to frame (the "musical noise" of spectral subtraction).
    gain = uniform_filter(gain, size=(3, 3), mode="nearest")
    freqs = np.fft.rfftfreq(N, 1 / SR)
    # From start_hz up, eased in over an octave below it.
    weight = np.clip((np.log2(np.maximum(freqs, 1.0)) - np.log2(start_hz / 2)), 0.0, 1.0)
    gain = 1.0 - weight * (1.0 - gain)
    y = np.zeros(len(pad))
    for k, i in enumerate(starts):
        y[i:i + N] += np.fft.irfft(spectra[k] * gain[k], N) * w
    y = y[N:N + len(x)] / 2.0
    y = sosfiltfilt(butter(4, lowpass_hz, fs=SR, output="sos"), y)
    if notch:
        y = sosfiltfilt(butter(2, notch, btype="bandstop", fs=SR, output="sos"), y)
    y *= 0.89 / max(np.max(np.abs(y)), 1e-12)
    with wave.open(out, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes((y * 32767).astype(np.int16).tobytes())
    notched = f", notched {notch[0]:.0f}-{notch[1]:.0f} Hz" if notch else ""
    print(f"{src}: {len(x) / SR:.1f} s, floor taken from {start_hz:.0f} Hz up, low-passed at {lowpass_hz:.0f} Hz{notched} -> {out}")


main()
