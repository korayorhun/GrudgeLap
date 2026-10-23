"""Cuts a seamless loop (tyres, road) out of a recording.
Usage: python -I makeloop.py <in> <start s> <duration s> <out.wav> [crossfade s] [peak]
Decodes with ffmpeg to mono 22050 Hz, takes the slice, folds its tail into its head with an equal-power
crossfade so the loop point is inaudible, normalises to the given peak and writes 16-bit PCM."""
import subprocess, sys, site, wave, struct
sys.path.append(site.getusersitepackages())
import numpy as np

src, start, duration, out = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
crossfade = float(sys.argv[5]) if len(sys.argv) > 5 else 0.08
peak = float(sys.argv[6]) if len(sys.argv) > 6 else 0.7
sr = 22050
raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", str(start), "-t", str(duration + crossfade), "-i", src,
                      "-ac", "1", "-ar", str(sr), "-f", "s16le", "-"], capture_output=True, check=True).stdout
x = np.frombuffer(raw, dtype=np.int16).astype(np.float64) / 32768.0
n = int(duration * sr)
xf = int(crossfade * sr)
if len(x) < n + xf:
    raise SystemExit(f"recording too short: {len(x)/sr:.2f} s available")
body = x[:n].copy()
tail = x[n:n + xf]
t = np.linspace(0.0, 1.0, xf, endpoint=False)
# Equal-power: the head fades in while the piece that followed the loop end fades out on top of it.
body[:xf] = body[:xf] * np.sin(t * np.pi / 2) + tail * np.cos(t * np.pi / 2)
# Remove DC and normalise.
body -= body.mean()
body *= peak / max(np.max(np.abs(body)), 1e-6)
with wave.open(out, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(sr)
    w.writeframes((body * 32767).astype(np.int16).tobytes())
print(f"{out}: {n/sr:.2f} s, rms {np.sqrt(np.mean(body*body)):.3f}")
