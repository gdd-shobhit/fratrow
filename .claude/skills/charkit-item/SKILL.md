---
name: charkit-item
description: "Add or change a character cosmetic (hair, top, bottom, legwear, shoes, rigid accessory): Blender build loop, slider/mask checks, FBX + sidecar export, Unity import check."
argument-hint: "<SKM_F_Slot_Name | SM_F_Slot_Name> [new | change] [--like <existing item id>]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Write, Edit, Bash, AskUserQuestion, mcp__blender__execute_blender_code, mcp__blender__look, Bash(bash "*/.claude/skills/charkit-item/../../hooks/yaml-helper.sh" resolve_config *)
model: sonnet
---

!`bash "${CLAUDE_SKILL_DIR}/../../hooks/yaml-helper.sh" resolve_config --keys automation`

# Charkit Item

Builds one cosmetic for the modular female character from a script in
`tools/blender/charkit/items/`, checks it in Blender, exports `<id>.fbx` +
`<id>.cosmetic.json` to `Assets/Art/Characters/Female/Cosmetics/`, and confirms
Unity turned it into a `CosmeticItem`. Design rules live in
`design/art/character-pipeline.md`; how to run charkit is in
`tools/blender/charkit/README.md`; Unity helpers in `tools/unity/README.md`.

Every `AskUserQuestion` call follows `.claude/docs/automation-modes.md`
(collaborative asks always · guided major-only · autonomous logs and proceeds;
`automation_always_ask` categories always prompt).

**Output:** `tools/blender/charkit/items/<module>.py`, the exported FBX +
sidecar, and a per-step result table ending in **COMPLETE**, **BLOCKED** or
**NOT ASSESSED**.

In every snippet below, `<repo>` is the **literal absolute path** of the
checkout you are editing (e.g. `D:\fratrow\.claude\worktrees\female-base-character`).
Never compute it in a shell variable — see Gotchas.

---

## Phase 1: Parse Arguments and Name the Item

- **Id**: `SKM_F_<Slot>_<Name>` for skinned items, `SM_F_<Slot>_<Name>` for rigid
  socket items; `<Name>` is PascalCase. Existing ids use `Hair`, `Top`, `Bottom`,
  `Legwear`, `Shoes`, and `Ear` for earrings (`SM_F_Ear_HoopSmall`).
- **Module**: `items/f_<slot>_<snake_name>.py` (e.g. `f_top_cami_crop.py`).
- **Sidecar slot**: exactly one of the `CosmeticSlot` enum names in
  `Assets/Scripts/Characters/CosmeticItem.cs`: `Hair, Top, Bottom, FullBody,
  Legwear, Shoes, Ears, Face, HeadTop, Hand, Back`. The Unity importer silently
  falls back to `Top` on an unknown slot, so a typo does not error — Phase 7
  checks the imported slot.
- **Mode**: `new` (default when the module does not exist) or `change`.
- **Colours** are **sRGB** everywhere: in item code (`cosmetic.material` converts
  to linear for Blender), in sidecars, and in recipes (hex).

## Phase 2: Preflight

1. Connect charkit in Blender from **this** checkout and read the status:

   ```python
   import sys
   p = r"<repo>\tools\blender"
   for m in [m for m in sys.modules if m == "charkit" or m.startswith("charkit.")]:
       del sys.modules[m]          # drop a charkit imported from another checkout
   if p not in sys.path: sys.path.insert(0, p)
   from charkit import tasks, export_fbx
   tasks.reload()
   print(export_fbx.REPO)
   print(tasks.status())
   ```

   Run it with `mcp__blender__execute_blender_code`. `export_fbx.REPO` must equal
   `<repo>`; exports go wherever charkit was imported from.
2. The master `ArtSource/Characters/SK_Human_Female.blend` must be the open file
   (status shows it). If `tasks` cannot be imported, stop: **BLOCKED — charkit
   tasks.py not available**.
3. Find out which checkout Unity has open (see `tools/unity/README.md`, *Which
   route*). If it is a different tree, Phase 7 must use batch mode on `<repo>`.

## Phase 3: Pick the Template

Copy the closest existing item; do not start from a blank file.

| Slot | Template | Shared helpers | Notes |
|---|---|---|---|
| Hair | `f_hair_bob.py` (bob + bangs), `f_hair_high_ponytail.py` (tube tail + bow), `f_hair_space_buns.py` | `_hair.py`: `cap`, `inflate`, `scallop`, `fringe`, `hairline_roots`, `head_weights`, `finish` | Rides `Head`; long parts blend to `Spine2` via `head_weights(blend_to=...)` |
| Top | `f_top_turtleneck_crop.py` (sleeves, collar), `f_top_cami_crop.py` (straps added after `finalize`) | `_garment.py`: `shell` → shaping → `finalize` → `cosmetic.finish` | `f_top_offshoulder_crop.py` writes every step out by hand: read it to understand the steps |
| Bottom | `f_bottom_track_pants.py` (flare, side stripe), `f_bottom_cargo_pants.py`, `f_bottom_pleated_skirt.py` | `_garment.shell/limb_scale/finalize` | Skirts only skin-deform for now (no cloth) |
| Legwear | `f_legwear_fishnet_band.py` | `shapes.ribbon_net` | `finish(..., mask=False)` when it hides no skin |
| Shoes | `f_shoes_sneakers.py`, `f_shoes_ankle_boots.py`, `f_shoes_chunky_boots.py` | `_footwear.build/run` | A new shoe is usually just a new `SPEC` dict |
| Rigid (Ears, Face, HeadTop, Hand, Back) | `f_ear_hoop_small.py` | `cosmetic.export` with `kind: "rigid"` + `attachments` (node → `Socket_*`) | Modelled where it sits on the rest body; no mask, no sliders |
| FullBody | none yet | `_garment.shell` over Chest/Belly/Pelvis/Thigh | New ground: say so and plan it with the user first |

Module contract (see `items/__init__.py`): `NAME`, `build()` → object(s),
`run(out_dir)` → list of written paths. Body-grown items set
`obj[cosmetic.COVERED_PROP] = covered` so the skin mask stays inside the cut lines.

## Phase 4: Write the Module

Show the planned constants (heights such as `HEM_Z`, `OFFSET`, triangle target,
colours) and ask: "May I write `tools/blender/charkit/items/<module>.py`?"
Write it with the Write/Edit tools (never PowerShell `Set-Content`: BOM).

Body reference for constants: 1 unit = 1 m, height ~1.66 m, feet on Z = 0,
facing −Y. Measure the body rather than guessing, e.g. existing items put the
crop-top hem at Z 1.09–1.10, a high waist at 1.03, the ankle at 0.10.

## Phase 5: Build and Look (iterate)

```python
from charkit import tasks
tasks.reload(); tasks.build("<ID>")
tasks.isolate(["<ID>"])          # the item plus the body
tasks.focus(tasks.HEAD)          # HEAD / FACE / FEET, or a centre tuple, e.g. (0, 0, 1.15), size=0.6
```

Then `mcp__blender__look(mode="angles", target=["_focus"])` (see the tool's
schema for views and `max_size`; a small size keeps the session cheap). Look from
front, side, back and three-quarter, plus below for skirts and above for hair.
`tasks.unfocus()` and `tasks.show_all()` when done; `tasks.viewport_colors(mode)`
switches viewport colouring (modes in its docstring).

Per iteration, check: clean hems (no zig-zags), no skin poking through, fabric
bridges hollows, nothing floats, and the triangle count:

| Item | Current range | Target |
|---|---|---|
| Hair | 0.8–1.5k tris | ≤ 1.5k |
| Top | 1.1–1.8k | stay in range |
| Bottom | 1.3–1.9k | stay in range |
| Shoes (pair) | 0.9–1.5k | stay in range |
| **Dressed girl** (body after skin hiding + all items) | **6–10k** | **≤ 10k** — the binding budget |

The pipeline doc's older "Top + Bottom + Shoes ≤ 3,000 combined" is already
exceeded by the current items; the dressed total is what is enforced. Count with
`sum(len(p.vertices) - 2 for p in bpy.data.objects["<ID>"].data.polygons)`;
`cosmetic.finish` also prints it at export.

## Phase 6: Checks Before Export

Record each as PASS / FAIL / NOT ASSESSED.

1. **Slider spikes** — `tasks.slider_spikes(["<ID>"])` (threshold 0.005 m). Any
   vertex listed is a **FAIL** until explained; it usually means bind code or a
   rest-offset problem, not the item.
2. **Skin mask** — `from charkit import review; review.mask_preview(["<ID>"])`,
   then look at the copy from every side for see-through gaps (skin hidden where
   the item leaves a gap). `review.clear_previews()` afterwards. Rigid items and
   `mask=False` items: NOT APPLICABLE.
3. **Body variants and poses** — `review.preview_variants(C.BODY_PRESETS,
   items=["<ID>"], pose="walk")` with `from charkit import config as C`; repeat
   with `pose="arms_up"` (tops) or `"squat"` (bottoms), and a maxed variant
   `{"AllMax": {k: 1.0 for k in C.SHAPE_KEYS}}`. Look for clipping.
4. **Budget** — within Phase 5's table.

Any FAIL: return to Phase 5. Do not export a FAIL.

## Phase 7: Export and Unity Import Check

1. Ask: "May I export `<ID>` (overwrites its FBX + sidecar in
   `Assets/Art/Characters/Female/Cosmetics/`) and save the master .blend?"
   Then `tasks.export(["<ID>"])` and `tasks.save()`.
2. Unity imports the sidecar into `<ID>.asset` (`CharacterModelImporter`):
   - **Editor open on `<repo>`**: `python <repo>\tools\unity\wait_not_playing.py`,
     then `python <repo>\tools\unity\mcpcall.py compile`, then run the
     CosmeticItem probe from `tools/unity/README.md` with `mcpcall.py code`.
   - **Editor closed**: `python <repo>\tools\unity\unity_batch.py --project <repo> --import-only`
     and read the printed `[CosmeticSidecar] <path>: <slot> <kind>, model=..., mask=<n> body vertices`
     line for `<ID>`; then `--tests EditMode`.
3. PASS when the asset exists with the intended slot and kind, the model is
   found, the mask count is non-zero for masking items, and there are no
   `[CosmeticSidecar]` errors. No `[CosmeticSidecar]` line for `<ID>` means the
   import did not run: NOT ASSESSED, not PASS. If the editor is open on another tree and batch
   mode is refused, this step is **NOT ASSESSED** — say so.

## Phase 8: Report

| Step | Result | Detail |
|---|---|---|
| Template / module | | |
| Look review (angles) | | |
| Slider spikes | | |
| Skin mask | | |
| Variants / poses | | |
| Triangles | | `<n>` tris |
| Export | | paths |
| Unity import | | slot / kind / mask |

Verdict: **COMPLETE** (all PASS or not applicable) · **BLOCKED** (a FAIL, or a
missing prerequisite) · **NOT ASSESSED** (a step could not run — name it).
NOT ASSESSED outranks COMPLETE: an unrun check is not a pass.

## Gotchas

- **Git LFS is disabled** for this repo (push 403). Commit FBX/PNG/.blend as
  normal git files; never add `filter=lfs` rules.
- **Worktree-isolated sessions**: Bash commands that compute paths at runtime
  (variables, `$(...)`, pipelines into git/python/sed) can be refused. Use
  literal paths, plain separate commands, `git -C <literal path>`. `cd` in Bash
  changes the session's working directory.
- **PowerShell 5.1 `Set-Content -Encoding utf8` writes a BOM** — never use it for
  source or JSON; use Write/Edit.
- **Unity 6.5: `Object.GetInstanceID()` is an error** — key dictionaries by the
  object reference.
- **Play mode**: wait with `tools/unity/wait_not_playing.py` before reimports or
  MCP checks.
- **Preview scenes**: SkinnedMeshRenderers do not re-skin for manual camera
  renders (bake proxies); PlayableGraph does not drive bones there (use
  AnimationMode sampling); global fog leaks in
  (`Unsupported.SetRenderSettingsUseFogNoDirty`).
- **Blender face sets**: read the `src_face_set` attribute with `foreach_get`
  **before** any bmesh edit of the body.
- **`look` cannot show unsaved generated images**: save a PNG (crop) and Read it.
- **`review.render` renders every object not `hide_render`-hidden**; viewport
  hiding does not count.
- **`export_fbx.verify()` purges orphan data**: regenerated images (face
  previews) vanish; repaint afterwards if you need them.
- **Body UVs are an atlas** (`config.UV_ATLAS`). Skin masks key on body vertex
  ids (uv2) and the body topology hash: **rebuild and re-export every item after
  any body or skeleton change**.
- **Shape-key push-out compares against each vertex's signed rest offset**
  (`cosmetic.bind_to_body`). A past bug pushed a boot toe 2 cm out on every
  slider: run `slider_spikes` after touching bind code.

## Recommended Next Steps

- `/charkit-character <Name>` to put the item on a recipe.
- `/charkit-verify` before the milestone closes (full gate + evidence).
