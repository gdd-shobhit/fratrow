# AI Asset Pipeline — Tripo → Blender → Unity

> Status: **tooling in place 2026-10-19, untested on a real Tripo download.**
> Every Blender stage passes end to end on scripted stand-ins for Tripo outputs
> (cosmetics 19/19 checks, props 34/34, see *Verification*); the Unity side is
> compile-checked only. The first real generation is the next step.
> Research behind every decision here (sources, dates, prices):
> `Docs/Reports/tripo-pipeline/Tripo in game asset pipelines.md` (notes per
> topic in `Docs/Reports/tripo-pipeline/notes/`).

## Decision

**Tripo models and textures. We rig.** No character that wears cosmetics takes
its skeleton from Tripo, Mixamo or any other auto-rigger.

An auto-rigger fits a new skeleton to each mesh. Tripo's 2026 rigger can *name*
bones the Mixamo or Unity way, but the bone positions, bind pose and finger
chains are its own, and it has no `Socket_*` bones and no body sliders. On a
FratRow character that breaks every cosmetic's bind pose, every skin mask (they
are keyed to `SKM_F_Body` vertex ids), every socket attachment and all 16 body
sliders at once. charkit's `bind_to_body` already does the rigging half for any
mesh that sits on the rest body: weights and every slider come from the skin
under each vertex. A generated item only needs to be *brought to the body*,
which is what `charkit/ingest.py` does.

Tripo's own rig is fine where `SK_Human` does not apply:

| Use Tripo's rig for | Why |
|---|---|
| Non-humanoid creatures (campus dog, mascot) | `SK_Human` cannot serve them; Tripo rigs quadrupeds and other shapes |
| Throwaway prototype characters | No cosmetics, no sliders, deleted later |
| Animation clips (experiment) | Rig a throwaway proxy with rig v3.0 + `unity`/`mixamo` spec, export clips, import as Humanoid like Mixamo clips. The proxy never ships. Unverified: finger curves, root motion, frame rate, licence |

Background NPCs are recipes on the shared body, not Tripo rigs: one avatar, one
material scheme, every cosmetic.

## What to generate where

| Asset | Tripo route | Our side | Notes |
|---|---|---|---|
| Static props (cups, kegs, furniture, signs) | Image or text → model, PBR on, Remove Lighting on | `propkit.ingest` → `Assets/Art/Props/<Name>/` | Decimate keeps UVs; no retopo or rebake for background props |
| Rigid accessories (earrings, glasses, hats, bags) | Garment-alone image, or part of a dressed character | `ingest.rigid_item` → socket | Easiest first win |
| Shoes, structured/puffy garments | Dressed character on the reference sheet, split into parts | `ingest.skinned_item` | Shape the body-grown method cannot make |
| Tight garments (crop tops, leggings, socks) | Prefer the body-grown shell (`items/f_top_offshoulder_crop.py`); Tripo for the *look* | hand-built item | Exact masks and fit; baking Tripo's texture onto the shell is a later experiment |
| Hair | Part of a dressed character (one helmet volume) | `ingest.hair_item` | 100% `Head`; long hair moves rigidly until spring bones |
| Hero-quality hard-surface props | Rodin Gen-2.5 or Hunyuan 3D 3.1 may give cleaner topology | `propkit` | Optional second generator; Tripo first |

## Accounts, credits and licence

- **Studio and the API are billed separately.** A Studio subscription does not
  pay for API calls. Studio downloads enter the pipeline with
  `tools/tripo/tripo_gen.py register`; the API path (`image`, `multiview`, …)
  needs API credits (pay-as-you-go, 100 credits = $1, never expire).
- Studio costs per step (2026-10): P2.0 mesh 100, Smart UV 20, texture 20,
  rigging 20 (skip it). A cosmetic is about 140 credits: Pro's 3,000/month is
  ~21 cosmetics before retries.
- **Tripo's v2 API shuts down 2026-10-31.** `tools/tripo/requirements.txt` pins
  `tripo3d>=1.0.0,<2.0.0`. The official Tripo Blender add-on bundles the old
  SDK and is expected to break after that date; use Studio downloads or our CLI.
- Output URLs expire after ~5 minutes; the CLI downloads immediately.
- **Commercial use needs a paid plan.** Tripo's terms keep all rights to Free
  users' outputs. Nothing generated on a free account ships.
- **Steam AI disclosure.** Shipped AI-generated art must be described on the
  Steamworks content survey. Every generated asset carries
  `ArtSource/AI/<Asset>/provenance.json` (generator, model version, task ids,
  input hashes, cleanup done) and every generated cosmetic's sidecar links to
  it (`provenance`), so the survey can be filled in from the repo.

## Folders

| Path | Committed | Contents |
|---|---|---|
| `ArtSource/AI/_reference/` | yes | Body reference sheet (`ingest.reference_sheet`) |
| `ArtSource/AI/<Asset>/provenance.json` | yes | Append-only record of every Tripo task / Studio download |
| `ArtSource/AI/<Asset>/ref/` | yes | Input images (keep each ≤ ~1 MB) |
| `ArtSource/AI/<Asset>/raw/` | **no** (gitignored) | Downloads (up to 2M faces, 4–8K textures), review renders |
| `ArtSource/AI/<Asset>/<Asset>_clean.glb` | yes | Fitted, cleaned, lean copy (≤ 20k tris per part, 1K textures): what item modules and prop rebuilds read |
| `Assets/Art/Characters/Female/Cosmetics/` | yes | Item FBX + `T_*` textures + sidecar → `CosmeticItem` + `M_*.mat` |
| `Assets/Art/Props/<Name>/` | yes | `SM_<Name>.fbx` (LOD0–2) + `T_*` + `M_<Name>.mat` |

LFS is disabled on this repository, so only the lean files are committed.

## Generating a cosmetic

1. **Reference sheet** (once per body change): in Blender with the master open,
   `ingest.reference_sheet()` renders the bare A-pose body from front, left,
   back and right (level, orthographic) into `ArtSource/AI/_reference/`.
2. **Design** the outfit over those renders (Studio's image tools or any
   painter). Same body, same pose: the fit then locks onto bare skin to the
   millimetre. A model generated on a guessed body fits globally and the
   conform step fixes the rest.
3. **Generate** in Studio: P2.0 multi-view from the four views, **Remove
   Lighting on**, native quads, the lowest face-count variant that keeps the
   silhouette, PBR on. Download GLB. (API: `tripo_gen.py multiview`.)
4. **Record**: `py -3.12 tools/tripo/tripo_gen.py register --name F_Top_Corset
   --file <download.glb> --ref <sheet/painted images…>`.
5. **Prepare** (Blender, master open):
   `ingest.prepare(r"ArtSource/AI/F_Top_Corset/raw/<download>.glb", "F_Top_Corset")`.
   It cleans each part, fits the whole model onto the body (uniform scale +
   turn + move, trimmed ICP on bare skin), writes `<Asset>_clean.glb`, renders
   the fit to `raw/review/` and prints each part with the body regions under it
   and the fit residuals. Read the residuals and look at the renders: nothing
   is judged for you.
6. **Item module**: copy `tools/blender/charkit/items/_generated_template.py`,
   set `ASSET`, `NAME`, `SLOT`, `PARTS` (from the printout), `BUDGET`, and the
   builder (`skinned_item`, `hair_item`, `rigid_item`). Run it with the
   Cosmetics folder as `out_dir`.
7. **Review** like any item: `review.mask_preview([NAME])`,
   `review.preview_variants(presets, items=[NAME], pose=...)` with every slider
   maxed and the test poses. Then open Unity: the importer builds the
   `CosmeticItem` and the textured `M_*.mat`. Evidence screenshots go to
   `production/qa/evidence/` as for every cosmetic.

### What `skinned_item` does to a generated part

| Step | Why |
|---|---|
| Limit the skin mask to the body regions the part covers | Generated items have no cut lines, so the ray test alone could hide skin a sleeve hovers over elsewhere |
| `strip_inner`: delete faces looking at the skin | Generators return garments as closed solids; the inner wall would be pushed onto the outer one and z-fight |
| Decimate to half the budget (symmetric) | Solidify doubles it |
| `conform`: push to ≥ 4 mm off the rest skin; `max_gap` pulls loose fabric in for tight items | Fixes a guessed body's centimetres; leave `max_gap` off for anything meant to hang |
| Solidify 3 mm, `ensure_clearance` 3 mm | Same final shell as hand-built items |
| Textures → `T_<Item>_BaseMap/Normal/MetallicSmoothness.png` (1K) | URP Lit packing; the sidecar names them |
| `bind_to_body`, `skin_mask`, `export` | Unchanged charkit path: weights + 16 sliders from the body, any vertex > 15 cm from skin aborts |

## Props

`propkit.ingest.run(src, name, height, budget, rotate_z=0, provenance=None)`
(Blender, headless works; see `tools/blender/propkit/README.md`): import → join
→ clean → turn by `rotate_z` if the model does not face −Y → stand on the floor
at `height` m → LOD0–2 at 100/50/25% of the budget tier (small 1,500 / medium
4,000 / large 10,000 tris), each decimated from the full-density mesh and
re-seated on Z = 0 (`SM_<Name>_LOD0..2`; Unity builds the LODGroup from the
names) → 1K textures → `Assets/Art/Props/<Name>/SM_<Name>.fbx` + observations
in `SM_<Name>.prop.json` → `ArtSource/AI/<Name>/<Name>_clean.glb`. Unity's
`PropModelImporter` gives props scale 1, no rig/animation, and a URP Lit
`M_<Name>.mat` built from the `T_` maps (logs the LODGroup it finds).

## Unity import

| File | Does |
|---|---|
| `Assets/Art/Characters/Editor/CosmeticMaterialImporter.cs` | For sidecar materials that name a `baseMap`: creates/updates `M_<Item>.mat` (URP Lit, white base colour so `CharacterAppearance` tints still multiply), remaps the item FBX's material to it; `T_*_Normal.png` → normal map, `T_*_MetallicSmoothness.png` → linear |
| `Assets/Art/Props/Editor/PropModelImporter.cs` | Prop FBX rules and materials |

Neither importer uses `OnPreprocessModel` / `OnPreprocessTexture`: a
postprocessor hooking an importer type becomes an import dependency of every
asset of that type, so adding one would re-import every model or texture in the
project. They set import options on their own files from
`OnPostprocessAllAssets` and re-import just those (one extra import per new
asset).

Textured cosmetics are not recolourable yet (`recolorable: false`): tinting a
baked albedo muddies it. Recolourable generated items need a greyscale albedo
plus a colour-region mask (later).

## Style consistency

- Same reference sheet, same pose, same prompt template for every cosmetic:
  `"<item> for a stylised college-party game character, clean flat colours,
  simple folds, no logos, no text"`, plus the art bible's palette words.
- Remove Lighting on, always: baked shadows in an albedo never match the scene.
- `aikit.report.observe` prints base-colour luminance stats; a strong
  top-to-bottom luminance ramp is the baked-lighting symptom.

## Known limits and open experiments

- **Loose garments** (skirts, wide sleeves) take weights from the nearest skin,
  so cloth between the legs follows one leg. Fix candidates: Robust Weight
  Transfer-style inpainting in `bind_to_body` (the add-on itself is unverified
  on Blender 5.0), or a skirt helper bone.
- **Long hair** past 15 cm from skin is fine for `hair_item` (Head-only weights)
  but swings rigidly; spring bones come later.
- **Cleanup time is the real cost** and is unmeasured. Run in this order and
  time each: one earring (rigid), one pair of shoes, one loose skirt. That data
  decides how much of the wardrobe Tripo builds.
- Hair cards, recolourable generated items and Tripo-texture-on-body-grown-shell
  bakes are not built.

## Verification

| Check | Command | Covers | Result 2026-10-19 |
|---|---|---|---|
| Cosmetic ingest end to end | `blender -b ArtSource/Characters/SK_Human_Female.blend --python tools/blender/charkit/tests/test_ingest_generated.py` | Stand-in Tripo parts GLB (×0.6 scale, 4° turn, offset; closed-volume top, helmet hair, earring, body) → fit, clean file, skinned/hair/rigid items, sidecars, textures, clearance | 19/19: fit scale 1.66666 (true 1.66667), turn 4.000°, bare-skin median 0.0 mm; top 1,444/1,500 tris, 16 sliders, hides 390 skin vertices, min gap 2.9 mm |
| Prop ingest end to end | `blender -b --factory-startup --python-exit-code 1 --python tools/blender/propkit/tests/test_ingest.py` | 187k-tri textured chair stand-in (×37, turned 90°, floaters, cracked seams, non-manifold fin) → budget, LODs, scale, floor, facing, URP maps, FBX + GLB round trip | 34/34: LODs 1,498/748/373, height 0.90026 m, 5/5 floaters removed |
| Tripo CLI | `py -3.12 -m unittest discover tools/tripo/tests` | Provenance, register, request building, downloads, zip-slip guard (no network) | 29 tests OK (1 needs the SDK installed; passed in a venv) |
| Unity C# | `tools/ci/unity-compile-check.ps1` | Both importers compile | PASS, 0 errors |

Each of the cosmetic test's fit, mask and budget checks failed on an earlier
version of `ingest.py` (one-way ICP stopped at scale 1.634; re-imported parts
were not welded, so the mask hid 5 vertices; the rim was not budgeted, 2,036
tris) and passes on the fixed one.

Not yet verified: a real Tripo download, the Unity-side import of a textured
cosmetic and of a prop in the Editor (LODGroup, material remap), and any live
API call (no API key on this machine).
