"""The real land round the plain (0.37): a stretch of Jackson Hole, Wyoming, the Teton Range on one side and the
Gros Ventre on the other, from open data, for WorldBuilder.BuildRealLand.

Until 0.37 the mountains on the horizon were a ring of noise with vertex colours: under the haze they read as a bank
of cloud (the Godot/Unreal comparison of 7 November 2026). This tool cuts a square of real ground round the circuit
and writes two files the game reads:

  <out>.bin        zlib: a header and, per sample of a regular grid (step metres), the height over the valley floor
                   at the circuit (int16, decimetres), the sun's visibility from the game's sun (uint8: the mountains
                   throw their shadows, too far for the shadow maps), the sky's (uint8: valleys and gullies darker) and,
                   since version 2 (0.55), the visibility of the sunrise's sun (uint8, WorldBuilder.SunriseDirection) and,
                   since version 3 (0.60), the dusk's (uint8, WorldBuilder.DuskDirection: the sun just behind the Tetons)
  <out>_albedo.jpg the ground's colour from a Sentinel-2 image, the image's own sunlight divided out (its sun stood
                   higher than the game's) and the snow kept white

Sources (THIRD_PARTY.md): the elevation is the USGS 3D Elevation Program's 1 arc-second model (public domain), the
colour Copernicus Sentinel-2 L2A (free and open, credited as "Contains modified Copernicus Sentinel data 2024").

The game's frame: +Y up, the circuit centred at (cx, cz); --heading is the compass bearing of +X (270: +X points
west, +Z north, so the start straight looks at the Tetons and the game's sun, from 144 degrees, lights their face
as the image's did).

Usage: python tools/make-terrain.py --dem USGS_1_n44w111.tif --image S2B_12TWP_20240705_TCI.tif --sun-az 143.0
       --sun-el 65.1 --lat 43.668 --lon -110.664 --out game/assets/terrain/jackson
       python tools/make-terrain.py --from-bin game/assets/terrain/jackson.bin --out game/assets/terrain/jackson
         (a written grid's heights and sky kept and both suns' shadows cast again, without the rasters)
"""
import argparse
import math
import struct
import zlib

import numpy as np
import tifffile
from PIL import Image
from pyproj import Transformer
from scipy import ndimage

MAGIC = b"GLPT"
VERSION = 3
HEADER = "<4sIIffffff"


def geotiff(path):
    """The raster and its georeference: (array, x of the top-left corner, y of it, pixel width, pixel height)."""
    with tifffile.TiffFile(path) as tif:
        page = tif.pages[0]
        scale = page.tags["ModelPixelScaleTag"].value
        tie = page.tags["ModelTiepointTag"].value
        return page.asarray(), tie[3], tie[4], scale[0], scale[1]


def bilinear(array, rows, cols):
    """Samples a 2D array (or each channel of a 3D one) at fractional rows and columns; outside is the nearest edge."""
    coords = np.vstack([rows.ravel(), cols.ravel()])
    if array.ndim == 2:
        return ndimage.map_coordinates(array, coords, order=1, mode="nearest").reshape(rows.shape)
    return np.stack([ndimage.map_coordinates(array[..., c], coords, order=1, mode="nearest").reshape(rows.shape)
                     for c in range(array.shape[2])], axis=-1)


def frame(heading):
    """The map's east and north from the game's x and z (relative to the circuit's centre), and back (the same matrix:
    a reflection, as seen from above the game's x and z run like a map's east and south)."""
    s, c = math.sin(math.radians(heading)), math.cos(math.radians(heading))
    return lambda x, z: (x * s + z * c, x * c - z * s)


def sun_visibility(height, step, sun):
    """How much of a sun each sample sees (0 to 1): the land marched towards it for 12 km, the edge soft over 8 m."""
    sx, sy, sz = (float(v) for v in sun.split(","))
    length = math.hypot(sx, sz)
    ux, uz = sx / length, sz / length         # towards the sun, along the ground
    tan_el = sy / length
    n = height.shape[0]
    rows0, cols0 = np.mgrid[0:n, 0:n].astype(np.float32)
    lit = np.ones_like(height)
    t = step
    while t < 12000.0:
        dz, dx = uz * t / step, ux * t / step
        ahead = ndimage.map_coordinates(height, [rows0 + dz, cols0 + dx], order=1, mode="nearest")
        # Soft over the last 8 m: a ridge's shadow edge without stair steps.
        clearance = height + t * tan_el - ahead
        lit = np.minimum(lit, np.clip(clearance / 8.0 + 0.5, 0.0, 1.0))
        t *= 1.04
        t += step
    return lit


def write_grid(path, n, step, half, base, cx, cz, heading, height, lit, sky, lit_sunrise, lit_dusk):
    with open(path, "wb") as f:
        body = bytearray()
        body += struct.pack(HEADER, MAGIC, VERSION, n, step, half, base, cx, cz, heading)
        body += np.clip(np.round(height * 10.0), -32768, 32767).astype("<i2").tobytes()
        for array in (lit, sky, lit_sunrise, lit_dusk):
            body += np.clip(np.round(array * 255.0), 0, 255).astype(np.uint8).tobytes()
        f.write(zlib.compress(bytes(body), 9))
    print("wrote", path)


def from_bin(path, out, game_sun, sunrise_sun, dusk_sun):
    """A written grid again: its heights and sky kept, both suns' shadows cast anew (the rasters not needed)."""
    raw = zlib.decompress(open(path, "rb").read())
    magic, version, n, step, half, base, cx, cz, heading = struct.unpack_from(HEADER, raw, 0)
    if magic != MAGIC:
        raise SystemExit(f"{path} is not a land grid")
    off = struct.calcsize(HEADER)
    height = np.frombuffer(raw, dtype="<i2", count=n * n, offset=off).reshape(n, n).astype(np.float32) / 10.0
    sky = np.frombuffer(raw, dtype=np.uint8, count=n * n, offset=off + 3 * n * n).reshape(n, n).astype(np.float32) / 255.0
    lit = sun_visibility(height, step, game_sun)
    lit_sunrise = sun_visibility(height, step, sunrise_sun)
    lit_dusk = sun_visibility(height, step, dusk_sun)
    print(f"in shadow: the game's sun {100 * (lit < 0.5).mean():.1f} %, the sunrise's {100 * (lit_sunrise < 0.5).mean():.1f} %, the dusk's {100 * (lit_dusk < 0.5).mean():.1f} %")
    write_grid(out + ".bin", n, step, half, base, cx, cz, heading, height, lit, sky, lit_sunrise, lit_dusk)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--from-bin", help="cast the shadows of a written grid again instead of reading the rasters")
    ap.add_argument("--dem")
    ap.add_argument("--image")
    ap.add_argument("--image-epsg", type=int, default=32612)
    ap.add_argument("--sun-az", type=float, help="the image's sun: azimuth, degrees from north")
    ap.add_argument("--sun-el", type=float, help="the image's sun: elevation, degrees")
    ap.add_argument("--lat", type=float, help="the circuit's centre")
    ap.add_argument("--lon", type=float)
    ap.add_argument("--heading", type=float, default=270.0, help="compass bearing of the game's +X")
    ap.add_argument("--cx", type=float, default=-7.4, help="the circuit's centre in the game (Godot x)")
    ap.add_argument("--cz", type=float, default=115.6, help="the circuit's centre in the game (Godot z)")
    ap.add_argument("--game-sun", default="-0.431,0.682,-0.591", help="WorldBuilder.SunDirection (towards the sun, Godot)")
    ap.add_argument("--sunrise-sun", default="-0.991,0.102,0.087", help="WorldBuilder.SunriseDirection (towards the sun, Godot)")
    ap.add_argument("--dusk-sun", default="0.961,0.102,0.257", help="WorldBuilder.DuskDirection (towards the sun, Godot)")
    ap.add_argument("--half", type=float, default=24000.0, help="half the square's side, metres")
    ap.add_argument("--step", type=float, default=30.0, help="the height grid's step, metres")
    ap.add_argument("--texture", type=int, default=5120, help="the colour texture's side, pixels")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    if a.from_bin:
        from_bin(a.from_bin, a.out, a.game_sun, a.sunrise_sun, a.dusk_sun)
        return
    if not (a.dem and a.image and a.sun_az is not None and a.sun_el is not None and a.lat is not None and a.lon is not None):
        raise SystemExit("--dem, --image, --sun-az, --sun-el, --lat and --lon are needed (or --from-bin)")

    to_map = frame(a.heading)
    tm = f"+proj=tmerc +lat_0={a.lat} +lon_0={a.lon} +k=1 +x_0=0 +y_0=0 +datum=WGS84 +units=m +no_defs"
    tm_to_geo = Transformer.from_crs(tm, "EPSG:4326", always_xy=True)
    tm_to_image = Transformer.from_crs(tm, f"EPSG:{a.image_epsg}", always_xy=True)

    dem, dem_x0, dem_y0, dem_dx, dem_dy = geotiff(a.dem)
    dem = dem.astype(np.float32)
    dem[dem < -1000] = np.nan

    def heights_at(x, z):
        east, north = to_map(x, z)
        lon, lat = tm_to_geo.transform(east, north)
        # Pixel is area: a pixel's centre is half a pixel in from its corner.
        cols = (lon - dem_x0) / dem_dx - 0.5
        rows = (dem_y0 - lat) / dem_dy - 0.5
        return bilinear(dem, rows, cols)

    # ---- the height grid -------------------------------------------------------------------------------------
    n = int(round(2 * a.half / a.step)) + 1
    axis = np.linspace(-a.half, a.half, n)
    gx, gz = np.meshgrid(axis, axis)          # [row = z, column = x], relative to the circuit's centre
    raw = heights_at(gx, gz)
    if np.isnan(raw).any():
        raise SystemExit("the elevation model does not cover the square")
    near = gx ** 2 + gz ** 2 < 2500.0 ** 2
    base = float(np.median(raw[near]))
    height = raw - base
    print(f"grid {n} x {n} at {a.step} m; valley floor {base:.0f} m; heights {height.min():.0f} .. {height.max():.0f} m")

    # ---- the game's sun over the land: shadows and the sky's share -------------------------------------------
    lit = sun_visibility(height, a.step, a.game_sun)
    lit_sunrise = sun_visibility(height, a.step, a.sunrise_sun)
    lit_dusk = sun_visibility(height, a.step, a.dusk_sun)
    rows0, cols0 = np.mgrid[0:n, 0:n].astype(np.float32)
    sky = np.zeros_like(height)
    directions = 12
    for k in range(directions):
        angle = 2 * math.pi * k / directions
        dxk, dzk = math.cos(angle), math.sin(angle)
        horizon = np.zeros_like(height)
        t = a.step
        while t < 3000.0:
            ahead = ndimage.map_coordinates(height, [rows0 + dzk * t / a.step, cols0 + dxk * t / a.step], order=1, mode="nearest")
            horizon = np.maximum(horizon, (ahead - height) / t)
            t *= 1.15
            t += a.step
        sky += 1.0 - np.sin(np.arctan(horizon))
    sky /= directions
    print(f"in shadow {100 * (lit < 0.5).mean():.1f} % (sunrise {100 * (lit_sunrise < 0.5).mean():.1f} %), sky {sky.min():.2f} .. {sky.max():.2f}")

    write_grid(a.out + ".bin", n, a.step, a.half, base, a.cx, a.cz, a.heading, height, lit, sky, lit_sunrise, lit_dusk)

    # ---- the colour --------------------------------------------------------------------------------------------
    image, im_x0, im_y0, im_dx, im_dy = geotiff(a.image)
    # Only the window the square covers (plus a margin), each band contiguous: the whole 110 km tile as floats is
    # 1.4 GB and every lookup would copy a band of it.
    corner_x = np.array([-a.half, a.half, -a.half, a.half]) * 1.02
    corner_z = np.array([-a.half, -a.half, a.half, a.half]) * 1.02
    ce, cn = to_map(corner_x, corner_z)
    cix, ciy = tm_to_image.transform(ce, cn)
    c0 = max(int((cix.min() - im_x0) / im_dx) - 2, 0)
    c1 = min(int((cix.max() - im_x0) / im_dx) + 2, image.shape[1])
    r0w = max(int((im_y0 - ciy.max()) / im_dy) - 2, 0)
    r1w = min(int((im_y0 - ciy.min()) / im_dy) + 2, image.shape[0])
    bands = [np.ascontiguousarray(image[r0w:r1w, c0:c1, b], dtype=np.float32) / 255.0 for b in range(3)]
    im_x0 += c0 * im_dx
    im_y0 -= r0w * im_dy
    del image
    size = a.texture
    texel = 2 * a.half / size
    # The image's sun, in the game's frame, for its shading.
    az, el = math.radians(a.sun_az), math.radians(a.sun_el)
    e_s, n_s = math.sin(az) * math.cos(el), math.cos(az) * math.cos(el)
    s_x, s_z = to_map(e_s, n_s)                # the same matrix takes east/north back to x/z
    s_y = math.sin(el)
    # Normals of the height grid (Godot frame: rows run with +z, columns with +x).
    dhdz, dhdx = np.gradient(height, a.step)
    norm = np.sqrt(dhdx ** 2 + dhdz ** 2 + 1.0)
    shade = (-dhdx * s_x - dhdz * s_z + s_y) / norm
    ambient = 0.35
    relative = (ambient + (1 - ambient) * np.clip(shade, 0, 1)) / (ambient + (1 - ambient) * s_y)
    out = np.zeros((size, size, 3), np.uint8)
    block = 256
    centres = (np.arange(size) + 0.5) * texel - a.half
    for r0 in range(0, size, block):
        z = centres[r0:r0 + block][:, None] * np.ones((1, size))
        x = np.ones((z.shape[0], 1)) * centres[None, :]
        east, north = to_map(x, z)
        ix, iy = tm_to_image.transform(east, north)
        cols = (ix - im_x0) / im_dx - 0.5
        rows = (im_y0 - iy) / im_dy - 0.5
        rgb = np.stack([bilinear(b, rows, cols) for b in bands], axis=-1)
        # Sentinel-2's true-colour product maps reflectance 0..0.2 to 0..255 and saturates above: the snow is white in
        # all three bands, before any shading is taken out.
        snow = np.clip((rgb.min(axis=-1) - 0.88) / 0.10, 0.0, 1.0)[..., None]
        # The image's own sunlight divided out, most of it: what is left is the ground's colour for the game's sun
        # to light. The ratio is held in so a shadowed slope is not lifted into noise.
        grid_r = (z + a.half) / a.step
        grid_c = (x + a.half) / a.step
        rel = bilinear(relative, grid_r, grid_c)
        rgb = rgb / np.clip(rel, 0.7, 1.4)[..., None] ** 0.75
        # Reflectance from the product's scale (0.2 at 255), lifted a little (the product is of the ground under a
        # clear summer sky; the game's light is softer); dark ground ends near its old value as an sRGB colour,
        # brighter ground is held down towards its reflectance.
        linear = np.clip(rgb, 0, 1) * 0.2 * 1.6
        srgb = np.where(linear <= 0.0031308, linear * 12.92, 1.055 * np.power(np.clip(linear, 1e-6, None), 1 / 2.4) - 0.055)
        # The valley floor (below 350 m over the circuit) graded towards the olive grey the sagebrush shows from the
        # ground (the product shows it a pinkish tan from above), so the plain's green runs into it without a band.
        valley = np.clip((350.0 - bilinear(height, grid_r, grid_c)) / 250.0, 0.0, 1.0)[..., None]
        srgb = srgb * (1.0 - valley * (1.0 - np.array([0.84, 0.95, 0.68])))
        srgb = srgb * (1 - snow) + 0.93 * snow
        out[r0:r0 + block] = np.clip(srgb * 255.0 + 0.5, 0, 255).astype(np.uint8)
        print(f"  colour rows {r0 + z.shape[0]}/{size}", end="\r")
    print()
    # Row 0 is -z (the texture is laid with u along +x and v along +z).
    Image.fromarray(out).save(a.out + "_albedo.jpg", quality=90, optimize=True)
    print("wrote", a.out + "_albedo.jpg")


if __name__ == "__main__":
    main()
