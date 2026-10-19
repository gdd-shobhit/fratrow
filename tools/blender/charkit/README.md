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

`build.run()` runs all five. `review.py` holds the deformation test poses
(`rest`, `t_pose`, `arms_up`, `squat`, `walk`, `fists`, `twist`), a preset
line-up (`preview_variants`) and a Workbench render helper used for the evidence
in `production/qa/evidence/`.

## Rules

- The skeleton is the contract. Meshes may be replaced or re-sculpted; bone
  names, hierarchy and rest positions change only with a pipeline-doc update,
  because every cosmetic and animation depends on them.
- Keep the `src_face_set` attribute on the body: `skeleton.py` and `bind.py`
  read it.
- Run `validate.run()` before exporting anything.
