"""The prairie's night (0.59): a loop of Danjocross's "Kansas Night" (Freesound #503207, CC0): frogs and toads, the
insects, now and then a whip-poor-will, no cars, on the Kansas prairie.

    python tools/make-night.py 503207_2977885-hq.mp3 game/assets/audio/ambience/night.wav
"""
import subprocess
import sys
import wave

import numpy as np

SR = 44100
src, dst = sys.argv[1], sys.argv[2]
raw = subprocess.run(["ffmpeg", "-v", "error", "-i", src, "-ac", "2", "-ar", str(SR), "-af", "highpass=f=90", "-f", "f32le", "-"],
                     capture_output=True, check=True).stdout
x = np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()
print("seconds", len(x) / SR)
# 60 s from 30 s in, the last 4 s cross-faded (equal power) into the first, which are then dropped: a 56 s loop.
start, seconds, fade = 30 * SR, 60 * SR, 4 * SR
x = x[start:start + seconds]
t = np.linspace(0.0, 1.0, fade, dtype=np.float32)[:, None]
x = np.concatenate([x[fade:-fade], x[-fade:] * np.cos(t * np.pi / 2) + x[:fade] * np.sin(t * np.pi / 2)])
rms = float(np.sqrt(np.mean(x * x)))
# The day's bed is at -30 dBFS RMS (make-ambience.py); the night's the same, the game sets the two against each other.
x *= 10 ** (-30 / 20) / rms
print("peak", 20 * np.log10(np.abs(x).max()), "dBFS")
pcm = np.clip(x * 32767.0, -32768, 32767).astype("<i2")
with wave.open(dst, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())
print("wrote", dst, len(x) / SR, "s")
