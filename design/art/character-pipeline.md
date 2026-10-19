# Character Pipeline — Modular Humanoid (Female first)

> Status: **M1 done 2026-10-18** (master rig + sliders, evidence in
> `production/qa/evidence/m1-female-base/`) · next: M2 Unity import ·
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
| 2. Body `SKM_F_Body` | One female body mesh, split into hideable regions. | Shape keys (body type), skin tone, face texture |
| 3. Cosmetics | Skinned items bound to `SK_Human`, rigid items on socket bones. | Slot, mesh, colours |
| 4. Recipe | Body values + skin tone + face + one item per slot + colours. | Per character |

### Skinned vs rigid cosmetics

Anything that must bend with the body is a **skinned** mesh bound to `SK_Human`
(weights copied from the body by script). Static meshes are only for **rigid**
accessories, which parent to a `Socket_*` bone.

| Slot | Kind | Hides body regions |
|---|---|---|
| Hair | skinned (head/neck) | — |
| Top | skinned | per item, e.g. crop top → Chest |
| Bottom | skinned | per item, e.g. cargo pants → Pelvis, Thigh, Calf |
| FullBody | skinned | dress/jumpsuit; replaces Top + Bottom |
| Legwear | skinned | socks, leg warmers, fishnets |
| Shoes | skinned | Feet (+ Calf for boots) |
| HeadTop / Face / Ear / Hand / Back | rigid on socket | — |

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

## Body regions

Regions are **material slots** on the single body mesh, named `BR_<Region>`.
Unity imports each slot as a submesh; the runtime builder (later phase) drops the
submeshes a recipe hides and merges the rest under one skin material, so a dressed
character is still one draw call for skin.

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
| `ArtSource/Characters/SK_Human_Female.blend` | Master: skeleton + body (source of truth) |
| `ArtSource/Characters/Cosmetics_Female.blend` | Cosmetic items, bound to the master armature (M3+) |
| `ArtSource/Characters/Characters_Female.blend` | The three assembled girls for review (M4) |
| `tools/blender/charkit/*.py` | Re-runnable pipeline scripts (see README there) |
| `Assets/Art/Characters/Female/` | FBX exports Unity imports |

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
