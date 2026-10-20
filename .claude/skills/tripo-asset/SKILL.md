---
name: tripo-asset
description: "Bring a Tripo (or other image-to-3D) model into the game: record its provenance, clean and fit it in Blender, then build a cosmetic on SK_Human (skinned garment, hair or rigid accessory) or a static prop with LODs, and check it in Unity. Rigging never comes from Tripo. Use when someone has a Tripo Studio download, wants a new prop or outfit piece generated, or asks how AI-generated assets enter the pipeline."
argument-hint: "[prop | top | bottom | fullbody | legwear | shoes | hair | accessory] <AssetName> [--file <download>] [--image <ref> | --views F,L,B,R | --prompt \"...\"]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash, Write, Edit
---

# Tripo Asset

**Automation mode**: resolve `modes.automation` (`project.local.yaml` →
`project.yaml` → default `collaborative`). Every question and every file write
below follows `.claude/docs/automation-modes.md`: at `collaborative`, ask "May I
write …?" before each write named in Phases 3–4 and show what will be written
first; at `autonomous`, log the write and proceed.

The pipeline and every decision behind it: `design/art/ai-asset-pipeline.md`
(read it first). Short version: **Tripo models and textures, charkit rigs.** A
cosmetic is fitted to the rest-pose body and takes `SK_Human`'s weights and all
16 body sliders from the skin under it; Tripo's auto-rig is never used on a
character or cosmetic.

Tools:

| Stage | Tool |
|---|---|
| Generate / record | `py -3.12 tools/tripo/tripo_gen.py` (README in `tools/tripo/`) |
| Shared Blender ingest | `tools/blender/aikit/` (import, clean, normalise, decimate, textures, observe) |
| Cosmetics | `tools/blender/charkit/ingest.py` + an item module copied from `items/_generated_template.py` |
| Props | `tools/blender/propkit/ingest.py` |
| Unity | `CosmeticMaterialImporter`, `PropModelImporter` (automatic on import) |

Blender: `"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe"`. Run
stages **headless** (`-b`) so they are reproducible and never touch the user's
open scene; use the live Blender (MCP `execute_blender_code`) only when the user
wants to watch or tweak. Never save `ArtSource/Characters/SK_Human_Female.blend`
from these runs.

## Phase 1: Classify and check access

1. Asset kind from the argument (ask only if it is missing and not obvious from
   the image): `prop`, a skinned slot (`top`, `bottom`, `fullbody`, `legwear`,
   `shoes`), `hair`, or `accessory` (rigid, on a `Socket_*`).
   Tight garments (crop tops, leggings, socks) are usually better as
   **body-grown** items (`items/f_top_offshoulder_crop.py`); say so and offer
   that path.
2. Source of the model, and report which one applies:
   - `--file` given → a Studio download → `register` (no API key needed).
   - otherwise the API path → needs `TRIPO_API_KEY` **and API credits, which
     the Studio subscription does not include**. If the key is unset, report
     `GENERATE: NOT RUN — no TRIPO_API_KEY (Studio downloads go through --file)`
     and stop at this phase unless a file is supplied. Never invent a key or a
     download.
3. Paid plan only: Tripo keeps all rights to Free-tier outputs. If the user says
   the asset came from a free account, stop: it cannot ship.

## Phase 2: Inputs

- **Cosmetics / hair / accessories**: generate on the body reference sheet so
  the fit locks onto our own skin. If `ArtSource/AI/_reference/` is empty or the
  body changed, render it:
  `blender -b ArtSource/Characters/SK_Human_Female.blend --python-expr "import sys; sys.path.insert(0, r'<repo>/tools/blender'); from charkit import ingest; print(ingest.reference_sheet())"`
- **Props**: one clean reference image (plain background) or a prompt.
- Prompt template (style consistency):
  `"<item> for a stylised college-party game character, clean flat colours, simple folds, no logos, no text"`.
- Studio settings to tell the user: P2.0, multi-view for cosmetics, **Remove
  Lighting on**, PBR on, native quads, lowest face-count variant that keeps the
  silhouette, download GLB.

## Phase 3: Generate or register

- Studio download:
  `py -3.12 tools/tripo/tripo_gen.py register --name <Asset> --file <download> --ref <image> [...]`
- API: `py -3.12 tools/tripo/tripo_gen.py multiview --name <Asset> --front F --left L --back B --right R`
  (or `image`, `text`). Downloads land in `ArtSource/AI/<Asset>/raw/` immediately
  (URLs expire in ~5 minutes) and every task is appended to `provenance.json`.

Before running either: "May I write `ArtSource/AI/<Asset>/` (raw download,
`ref/` copies of the inputs, `provenance.json`)?" — and for the API path state
the credit cost from `tools/tripo/README.md` first, since it spends money.

Check `ArtSource/AI/<Asset>/provenance.json` exists and names the raw file
before going on. No provenance → do not ingest (Steam AI disclosure depends on it).

## Phase 4: Ingest

Name the files a run will write and ask before writing them: "May I write
`ArtSource/AI/<Asset>/<Asset>_clean.glb`, the item module
`tools/blender/charkit/items/f_<slot>_<name>.py`, and the export into
`Assets/Art/Characters/Female/Cosmetics/` (or `Assets/Art/Props/<Name>/`)?"

**Prop**:
`blender -b --factory-startup --python-expr "import sys; sys.path.insert(0, r'<repo>/tools/blender'); from propkit import ingest; print(ingest.run(r'<raw>', '<Name>', height=<m>, budget='small|medium|large'))"`
Ask for the real-world height if it is not obvious (a cup 0.12, a keg 0.6, a couch 0.85).

**Cosmetic / hair / accessory**:
1. `ingest.prepare(r'<raw>', '<Asset>')` in the master (headless as above). It
   writes `ArtSource/AI/<Asset>/<Asset>_clean.glb`, renders the fit to
   `raw/review/`, and prints each part with the body regions under it plus the
   fit residuals (`trusted_median_mm`, `all_p90_mm`).
2. **Look at the fit renders** (Read the PNGs). Report the residuals as numbers;
   if the model does not face the body (`rotation_deg` large, parts floating),
   re-run with `rotate_z=` rather than forcing on.
3. Pick the parts for the item from the printout (regions tell you which part is
   the top, the hair, the earring). Copy `items/_generated_template.py` to
   `items/f_<slot>_<name>.py`, fill `ASSET`, `NAME` (`SKM_F_<Slot>_<Name>` or
   `SM_F_<Slot>_<Name>`), `SLOT`, `PARTS`, `BUDGET` (character-pipeline.md budgets),
   and the builder: `skinned_item` (garments; `max_gap≈0.012` only for tight
   items on a guessed body), `hair_item`, or `rigid_item` (with its socket).
4. Run the item module with `out_dir` = `Assets/Art/Characters/Female/Cosmetics`.
   A `ValueError` about vertices > 15 cm from the skin means the part is not on
   the body (wrong part, or a bad fit) — fix the input, never raise the limit to pass.

## Phase 5: Review

- Cosmetics: `review.mask_preview([NAME])` and
  `review.preview_variants(C.BODY_PRESETS…, items=[NAME], pose="arms_up")`, then
  `review.render(...)` and Read the images. Look for skin poking through, gaps
  at hems, and cloth stuck between the legs (loose skirts: known limit).
- Props: open the `.prop.json` observations (tris per LOD, bbox, texture sizes,
  luminance ramp). A strong top-to-bottom luminance ramp means baked lighting:
  regenerate with Remove Lighting on.

## Phase 6: Unity

1. `powershell -NoProfile -ExecutionPolicy Bypass -File tools\ci\unity-compile-check.ps1` must PASS.
2. If the Editor is open on this project (`unity-mcp status`), refresh assets and
   check the import: the `CosmeticItem` / `M_*.mat` exist (or the prop's LODGroup
   and material), and take a screenshot into `production/qa/evidence/<asset>/`
   (`.claude/docs/run-and-observe.md`). Cosmetics: run the dressed check from
   *Tools → FratRow → Characters*.
3. Editor not open → `UNITY IMPORT: NOT RUN — Editor closed` in the report. Do
   not describe the asset as working in game.

## Report

One line per phase, each `DONE`, `NOT RUN — <reason>` or `FAILED — <error>`:
classify, generate/register, inputs, ingest (with fit residuals or prop tris),
review (with the images looked at), compile check, Unity import, evidence path.
Then: files written, minutes of cleanup this asset needed (the pipeline's main
unknown — say "not measured" if you did not time it), and anything the user must
do in Studio.

## Next steps

- `/asset-audit` — naming, triangle and texture budgets across the new files.
- Add the item to a character recipe and run the dressed check in Unity; once the
  Editor import is observed, the evidence screenshot closes the story (`/story-done`).
- A loose garment that bends wrongly between the legs is the known weight-transfer
  limit (`design/art/ai-asset-pipeline.md`, *Known limits*), not a fit bug.
