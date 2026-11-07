# The scanned trees

Impostor atlases of Poly Haven's photoscanned trees (CC0 1.0), read by `game/src/FoliageBuilder.cs` (0.39).

| Files | Source |
|---|---|
| `fir_a_*`, `fir_b_*`, `fir_c_*` | "Fir Tree 01", https://polyhaven.com/a/fir_tree_01, its objects `fir_tree_01_a_LOD0`, `_b_LOD0`, `_c_LOD0` |
| `jacaranda_*` | "Jacaranda Tree", https://polyhaven.com/a/jacaranda_tree |
| `tree_small_02_*` | "Tree Small 02", https://polyhaven.com/a/tree_small_02 |

Each `_albedo.png` (RGBA, sRGB) and `_normal.png` (the normal in each view's camera frame) holds 12 views, 4 x 3, and
the `.json` the frame's size in metres. To make them again with Blender 5.2 (the glTF at 1K from Poly Haven):

```
blender -b --factory-startup --python tools/bake-impostors.py -- fir_tree_01_1k.gltf bake/fir_a --object fir_tree_01_a_LOD0 --views 12 --frame 1024
python tools/pack-impostors.py bake/fir_a game/assets/foliage fir_a 768
```

The atlases' `.import` ask for VRAM compression and mipmaps; after changing that, delete their `.md5` under
`game/.godot/imported` and run `godot --headless --path game --import`.
