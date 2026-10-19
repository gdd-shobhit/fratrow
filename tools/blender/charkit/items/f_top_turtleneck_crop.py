"""Dark green long-sleeve turtleneck crop top (characters1, black bob)."""
from mathutils import Vector

from .. import cosmetic
from . import _garment as G

NAME = "SKM_F_Top_TurtleneckCrop"
HEM_Z = 1.10
COLLAR_Z = 1.425     # just under the jaw
CUFF_T = 0.93        # along the forearm (0 elbow .. 1 wrist)
OFFSET = 0.006
COLLAR_ROLL = 0.35   # extra radius of the rolled collar
SHELL_TRIS = 800
GREEN = (0.14, 0.30, 0.23)


def build():
    torso = lambda g: all(abs(v.co.x) < 0.19 for v in (g.verts if hasattr(g, "verts") else [g]))
    cuts = [((0, 0, HEM_Z), (0, 0, -1), torso), ((0, 0, COLLAR_Z), (0, 0, 1), None)]
    for sign in (1, -1):
        elbow, wrist = G.bone_axis("LeftForeArm")
        elbow.x *= sign
        wrist.x *= sign
        on_arm = (lambda s: lambda g: all(v.co.x * s > 0.2 for v in (g.verts if hasattr(g, "verts") else [g])))(sign)
        cuts.append((elbow.lerp(wrist, CUFF_T), wrist - elbow, on_arm))
    bm, covered, surface = G.shell({"Chest", "Belly", "Neck", "UpperArm", "Forearm"}, cuts, OFFSET)
    edge = G.edge_distance(bm)
    neck_base, head_base = G.bone_axis("Neck")
    for v in bm.verts:
        if v.co.z > neck_base.z - 0.01 and abs(v.co.x) < 0.09:
            # Rolled collar: puff the top of the neck tube outward.
            roll = G.smoothstep((v.co.z - (COLLAR_Z - 0.06)) / 0.05)
            axis = Vector((0.0, neck_base.y + (head_base.y - neck_base.y) * 0.5, v.co.z))
            v.co = axis + (v.co - axis) * (1.0 + COLLAR_ROLL * roll)
    bm.normal_update()
    obj = G.finalize(bm, NAME, [("M_Top_TurtleneckCrop", GREEN)], SHELL_TRIS,
                     cover=[surface.co[i] for i in covered])
    obj[cosmetic.COVERED_PROP] = covered
    return obj


def run(out_dir):
    obj = build()
    return [cosmetic.finish(obj, out_dir, "Top", G.material_info(obj))]
