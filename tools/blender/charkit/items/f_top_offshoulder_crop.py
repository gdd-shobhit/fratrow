"""Blue off-shoulder puff-sleeve crop top (characters1, blonde).

Shell grown from the chest, belly and upper arms; straight neckline across the
chest and upper arms, cropped above the navel, puffed sleeves gathered at the
cuff with a small bow on each.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from .. import config as C
from .. import cosmetic

NAME = "SKM_F_Top_OffShoulderCrop"
SLOT = "Top"
MATERIAL = "M_Top_OffShoulderCrop"
COLOR = (0.45, 0.58, 0.95)   # periwinkle, recolourable in Unity

NECKLINE_Z = 1.265    # above the armpits (lower splits sleeves from bodice), below the collarbones
HEM_Z = 1.10          # cropped above the navel
SLEEVE_END = 0.6      # cuff position along the upper arm (0 shoulder .. 1 elbow)
OFFSET = 0.008        # shell distance from the skin
RELAX_ITERATIONS = 25
PUFF = 0.55           # extra sleeve radius at the middle of the puff
EDGE_GATHER = 0.03    # puff fades to nothing this close to an open edge
MAX_SLEEVE_RADIUS = 0.07  # sanity cap: farther from the arm axis than this is not sleeve
THICKNESS = 0.004
MIN_GAP = 0.003       # inner layer never closer to the skin than this
SHELL_TRIS = 520      # before solidify; the finished item is ~2.3x this
BOW_SIZE = 0.016


def _arm(side):
    bone = bpy.data.objects[C.ARMATURE].data.bones[f"{side}Arm"]
    return bone.head_local.copy(), bone.tail_local.copy()


def _shell():
    bm = cosmetic.body_bmesh({"Chest", "Belly", "UpperArm"})
    cosmetic.cut(bm, Vector((0, 0, NECKLINE_Z)), Vector((0, 0, 1)))
    cosmetic.cut(bm, Vector((0, 0, HEM_Z)), Vector((0, 0, -1)))
    for side, sign in (("Left", 1), ("Right", -1)):
        head, tail = _arm(side)
        along = (tail - head).normalized()
        cuff = head.lerp(tail, SLEEVE_END)
        # Only the arm's own geometry: the cuff plane would also slice the torso.
        on_arm = (lambda s: lambda g: all(v.co.x * s > 0.13 for v in
                  (g.verts if hasattr(g, "verts") else [g])))(sign)
        cosmetic.cut(bm, cuff, along, geom_filter=on_arm)
    cosmetic.keep_largest_islands(bm, min_faces=20)
    return bm


def _edge_distance(bm):
    """Approximate distance of each vertex to the nearest open edge (hem, neckline, cuff)."""
    boundary = [v.co.copy() for v in bm.verts if v.is_boundary]
    return {v: min((v.co - b).length for b in boundary) for v in bm.verts}


def _puff(bm, surface):
    """Balloon each sleeve between the neckline and the cuff.

    Only vertices over upper-arm skin are puffed (not the torso side next to
    the armpit, which sits far from the arm axis and would be flung outward).
    The puff fades out within EDGE_GATHER of every open edge, so the neckline
    and cuff hug the arm like elastic (and there is no gap to see into).
    """
    edge = _edge_distance(bm)
    on_arm = {v for v in bm.verts if surface.regions[surface.bvh.find_nearest(v.co)[2]] == "UpperArm"}
    for side, sign in (("Left", 1), ("Right", -1)):
        head, tail = _arm(side)
        axis = tail - head
        start = (head.z - NECKLINE_Z) / (head.z - tail.z)  # where the neckline crosses the arm
        for v in bm.verts:
            if v not in on_arm or v.co.x * sign <= 0:
                continue
            t = (v.co - head).dot(axis) / axis.length_squared
            u = (t - start) / (SLEEVE_END - start)
            if not 0.0 < u < 1.0:
                continue
            foot = head + axis * t
            radial = v.co - foot
            if radial.length > MAX_SLEEVE_RADIUS:
                continue
            gather = min(edge[v] / EDGE_GATHER, 1.0)
            gather = gather * gather * (3 - 2 * gather)
            v.co = foot + radial * (1.0 + PUFF * gather * math.sin(math.pi * u) ** 0.8)
    bm.normal_update()


def _bows(material_index):
    """A small bow on the outer side of each cuff (knot + two loops)."""
    body_surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])
    bm = bmesh.new()
    for side, sign in (("Left", 1), ("Right", -1)):
        head, tail = _arm(side)
        axis = (tail - head).normalized()
        cuff = head.lerp(tail, SLEEVE_END)
        outward = Vector((sign, 0, 0))
        outward = (outward - axis * outward.dot(axis)).normalized()
        _, _, skin, _, _ = body_surface.nearest(cuff + outward * 0.1)
        centre = skin + outward * (OFFSET + THICKNESS + 0.002)
        frame = Matrix((axis.cross(outward), axis, outward)).transposed().to_4x4()
        place = Matrix.Translation(centre) @ frame
        s = BOW_SIZE
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=s * 0.35, matrix=place)
        for lr in (-1, 1):
            loop = Matrix.Translation((lr * s * 0.75, 0, 0)) @ Matrix.Scale(0.45, 4, (0, 0, 1))
            bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=4, radius=s * 0.6,
                                      matrix=place @ loop @ Matrix.Scale(0.55, 4, (0, 1, 0)))
    for f in bm.faces:
        f.material_index = material_index
        f.smooth = True
    return bm


def build():
    cosmetic.remove_item(NAME)
    surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])
    mat = cosmetic.material(MATERIAL, COLOR, roughness=0.7)
    bm = _shell()
    covered = cosmetic.covered_body_vertices(bm)
    cosmetic.relax(bm, surface, OFFSET, RELAX_ITERATIONS)
    _puff(bm, surface)
    obj = cosmetic.to_object(bm, NAME, mat)
    obj[cosmetic.COVERED_PROP] = covered
    cosmetic.decimate(obj, SHELL_TRIS)
    cosmetic.solidify(obj, THICKNESS)
    cosmetic.ensure_clearance(obj, MIN_GAP)
    bows = _bows(0)
    bows.from_mesh(obj.data)
    bows.to_mesh(obj.data)
    bows.free()
    obj.data.shade_smooth()
    return obj


def run(out_dir):
    obj = build()
    cosmetic.bind_to_body(obj)
    body = bpy.data.objects[C.BODY]
    hidden = cosmetic.skin_mask(obj)
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    path = cosmetic.export(NAME, [obj], out_dir, {
        "slot": SLOT,
        "kind": "skinned",
        "hiddenBodyVertices": cosmetic.encode_mask(hidden, len(body.data.vertices)),
        "hiddenBodyVertexCount": len(hidden),
        "materials": [{"name": MATERIAL, "color": list(COLOR), "recolorable": True}],
    })
    print(f"[{NAME}] {tris} tris, hides {len(hidden)} body vertices -> {path}")
    return [path]
