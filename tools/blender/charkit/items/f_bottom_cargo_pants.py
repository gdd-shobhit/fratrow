"""Black baggy cargo pants with side pockets and gathered cuffs (characters1, black bob)."""
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

from .. import cosmetic
from . import _garment as G

NAME = "SKM_F_Bottom_CargoPants"
WAIST_Z = 0.975      # mid rise; the fishnet band shows above it
HEM_Z = 0.20         # cuffs tuck into the chunky boots (top 0.22): no ankle gap
OFFSET = 0.008
BAGGY = 0.28         # extra radius through thigh and calf
INNER_HUG = 0.8      # the inner side of each leg stays close, so thick thighs do not merge the legs
GATHER = 0.06        # cuffs/waistband hug the body within this of an open edge
SHELL_TRIS = 800
POCKET = (0.085, 0.11, 0.022)   # width, height, depth
POCKET_Z = 0.62
BLACK = (0.13, 0.13, 0.14)


def _pockets(obj):
    """A box pocket with a flap on each outer thigh, sitting on the pants surface."""
    me = obj.data
    bvh = BVHTree.FromPolygons([v.co for v in me.vertices], [tuple(p.vertices) for p in me.polygons])
    bm = bmesh.new()
    w, h, d = POCKET
    for sign in (1, -1):
        out = Vector((sign, -0.15, 0)).normalized()
        hip, knee = G.bone_axis("LeftUpLeg")
        axis = hip.lerp(knee, (hip.z - POCKET_Z) / (hip.z - knee.z))
        axis.x *= sign
        hit = bvh.ray_cast(axis + out * 0.5, -out, 0.6)
        if hit[0] is None:
            continue
        normal = hit[1]
        up = Vector((0, 0, 1))
        side = up.cross(normal).normalized()
        up = normal.cross(side).normalized()
        frame = Matrix((side, up, normal)).transposed().to_4x4()
        for size, offset, lift in (((w, h, d), d / 2 - 0.004, 0.0), ((w * 1.08, h * 0.28, d * 0.6), d * 0.85, h * 0.4)):
            place = Matrix.Translation(hit[0] + normal * offset + up * lift) @ frame
            box = bmesh.ops.create_cube(bm, size=1.0, matrix=place @ Matrix.Diagonal((*size, 1.0)))
            for f in {f for v in box["verts"] for f in v.link_faces}:
                f.smooth = False
    bm.from_mesh(me)
    bm.to_mesh(me)
    bm.free()


def build():
    bm, covered, surface = G.shell(
        {"Belly", "Pelvis", "Thigh", "Calf"},
        [((0, 0, WAIST_Z), (0, 0, 1), None), ((0, 0, HEM_Z), (0, 0, -1), None)],
        OFFSET)
    edge = G.edge_distance(bm)

    def baggy(bone):
        def factor(v, t):
            head, tail = G.bone_axis(G.side_bone(v.co, bone))
            inner = G.smoothstep((abs(head.x + (tail.x - head.x) * t) - abs(v.co.x)) / 0.05)
            return 1.0 + BAGGY * (1.0 - INNER_HUG * inner) * G.smoothstep(edge.get(v, 1.0) / GATHER)
        return factor

    knee_z = G.bone_axis("LeftLeg")[0].z
    G.limb_scale(bm, "LeftUpLeg", baggy("LeftUpLeg"), select=lambda v: knee_z < v.co.z < 0.86 and abs(v.co.x) > 0.015)
    G.limb_scale(bm, "LeftLeg", baggy("LeftLeg"), select=lambda v: v.co.z <= knee_z)
    obj = G.finalize(bm, NAME, [("M_Bottom_CargoPants", BLACK)], SHELL_TRIS,
                     cover=[surface.co[i] for i in covered])
    _pockets(obj)
    obj[cosmetic.COVERED_PROP] = covered
    return obj


def run(out_dir):
    obj = build()
    return [cosmetic.finish(obj, out_dir, "Bottom", G.material_info(obj))]
