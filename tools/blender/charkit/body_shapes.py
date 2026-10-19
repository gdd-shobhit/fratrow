"""Body shape sliders: shape keys on the master body (Unity blend shapes).

Each key is a smooth displacement of the rest mesh, masked to body regions and
measured from the mesh and skeleton (breast/glute apex, bone axes), so the keys
survive changes to the proportion pass. Hands, head and eyes are never moved.
Keys are built from the left side and applied mirrored, so the body stays
symmetric. Names and order come from config.SHAPE_KEYS.

    from charkit import body_shapes
    body_shapes.run()
"""
import math

import bpy
from mathutils import Vector

from . import config as C

# Strength of each slider at 100%. Positive = grow, negative = shrink.
STRENGTH = {
    "Bust_Small": -0.45, "Bust_Large": 0.8,
    "Hips_Narrow": -0.02, "Hips_Wide": 0.035,
    "Butt_Small": -0.35, "Butt_Large": 0.6,
    "Thighs_Thin": -0.15, "Thighs_Thick": 0.25,
    "Waist_Narrow": -0.16, "Waist_Wide": 0.2,
    "Belly_Soft": 0.035,
    "Arms_Thin": -0.2, "Arms_Thick": 0.3,
    "Calves_Thick": 0.2,
    "Shoulders_Narrow": -0.012, "Shoulders_Wide": 0.015,
}
BUST_RADIUS = 0.085
BUST_DEPTH = 0.05          # scaling centre sits this far behind the apex
BUST_X_FRACTION = 0.55     # breast centre x as a fraction of the shoulder joint x
BUTT_RADIUS = 0.11
BUTT_DEPTH = 0.07
HIP_BAND = 0.17            # half-height of the hip widening band
WAIST_BAND = 0.12
BELLY_BAND = 0.1

LIMB_REGIONS = {"UpperArm", "Forearm", "Hand"}
LOCKED_REGIONS = {"Head"}  # hands are excluded per shaper; Shoulders must carry them


def _falloff(t):
    """1 at t=0, 0 at t>=1, smooth at both ends."""
    if t >= 1.0:
        return 0.0
    u = 1.0 - t * t
    return u * u


def _smoothstep(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def _side(p):
    return 1.0 if p.x >= 0 else -1.0


def _mirror(v, side):
    return Vector((v.x * side, v.y, v.z))


class Body:
    """Rest positions, per-vertex regions and skeleton landmarks."""

    def __init__(self, body, arm):
        mesh = body.data
        self.co = [v.co.copy() for v in mesh.vertices]
        self.regions = [set() for _ in mesh.vertices]
        for poly in mesh.polygons:
            for v in poly.vertices:
                self.regions[v].add(C.REGIONS[poly.material_index])
        bones = arm.data.bones
        self.bone = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in bones}
        self.hip_z = self.bone["Hips"][0].z
        self.shoulder_z = self.bone["LeftArm"][0].z

        def apex(regions, zmin, zmax, key):
            pts = [c for c, r in zip(self.co, self.regions)
                   if r & regions and not r & LIMB_REGIONS and c.x > 0.02 and zmin < c.z < zmax]
            return max(pts, key=key)

        # The stylized chest is broad and flat-fronted, so the most forward point
        # is unreliable in x: take its height, but anchor x anatomically.
        chest_top = self.bone["Spine2"][1].z
        front = apex({"Chest"}, self.bone["Spine1"][0].z, chest_top, lambda c: -c.y)
        bust_x = BUST_X_FRACTION * self.bone["LeftArm"][0].x
        surface = min((c for c, r in zip(self.co, self.regions)
                       if "Chest" in r and c.y < 0 and abs(c.z - front.z) < 0.03),
                      key=lambda c: abs(c.x - bust_x))
        self.bust = Vector((bust_x, surface.y, front.z))
        self.butt = apex({"Pelvis", "Thigh"}, self.hip_z - 0.2, self.hip_z + 0.1, lambda c: c.y)
        torso = [c for c, r in zip(self.co, self.regions) if not r & LIMB_REGIONS and abs(c.x) < 0.15]
        self.waist_z = self.bone["Spine"][0].z + 0.04
        band = [c.y for c in torso if abs(c.z - self.waist_z) < 0.03]
        self.torso_y = sum(band) / len(band)
        hips = [c for c in torso if abs(c.z - self.hip_z) < 0.15]
        self.hip_widest_z = max(hips, key=lambda c: abs(c.x)).z

    def free(self, i):
        return not self.regions[i] & LOCKED_REGIONS

    def limb(self, p, name):
        """(radial offset from the bone axis, parameter along the bone) on p's side."""
        bone = name if p.x >= 0 else name.replace("Left", "Right")
        a, b = self.bone[bone]
        ab = b - a
        t = (p - a).dot(ab) / ab.length_squared
        return p - (a + ab * t), t


def _bust(B, i, k):
    p = B.co[i]
    if not B.regions[i] & {"Chest", "Belly"} or B.regions[i] & LIMB_REGIONS:
        return Vector()
    apex = _mirror(B.bust, _side(p))
    centre = apex + Vector((0, BUST_DEPTH, 0))
    f = _falloff((p - apex).length / BUST_RADIUS)
    sag = Vector((0, 0, -0.012)) if k > 0 else Vector()
    return ((p - centre) * k + sag) * f


def _butt(B, i, k):
    p = B.co[i]
    if not B.regions[i] & {"Pelvis", "Thigh", "Belly"} or B.regions[i] & LIMB_REGIONS:
        return Vector()
    apex = _mirror(B.butt, _side(p))
    centre = apex - Vector((0, BUTT_DEPTH, 0))
    f = _falloff((p - apex).length / BUTT_RADIUS)
    behind = _smoothstep((p.y - centre.y + 0.02) / 0.05)
    return (p - centre) * k * f * behind


def _hips(B, i, k):
    p = B.co[i]
    if B.regions[i] & LIMB_REGIONS:
        return Vector()
    f = _falloff(abs(p.z - B.hip_widest_z) / HIP_BAND) * _smoothstep(abs(p.x) / 0.15)
    return Vector((_side(p) * k * f, 0, 0))


def _waist(B, i, k):
    p = B.co[i]
    if B.regions[i] & LIMB_REGIONS:
        return Vector()
    f = _falloff(abs(p.z - B.waist_z) / WAIST_BAND)
    return Vector((p.x * k, (p.y - B.torso_y) * k * 0.5, 0)) * f


def _belly(B, i, k):
    p = B.co[i]
    if B.regions[i] & LIMB_REGIONS:
        return Vector()
    centre_z = (B.waist_z + B.hip_z) / 2
    f = _falloff(abs(p.z - centre_z) / BELLY_BAND) * _falloff(abs(p.x) / 0.13)
    front = _smoothstep((B.torso_y - p.y) / 0.06)
    return Vector((0, -k, -k * 0.15)) * f * front


def _thighs(B, i, k):
    p = B.co[i]
    if not B.regions[i] & {"Thigh", "Pelvis"}:
        return Vector()
    radial, t = B.limb(p, "LeftUpLeg")
    w = _smoothstep(t / 0.15) * _smoothstep((0.95 - t) / 0.25)
    return radial * k * w


def _calves(B, i, k):
    p = B.co[i]
    if not B.regions[i] & {"Calf", "Thigh"}:
        return Vector()
    radial, t = B.limb(p, "LeftLeg")
    return radial * k * _falloff(abs(t - 0.3) / 0.4)


def _arms(B, i, k):
    p = B.co[i]
    if not B.regions[i] & {"UpperArm", "Forearm"} or "Hand" in B.regions[i]:
        return Vector()
    upper, tu = B.limb(p, "LeftArm")
    lower, tl = B.limb(p, "LeftForeArm")
    if "UpperArm" in B.regions[i] and (upper.length <= lower.length or "Forearm" not in B.regions[i]):
        return upper * k * _smoothstep((tu + 0.05) / 0.3)
    return lower * k * 0.6 * _smoothstep((0.95 - tl) / 0.35)


def _shoulders(B, i, k):
    p = B.co[i]
    if B.regions[i] & LIMB_REGIONS:
        w = 1.0
    elif B.regions[i] & {"Chest", "Neck"}:
        w = _smoothstep((abs(p.x) - 0.05) / 0.1) * _falloff(abs(p.z - B.shoulder_z) / 0.15)
    else:
        w = 0.0
    return Vector((_side(p) * k * w, 0, 0))


SHAPERS = {
    "Bust": _bust, "Butt": _butt, "Hips": _hips, "Waist": _waist, "Belly": _belly,
    "Thighs": _thighs, "Calves": _calves, "Arms": _arms, "Shoulders": _shoulders,
}


def build(body, arm):
    """(Re)create the Basis and every key in config.SHAPE_KEYS."""
    if body.data.shape_keys:
        body.shape_key_clear()
    B = Body(body, arm)
    body.shape_key_add(name="Basis", from_mix=False)
    report = {}
    for name in C.SHAPE_KEYS:
        shaper = SHAPERS[name.split("_")[0]]
        k = STRENGTH[name]
        key = body.shape_key_add(name=name, from_mix=False)
        key.slider_min, key.slider_max = 0.0, 1.0
        biggest = 0.0
        for i, p in enumerate(B.co):
            d = shaper(B, i, k) if B.free(i) else Vector()
            key.data[i].co = p + d
            biggest = max(biggest, d.length)
        report[name] = round(biggest * 100, 1)  # cm
    return report


def set_values(body, values):
    """Set slider values {name: 0..1}; unnamed sliders go to 0."""
    for key in body.data.shape_keys.key_blocks[1:]:
        key.value = values.get(key.name, 0.0)


def run():
    body = bpy.data.objects[C.BODY]
    arm = bpy.data.objects[C.ARMATURE]
    report = build(body, arm)
    print(f"[body_shapes] max displacement (cm) per key: {report}")
    return report
