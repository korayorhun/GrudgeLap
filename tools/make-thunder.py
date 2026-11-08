"""The thunder (0.63): three claps cut from CC0 recordings on Freesound, near, middling and far.

    python tools/make-thunder.py 446753.mp3 367702.mp3 361401.mp3 game/assets/audio/thunder

near: BlueDelta, "Heavy Thunder Strike - no Rain" (#446753): a sudden heavy strike on a hot summer afternoon, recorded
      without distortion or compression; 26 s from 0.8 s in, the last 5 s faded.
mid:  BlueDelta, "THUNDER long rumbling - no Rain" (#367702): a lightning and its rolling thunder; 24 s from the start,
      the last 5 s faded.
far:  richwise, "Distant Thunder" (#361401): distant thunder rumbling; 14 s from 16.5 s in, the last 4 s faded.
"""
import subprocess
import sys
import wave

import numpy as np

SR = 44100


def decode(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "2", "-ar", str(SR), "-af", "highpass=f=25",
                          "-f", "f32le", "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()


def cut(x, start, seconds, fade_out, peak_dbfs):
    y = x[int(start * SR):int((start + seconds) * SR)].copy()
    n_in = int(0.03 * SR)
    y[:n_in] *= np.linspace(0.0, 1.0, n_in, dtype=np.float32)[:, None]
    n_out = int(fade_out * SR)
    y[-n_out:] *= (np.cos(np.linspace(0.0, np.pi / 2, n_out, dtype=np.float32)) ** 2)[:, None]
    return y * (10 ** (peak_dbfs / 20) / np.abs(y).max())


def save(path, y):
    pcm = np.clip(y * 32767.0, -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(path, f"{len(y) / SR:.1f} s, RMS {20 * np.log10(np.sqrt(np.mean(y * y))):.1f} dBFS")


near_src, mid_src, far_src, out = sys.argv[1:5]
save(f"{out}/near.wav", cut(decode(near_src), 0.8, 26.0, 5.0, -1.0))
save(f"{out}/mid.wav", cut(decode(mid_src), 0.0, 24.0, 5.0, -2.0))
save(f"{out}/far.wav", cut(decode(far_src), 16.5, 14.0, 4.0, -3.0))
