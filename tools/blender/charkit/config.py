"""Shared names and tuning knobs for the character pipeline.

Every number an artist may want to change lives here so the scripts stay
re-runnable. See design/art/character-pipeline.md.
"""
import os

# --- Source -------------------------------------------------------------------
# Blender Studio Human Base Meshes v1.4.1 (CC0). Only needed for a from-scratch
# rebuild; ArtSource/Characters/SK_Human_Female.blend already holds the result.
HBM_BLEND = os.environ.get("CHARKIT_HBM_BLEND", "")
HBM_BODY = "GEO-body_female_stylized"
HBM_EYES = ("GEO-body_female_stylized.eye.L", "GEO-body_female_stylized.eye.R")
HBM_EYE_OFFSET_X = -0.388  # the bundle's eyes are authored 38.8 cm off the body

# --- Object names -------------------------------------------------------------
COLLECTION = "SK_Human_Female"
ARMATURE = "SK_Human"
BODY = "SKM_F_Body"
EYES = "SKM_F_Eyes"
EYE_MATERIAL = "M_Eye"
REGION_PREFIX = "BR_"
FACE_SET_ATTR = "src_face_set"  # source face-set id per face, kept for skeleton.py
VERTEX_ID_UV = "BodyVertexId"   # 2nd UV channel (Unity uv2): body vertex index, for skin masks

# --- Proportion pass (source metres: feet on Z=0, before height scaling) ------
HEAD_SCALE = 0.90                # inspo heads are smaller than the base mesh's
HEAD_PIVOT = (0.0, -0.02, 1.33)  # top of the neck
HEAD_BLEND_Z = (1.28, 1.37)      # neck band where the head scale fades in
LEG_STRETCH = 1.06               # longer legs, fashion-illustration proportions
LEG_STRETCH_Z = (0.10, 0.80)     # ankle .. hip; feet keep their size
TARGET_HEIGHT = 1.66

# --- Decimation ---------------------------------------------------------------
# Triangle budget per group of regions, decimated in this order. Regions not
# listed share the "*" budget. The face gets the most: it is what players read.
DECIMATE_BUDGETS = (
    (("Head",), 2400),
    (("Hand",), 900),
    (("Feet",), 400),   # mostly hidden by shoes
    (("*",), 2600),
)
EYE_SEGMENTS = 12
EYE_RINGS = 8

# --- Body regions -------------------------------------------------------------
# Order is the material-slot order on the body (and the submesh order in Unity).
REGIONS = ("Head", "Neck", "Chest", "Belly", "Pelvis",
           "UpperArm", "Forearm", "Hand", "Thigh", "Calf", "Feet")

# Sculpt face-set ids on GEO-body_female_stylized -> region.
REGION_FACE_SETS = {
    "Head": (27, 28, 29, 37, 44, 45, 46, 47, 48, 49),
    "Neck": (6,),
    "Chest": (259, 260),
    "Belly": (258,),
    "Pelvis": (256,),  # also covers the thighs; split below
    "UpperArm": (263,),
    "Forearm": (262,),
    "Hand": tuple(range(272, 287)) + (359,),
    "Calf": (257,),
    "Feet": (288, 316, 317, 318, 319, 320, 321, 322),
}
PELVIS_THIGH_SPLIT_Z = 0.72  # faces of set 256 below this are Thigh
ARM_REGIONS = ("UpperArm", "Forearm", "Hand")  # move rigidly in the leg stretch

# --- Body shape sliders (shape keys -> Unity blend shapes, 0..100) -----------
# Authored once on the master body; every character is the same mesh with
# different values. Unity blend shapes cannot go negative, so features that
# vary both ways get a pair. Cosmetics receive the same keys (M3).
# Ranges/centres are relative to the body after the proportion pass.
SHAPE_KEYS = (
    "Bust_Small", "Bust_Large",
    "Hips_Narrow", "Hips_Wide",
    "Butt_Small", "Butt_Large",
    "Thighs_Thin", "Thighs_Thick",
    "Waist_Narrow", "Waist_Wide",
    "Belly_Soft",
    "Arms_Thin", "Arms_Thick",
    "Calves_Thick",
    "Shoulders_Narrow", "Shoulders_Wide",
)
# Height is not a shape key: the recipe scales the whole character.
HEIGHT_SCALE_RANGE = (0.95, 1.05)

# Saved slider combinations (data, not extra keys). "height" is the whole-body scale.
BODY_PRESETS = {
    "Petite": {"Bust_Small": 0.5, "Hips_Narrow": 0.4, "Butt_Small": 0.3, "Thighs_Thin": 0.5,
               "Arms_Thin": 0.5, "Shoulders_Narrow": 0.5, "height": 0.96},
    "Curvy": {"Bust_Large": 0.7, "Hips_Wide": 0.9, "Butt_Large": 0.9, "Thighs_Thick": 0.8,
              "Waist_Narrow": 0.5, "Calves_Thick": 0.4, "Arms_Thick": 0.3, "Belly_Soft": 0.2,
              "height": 1.0},
    "TallSlim": {"Waist_Narrow": 0.5, "Hips_Wide": 0.2, "Thighs_Thin": 0.25, "Arms_Thin": 0.35,
                 "Shoulders_Narrow": 0.2, "Bust_Small": 0.15, "height": 1.04},
    "Athletic": {"Shoulders_Wide": 0.6, "Waist_Narrow": 0.2, "Butt_Large": 0.4, "Thighs_Thick": 0.4,
                 "Calves_Thick": 0.6, "Arms_Thick": 0.4, "Bust_Small": 0.2, "height": 1.02},
}

# Body UV atlas: the source body's UDIM tiles (head+neck 0, torso and limbs 1,
# hands 2, feet 4) packed into 0-1, because Unity wraps UVs and the tiles would
# overlap. tile -> (u offset, v offset, scale); the head gets the most room so
# per-character face textures (charkit.faces) have the detail. 0-0.25 in v is free.
UV_ATLAS = {0: (0.0, 0.25, 0.75), 1: (0.75, 0.75, 0.25), 2: (0.75, 0.5, 0.25), 4: (0.75, 0.25, 0.25)}

# Preview colours only; in Unity the recipe sets these per character.
SKIN_TONE = (0.80, 0.58, 0.46)
EYE_COLOR = (0.92, 0.92, 0.90)
