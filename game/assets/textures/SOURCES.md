# Textures and sky

All CC0 (public domain dedication); re-encoded here at 1K as JPEG to keep the repository small.

| File | Source | Author / site | License |
|---|---|---|---|
| `grass_color.jpg`, `grass_normal.jpg`, `grass_roughness.jpg` | ambientCG **Grass004** (1K-JPG: Color, NormalGL, Roughness) | https://ambientcg.com/view?id=Grass004 | CC0 1.0 |
| `asphalt_color.jpg`, `asphalt_normal.jpg`, `asphalt_roughness.jpg` | ambientCG **Asphalt004** (1K-JPG) | https://ambientcg.com/view?id=Asphalt004 | CC0 1.0 |
| `concrete_color.jpg`, `concrete_normal.jpg`, `concrete_roughness.jpg` | ambientCG **Concrete034** (1K-JPG) | https://ambientcg.com/view?id=Concrete034 | CC0 1.0 |
| `dirt_color.jpg`, `dirt_normal.jpg`, `dirt_roughness.jpg` | ambientCG **Ground037** (1K-JPG) — shoulders, ploughed fields, hay | https://ambientcg.com/view?id=Ground037 | CC0 1.0 |
| `bark_color.jpg`, `bark_normal.jpg`, `bark_roughness.jpg` | ambientCG **Bark012** (1K-JPG) — tree trunks and branches | https://ambientcg.com/view?id=Bark012 | CC0 1.0 |
| `siding_color.jpg`, `siding_normal.jpg`, `siding_roughness.jpg` | ambientCG **WoodSiding009** (1K-JPG) — barn and houses | https://ambientcg.com/view?id=WoodSiding009 | CC0 1.0 |
| `roof_color.jpg`, `roof_normal.jpg`, `roof_roughness.jpg` | ambientCG **RoofingTiles003** (1K-JPG) — roofs | https://ambientcg.com/view?id=RoofingTiles003 | CC0 1.0 |
| `bricks_color.jpg`, `bricks_normal.jpg`, `bricks_roughness.jpg` | ambientCG **Bricks078** (1K-JPG) — the hut, chimneys | https://ambientcg.com/view?id=Bricks078 | CC0 1.0 |
| `metal_color.jpg`, `metal_normal.jpg`, `metal_roughness.jpg` | ambientCG **MetalPlates006** (1K-JPG) — shop roof and canopy, the train | https://ambientcg.com/view?id=MetalPlates006 | CC0 1.0 |
| `leather_color.jpg`, `leather_normal.jpg`, `leather_roughness.jpg` | ambientCG **Leather037** (1K-JPG) — seats, door trim, the dashboard and the wheel rim (tinted black) | https://ambientcg.com/view?id=Leather037 | CC0 1.0 |
| `carpet_color.jpg`, `carpet_normal.jpg` | ambientCG **Carpet016** (1K-JPG) — the cabin floor (tinted dark) | https://ambientcg.com/view?id=Carpet016 | CC0 1.0 |
| `fabric_color.jpg`, `fabric_normal.jpg` | ambientCG **Fabric030** (1K-JPG) — the roof lining | https://ambientcg.com/view?id=Fabric030 | CC0 1.0 |
| `leaf_clusters.png` | composited by `tools/make-foliage.py` from the cutouts of ambientCG **LeafSet001** (1K-JPG colour sheet kept as `tools/leafset001.png`) | https://ambientcg.com/view?id=LeafSet001 | CC0 1.0 |
| `grass_blades.png` | drawn by `tools/make-foliage.py` (no source material) | this repository | MIT |
| `road_wear.png` | drawn by `tools/make-road-wear.py` (no source material): the road's wear sheet, tar snakes, patch seams and oil spots, multiplied into the asphalt over its second UV set | this repository | MIT |
| `clouds.png` | drawn by `tools/make-clouds.py` (no source material): a tileable sheet of fair-weather cumulus, white with a grey underside on a transparent ground | this repository | MIT |
| `sky.hdr` | Poly Haven **kloofendal_43d_clear_puresky** (2K HDR; the partly cloudy 48d sky of the same place was used before, its haze washed the plain out) | https://polyhaven.com/a/kloofendal_43d_clear_puresky | CC0 1.0 |

ambientCG: https://ambientcg.com (assets dedicated to the public domain under CC0). Poly Haven:
https://polyhaven.com (all assets CC0). The normal maps are OpenGL convention, as Godot expects. The sun's
direction in the HDRI was measured from the brightest patch of the image and the directional light is
aimed the same way so shadows agree with the sky.
