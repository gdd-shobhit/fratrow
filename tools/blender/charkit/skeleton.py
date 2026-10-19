"""Build the SK_Human armature, fitted to the master body.

Joints are measured, not hand-placed: most sit on the seam between two of the
source mesh's face sets (knee = thigh|calf seam, each finger knuckle = the seam
between two finger segments). Left-side joints are measured and mirrored to the
right, so the rig is exactly symmetric. Bone names follow Mixamo, which Unity's
Humanoid avatar maps automatically. See design/art/character-pipeline.md.

    from charkit import skeleton
    skeleton.run()
"""
from collections import defaultdict

import bpy
from mathutils import Vector

from . import config as C

# Finger chains on the source mesh's left hand, as face-set ids
# (segment 1 -> 3). Segment 1 of each chain borders the palm.
PALM = 273
FINGERS = {
    "Thumb": (359, 285, 284),
    "Index": (278, 283, 282),
    "Middle": (277, 279, 272),
    "Ring": (280, 274, 275),
    "Pinky": (281, 286, 276),
}
NECK, CHEST = (6,), (259, 260)
HEAD = C.REGION_FACE_SETS["Head"]
UPPER_ARM, FOREARM, CALF, ANKLE = 263, 262, 257, 288
HIPS_AND_THIGHS = 256
TOES = (318, 319, 320, 321, 322)
FOOT = (316, 317)
EARS = 29

# Tuning knobs (metres unless noted).
HIP_ABOVE_CROTCH = 0.07       # femoral head above the crotch line
THIGH_MAX_X = 0.2             # thigh cross-section stops before the hanging hand
SPINE_FRACTIONS = (0.21, 0.46, 0.73)  # Spine/Spine1/Spine2 between Hips and Neck (Mixamo ratios)
SPINE_BACK_BIAS = 0.25        # 0 = cross-section centre, 1 = skin of the back
SHOULDER_INSET = 0.025        # LeftShoulder head distance from the centre line
SOCKET_LENGTH = 0.05


class Landmarks:
    """Joint positions measured on the body mesh (left side, +X)."""

    def __init__(self, body):
        mesh = body.data
        sets = [0] * len(mesh.polygons)
        mesh.attributes[C.FACE_SET_ATTR].data.foreach_get("value", sets)
        self.co = [v.co.copy() for v in mesh.vertices]
        self.edges = [tuple(e.vertices) for e in mesh.edges]
        self.faces_of = defaultdict(list)       # face set -> polygons
        self.sets_of_vert = defaultdict(set)    # vertex -> face sets touching it
        for poly in mesh.polygons:
            fs = sets[poly.index]
            self.faces_of[fs].append(poly)
            for v in poly.vertices:
                self.sets_of_vert[v].add(fs)

    def verts(self, face_sets, left=True):
        face_sets = set(face_sets)
        out = {v for fs in face_sets for p in self.faces_of[fs] for v in p.vertices}
        return [i for i in out if (self.co[i].x > 1e-4) == left or abs(self.co[i].x) <= 1e-4]

    def seam(self, a, b, left=True):
        """Centroid of the vertices shared by face-set groups a and b."""
        a, b = set(a), set(b)
        shared = [i for i, s in self.sets_of_vert.items()
                  if s & a and s & b and (self.co[i].x > 1e-4 or not left)]
        if not shared:
            raise ValueError(f"no seam between face sets {sorted(a)} and {sorted(b)}")
        return sum((self.co[i] for i in shared), Vector()) / len(shared)

    def centroid(self, face_sets, left=True):
        idx = self.verts(face_sets, left)
        return sum((self.co[i] for i in idx), Vector()) / len(idx)

    def section(self, z, pred):
        """Centroid, min-y and max-y of where the mesh's edges cross height z (filtered by pred)."""
        pts = []
        for a, b in self.edges:
            ca, cb = self.co[a], self.co[b]
            if (ca.z - z) * (cb.z - z) <= 0 and ca.z != cb.z:
                p = ca.lerp(cb, (z - ca.z) / (cb.z - ca.z))
                if pred(p):
                    pts.append(p)
        if not pts:
            raise ValueError(f"empty cross-section at z={z:.3f}")
        return sum(pts, Vector()) / len(pts), min(p.y for p in pts), max(p.y for p in pts)

    def farthest(self, face_sets, origin, direction):
        idx = self.verts(face_sets)
        return max((self.co[i] for i in idx), key=lambda c: (c - origin).dot(direction))


def measure(body, eyes):
    """Return {bone: (head, tail)} for the centre line and the left side."""
    L = Landmarks(body)
    j = {}

    # Legs.
    centre_line = [c for c in L.co if abs(c.x) < 0.004 and 0.5 < c.z < 1.0]
    crotch_z = min(c.z for c in centre_line)
    hip_z = crotch_z + HIP_ABOVE_CROTCH
    thigh, _, _ = L.section(crotch_z - 0.04, lambda c: 0.02 < c.x < THIGH_MAX_X)  # not the hanging hand
    hip = Vector((thigh.x, thigh.y, hip_z))
    knee = L.seam([HIPS_AND_THIGHS], [CALF])
    ankle = L.centroid([ANKLE])
    ball = L.seam(FOOT, TOES)
    toe_tip = L.farthest(TOES, ball, Vector((0, -1, 0)))
    toe_tip = Vector((ball.x, toe_tip.y, ball.z))
    j["LeftUpLeg"] = (hip, knee)
    j["LeftLeg"] = (knee, ankle)
    j["LeftFoot"] = (ankle, ball)
    j["LeftToeBase"] = (ball, toe_tip)

    # Spine: Mixamo proportions between the hip line and the neck base.
    neck_base = L.seam(CHEST, NECK, left=False)
    neck_base.x = 0.0
    head_base = L.seam(NECK, HEAD, left=False)
    head_base.x = 0.0
    top = max(L.co, key=lambda c: c.z)

    def spine_point(z):
        centre, _, back = L.section(z, lambda c: abs(c.x) < 0.12)
        return Vector((0.0, centre.y + (back - centre.y) * SPINE_BACK_BIAS, z))

    hips = spine_point(hip_z)
    chain = [hips] + [spine_point(hip_z + f * (neck_base.z - hip_z)) for f in SPINE_FRACTIONS] + [neck_base]
    for name, (a, b) in zip(("Hips", "Spine", "Spine1", "Spine2"), zip(chain, chain[1:])):
        j[name] = (a, b)
    j["Neck"] = (neck_base, head_base)
    j["Head"] = (head_base, Vector((0.0, head_base.y, top.z)))

    # Arms.
    shoulder = L.seam([UPPER_ARM], CHEST)
    elbow = L.seam([UPPER_ARM], [FOREARM])
    wrist = L.seam([FOREARM], [PALM])
    knuckle = L.seam([PALM], [FINGERS["Middle"][0]])
    j["LeftShoulder"] = (Vector((SHOULDER_INSET, neck_base.y, neck_base.z - 0.02)), shoulder)
    j["LeftArm"] = (shoulder, elbow)
    j["LeftForeArm"] = (elbow, wrist)
    j["LeftHand"] = (wrist, knuckle)
    for finger, segments in FINGERS.items():
        joints = [L.seam([PALM], [segments[0]])]
        joints += [L.seam([a], [b]) for a, b in zip(segments, segments[1:])]
        direction = (joints[-1] - joints[-2]).normalized()
        joints.append(L.farthest([segments[-1]], joints[-1], direction))
        for k in range(3):
            j[f"LeftHand{finger}{k + 1}"] = (joints[k], joints[k + 1])

    # Eyes: centre of the left eyeball, tail pointing forward (-Y).
    eye = [v.co for v in eyes.data.vertices if v.co.x > 0]
    eye_c = sum(eye, Vector()) / len(eye)
    j["LeftEye"] = (eye_c, eye_c + Vector((0, -0.025, 0)))

    # Sockets (non-deforming attach points).
    face_front = min((c for c in L.co if abs(c.x) < 0.01 and abs(c.z - eye_c.z) < 0.01), key=lambda c: c.y)
    ear_lobe = min((L.co[i] for i in L.verts([EARS])), key=lambda c: c.z)
    palm = L.centroid([PALM])
    _, _, back_y = L.section(chain[3].z, lambda c: abs(c.x) < 0.05)
    s = SOCKET_LENGTH
    j["Socket_HeadTop"] = (Vector((0, head_base.y, top.z)), Vector((0, head_base.y, top.z + s)))
    j["Socket_Face"] = (Vector((0, face_front.y, eye_c.z)), Vector((0, face_front.y - s, eye_c.z)))
    j["Socket_LeftEar"] = (ear_lobe, ear_lobe - Vector((0, 0, s)))
    j["Socket_LeftHand"] = (palm, palm + (knuckle - wrist).normalized() * s)
    j["Socket_Back"] = (Vector((0, back_y, chain[3].z)), Vector((0, back_y + s, chain[3].z)))
    j["_palm_centre"] = (palm, palm)  # used for the thumb roll, not a bone
    return j


# bone -> (parent, deform, roll hint: the direction the bone's local Z should face)
# THUMB: the thumb sits ~90 degrees from the fingers, so its Z is computed as
# "away from the palm centre" (the nail side) instead of a fixed direction.
FWD, UP, OUT, THUMB = Vector((0, -1, 0)), Vector((0, 0, 1)), Vector((1, 0, 0)), "THUMB"
HIERARCHY = {
    "Hips": (None, True, FWD), "Spine": ("Hips", True, FWD), "Spine1": ("Spine", True, FWD),
    "Spine2": ("Spine1", True, FWD), "Neck": ("Spine2", True, FWD), "Head": ("Neck", True, FWD),
    "LeftEye": ("Head", True, UP),
    "LeftShoulder": ("Spine2", True, FWD), "LeftArm": ("LeftShoulder", True, FWD),
    "LeftForeArm": ("LeftArm", True, FWD), "LeftHand": ("LeftForeArm", True, OUT),
    "LeftUpLeg": ("Hips", True, FWD), "LeftLeg": ("LeftUpLeg", True, FWD),
    "LeftFoot": ("LeftLeg", True, UP), "LeftToeBase": ("LeftFoot", True, UP),
    "Socket_HeadTop": ("Head", False, FWD), "Socket_Face": ("Head", False, UP),
    "Socket_LeftEar": ("Head", False, FWD), "Socket_LeftHand": ("LeftHand", False, OUT),
    "Socket_Back": ("Spine2", False, UP),
}
for _finger in FINGERS:
    for _k in range(3):
        HIERARCHY[f"LeftHand{_finger}{_k + 1}"] = (
            "LeftHand" if _k == 0 else f"LeftHand{_finger}{_k}", True,
            THUMB if _finger == "Thumb" else OUT)


def mirror_name(name):
    return name.replace("Left", "Right") if "Left" in name else name


def _mirror(v):
    return Vector((-v.x, v.y, v.z))


def build(joints):
    """(Re)create the armature object from measured joints."""
    old = bpy.data.objects.get(C.ARMATURE)
    if old is not None:
        data = old.data
        bpy.data.objects.remove(old, do_unlink=True)
        bpy.data.armatures.remove(data)
    arm_data = bpy.data.armatures.new(C.ARMATURE)
    arm = bpy.data.objects.new(C.ARMATURE, arm_data)
    bpy.data.collections[C.COLLECTION].objects.link(arm)
    arm.show_in_front = True
    arm_data.display_type = "STICK"

    view_layer = bpy.context.view_layer
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for obj in view_layer.objects:
        obj.select_set(False)
    view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    edit = arm_data.edit_bones

    def add(name, head, tail, parent, deform, z_axis):
        bone = edit.new(name)
        bone.head, bone.tail = head, tail
        bone.align_roll(z_axis)
        bone.use_deform = deform
        bone.use_connect = False
        if parent:
            bone.parent = edit[parent]

    palm = joints["_palm_centre"][0]
    for name, (parent, deform, z_axis) in HIERARCHY.items():  # dict order: parents first
        head, tail = joints[name]
        if z_axis == THUMB:
            along = (tail - head).normalized()
            away = head - palm
            z_axis = (away - along * away.dot(along)).normalized()
        add(name, head, tail, parent, deform, z_axis)
        if "Left" in name:
            add(mirror_name(name), _mirror(head), _mirror(tail),
                mirror_name(parent) if parent else None, deform, _mirror(z_axis))
    # Connect chains whose child starts exactly where the parent ends.
    for bone in edit:
        if bone.parent and (bone.parent.tail - bone.head).length < 1e-5:
            bone.use_connect = True
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def run():
    body = bpy.data.objects[C.BODY]
    eyes = bpy.data.objects[C.EYES]
    arm = build(measure(body, eyes))
    deform = [b.name for b in arm.data.bones if b.use_deform]
    sockets = [b.name for b in arm.data.bones if not b.use_deform]
    print(f"[skeleton] {len(deform)} deform bones, {len(sockets)} sockets")
    return arm
