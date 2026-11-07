"""The prairie's ambience loop (0.52) from dsebeste's PrairieSummer_ambience (Freesound #351609, CC0)."""
import subprocess, sys, wave
import numpy as np

src, dst = sys.argv[1], sys.argv[2]
SR = 44100
raw = subprocess.run(["ffmpeg", "-v", "error", "-i", src, "-ac", "2", "-ar", str(SR), "-af", "highpass=f=80", "-f", "f32le", "-"],
                     capture_output=True, check=True).stdout
x = np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()
print("seconds", len(x) / SR)
# Leave the first and last two seconds (the recorder's handling) and take up to 50 s.
start = 2 * SR
x = x[start:start + 50 * SR]
fade = 3 * SR
# A seamless loop: the last three seconds cross-faded (equal power) into the first three, which are then dropped.
t = np.linspace(0.0, 1.0, fade, dtype=np.float32)[:, None]
head = x[:fade]
tail = x[-fade:]
x = np.concatenate([x[fade:-fade], tail * np.cos(t * np.pi / 2) + head * np.sin(t * np.pi / 2)])
# The recorder's ticks (broadband clicks a few milliseconds long): found where the band over 6 kHz jumps far above its
# running level, and patched with the sound of a tenth of a second before, cross-faded over 5 ms either side.
spec = np.fft.rfft(x.mean(axis=1))
freqs = np.fft.rfftfreq(len(x), 1.0 / SR)
spec[freqs < 6000] = 0
high = np.fft.irfft(spec, len(x))
win = int(0.005 * SR)
def moving_mean(v, n):
    c = np.cumsum(np.concatenate([np.zeros(n // 2 + 1), v, np.full(n - n // 2, v[-1])]))
    return (c[n:n + len(v)] - c[:len(v)]) / n
env = np.sqrt(np.maximum(moving_mean(high * high, win), 0))
ref = moving_mean(env, SR)
clicks = np.flatnonzero(env > 4.5 * np.maximum(ref, 1e-9))
patched = 0
i = 0
while i < len(clicks):
    j = i
    while j + 1 < len(clicks) and clicks[j + 1] - clicks[j] < win * 4:
        j += 1
    a, b = max(clicks[i] - 4 * win, SR // 5), min(clicks[j] + 4 * win, len(x) - 1)
    if b > a:
        lag = SR // 10
        ramp = np.ones(b - a, dtype=np.float32)
        r = min(win, (b - a) // 2)
        ramp[:r] = np.linspace(0, 1, r)
        ramp[-r:] = np.linspace(1, 0, r)
        x[a:b] = x[a:b] * (1 - ramp[:, None]) + x[a - lag:b - lag] * ramp[:, None]
        patched += 1
    i = j + 1
print("ticks patched", patched)
# And the two the detector lets through on this recording, at the loop's 17.6 and 19.5 s, by hand.
for lo, hi in ((17.61, 17.67), (19.45, 19.70)):
    a, b = int(lo * SR), int(hi * SR)
    lag = SR // 10
    ramp = np.ones(b - a, dtype=np.float32)
    r = int(0.005 * SR)
    ramp[:r] = np.linspace(0, 1, r)
    ramp[-r:] = np.linspace(1, 0, r)
    x[a:b] = x[a:b] * (1 - ramp[:, None]) + x[a - lag:b - lag] * ramp[:, None]
rms = float(np.sqrt(np.mean(x * x)))
# A quiet bed: -30 dBFS RMS; the game turns it down further for the glass and the weather.
x *= (10 ** (-30 / 20)) / max(rms, 1e-9)
peak = float(np.abs(x).max())
print("rms before", 20 * np.log10(rms), "dBFS, peak after", 20 * np.log10(peak), "dBFS, seconds", len(x) / SR)
pcm = (np.clip(x, -1.0, 1.0) * 32767).astype("<i2")
with wave.open(dst, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())
print("written", dst)
