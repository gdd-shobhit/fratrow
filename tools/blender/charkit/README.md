# charkit — character pipeline scripts (Blender 5.0)

Re-runnable scripts that build the modular humanoid characters. The design and
conventions live in `design/art/character-pipeline.md`; this file is how to run
them.

## Running

Inside Blender (Scripting tab → Python console, or through the Blender MCP):

```python
import sys; sys.path.insert(0, r"<repo>/tools/blender")
from charkit import build
build.run(hbm_blend=r"<path>/human_base_meshes_bundle.blend")
```

`hbm_blend` is Blender Studio's Human Base Meshes v1.4.1 bundle (CC0,
`mirror.blender.org/demo/asset-bundles/human-base-meshes/human-base-meshes-bundle-v1.4.1.zip`).
It is only needed for a from-scratch rebuild; you can also set
`CHARKIT_HBM_BLEND`. The result is saved as
`ArtSource/Characters/SK_Human_Female.blend`.

To re-run a single stage on the saved master (e.g. after tuning a knob), open
the master and call that module's `run()` plus everything after it in the order
below. While iterating, `importlib.reload()` the modules you edited.

## Stages

| Order | Module | Does | Tuning knobs |
|---|---|---|---|
| 1 | `body_prepare` | Append the CC0 body, region materials from face sets, proportion pass, per-region decimation, height, low-poly eyes | `config.py`: `HEAD_SCALE`, `LEG_STRETCH`, `TARGET_HEIGHT`, `DECIMATE_BUDGETS`, `REGION_FACE_SETS` |
| 2 | `skeleton` | Measure joints on face-set seams, build `SK_Human` (Mixamo names, 54 deform + 7 sockets), mirrored | top of `skeleton.py` |
| 3 | `bind` | Heat weights, face-set hand weights + smoothing, eye weights, 4-influence limit | top of `bind.py` |
| 4 | `body_shapes` | 16 body sliders (shape keys) | `STRENGTH` and radii in `body_shapes.py`, presets in `config.BODY_PRESETS` |
| 5 | `validate` | Checks the contract (names, weights, budgets, symmetry, sliders); reports, never fixes | — |
| 6 | `export_fbx` | Export to `Assets/Art/Characters/Female/SK_Female_Base.fbx` (`run()`), then `verify(path)` round-trips it | `FBX_SETTINGS` |

`build.run()` runs stages 1–5; export is separate so you can rebuild without
touching Unity.

## Cosmetics

`cosmetic.py` is the garment toolkit (shell from body regions, plane cuts,
cloth-like relax, decimate/solidify/clearance, bind weights + sliders, skin
mask, FBX + `.cosmetic.json` export). Each item is one module in `items/`
exposing `run(out_dir)`:

```python
from charkit import export_fbx
from charkit.items import f_top_offshoulder_crop as top
top.run(os.path.join(export_fbx.FEMALE_DIR, "Cosmetics"))
```

Before exporting a new item, check it with `review.mask_preview([name])` (look
for see-through gaps from every side) and `review.preview_variants(presets,
items=[name], pose=...)` (body presets, all sliders maxed, test poses). Rebuild
every item after any change to the body or skeleton: skin masks are tied to the
body's topology. On the Unity side, `Assets/Art/Characters/Editor/` applies the
import rules automatically (Humanoid, T-pose reference, blend shapes) — see the
pipeline doc's *Unity import* section. `review.py` holds the deformation test poses
(`rest`, `t_pose`, `arms_up`, `squat`, `walk`, `fists`, `twist`), a preset
line-up (`preview_variants`) and a Workbench render helper used for the evidence
in `production/qa/evidence/`.

## Tasks

`tasks.py` is the one-call API for scripted sessions (the Blender MCP's
`execute_blender_code` runs each call in a fresh namespace, so every call
starts with the import). Items can be named by module (`f_hair_bob`) or id
(`SKM_F_Hair_Bob`).

```python
from charkit import tasks; tasks.status()            # body hash; items built/exported/stale; recipe PNGs
from charkit import tasks; tasks.reload()            # after editing any charkit module
from charkit import tasks; tasks.build("f_hair_bob") # rebuild in the file only (skinned items stay unbound)
from charkit import tasks; tasks.export(["f_hair_bob"])   # full build + FBX/sidecar; export() = every item
from charkit import tasks; tasks.slider_spikes()     # per-key max move; flags keys moving far-off vertices
from charkit import tasks; tasks.focus(tasks.HEAD)   # then look(mode="angles", target=["_focus"])
from charkit import tasks; tasks.isolate(["f_hair_bob"], with_armature=False)  # ... tasks.show_all()
from charkit import tasks; tasks.viewport_colors("TEXTURE")   # solid view shows face/eye images
from charkit import tasks; tasks.item_sheet("f_hair_bob")     # production/qa/evidence/items/
from charkit import tasks; tasks.paint_faces(["Blonde"]); tasks.lineup()
from charkit import tasks; tasks.save()              # tidy previews/images/focus boxes, save the master
```

`build`, `export`, `paint_faces` and `lineup` reload every charkit module
first (including `tasks.py`), so edits take effect without a manual reload.
`status()` calls an FBX stale when it is older than its item module or one of
the shared builders it imports (`STALE_SOURCES`). `slider_spikes()` flags a
key that moves an item vertex more than 5 mm while resting over 12 cm
(`SPIKE_REACH`) from every body vertex that key moves. Renders stamp the date
into the PNG metadata, so re-rendered evidence always shows as modified in git.

## Rules

- The skeleton is the contract. Meshes may be replaced or re-sculpted; bone
  names, hierarchy and rest positions change only with a pipeline-doc update,
  because every cosmetic and animation depends on them.
- Keep the `src_face_set` attribute on the body: `skeleton.py` and `bind.py`
  read it.
- Run `validate.run()` before exporting anything.
