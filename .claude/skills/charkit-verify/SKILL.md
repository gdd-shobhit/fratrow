---
name: charkit-verify
description: "End-to-end gate for character work: Blender status, slider spikes, mask/topology consistency, line-up evidence, Unity EditMode tests and the dressed check. PASS / FAIL / NOT ASSESSED per check."
argument-hint: "[all | item:<id> | character:<CH_F_Name>] [--milestone <evidence-folder>]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash, AskUserQuestion, mcp__blender__execute_blender_code, mcp__blender__look, Bash(bash "*/.claude/skills/charkit-verify/../../hooks/yaml-helper.sh" resolve_config *)
model: sonnet
---

!`bash "${CLAUDE_SKILL_DIR}/../../hooks/yaml-helper.sh" resolve_config --keys automation,qa.level`

# Charkit Verify

The gate between "character art changed" and "done". It re-checks everything
that can silently break when the body, an item, a recipe or the Unity import
code changes, and leaves retained evidence in `production/qa/evidence/`.

Every `AskUserQuestion` call follows `.claude/docs/automation-modes.md`
(collaborative asks always · guided major-only · autonomous logs and proceeds;
`automation_always_ask` categories always prompt). This skill writes no files
itself; the renders it triggers are written by charkit and Unity, and it asks
before triggering them.

`<repo>` below is the **literal absolute path** of the checkout under test.

---

## Phase 1: Parse Arguments

- Scope: `all` (default), `item:<id>`, or `character:<CH_F_Name>`.
- `--milestone <folder>`: evidence folder under `production/qa/evidence/`
  (existing: `m1-female-base`, `m2-unity-humanoid`, `m3-cosmetic-pipeline`,
  `m4-trio`). Default: the folder `recipes.EVIDENCE_DIR` points to.

Every check below ends as **PASS**, **FAIL** or **NOT ASSESSED** (with the
reason). Nothing is skipped silently: a check that did not run is listed.

## Phase 2: Preflight

1. **Same tree.** Connect charkit in Blender from `<repo>` (the snippet in
   `/charkit-item` Phase 2) and confirm `export_fbx.REPO == <repo>`. If Blender
   or `charkit.tasks` is unavailable, every Blender check is **NOT ASSESSED**.
2. **Unity route.** `Temp\UnityLockfile` in `<repo>` → editor open → MCP route
   (`tools/unity/mcpcall.py`). Absent → batch route (`tools/unity/unity_batch.py`).
   Editor open on a *different* checkout → MCP sees the wrong files: close it or
   use batch on `<repo>`; otherwise Unity checks are **NOT ASSESSED**.

## Phase 3: Blender Checks

| # | Check | How | PASS when |
|---|---|---|---|
| B1 | Status | `tasks.status()` | master .blend open, every item in scope built, nothing unsaved that matters |
| B2 | Body contract | `from charkit import validate; validate.run()` (when body/skeleton changed) | no problems reported |
| B3 | Slider spikes | `tasks.slider_spikes()` (all items, 0.005 m) | no vertex listed |
| B4 | Mask topology | compare every sidecar's `body.topologyHash` / `vertexCount` with `cosmetic.body_topology_hash(bpy.data.objects[C.BODY])` and `len(...data.vertices)` | all equal |
| B5 | Recipes resolve | every recipe item has a sidecar; slider names in `config.SHAPE_KEYS` (see `/charkit-character` Phase 4) | no misses |
| B6 | Line-up evidence | ask, then `tasks.lineup()`; Read every PNG written | renders exist, are non-blank, and show no skin gaps, clipping or wrong colours |
| B7 | Item close-ups (item scope) | `tasks.item_sheet("<id>")` or `tasks.focus(...)` + `mcp__blender__look` | no defects visible from any angle |

B4 snippet:

```python
import glob, json, os, bpy
from charkit import cosmetic, config as C, export_fbx
body = bpy.data.objects[C.BODY]
want = (len(body.data.vertices), cosmetic.body_topology_hash(body))
for p in sorted(glob.glob(os.path.join(export_fbx.FEMALE_DIR, "Cosmetics", "*.cosmetic.json"))):
    b = json.load(open(p, encoding="utf-8"))["body"]
    if (b["vertexCount"], b["topologyHash"]) != want:
        print("STALE MASK:", os.path.basename(p))
```

A stale mask means the item was exported against an older body: rebuild and
re-export every item (`/charkit-item`), not just that one.

## Phase 4: Unity Checks

| # | Check | Batch route (editor closed) | MCP route (editor open on `<repo>`) |
|---|---|---|---|
| U1 | Compile | first lines of any `unity_batch.py` run | `wait_not_playing.py`, then `mcpcall.py compile` |
| U2 | EditMode tests | `python <repo>\tools\unity\unity_batch.py --project <repo> --tests EditMode` | use a test-runner tool if `mcpcall.py list` shows one; otherwise close the editor and use batch |
| U3 | Sidecar import | probe each `CosmeticItem` (slot, kind, model, mask) — `tools/unity/README.md` snippet via `mcpcall.py code`, or the import log of the batch run | same |
| U4 | Dressed check | `--execute FratRow.Art.Characters.Editor.CharacterHumanoidCheck.RunDressed` | `EditorApplication.ExecuteMenuItem("Tools/FratRow/Characters/Run Dressed Check (M3)")` via `mcpcall.py code` |
| U5 | Recipe prefabs + trio check | `--execute FratRow.Art.Characters.Editor.CharacterTrioCheck.RunBatch` (rebuilds sidecar items, recipes, `Female/Prefabs/CH_*.prefab`, renders; exit 0 = no failures) | menu items *Rebuild Recipes and Prefabs*, then *Run Trio Check (M4)* via `mcpcall.py code` |

Confirm these entry points still exist, and pick up any newer check, by Grep
under `Assets/` with the pattern
`CharacterRecipeImporter|MenuItem\("Tools/FratRow/Characters` — a check menu
item that is not in this table is an extra row, not something to skip.

Rules:

- U2 **PASS** only with ≥ 1 test run and 0 failures, read from the
  `--- tests: total=... failed=...` line. `unity_batch.py` exits 5 on
  `total=0`: "0 tests" is **NOT ASSESSED** (assembly missing or not
  compiled), never PASS — confirm the count on the tests line as well.
- U5 **PASS** when `RunBatch` exits 0, `[TrioCheck] batch finished with 0
  failure(s)` is in the output, and the `unity_trio_*.png` and
  `unity_<id>_face_*.png` files in `production/qa/evidence/m4-trio/` have been
  Read and match the Blender line-up.
- U4 writes walk filmstrips (`<model>_<label>__<clip>.png`) to
  `production/qa/evidence/m3-cosmetic-pipeline/` (path is fixed in the code).
  Read each PNG: a missing, black or empty strip is **FAIL** (or NOT ASSESSED if
  rendering is unavailable in batch mode — then rerun through the editor).
- U5 with no generator in this checkout: **NOT ASSESSED — no prefab entry point**.
- Any newer check menu items found by the Grep (e.g. a trio check) run the same
  way and count as additional rows.

## Phase 5: Evidence

Retained evidence lives in `production/qa/evidence/<milestone>/` and stays on
disk when the work is closed (`.claude/docs/coding-standards.md`: a screenshot
captured and discarded is an assertion, not evidence):

- Blender: line-up front / three-quarter / back, per-character face close-ups
  (B6); item sheets for new items (B7).
- Unity: dressed walk filmstrips (U4) and a prefab render if U5 produces one.
- Keep each PNG ≤ ~1 MB (no LFS); name it after what it shows.

Every evidence file cited in the report must have been **opened and looked at**
in this run.

## Phase 6: Verdict

```text
=== Charkit Verify: <scope> — <date> ===
B1 Status            PASS
B3 Slider spikes     FAIL  (SKM_F_Shoes_X: 12 vertices > 5 mm on Calves_Thick)
U2 EditMode tests    NOT ASSESSED (editor open on D:\fratrow, batch refused)
...
Evidence: production/qa/evidence/<milestone>/... (n files, all reviewed)
Verdict: FAIL
```

Ranking: **FAIL** > **NOT ASSESSED** > **PASS**. The verdict is the worst row.

**Done** means all of:

1. Verdict PASS (every row PASS or not applicable to the scope, with the reason).
2. Evidence PNGs on disk in the milestone folder and reviewed.
3. Item modules, recipes, FBX, sidecars and textures written; master .blend
   saved (`tasks.save()`, ask first); no stale masks.
4. `design/art/character-pipeline.md` status line updated if a milestone closed.
5. Ready to commit as normal git files (no LFS) with a Conventional Commit
   message referencing the milestone.

## Gotchas

- **Git LFS is disabled** (push 403): never add `filter=lfs`.
- **Worktree-isolated sessions**: no runtime-computed paths in Bash; literal
  paths, plain separate commands, `git -C <literal path>`; `cd` persists.
- **PowerShell 5.1 `Set-Content -Encoding utf8` writes a BOM**: use Write/Edit.
- **Unity 6.5: `Object.GetInstanceID()` is an error**: references as keys.
- **Play mode**: always `wait_not_playing.py` before MCP checks or reimports.
- **Preview scenes**: bake SkinnedMeshRenderer proxies; AnimationMode sampling,
  not PlayableGraph; `Unsupported.SetRenderSettingsUseFogNoDirty` against fog.
- **Blender**: face sets via `foreach_get` before bmesh edits; `look` cannot show
  unsaved generated images (save a PNG crop and Read it); `review.render`
  renders everything not `hide_render`-hidden; `export_fbx.verify()` purges
  orphans (painted preview images vanish).
- **Body UV atlas / masks**: masks key on body vertex ids (uv2) and the topology
  hash — B4 catches items not rebuilt after a body/skeleton change.
- **Shape-key push-out vs signed rest offset**: the boot-toe bug (2 cm on every
  slider) is exactly what B3 exists to catch.

## Recommended Next Steps

- FAIL on an item: `/charkit-item <id> change`.
- FAIL on a recipe or face: `/charkit-character <Name>`.
- PASS: commit, then `/story-done` (or `/smoke-check`) for the owning story.
