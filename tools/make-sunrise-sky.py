"""The sunrise's sky (0.55): Poly Haven's "Syferfontein 6d Clear (Pure Sky)" (Greg Zaal, sky edits Jarod Guest, CC0), a
clear sky with the sun 5.85 degrees up, turned so that its sun stands where WorldBuilder.SunriseDirection says: compass
bearing 85, a September sunrise over the Gros Ventre, behind a driver on the start straight, whose low light catches the
Tetons' faces as the Jackson Hole postcards have it.

    python tools/make-sunrise-sky.py syferfontein_6d_clear_puresky_2k.hdr game/assets/textures/sky_sunrise.hdr
    python tools/make-sunrise-sky.py syferfontein_6d_clear_puresky_2k.hdr game/assets/textures/sky_dusk.hdr 285

The dusk's sky (0.60) is the same photo turned to bearing 285: the sun just set behind the Tetons, whose ridge stands
7.5 to 9 degrees high from the circuit that way, its glow over them (WorldBuilder.DuskDirection).

The game's panorama puts a direction (x, z) at u = 0.5 + atan2(-x, -z) / 2 pi (WorldBuilder.SunDirection's sun, u 0.600
in sky.hdr, measured the same way), and the game's frame has +X west and +Z north (tools/make-terrain.py), so bearing b
is (x, z) = (-sin b, cos b). Rolling the image's columns is the whole turn: nothing is resampled.
"""
import math
import sys

import numpy as np

BEARING = 85.0


def read_hdr(path):
    data = open(path, "rb").read()
    pos = 0
    header = []
    while True:
        end = data.index(b"\n", pos)
        line = data[pos:end]
        pos = end + 1
        if line == b"":
            break
        header.append(line)
    end = data.index(b"\n", pos)
    res = data[pos:end].split()
    pos = end + 1
    h, w = int(res[1]), int(res[3])
    out = np.zeros((h, w, 4), dtype=np.uint8)
    for y in range(h):
        if data[pos] == 2 and data[pos + 1] == 2:
            pos += 4
            for c in range(4):
                x = 0
                row = out[y, :, c]
                while x < w:
                    n = data[pos]
                    pos += 1
                    if n > 128:
                        n -= 128
                        row[x:x + n] = data[pos]
                        pos += 1
                    else:
                        row[x:x + n] = np.frombuffer(data, dtype=np.uint8, count=n, offset=pos)
                        pos += n
                    x += n
        else:
            out[y] = np.frombuffer(data, dtype=np.uint8, count=w * 4, offset=pos).reshape(w, 4)
            pos += w * 4
    return out, header


def rle(row):
    out = bytearray()
    n = len(row)
    i = 0
    while i < n:
        j = i
        while j + 1 < n and row[j + 1] == row[i] and j - i < 126:
            j += 1
        if j - i >= 2:
            out += bytes([128 + j - i + 1, row[i]])
            i = j + 1
            continue
        k = i
        while k < n and k - i < 128:
            if k + 2 < n and row[k] == row[k + 1] == row[k + 2]:
                break
            k += 1
        out += bytes([k - i]) + bytes(row[i:k])
        i = k
    return out


def write_hdr(path, rgbe, header):
    h, w = rgbe.shape[:2]
    with open(path, "wb") as f:
        f.write(b"#?RADIANCE\n")
        for line in header:
            if not line.startswith(b"#?") and not line.startswith(b"FORMAT"):
                f.write(line + b"\n")
        f.write(b"FORMAT=32-bit_rle_rgbe\n\n")
        f.write(f"-Y {h} +X {w}\n".encode())
        for y in range(h):
            f.write(bytes([2, 2, w >> 8, w & 255]))
            for c in range(4):
                f.write(rle(rgbe[y, :, c].tolist()))


def sun(rgbe):
    """Where the sun is: the weighted circular mean of the brightest 0.005 % of the pixels, as (u, v)."""
    h, w = rgbe.shape[:2]
    e = rgbe[:, :, 3].astype(np.int32)
    scale = np.where(e > 0, np.ldexp(1.0, e - 136), 0.0)
    lum = (rgbe[:, :, :3].astype(np.float64) * scale[:, :, None]) @ np.array([0.2126, 0.7152, 0.0722])
    ys, xs = np.nonzero(lum >= np.percentile(lum, 99.995))
    weights = lum[ys, xs]
    angle = 2 * np.pi * (xs + 0.5) / w
    u = (math.atan2((np.sin(angle) * weights).sum(), (np.cos(angle) * weights).sum()) / (2 * np.pi)) % 1.0
    v = float(((ys + 0.5) / h * weights).sum() / weights.sum())
    return u, v


src, dst = sys.argv[1], sys.argv[2]
if len(sys.argv) > 3:
    BEARING = float(sys.argv[3])
rgbe, header = read_hdr(src)
h, w = rgbe.shape[:2]
u, v = sun(rgbe)
b = math.radians(BEARING)
target = 0.5 + math.atan2(math.sin(b), -math.cos(b)) / (2 * math.pi)
shift = int(round((target - u) * w))
rgbe = np.roll(rgbe, shift, axis=1)
write_hdr(dst, rgbe, header)
u2, v2 = sun(rgbe)
elevation = 90.0 - v2 * 180.0
x, z = -math.sin(b), math.cos(b)
c = math.cos(math.radians(elevation))
print(f"sun at u {u:.4f} -> {u2:.4f} (target {target:.4f}, {shift} columns), elevation {elevation:.2f}")
print(f"towards the sun (Godot): ({x * c:.3f}, {math.sin(math.radians(elevation)):.3f}, {z * c:.3f})")
