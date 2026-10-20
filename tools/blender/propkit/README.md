# propkit — static props from AI-generated models (Blender 5.0 → Unity URP)

Furniture, cups, kegs, signage: anything that never deforms. Static props skip
retopology. Decimation keeps the generator's UVs and texture, so one call takes
a raw Tripo download to a Unity-ready FBX with LODs. The shared steps live in
`tools/blender/aikit/`. The research behind them is in
`Docs/Reports/tripo-pipeline/notes/practitioner_pipelines.md`.

## Running

Inside Blender, or headless with `blender -b --factory-startup --python <script>`:

```python
import sys; sys.path.insert(0, r"<repo>/tools/blender")
from propkit import ingest
ingest.run(r"<repo>/ArtSource/AI/RedCup/raw/red_cup.glb", "RedCup",
           height=0.12, budget="small", rotate_z=0,
           provenance={"generator": "Tripo", "model_version": "v3.1-20260211"})
```

| Argument | Meaning |
|---|---|
| `src` | Raw model (GLB/GLTF/FBX/OBJ), normally `ArtSource/AI/<name>/raw/` (gitignored) |
| `name` | PascalCase, no prefix: `RedCup` gives `SM_RedCup`, `T_RedCup_*` and `M_RedCup` |
| `height` | Real height in metres (bounding-box Z after grounding) |
| `budget` | LOD0 triangles: `"small"` 1500, `"medium"` 4000, `"large"` 10000, or a number |
| `lods` | LOD1, LOD2… as shares of LOD0, default `(0.5, 0.25)` |
| `texture_size` | Largest texture side, a power of two, never upscaled (default 1024) |
| `rotate_z` | Degrees about Z to bring the prop's front to −Y. Tripo often faces +X, so pass `-90` |
| `provenance` | Free-form dict copied into the sidecar, for the Steam AI-content disclosure |
| `out_root`, `clean_root` | Default `Assets/Art/Props` and `ArtSource/AI`; tests redirect them |

Steps: import, join, clean (merge by distance, loose geometry, floaters,
normals, unused slots), scale to `height` and ground (origin at the bottom
centre, centred on X/Y), `rotate_z`, then texture extraction. Next, LOD0/1/2
are each decimated from the cleaned full-density mesh, and smooth shading is
reset with hard edges above 45° (`SHARP_ANGLE_DEG`). Last come the FBX, the
clean GLB and the sidecar. Re-running the same `name` replaces the previous
objects in the open file.

## Output

| Path | Contents |
|---|---|
| `Assets/Art/Props/<name>/SM_<name>.fbx` | `SM_<name>_LOD0/1/2`. Unity builds the LODGroup from the `_LODn` names |
| `Assets/Art/Props/<name>/T_<name>_BaseMap.png` | sRGB albedo |
| `Assets/Art/Props/<name>/T_<name>_Normal.png` | OpenGL (+Y) normal map, the same convention as glTF and Unity |
| `Assets/Art/Props/<name>/T_<name>_MetallicSmoothness.png` | URP Lit packing: metallic in R, smoothness (1 − roughness) in A |
| `Assets/Art/Props/<name>/SM_<name>.prop.json` | The record: budget, per-LOD triangles, cleanup counts, `report.observe` numbers for the cleaned source (`observeSource`) and for LOD0 (`observe`), provenance |
| `ArtSource/AI/<name>/<name>_clean.glb` | LOD0 with its textures embedded: the lean committed source. Re-running `ingest.run` on it reproduces the Unity files without the raw download |

The sidecar holds observations, never verdicts. Read the luminance numbers
with the render: a base colour much brighter on up-facing surfaces than on
down-facing ones suggests the generator baked light into the albedo.

## Unity

`Assets/Art/Props/Editor/PropModelImporter.cs` applies these rules to every
`SM_*.fbx` under `Assets/Art/Props/`:
- **Model:** scale 1, rig None, no cameras, lights, animation or colliders, normals from the file.
- **Textures:** `T_*_Normal` imports as a normal map; MetallicSmoothness and Occlusion import as linear.
- **Material:** `M_<name>.mat` (URP Lit) is created next to the FBX and filled from the T_ files. The FBX's materials are remapped to it (one re-import on the first import). An artist's later changes to the material are kept; texture slots refresh when the T_ files change.

## Test

```
blender -b --factory-startup --python-exit-code 1 --python tools/blender/propkit/tests/test_ingest.py [-- --out <dir>]
```

The test builds a stand-in for a Tripo download and runs `ingest.run` on it,
writing to a temp folder (never `Assets/`). The stand-in is a dense,
fake-lit, textured chair with floaters, cracked seams and a non-manifold fin,
scaled ×37 and turned 90°. The test then checks:
- the budget and LOD names
- floaters removed and parts kept
- height to 1 mm, grounding, centring and facing
- 1024 textures and the URP channel packing
- FBX and GLB round trips with matching triangle counts

It exits 1 if any check fails.
