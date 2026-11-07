# The land round the plain

`jackson.bin` and `jackson_albedo.jpg` are made by `tools/make-terrain.py` (0.37) and read by `game/src/RealLand.cs`.

| File | Source | License |
|---|---|---|
| `jackson.bin` (heights, the game's sun's and the sky's visibility on a 30 m grid, 48 km square) | USGS 3DEP 1 arc-second DEM, tile n44w111: https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1/TIFF/current/n44w111/USGS_1_n44w111.tif | Public domain (U.S. Government work) |
| `jackson_albedo.jpg` (5120 px, 9.4 m a texel) | Copernicus Sentinel-2 L2A, S2B_12TWP_20240705, true-colour image: https://sentinel-cogs.s3.us-west-2.amazonaws.com/sentinel-s2-l2a-cogs/12/T/WP/2024/7/S2B_12TWP_20240705_0_L2A/TCI.tif (sun 142.8 degrees, 65.1 degrees up) | Contains modified Copernicus Sentinel data 2024 (free and open, attribution required) |

To make them again (Python with numpy, scipy, Pillow, tifffile, imagecodecs and pyproj):

```
python tools/make-terrain.py --dem USGS_1_n44w111.tif --image TCI.tif --sun-az 142.8 --sun-el 65.1 --lat 43.70 --lon -110.67 --out game/assets/terrain/jackson
```

Then run `godot --headless --path game --import`. The colour's `.import` asks for VRAM compression and mipmaps; after
changing those settings, delete its `.md5` under `game/.godot/imported` first (run headless, the importer notices a
changed source, not changed settings).
