"""The rain's sounds (0.54): the drumming on the roof, the rain round the car outside, and the wipers in time with the
blades on the glass, from three CC0 recordings on Freesound (assets/audio/SOURCES.md).

    python tools/make-rain.py <roof.mp3> <outside.mp3> <wipers.mp3> <out folder>

roof:    ondrosik, "rain-outside-the-car" (#332856): rain on a car's roof from inside, Zoom H1.
outside: klangfabrik, "mediumrain" (#160699): medium rain on concrete, grass and leaves, wide.
wipers:  BeeProductive, "Windscreen wipers" (#395608): a car's wipers from inside, a cycle every 1.53 s.
"""
import subprocess, sys, wave
import numpy as np

SR = 44100


def decode(path, highpass):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "2", "-ar", str(SR), "-af", f"highpass=f={highpass}",
                          "-f", "f32le", "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()


def bed_loop(x, start, seconds, fade=3.0):
    """A seamless loop: the last `fade` seconds cross-faded (equal power) into the first, which are then dropped."""
    x = x[int(start * SR):int((start + seconds) * SR)]
    n = int(fade * SR)
    t = np.linspace(0.0, 1.0, n, dtype=np.float32)[:, None]
    return np.concatenate([x[n:-n], x[-n:] * np.cos(t * np.pi / 2) + x[:n] * np.sin(t * np.pi / 2)])


def level(x, dbfs):
    rms = float(np.sqrt(np.mean(x * x)))
    return x * (10 ** (dbfs / 20) / rms)


def save(path, x):
    pcm = np.clip(x * 32767.0, -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(path, f"{len(x) / SR:.2f} s, peak {20 * np.log10(np.abs(x).max()):.1f} dBFS")


roof_src, outside_src, wipers_src, out = sys.argv[1:5]

# The roof: 45 s from the steady stretch after the first three, a bed at -24 dBFS RMS.
save(f"{out}/rain-roof.wav", level(bed_loop(decode(roof_src, 50), 3.0, 45.0), -24.0))

# Outside: 45 s, the low end (the recorder's handling and a far road) off below 100 Hz.
save(f"{out}/rain-outside.wav", level(bed_loop(decode(outside_src, 100), 4.0, 45.0), -26.0))

# The wipers. The recording's blades reverse every 0.765 s (a thump each time, the rubber's swish loudest between); the
# game's sweep up and back takes 1.1 s, so each stroke is shortened to 0.55 s by cutting out its middle, the swish,
# cross-faded over 60 ms: the thumps stay as they were, and so does the pitch (resampling would have raised the motor
# by six semitones). Four cycles from a steady stretch, the loop starting `PRE` before a park reversal.
x = decode(wipers_src, 40)
mono = x.mean(axis=1)
w = SR // 200
m = len(mono) // w
env = np.sqrt((mono[:m * w].reshape(m, w) ** 2).mean(axis=1))
# The reversals: the loudest 5 ms between 0.6 and 0.9 s after the last, starting from the park reversal at 2.50 s.
events = [int(2.50 * SR)]
while len(events) < 9:
    a = (events[-1] + int(0.6 * SR)) // w
    b = (events[-1] + int(0.9 * SR)) // w
    events.append((a + int(np.argmax(env[a:b]))) * w)
gaps = np.diff(events) / SR
print("strokes", " ".join(f"{g:.3f}" for g in gaps))
PRE = int(0.06 * SR)
STROKE = int(0.55 * SR)
XF = int(0.06 * SR)
half = (STROKE + XF) // 2
t = np.linspace(0.0, 1.0, XF, dtype=np.float32)[:, None]
strokes = []
for i in range(8):
    s0, s1 = events[i] - PRE, events[i + 1] - PRE
    head = x[s0:s0 + half]
    tail = x[s1 - (STROKE + XF - half):s1]
    mid = head[-XF:] * np.cos(t * np.pi / 2) + tail[:XF] * np.sin(t * np.pi / 2)
    strokes.append(np.concatenate([head[:-XF], mid, tail[XF:]]))
loop = np.concatenate(strokes)
# The wrap: the loop's last 20 ms lead into what came before its first sample in the recording.
n = int(0.02 * SR)
t = np.linspace(0.0, 1.0, n, dtype=np.float32)[:, None]
lead = x[events[0] - PRE - n:events[0] - PRE]
loop[-n:] = loop[-n:] * np.cos(t * np.pi / 2) + lead * np.sin(t * np.pi / 2)
print("wiper loop", len(loop) / SR, "s; park reversal at", PRE / SR, "s into each 1.1 s")
save(f"{out}/wipers.wav", level(loop, -24.0))
