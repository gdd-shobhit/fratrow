# aikit — ingest for generated models (Blender 5.0)

Shared first steps for any model that comes from an image-to-3D generator
(Tripo, Rodin, Hunyuan3D, TripoSR…). Used by `tools/blender/propkit` (static
props) and `tools/blender/charkit/ingest.py` (cosmetics). The pipeline and the
reasons behind it: `design/art/ai-asset-pipeline.md`.

```python
import sys; sys.path.insert(0, r"<repo>/tools/blender")
from aikit import importer, cleanup, normalize, reduce, textures, report, clean_glb

parts = importer.import_model(r"ArtSource/AI/Keg/raw/keg.glb")   # meshes only, world transforms kept
obj = cleanup.join(parts, "Keg")                                   # one object, transforms baked
print(cleanup.clean(obj))                                          # weld, loose bits, floaters, normals
normalize.to_ground(obj, height=0.6)                               # metres, bottom centre at origin
reduce.decimate_to(obj, 1500)                                      # collapse; UVs survive
maps = textures.extract(obj, out_dir, "Keg", size=1024)            # T_Keg_BaseMap/Normal/MetallicSmoothness.png
print(report.observe(obj))                                         # numbers only
clean_glb.save([obj], r"ArtSource/AI/Keg/Keg_clean.glb")
```

| Module | Does | Knobs (top of file) |
|---|---|---|
| `importer` | GLB/GLTF/FBX/OBJ → mesh objects; empties, cameras, lights (and armatures unless `keep_armature`) deleted | `IMPORTERS` |
| `cleanup` | `join`; `clean`: merge by distance, loose edges/verts, islands under `min_island_faces`, outward normals, unused material slots; returns counts | `MERGE_DISTANCE`, `MIN_ISLAND_FACES` |
| `normalize` | `to_ground(obj, height)`; `rotate_z(obj, degrees)` for a model that does not face −Y | `FACING` |
| `reduce` | `decimate_to(obj, target_tris, symmetry=False)`: collapse, second pass trims overshoot | `TOLERANCE` |
| `textures` | `extract`: resize (power of two, never up), repoint the material, write URP Lit maps; glTF metal/rough is packed into the .blend so a later GLB keeps glTF's layout | `DEFAULT_SIZE` |
| `report` | `observe`: tris, boundary/non-manifold edges, loose parts, materials, textures, size, base-colour luminance by facing | `LUMA_SAMPLES` |
| `clean_glb` | `save`: lean GLB, textures embedded, no Draco | — |
| `meshutil` | numpy mesh helpers (counts, bounds, components, transforms) used by the rest | — |

Rules:

- **Observations, never verdicts.** `report` and the `clean` counts are numbers
  for a person or a skill to judge against the art direction.
- **Find nodes by type and links, never by name** (Blender localises node names).
- A raw download can be 2M triangles: anything per-element goes through
  `foreach_get` + numpy (`meshutil`), not Python loops.
- glTF splits vertices at UV seams and flat edges. Anything that cares about
  connectivity (islands, inner walls, weights) runs `cleanup.clean` first.

Baked lighting: `report.observe(obj)["base_color_luminance"]` compares the
albedo on up-facing and down-facing surfaces (sampled through the UVs). A
painted top light shows as `up_down_ratio` well above 1 (the propkit test's
fake-lit chair reads 2.7); regenerate with Tripo's *Remove Lighting* on.
