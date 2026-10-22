"""Cuts a one-shot (a pop, a bang) out of a recording.
Usage: python -I make-oneshot.py <in> <start s> <duration s> <out.wav> [fade-out s] [peak]
Decodes with ffmpeg to mono 22050 Hz, takes the slice, ramps it in over 2 ms so it cannot click, fades the
tail out so the recording's background dies away with the sound, removes DC, normalises to the given peak
and writes 16-bit PCM."""
import subprocess, sys, site, wave
sys.path.append(site.getusersitepackages())
import numpy as np

src, start, duration, out = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
fade = float(sys.argv[5]) if len(sys.argv) > 5 else 0.07
peak = float(sys.argv[6]) if len(sys.argv) > 6 else 0.8
sr = 22050
raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", str(start), "-t", str(duration), "-i", src,
                      "-ac", "1", "-ar", str(sr), "-f", "s16le", "-"], capture_output=True, check=True).stdout
x = np.frombuffer(raw, dtype=np.int16).astype(np.float64) / 32768.0
if len(x) < int(duration * sr * 0.9):
    raise SystemExit(f"recording too short: {len(x)/sr:.2f} s available")
x -= x.mean()
ramp = int(0.002 * sr)
x[:ramp] *= np.linspace(0.0, 1.0, ramp, endpoint=False)
n = min(int(fade * sr), len(x))
x[-n:] *= 0.5 + 0.5 * np.cos(np.linspace(0.0, np.pi, n))
x *= peak / max(np.max(np.abs(x)), 1e-6)
with wave.open(out, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(sr)
    w.writeframes((x * 32767).astype(np.int16).tobytes())
print(f"{out}: {len(x)/sr:.3f} s, rms {np.sqrt(np.mean(x*x)):.3f}")
