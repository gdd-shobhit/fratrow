---
name: charkit-character
description: "Create or change a character recipe (body sliders, height, skin, painted face/eyes, worn items + colours), paint its textures, render the line-up, then generate/verify the Unity prefab."
argument-hint: "<Name> [--like CH_F_<Existing>] [--preset Petite|Curvy|TallSlim|Athletic]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Write, Edit, Bash, AskUserQuestion, mcp__blender__execute_blender_code, mcp__blender__look, Bash(bash "*/.claude/skills/charkit-character/../../hooks/yaml-helper.sh" resolve_config *)
model: sonnet
---

!`bash "${CLAUDE_SKILL_DIR}/../../hooks/yaml-helper.sh" resolve_config --keys automation`

# Charkit Character

A character is a **recipe**: `Assets/Art/Characters/Female/Recipes/CH_F_<Name>.recipe.json`.
Blender reads it to paint the face and eye textures (`charkit/faces.py`) and to
render the dressed line-up (`charkit/recipes.py`); Unity reads the same file to
build the character. Every girl is the same body: what differs is slider values,
height, skin, face paint and items.

Every `AskUserQuestion` call follows `.claude/docs/automation-modes.md`
(collaborative asks always · guided major-only · autonomous logs and proceeds;
`automation_always_ask` categories always prompt).

**Output:** the recipe JSON, `Textures/T_F_Face_<Name>.png` +
`T_F_Eyes_<Name>.png`, line-up evidence renders, the Unity check result, and a
verdict of **COMPLETE**, **BLOCKED** or **NOT ASSESSED**.

`<repo>` below is the **literal absolute path** of the checkout you are editing.

---

## Phase 1: Parse Arguments

- **Id** `CH_F_<Name>` (PascalCase); it is also the Unity prefab's name.
  `displayName` is the human name ("Black Bob").
- `--like CH_F_<Existing>` starts from a copy of that recipe.
- `--preset <name>` starts the body from `config.BODY_PRESETS[<name>]`
  (`Petite`, `Curvy`, `TallSlim`, `Athletic`).
- If the recipe exists, this is a **change**: edit it in place.

## Phase 2: Gather

Read, before asking anything:

- The existing recipes (`Glob Assets/Art/Characters/Female/Recipes/*.recipe.json`)
  — the schema below is what they contain.
- Available items: `Glob Assets/Art/Characters/Female/Cosmetics/*.cosmetic.json`;
  from each take `id`, `slot`, and `materials` (name, default sRGB colour,
  `recolorable`), which fixes how many colours the item takes and in what order.
- `tools/blender/charkit/config.py`: `SHAPE_KEYS`, `BODY_PRESETS`,
  `HEIGHT_SCALE_RANGE`.
- `tools/blender/charkit/faces.py`: `FACE_DEFAULTS`.

Missing an item the character needs? Stop and run `/charkit-item` for it first.

## Phase 3: Draft the Recipe

| Field | Value |
|---|---|
| `schema` | `1` |
| `id` / `displayName` | `CH_F_<Name>` / display name |
| `baseModel` | `"SK_Female_Base"` |
| `height` | whole-body scale, `config.HEIGHT_SCALE_RANGE` = 0.95–1.05 (current: 0.96, 1.0, 1.04) |
| `skin` | sRGB hex `"#RRGGBB"` |
| `sliders` | `[{ "name": <config.SHAPE_KEYS>, "value": 0..1 }]`; omit zeros; use at most one side of a pair (`Bust_Small`/`Bust_Large`, …) |
| `faceTexture` / `eyeTexture` | `"T_F_Face_<Name>"` / `"T_F_Eyes_<Name>"` (PNG names in `Female/Textures/`) |
| `items` | `[{ "id": <item id>, "colors": ["#hex", …] }]`, one colour per material slot in sidecar order; `[]` keeps the item's defaults |
| `face` | face-paint knobs (below); omitted keys take `FACE_DEFAULTS` |
| `iris` | sRGB hex iris colour |

**Sliders** (`config.SHAPE_KEYS`): `Bust_Small/Large`, `Hips_Narrow/Wide`,
`Butt_Small/Large`, `Thighs_Thin/Thick`, `Waist_Narrow/Wide`, `Belly_Soft`,
`Arms_Thin/Thick`, `Calves_Thick`, `Shoulders_Narrow/Wide`. A preset's
`"height"` entry goes to the recipe's `height`, **not** into `sliders`.

**Items**: one per slot; a `FullBody` item replaces Top and Bottom; when two
items collide the later one in the list wins (`CosmeticSlots.Resolve`).

**Face knobs** (`faces.FACE_DEFAULTS`; values used by the trio in brackets):

| Key | Default | Meaning |
|---|---|---|
| `brows` | `#6B4A35` | brow colour |
| `browThickness` | 1.0 | brow width multiplier [0.85–1.25] |
| `browArch` | 1.0 | arch height multiplier [0.8–1.1] |
| `lashes` | `#2A1D1D` | upper/lower lash-line colour |
| `wing` | 0.3 | eyeliner flick past the outer corner, 0 = none [0.2–1.0] |
| `eyeshadow` / `eyeshadowAlpha` | `#C99A8C` / 0.0 | lid shadow colour / opacity [0.25–0.3] |
| `lips` / `lipAlpha` | `#D9777F` / 0.85 | lip colour / opacity; the parting line is derived darker |
| `blush` / `blushAlpha` | `#F0A0A0` / 0.0 | cheek blush colour / opacity [0.25–0.4] |
| `freckles` | 0.0 | freckle strength 0–1 (fixed seed, nose and cheeks) |
| `freckleColor` | `#B0674A` | freckle colour |

The face texture stores *feature colour ÷ skin colour* (Unity multiplies it by
the skin tint), so **changing `skin` requires repainting**.

Present the draft and ask: "May I write
`Assets/Art/Characters/Female/Recipes/CH_F_<Name>.recipe.json`?" Write it with
the Write tool (UTF-8, no BOM — Python's `json.load` rejects a BOM).

## Phase 4: Validate in Blender

Connect charkit from this checkout (same preflight as `/charkit-item` Phase 2:
`export_fbx.REPO` must be `<repo>`), then:

```python
import os
from charkit import recipes, config as C
r = recipes.load(os.path.join(recipes.RECIPE_DIR, "CH_F_<Name>.recipe.json"))
print("bad sliders:", [s["name"] for s in r["sliders"] if s["name"] not in C.SHAPE_KEYS])
print("height ok:", C.HEIGHT_SCALE_RANGE[0] <= r["height"] <= C.HEIGHT_SCALE_RANGE[1])
cos = os.path.join(os.path.dirname(recipes.RECIPE_DIR), "Cosmetics")
print("no sidecar:", [i["id"] for i in r["items"] if not os.path.exists(os.path.join(cos, i["id"] + ".cosmetic.json"))])
```

Every list must be empty and height ok. Items must also be **built in the open
.blend** for the line-up (`recipes.item_objects` raises KeyError otherwise):
`tasks.build("<id>")` for any missing one.

## Phase 5: Paint the Face

`from charkit import tasks; tasks.paint_faces(["CH_F_<Name>"])` writes both
PNGs to `Assets/Art/Characters/Female/Textures/`. Inspect by **reading the
written PNG** (the Read tool), or render a close-up (Phase 6) — `look` cannot
display unsaved generated images. Tune the `face` knobs in the recipe and
repaint until the user is happy.

## Phase 6: Line-up Render

Ask: "May I render the line-up evidence?" then `tasks.lineup()`. It builds
dressed static copies of every recipe (sliders, height, skin mask, colours,
textures) and renders front / three-quarter / back plus face close-ups into
`recipes.EVIDENCE_DIR` (currently `production/qa/evidence/m4-trio/`). Read
every PNG and check: proportions, hidden skin under clothes with no gaps, item
colours, face paint, eyes. Renders include every object not `hide_render`-hidden.

## Phase 7: Unity Prefab

`CharacterRecipeImporter` (Assets/Art/Characters/Editor) turns the recipe into a
`CharacterRecipe` asset next to the JSON and a prefab
`Assets/Art/Characters/Female/Prefabs/<id>.prefab` (a variant of the base model
whose `CharacterAppearance` loads the recipe). `CharacterTrioCheck` then renders
every `CH_*` prefab. Confirm the entry points still exist before using them:
Grep under `Assets/` for `CharacterRecipeImporter|MenuItem\("Tools/FratRow/Characters`.

- **Editor closed**:
  `python <repo>\tools\unity\unity_batch.py --project <repo> --execute FratRow.Art.Characters.Editor.CharacterTrioCheck.RunBatch`
  — re-imports sidecars, rebuilds every recipe + prefab, writes walk filmstrips,
  `unity_trio_<view>.png` and `unity_<id>_face_<view>.png` into
  `production/qa/evidence/m4-trio/`, and exits non-zero on any failure. Then
  `--tests EditMode`.
- **Editor open on `<repo>`**: `wait_not_playing.py`, `mcpcall.py compile`,
  then through `mcpcall.py code`:
  `EditorApplication.ExecuteMenuItem("Tools/FratRow/Characters/Rebuild Recipes and Prefabs");`
  and `EditorApplication.ExecuteMenuItem("Tools/FratRow/Characters/Run Trio Check (M4)"); return "ok";`.

Verify: the prefab exists, the batch log has no missing-item / missing-texture
lines, and the Unity renders (Read them) match the Blender line-up — same
proportions, colours, face paint and hidden skin. If the entry points are gone
from this checkout, Phase 7 is **NOT ASSESSED — no prefab entry point found**;
say so.

## Phase 8: Report

| Step | Result | Detail |
|---|---|---|
| Recipe written / validated | | |
| Textures painted | | paths |
| Line-up reviewed | | paths |
| Unity prefab | | asset / prefab paths |

Verdict: **COMPLETE** · **BLOCKED** (a FAIL or missing prerequisite) ·
**NOT ASSESSED** (a step could not run — name it; it outranks COMPLETE).

## Gotchas

- **Git LFS is disabled** (push 403): commit PNG/FBX/.blend as normal files;
  never add `filter=lfs`. Keep evidence PNGs ≤ ~1 MB.
- **Worktree-isolated sessions**: no runtime-computed paths in Bash (variables,
  `$(...)`, pipelines into git/python/sed); literal paths, plain separate
  commands, `git -C <literal path>`. `cd` changes the session's directory.
- **PowerShell 5.1 `Set-Content -Encoding utf8` writes a BOM**: breaks
  `recipes.load`. Use Write/Edit.
- **Unity 6.5: `Object.GetInstanceID()` is an error**: use references as keys.
- **Play mode**: `tools/unity/wait_not_playing.py` before reimports/MCP checks.
- **Preview scenes**: bake SkinnedMeshRenderer proxies for manual renders;
  sample with AnimationMode (PlayableGraph does not drive bones there); disable
  fog with `Unsupported.SetRenderSettingsUseFogNoDirty`.
- **Blender**: read face sets with `foreach_get` before bmesh edits; `look`
  cannot show unsaved generated images (save a PNG crop and Read it);
  `review.render` renders everything not `hide_render`-hidden;
  `export_fbx.verify()` purges orphans, so painted preview images vanish —
  repaint after exporting.
- **Body UVs are an atlas** (`config.UV_ATLAS`); face features are placed from
  3D landmarks, so they survive a re-unwrap — but masks key on body vertex ids
  and the topology hash: rebuild every item after a body/skeleton change.
- **Shape-key push-out uses each vertex's signed rest offset**; a past bug
  pushed a boot toe 2 cm on every slider. Run `tasks.slider_spikes()` after
  bind-code changes.

## Recommended Next Steps

- `/charkit-verify` for the full gate and evidence.
- `/charkit-item` for any item the character still needs.
