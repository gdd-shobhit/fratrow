"""Red wide-leg track pants with a white side stripe (characters1, redhead)."""
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .. import cosmetic
from . import _garment as G

NAME = "SKM_F_Bottom_TrackPants"
WAIST_Z = 1.03       # high waist, just below the cami's hem
ANKLE_Z = 0.10       # shell stops at the ankle (feet excluded) ...
HEM_DROP = 0.06      # ... then the hem band drops this much toward the floor
OFFSET = 0.008
KNEE_Z = 0.55        # flare starts here
FLARE = 1.1          # extra radius at the hem (wide leg)
SHELL_TRIS = 800
STRIPE_WIDTH = 0.022
RED = (0.86, 0.13, 0.15)
WHITE = (0.97, 0.97, 0.97)


def _drop_hem(bm):
    hem = [v.co.copy() for v in bm.verts if v.is_boundary and v.co.z < 0.3]
    for v in bm.verts:
        d = min((v.co - h).length for h in hem)
        v.co.z -= HEM_DROP * (1.0 - G.smoothstep(d / 0.07))


def _leg_axis_at(z, sign):
    """Point on the leg's centre line at height z (hip joint up to the waist)."""
    hip, knee = G.bone_axis("LeftUpLeg")
    _, ankle = G.bone_axis("LeftLeg")
    for a, b in ((hip, knee), (knee, ankle)):
        if b.z <= z <= a.z:
            p = a.lerp(b, (a.z - z) / (a.z - b.z))
            return Vector((p.x * sign, p.y, z))
    p = hip if z > hip.z else ankle
    return Vector((p.x * sign, p.y, z))


def _stripes(obj):
    """A clean ribbon down each outer seam, laid 1 mm over the pants surface."""
    me = obj.data
    bvh = BVHTree.FromPolygons([v.co for v in me.vertices], [tuple(p.vertices) for p in me.polygons])
    top = WAIST_Z - 0.015
    bottom = min(v.co.z for v in me.vertices) + 0.01
    bm = bmesh.new()
    for sign in (1, -1):
        out = Vector((sign, 0, 0))
        rows = []
        for k in range(32):
            z = top + (bottom - top) * k / 31
            axis = _leg_axis_at(z, sign)
            hit = bvh.ray_cast(axis + out * 0.5, -out, 0.6)
            if hit[0] is not None:
                rows.append((hit[0] + hit[1] * 0.002, hit[1]))
        verts = []
        for i, (p, n) in enumerate(rows):
            nxt = rows[min(i + 1, len(rows) - 1)][0] - rows[max(i - 1, 0)][0]
            across = nxt.cross(n).normalized() * (STRIPE_WIDTH / 2)
            verts.append((bm.verts.new(p - across), bm.verts.new(p + across)))
        for (a, b), (c, d) in zip(verts, verts[1:]):
            f = bm.faces.new((a, b, d, c))
            if f.normal.dot(out) < 0:
                f.normal_flip()
            f.material_index = 1
            f.smooth = True
    bm.from_mesh(me)
    bm.to_mesh(me)
    bm.free()


def build():
    bm, covered, surface = G.shell(
        {"Belly", "Pelvis", "Thigh", "Calf"},
        [((0, 0, WAIST_Z), (0, 0, 1), None), ((0, 0, ANKLE_Z), (0, 0, -1), None)],
        OFFSET)

    def flare(v, t):
        below = G.smoothstep((KNEE_Z - v.co.z) / (KNEE_Z - ANKLE_Z))
        return 1.0 + FLARE * below ** 1.2 + 0.08 * (1.0 - below)

    G.limb_scale(bm, "LeftLeg", flare, select=lambda v: v.co.z < KNEE_Z + 0.06)
    _drop_hem(bm)
    obj = G.finalize(bm, NAME, [("M_Bottom_TrackPants", RED), ("M_Bottom_TrackPants_Stripe", WHITE)], SHELL_TRIS,
                     cover=[surface.co[i] for i in covered])
    _stripes(obj)
    obj.data.shade_smooth()
    obj[cosmetic.COVERED_PROP] = covered
    return obj


def run(out_dir):
    obj = build()
    return [cosmetic.finish(obj, out_dir, "Bottom", G.material_info(obj))]
