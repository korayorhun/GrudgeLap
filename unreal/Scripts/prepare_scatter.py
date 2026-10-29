"""Scatters dense grass, rocks and tree groups around the start of the track for the feasibility shots.

Usage: python prepare_scatter.py <track.glb> <track.json> <out scatter.json>

The game's own grass (track.json) is spread thinly over the whole circuit for Godot's sake; the shots need a meadow
that reaches the road edge. Heights come from the exported terrain mesh and nothing lands on the road, its
shoulders and lines, the fields, the buildings or the skid pad: those triangles are rasterised onto the same grid
as a mask. Output positions are metres on Godot axes (+Y up), like track.json, so build_scene.py maps both alike.
"""
import json
import math
import struct
import sys

import numpy as np

CELL = 0.5          # grid resolution, metres
HALF = 700.0        # the grid covers the car's position +- HALF on X and Z
BAND = 40.0          # metres either side of every road that get grass beyond the start
BAND_DENSITY = 0.35  # tufts per square metre there, as the meadow's outer ring
BAND_LIMIT = 260000
NO_GRASS = ("Road-", "Shoulder-", "Lines-", "Field-", "SkidPad", "Pillars-", "Railings-", "House-", "Barn-",
            "Shop-", "Hut-", "Gate-", "Barrier-", "Pump-", "Pier-", "Railway", "Towers")
rng = np.random.default_rng(11)


def load_glb(path):
    with open(path, "rb") as f:
        f.read(12)
        length, _ = struct.unpack("<II", f.read(8))
        doc = json.loads(f.read(length))
        length, _ = struct.unpack("<II", f.read(8))
        blob = f.read(length)
    return doc, blob


def accessor(doc, blob, index):
    a = doc["accessors"][index]
    view = doc["bufferViews"][a["bufferView"]]
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
    dtype = np.dtype({5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a["componentType"]])
    offset = view.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = view.get("byteStride", 0) or width * dtype.itemsize
    raw = np.frombuffer(blob, dtype=np.uint8, count=stride * (a["count"] - 1) + width * dtype.itemsize, offset=offset)
    rows = np.lib.stride_tricks.as_strided(raw, shape=(a["count"], width * dtype.itemsize), strides=(stride, 1))
    return rows.copy().view(dtype).reshape(a["count"], width)


def node_matrix(node):
    if "matrix" in node:
        return np.array(node["matrix"], dtype=float).reshape(4, 4).T
    t = node.get("translation", [0, 0, 0])
    x, y, z, w = node.get("rotation", [0, 0, 0, 1])
    s = node.get("scale", [1, 1, 1])
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = r * np.array(s)
    m[:3, 3] = t
    return m


def triangles_by_node(doc, blob):
    """Yields (top-level node name, world-space triangles (n, 3, 3)) for every mesh in the scene."""
    nodes = doc["nodes"]

    def walk(index, parent, top):
        node = nodes[index]
        world = parent @ node_matrix(node)
        if "mesh" in node:
            for prim in doc["meshes"][node["mesh"]]["primitives"]:
                pos = accessor(doc, blob, prim["attributes"]["POSITION"]).astype(float)
                pos = pos @ world[:3, :3].T + world[:3, 3]
                idx = accessor(doc, blob, prim["indices"]).reshape(-1) if "indices" in prim else np.arange(len(pos))
                yield top, pos[idx.reshape(-1, 3)]
        for child in node.get("children", []):
            yield from walk(child, world, top)

    for root in doc["scenes"][doc.get("scene", 0)]["nodes"]:
        for child in nodes[root].get("children", []):
            yield from walk(child, node_matrix(nodes[root]), nodes[child].get("name", ""))


class Grid:
    def __init__(self, centre, half=HALF, cell=CELL):
        self.cell = cell
        self.origin = np.array([centre[0] - half, centre[2] - half])
        self.n = int(2 * half / cell)
        self.height = np.full((self.n, self.n), np.nan, dtype=np.float32)
        self.blocked = np.zeros((self.n, self.n), dtype=bool)

    def rasterise(self, tris, height):
        """Writes every cell whose centre lies in a triangle (XZ projection): its height, or the block flag."""
        xz = tris[:, :, [0, 2]]
        lo = np.floor((xz.min(1) - self.origin) / self.cell).astype(int)
        hi = np.ceil((xz.max(1) - self.origin) / self.cell).astype(int)
        keep = (hi[:, 0] >= 0) & (hi[:, 1] >= 0) & (lo[:, 0] < self.n) & (lo[:, 1] < self.n)
        for tri, (i0, j0), (i1, j1) in zip(tris[keep], lo[keep], hi[keep]):
            i0, j0 = max(i0, 0), max(j0, 0)
            i1, j1 = min(i1, self.n), min(j1, self.n)
            if i1 <= i0 or j1 <= j0:
                continue
            gx, gz = np.meshgrid(self.origin[0] + (np.arange(i0, i1) + 0.5) * self.cell,
                                 self.origin[1] + (np.arange(j0, j1) + 0.5) * self.cell, indexing="ij")
            (ax, ay, az), (bx, by, bz), (cx, cy, cz) = tri
            det = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz)
            if abs(det) < 1e-9:
                continue
            w0 = ((bz - cz) * (gx - cx) + (cx - bx) * (gz - cz)) / det
            w1 = ((cz - az) * (gx - cx) + (ax - cx) * (gz - cz)) / det
            w2 = 1 - w0 - w1
            inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
            if height:
                block = self.height[i0:i1, j0:j1]
                block[inside] = (w0 * ay + w1 * by + w2 * cy)[inside]
            else:
                self.blocked[i0:i1, j0:j1] |= inside

    def dilate(self, cells):
        blocked = self.blocked.copy()
        for di in range(-cells, cells + 1):
            for dj in range(-cells, cells + 1):
                if di * di + dj * dj <= cells * cells:
                    blocked |= np.roll(np.roll(self.blocked, di, 0), dj, 1)
        self.blocked = blocked

    def lookup(self, x, z):
        i = np.floor((x - self.origin[0]) / self.cell).astype(int)
        j = np.floor((z - self.origin[1]) / self.cell).astype(int)
        ok = (i >= 0) & (j >= 0) & (i < self.n) & (j < self.n)
        h = np.full(len(x), np.nan)
        b = np.ones(len(x), dtype=bool)
        h[ok] = self.height[i[ok], j[ok]]
        b[ok] = self.blocked[i[ok], j[ok]]
        return h, b


def sample(grid, centre, forward, count, r0, r1, half_angle, avoid=None):
    """Uniform points over the ring sector r0..r1 around forward (+-half_angle), off the mask, on the terrain."""
    u = rng.random(count)
    r = np.sqrt(r0 * r0 + u * (r1 * r1 - r0 * r0))
    a = math.atan2(forward[2], forward[0]) + (rng.random(count) * 2 - 1) * half_angle
    x = centre[0] + r * np.cos(a)
    z = centre[2] + r * np.sin(a)
    h, blocked = grid.lookup(x, z)
    keep = ~blocked & ~np.isnan(h)
    if avoid is not None:
        hb, bb = avoid.lookup(x, z)
        keep &= ~bb
    return x[keep], h[keep], z[keep]


def roadside_grass(meshes, car, forward):
    """Grass along the rest of the circuit: on the verges and the ground beside every road out to BAND metres, at
    the density of the meadow's outer ring, over a 1 m grid of the whole track. The meadow at the start keeps its
    own denser rings (skipped here)."""
    roads = np.concatenate([t for name, t in meshes if name.startswith(("Road-", "Shoulder-"))])
    lo = roads[:, :, [0, 2]].reshape(-1, 2).min(0) - BAND - 20.0
    hi = roads[:, :, [0, 2]].reshape(-1, 2).max(0) + BAND + 20.0
    centre = np.array([(lo[0] + hi[0]) / 2, 0.0, (lo[1] + hi[1]) / 2])
    track = Grid(centre, half=float((hi - lo).max() / 2), cell=1.0)
    near = Grid(centre, half=float((hi - lo).max() / 2), cell=1.0)
    for name, tris in meshes:
        if name == "Terrain":
            track.rasterise(tris, True)
        elif name.startswith(NO_GRASS) or name == "PlayerCar":
            track.rasterise(tris, False)
        if name.startswith(("Road-", "Shoulder-")):
            near.rasterise(tris, False)
    track.dilate(1)
    # Within BAND of a road: the road mask grown one 2 m cell at a time on a halved grid.
    n = near.n // 2 * 2
    coarse = near.blocked[:n, :n].reshape(n // 2, 2, n // 2, 2).any(axis=(1, 3))
    grown = coarse.copy()
    for _ in range(int(BAND / 2)):
        step = grown.copy()
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            step |= np.roll(np.roll(grown, di, 0), dj, 1)
        grown = step
    band = np.zeros_like(near.blocked)
    band[:n, :n] = np.repeat(np.repeat(grown, 2, 0), 2, 1)

    # Uniform over the band: a cell drawn for every tuft, a point drawn inside the cell.
    cells = np.flatnonzero(band)
    i, j = np.unravel_index(rng.choice(cells, size=int(len(cells) * BAND_DENSITY)), band.shape)
    x = track.origin[0] + (i + rng.random(len(i))) * track.cell
    z = track.origin[1] + (j + rng.random(len(j))) * track.cell
    h, blocked = track.lookup(x, z)
    keep = ~blocked & ~np.isnan(h)
    # The meadow's rings (main): r < 30, 30..100 within 70 degrees of forward, 100..400 within 60 degrees.
    dx, dz = x - car[0], z - car[2]
    r = np.hypot(dx, dz)
    off = np.abs((np.arctan2(dz, dx) - math.atan2(forward[2], forward[0]) + math.pi) % (2 * math.pi) - math.pi)
    meadow = (r < 30) | ((r < 100) & (off < math.radians(70))) | ((r < 400) & (off < math.radians(60)))
    keep &= ~meadow
    x, h, z = x[keep], h[keep], z[keep]
    out = []
    for xi, yi, zi in zip(x[:BAND_LIMIT], h[:BAND_LIMIT], z[:BAND_LIMIT]):
        out.append([round(float(xi), 3), round(float(yi), 3), round(float(zi), 3),
                    round(float(rng.random() * 2 * math.pi), 3), round(float(rng.uniform(1.6, 2.8)), 3)])
    print(f"roadside grass: {len(out)} tufts within {BAND:.0f} m of the roads")
    return out


def main():
    glb, placements_path, out = sys.argv[1:4]
    doc, blob = load_glb(glb)
    placements = json.load(open(placements_path, encoding="utf-8"))
    car = np.array(placements["car"]["position"])
    basis_z = np.array(placements["car"]["basis"][2])
    forward = -basis_z / np.linalg.norm(basis_z)

    meshes = list(triangles_by_node(doc, blob))
    grid = Grid(car)
    for name, tris in meshes:
        if name == "Terrain":
            grid.rasterise(tris, True)
        elif name.startswith(NO_GRASS) or name == "PlayerCar":
            grid.rasterise(tris, False)
    grid.dilate(2)  # keep a metre off the asphalt and shoulder edges
    # Trees and bushes want more room from the road than grass does.
    wide = Grid(car)
    wide.height, wide.blocked = grid.height, grid.blocked.copy()
    wide.dilate(10)

    grass = []
    # (count of candidates, inner and outer radius, half angle, scale range): dense by the car, thinner to the horizon.
    for count, r0, r1, half, (s0, s1) in ((30000, 0, 30, math.pi, (1.0, 1.6)),
                                          (45000, 30, 100, math.radians(70), (1.2, 2.0)),
                                          (90000, 100, 400, math.radians(60), (1.6, 2.8))):
        x, y, z = sample(grid, car, forward, count, r0, r1, half)
        for xi, yi, zi in zip(x, y, z):
            grass.append([round(float(xi), 3), round(float(yi), 3), round(float(zi), 3),
                          round(float(rng.random() * 2 * math.pi), 3), round(float(rng.uniform(s0, s1)), 3)])

    grass += roadside_grass(meshes, car, forward)

    rocks = []
    for count, r0, r1, kind, (s0, s1) in ((400, 0, 60, "rock_07", (1.0, 3.0)), (250, 0, 60, "rock_09", (2.0, 5.0)),
                                          (40, 15, 250, "boulder_01", (0.5, 1.4))):
        x, y, z = sample(grid, car, forward, count, r0, r1, math.radians(80), avoid=wide if kind == "boulder_01" else None)
        for xi, yi, zi in zip(x, y, z):
            rocks.append({"kind": kind, "position": [float(xi), float(yi) - 0.05, float(zi)],
                          "yaw": float(rng.random() * 2 * math.pi), "scale": float(rng.uniform(s0, s1))})

    trees = []
    # Copses and hedgerows the way the countryside has them: a few dozen groups, each a handful of trees.
    cx, cy, cz = sample(grid, car, forward, 80, 60, 650, math.radians(65), avoid=wide)
    for gx, gz in list(zip(cx, cz))[:40]:
        n = int(rng.integers(3, 12))
        x = gx + rng.normal(0, 9, n)
        z = gz + rng.normal(0, 9, n)
        h, blocked = wide.lookup(x, z)
        for xi, yi, zi, bad in zip(x, h, z, blocked):
            if not bad and not np.isnan(yi):
                trees.append({"position": [float(xi), float(yi), float(zi)], "yaw": float(rng.random() * 360),
                              "scale": float(rng.uniform(0.8, 1.25))})
    bushes = []
    x, y, z = sample(grid, car, forward, 400, 8, 200, math.radians(75), avoid=wide)
    for xi, yi, zi in zip(x, y, z):
        bushes.append({"position": [float(xi), float(yi), float(zi)], "yaw": float(rng.random() * 360),
                       "scale": float(rng.uniform(0.6, 1.3))})

    # A photographer's view of the car from the meadow: ahead and to the left, looking back at the car. The first
    # spot off the road along that line is taken, so the grass fills the foreground. The eye stays a metre over the
    # road: the road and its verges are slabs floating up to 1.4 m over the dipping terrain, with no embankment
    # under their edges, and a lower eye looks into the shadow beneath them.
    right = -np.array(placements["car"]["basis"][0]) / np.linalg.norm(placements["car"]["basis"][0])
    roadside = None
    for side in np.arange(9.0, 30.0, 0.5):
        spot = car + forward * 13.0 + right * side
        h, blocked = grid.lookup(np.array([spot[0]]), np.array([spot[2]]))
        if not blocked[0] and not np.isnan(h[0]):
            eye = [float(spot[0]), max(float(h[0]) + 1.1, float(car[1]) + 1.0), float(spot[2])]
            target = [float(car[0]), float(car[1]) + 0.1, float(car[2])]
            roadside = {"eye": eye, "target": target, "fov": 38.0}
            break

    json.dump({"units": placements["units"], "grass": grass, "rocks": rocks, "trees": trees, "bushes": bushes,
               "roadside_camera": roadside}, open(out, "w", encoding="utf-8"))
    print(f"grass {len(grass)}, rocks {len(rocks)}, trees {len(trees)}, bushes {len(bushes)}, roadside {roadside} -> {out}")


main()
