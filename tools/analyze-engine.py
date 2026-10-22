"""RPM profile of an engine recording: decode with ffmpeg, STFT, harmonic product spectrum, print f0 per frame.
Usage: python -I analyze.py <file> <cylinders>
Firing frequency of a four-stroke engine: f = rpm / 60 * cylinders / 2."""
import subprocess, sys, site, struct
sys.path.append(site.getusersitepackages())
import numpy as np

path, cylinders = sys.argv[1], int(sys.argv[2])
sr = 22050
raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(sr), "-f", "s16le", "-"], capture_output=True, check=True).stdout
x = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
frame = 4096
hop = int(sr * 0.1)
window = np.hanning(frame)
print(f"{path}: {len(x)/sr:.1f} s, peak {np.max(np.abs(x)):.2f}, rms {np.sqrt(np.mean(x*x)):.3f}")
print("  t     f0    rpm   level   (hps peak; rpm = f0*120/cyl)")
for start in range(0, len(x) - frame, hop):
    seg = x[start:start + frame] * window
    spec = np.abs(np.fft.rfft(seg))
    level = np.sqrt(np.mean(seg * seg))
    # harmonic product spectrum over 3 harmonics, restricted to 25..700 Hz
    hps = spec.copy()
    for h in (2, 3):
        dec = spec[::h]
        hps[:len(dec)] *= dec
    lo, hi = int(25 * frame / sr), int(700 * frame / sr)
    k = lo + int(np.argmax(hps[lo:hi]))
    f0 = k * sr / frame
    rpm = f0 * 120 / cylinders
    bar = "#" * int(min(level * 60, 40))
    print(f"{start/sr:5.1f} {f0:6.1f} {rpm:6.0f} {level:7.3f} {bar}")
