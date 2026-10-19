# Character Pipeline — Modular Humanoid (Female first)

> Status: **M3 done 2026-10-18** — cosmetic pipeline end to end: the blonde's
> off-shoulder crop top (skinned) and gold hoops (rigid) built in Blender,
> imported as CosmeticItems, worn through `CharacterAppearance` with skin
> masking, synced body sliders and tints; walk verified on Base and Curvy.
> Evidence in `production/qa/evidence/m1-female-base/`, `.../m2-unity-humanoid/`,
> `.../m3-cosmetic-pipeline/` · next: M4 the characters1 trio ·
> Branch: `feat/female-base-character`
> Inspo: `characters1.png` (first three girls), `characters2.png` (cosmetic backlog).

## Goal

One master skeleton and one base body per body type. Everything players see on a
character is a cosmetic layered on that base, so a new outfit never needs a new
rig, and every Humanoid animation in the project plays on every character.

## Architecture

| Layer | What it is | Varies by |
|---|---|---|
| 1. Skeleton `SK_Human` | One armature for all characters (girls now, guys later). Mixamo bone names, imports to Unity as **Humanoid**. | Never per character |
| 2. Body `SKM_F_Body` | One female body mesh; worn items hide the skin they cover. | Shape keys (body type), skin tone, face texture |
| 3. Cosmetics | Skinned items bound to `SK_Human`, rigid items on socket bones. | Slot, mesh, colours |
| 4. Recipe | Body values + skin tone + face + one item per slot + colours. | Per character |

### Skinned vs rigid cosmetics

Anything that must bend with the body is a **skinned** mesh bound to `SK_Human`
(weights copied from the body by script). Static meshes are only for **rigid**
accessories, which parent to a `Socket_*` bone.

| Slot | Kind | Skin it hides |
|---|---|---|
| Hair | skinned (head/neck) | — |
| Top | skinned | its own skin mask (computed) |
| Bottom | skinned | its own skin mask |
| FullBody | skinned | dress/jumpsuit; replaces Top + Bottom |
| Legwear | skinned | socks, leg warmers, fishnets |
| Shoes | skinned | its own skin mask |
| Ears / Face / HeadTop / Hand / Back | rigid on socket | — |

## Skeleton `SK_Human`

Rest pose is the base mesh's **A-pose**; Unity's avatar configuration enforces
T-pose when mapping. `Hips` is the top bone (Mixamo convention); the armature
object is the root.

```
Hips
├─ Spine ─ Spine1 ─ Spine2
│                    ├─ Neck ─ Head ─ LeftEye, RightEye
│                    │                Socket_HeadTop, Socket_Face, Socket_LeftEar, Socket_RightEar
│                    ├─ LeftShoulder ─ LeftArm ─ LeftForeArm ─ LeftHand
│                    │     └─ LeftHand{Thumb,Index,Middle,Ring,Pinky}{1,2,3}, Socket_LeftHand
│                    ├─ RightShoulder … (mirror)
│                    └─ Socket_Back
├─ LeftUpLeg ─ LeftLeg ─ LeftFoot ─ LeftToeBase
└─ RightUpLeg … (mirror)
```

- Names match the ithappy pack already in the project and Mixamo downloads.
- `Socket_*` bones are non-deforming (no weights). Unity Humanoid ignores them;
  code finds them by name.
- Fingers are full three-segment chains so the hand can hold the phone and cups.

## Hiding skin under clothes

Each skinned item carries its own **skin mask**: the body vertices it covers.
`CharacterAppearance` (Unity) hides every body triangle whose three vertices are
all masked by a worn item, and merges what remains into one submesh under one
skin material, so a dressed character is one draw call for skin and covered skin
costs nothing. Triangles crossing an item's edge stay visible, which is the
margin that prevents gaps.

- **How the mask is computed** (`charkit/cosmetic.py`): items grown from the
  body record exactly which body vertices survived their hem/neckline/cuff cuts,
  and of those keep only the ones under *tight* fabric (a ray along the skin
  normal hits the item within 2.5 cm). Skin under loose fabric, like a puffed
  sleeve, stays visible: it can be seen through the gap at the fabric's edge.
- **Body vertex ids**: the body's second UV channel (`BodyVertexId`, Unity
  `uv2`) holds each vertex's index. Unity splits and reorders vertices on
  import; every copy keeps its id, so masks stay valid. Lightmap UV generation
  is off for character meshes so nothing overwrites it.
- A mask is only valid for the body topology it was computed on (vertex count
  and topology hash are stored with the item); rebuild items after changing the
  body.

### Body regions (coarse fallback)

The body also keeps **material slots** per region, named `BR_<Region>`, derived
from the source face sets. They are no longer how skin is hidden (an off-shoulder
top covers the chest but not the shoulders, which share `BR_Chest`), but they
remain useful as named areas, e.g. for region-specific skin effects.

| Region | Covers |
|---|---|
| `BR_Head` | skull, face, ears |
| `BR_Neck` | neck |
| `BR_Chest` | shoulders, collarbones, chest |
| `BR_Belly` | waist, stomach, lower back |
| `BR_Pelvis` | hips, buttocks, groin |
| `BR_UpperArm` / `BR_Forearm` / `BR_Hand` | arms and hands |
| `BR_Thigh` / `BR_Calf` / `BR_Feet` | legs and feet |

Region assignment is derived from the base mesh's sculpt face sets by
`tools/blender/charkit/body_prepare.py`, so it is reproducible.

## Building a cosmetic

One module per item in `tools/blender/charkit/items/` (copy
`f_top_offshoulder_crop.py` for garments, `f_ear_hoop_small.py` for rigid
accessories). A garment is built from the master body:

1. **Shell** — copy the body regions it covers (`body_bmesh`), then cut clean
   hems/necklines/cuffs with planes (`cut`); cutting along the low-poly body's
   triangles would leave zig-zag edges.
2. **Mask source** — `covered_body_vertices` records the body vertices that
   survived the cuts (before anything moves).
3. **Relax** — Laplacian smoothing that never lets a vertex closer to the skin
   than an offset, so fabric bridges hollows (cleavage, armpits) like cloth.
4. **Shape** — item-specific detail (puff sleeves, flares), faded out near open
   edges so hems hug the body.
5. **Decimate, solidify, clearance** — to the triangle budget, thickness inward,
   then `ensure_clearance` pushes any vertex closer than 3 mm back out.
6. **Bind** (`bind_to_body`) — bone weights and all 16 body sliders copied from
   the skin under each vertex; each slider is checked against the shaped body
   and sunken vertices pushed out. Any vertex > 15 cm from the skin aborts the
   build (catches broken geometry).
7. **Mask + export** — `skin_mask`, then `export` writes `<name>.fbx` (with the
   armature) and `<name>.cosmetic.json` into `Assets/Art/Characters/Female/Cosmetics/`.

Rigid items are modelled where they sit on the rest-pose body and list
`attachments` (node → `Socket_*`); Unity keeps that placement relative to the
socket. Check every item with `review.mask_preview` and `review.preview_variants`
(body presets, all sliders maxed, test poses) before exporting.

## Unity runtime (`Assets/Scripts/Characters`, assembly `FratRow.Characters`)

| Type | Role |
|---|---|
| `CosmeticItem` (ScriptableObject) | One wearable: slot, skinned/rigid, model, skin mask, socket attachments, default colours. Generated from the sidecar by `CharacterModelImporter` — rebuild in Blender rather than editing. |
| `CharacterAppearance` (component on the character) | `Apply()`: spawns items and re-binds skinned ones to the character's bones by name, parents rigid ones to sockets, syncs body sliders onto the body and every item, hides masked skin (body mesh copy cached per item combination, one submesh), tints skin and items via property blocks. |
| `BodyMask` | Pure mask logic (decode, union, triangle filter, cache key); covered by `Assets/Tests/EditMode/Characters/BodyMaskTests.cs`. |

Skin uses the shared `Assets/Art/Characters/Materials/M_Skin.mat` (URP Lit),
tinted per character. **Tools → FratRow → Characters → Run Dressed Check (M3)**
dresses the base with the crop top and hoops on the Base and Curvy presets and
writes walk filmstrips to `production/qa/evidence/m3-cosmetic-pipeline/`.

## Body shape sliders

Every girl uses the **same** body mesh; what makes them different is slider
values. The sliders are shape keys authored once on the master body and export
to Unity as blend shapes (0–100). Unity blend shapes cannot go negative, so
features that vary in both directions get a pair.

| Area | Sliders |
|---|---|
| Bust | `Bust_Small`, `Bust_Large` |
| Hips | `Hips_Narrow`, `Hips_Wide` |
| Butt | `Butt_Small`, `Butt_Large` |
| Thighs | `Thighs_Thin`, `Thighs_Thick` |
| Waist | `Waist_Narrow`, `Waist_Wide` |
| Belly | `Belly_Soft` |
| Arms | `Arms_Thin`, `Arms_Thick` |
| Calves | `Calves_Thick` |
| Shoulders | `Shoulders_Narrow`, `Shoulders_Wide` |

- **Cosmetics carry the same sliders.** The cosmetic script copies every body
  shape key onto each skinned item, and the runtime builder sets the item's
  values to match the body, so clothes follow the hips/bust instead of clipping.
- **Height** is a whole-character scale (0.95–1.05), not a shape key, so the
  skeleton and animations are untouched.
- **Presets** ("Slim", "Curvy", "Athletic") are saved slider combinations in
  data, not extra shape keys.
- Face sliders (jaw, cheeks, nose, lips, eye size) follow the same pattern in a
  later face pass.

## Animations

- **Rest pose stays A-pose.** Unity's Humanoid avatar enforces T-pose internally
  when mapping, so clips authored on T-pose characters play correctly; A-pose
  deforms the shoulders better for the arms-down poses that dominate gameplay.
  `review.py`'s `t_pose` test checks that the conversion deforms cleanly.
- **Mixamo: animations only.** Download clips with Mixamo's own character
  ("Y Bot"), *Without Skin*, *FBX for Unity*; import as Humanoid; they retarget
  onto every character. Do **not** upload our character to Mixamo's auto-rigger:
  it replaces `SK_Human` and drops finger weights, sockets and shape keys.
- Locomotion clips: *In Place* (the controller drives movement via `Speed`).
- Existing Humanoid clips (CityPeople female locomotion/idle/dance, ithappy
  basic set) work immediately; M2 verifies with the CityPeople walk.
- Bone names match Mixamo minus the `mixamorig:` prefix, so Mixamo FBX can also
  be previewed on the rig in Blender.
- Licence: Mixamo needs an Adobe account; clips are royalty-free in a shipped
  game but may not be redistributed as standalone files.
- Later: phone/cup holds as Animator layers with hand avatar masks; spring bones
  for ponytails and skirts.

## Budgets

| Part | Triangles |
|---|---|
| Body (all regions) | ~6,000 |
| Eyes (pair) | ~300 |
| Hair | ≤ 1,500 |
| Top + Bottom + Shoes | ≤ 3,000 combined |
| **Dressed character (body regions hidden)** | **6,000–10,000 rendered** |

Max 4 bone influences per vertex. One skin material + one material per cosmetic.

## Files

| Path | Contents |
|---|---|
| `ArtSource/Characters/SK_Human_Female.blend` | Master: skeleton + body (source of truth); cosmetics live in its `Cosmetics_Female` collection, rebuildable from the item scripts |
| `ArtSource/Characters/Characters_Female.blend` | The three assembled girls for review (M4) |
| `tools/blender/charkit/*.py` | Re-runnable pipeline scripts (see README there); `items/` = one module per cosmetic |
| `Assets/Art/Characters/Female/` | Base FBX; `Cosmetics/` holds item FBX + `.cosmetic.json` + generated `CosmeticItem` assets |
| `Assets/Art/Characters/Materials/` | Shared character materials (`M_Skin`) |
| `Assets/Art/Characters/Editor/` | Unity import rules (`CharacterModelImporter`, `CosmeticSidecar`) and the check tools |
| `Assets/Scripts/Characters/` | Runtime: `CosmeticItem`, `CharacterAppearance`, `BodyMask` |
| `Assets/Tests/EditMode/Characters/` | EditMode tests for the runtime logic |

Naming: `SK_` skeleton, `SKM_` skinned mesh, `SM_` static mesh, `CH_` assembled
character, `M_` material, `BR_` body-region material. Cosmetics are
`SKM_F_<Slot>_<Name>` (e.g. `SKM_F_Top_OffShoulderCrop`).

`.blend`, `.fbx` and `.png` under these folders are tracked by Git LFS
(`.gitattributes`).

## Export conventions (Blender → Unity)

- 1 Blender unit = 1 m; character ~1.66 m tall, feet on Z = 0, facing −Y.
- FBX: *Apply Transform*, forward −Z / up Y, no leaf bones, only deform bones +
  sockets, mesh modifiers applied, shape keys exported.
- Unity import: Rig = **Humanoid**, Avatar = Create From This Model, scale 1, no
  rotation fix-up needed.

## Unity import

`Assets/Art/Characters/Editor/CharacterModelImporter.cs` applies the rules to
every FBX under `Assets/Art/Characters/`, so nobody sets import options by hand:

- All character FBX: scale 1, blend shapes on (normals recalculated), rig
  transforms kept (`optimizeGameObjects` off, so sockets can be found by name),
  no cameras/lights/animation.
- Base models (`SK_*`, `CH_*`): **Humanoid**, avatar created from the model,
  explicit Mixamo → Unity bone map (54 human bones incl. eyes and all fingers).
- **T-pose reference, A-pose bind.** On first import the importer computes a
  T-pose reference skeleton (arms/forearms/hands aimed sideways, legs straight
  down) and re-imports with it — the automatic equivalent of the Avatar window's
  *Enforce T-Pose*. Without it, every Humanoid clip plays with the arms ~40° off.
  `userData: charkit-avatar-v1` in the `.meta` marks it done; bump
  `AvatarVersion` to re-bake every base model after changing the logic.
- Region materials (`BR_*`) import via the material description and render as a
  pale placeholder; the real skin material arrives with the runtime builder.

**Check tool:** *Tools → FratRow → Characters → Run Humanoid Check (M2)* (or
`CharacterHumanoidCheck.Run(model, clip, outDir, frames)`) plays a Humanoid
clip on a character in a hidden preview scene and writes a filmstrip PNG to
`production/qa/evidence/`; it never touches the open scene. It samples with
`AnimationMode` and bakes skinning per frame — a manually evaluated
`PlayableGraph` does not move bones in a preview scene, and a manually
rendered preview camera does not re-skin.

## Tuning knobs

All live as constants at the top of `body_prepare.py` and `skeleton.py`:
head scale, leg length factor, target height, decimation ratio and protected
regions, bone placement offsets.

## Milestones

| | Deliverable | Evidence |
|---|---|---|
| M1 | Skeleton + body + shape sliders in Blender, pose-tested | front/side/¾, slider and pose renders in `production/qa/evidence/` |
| M2 | FBX in Unity as Humanoid; CityPeople female walk plays | Unity screenshot |
| M3 | One skinned item (crop top) + one rigid accessory; region hiding | renders + Unity screenshot |
| M4 | The characters1 trio assembled | turntable renders + Unity screenshot |
| Later | Unity recipe ScriptableObject, runtime builder, network sync, player/NPC adoption | separate task |

## Risks

- Geometry is script-built, not hand-sculpted: skeleton, weights, regions, naming
  and export are production-grade; body/face/hair/clothes are a clean first pass
  an artist can replace without touching the skeleton.
- Pleated skirts and long ponytails only skin-deform in the first pass; spring
  bones / cloth come later.

## Source and licence

Base body: Blender Studio **Human Base Meshes v1.4.1**, object
`GEO-body_female_stylized`, downloaded from
`mirror.blender.org/demo/asset-bundles/human-base-meshes/human-base-meshes-bundle-v1.4.1.zip`
(SHA-256 `811F43AC…2B3515`). The bundle README and blender.org both state
**CC0**. The bundle's embedded "License" text block is a leftover from the Rain
Rig (CC-BY) and does not apply to these meshes.
