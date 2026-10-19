"""Black blunt chin-length bob with bangs (characters1, black bob).

Grown from the head and neck skin (face, front of the neck and ears cut away,
ear holes filled), then every vertex is moved onto a smooth skull-fitted
ellipsoid that hangs straight below the ears, plus volume flaring toward the
blunt chin-length cut. The ears end up inside the hair.
"""
from mathutils import Vector

from .. import config as C
from .. import cosmetic
from . import _garment as G
from . import _hair as H

NAME = "SKM_F_Hair_Bob"
FACE_PLANE = ((0.0, -0.085, 1.55), (0.0, -0.97, -0.25))   # point, normal: cuts the cheeks away (below the brows)
CHIN_PLANE = ((0.0, -0.03, 1.44), (0.0, -0.6, -0.8))      # clears the front of the neck
BANGS_Z = 1.552      # blunt bangs: the forehead shell is cut straight across at the brows
BOTTOM_Z = 1.40      # blunt cut at chin height
FRONT_HUG = 0.7      # how much the front of the hair stays near the cheeks (no flicks)
OFFSET = 0.008
VOLUME_TOP = 0.016
VOLUME_EARS = 0.034  # extra at ear height so the shell clears the ears by itself
VOLUME_BOTTOM = 0.03
CAP_TRIS = 900
EAR_X = 0.06
BLACK = (0.1, 0.1, 0.12)


def _skull(surface, centre):
    """Semi-axes of an axis-aligned ellipsoid around the skull (ears excluded)."""
    import bpy
    sets = [0] * len(bpy.data.objects[C.BODY].data.polygons)
    bpy.data.objects[C.BODY].data.attributes[C.FACE_SET_ATTR].data.foreach_get("value", sets)
    pts = {v for poly, fs, region in zip(surface.polys, sets, surface.regions)
           if region == "Head" and fs not in H.EARS for v in poly}
    co = [surface.co[i] for i in pts if surface.co[i].z > centre.z - 0.02]
    rx = max(abs(c.x - centre.x) for c in co)
    ry = max(abs(c.y - centre.y) for c in co)
    rz = max(c.z - centre.z for c in co)
    return rx, ry, rz


def build():
    surface = cosmetic.BodySurface(__import__("bpy").data.objects[C.BODY])
    bm = cosmetic.body_bmesh({"Head", "Neck"}, H.FACE + H.EARS)
    below_brows = lambda g: all(v.co.z < BANGS_Z + 0.004 for v in (g.verts if hasattr(g, "verts") else [g]))
    front = lambda g: all(v.co.y < -0.04 for v in (g.verts if hasattr(g, "verts") else [g]))
    cosmetic.cut(bm, Vector(FACE_PLANE[0]), Vector(FACE_PLANE[1]), geom_filter=below_brows)
    cosmetic.cut(bm, Vector((0, 0, BANGS_Z)), Vector((0, 0, -1)), geom_filter=front)
    for point, normal in (CHIN_PLANE, ((0, 0, BOTTOM_Z), (0, 0, -1))):
        cosmetic.cut(bm, Vector(point), Vector(normal))
    cosmetic.keep_largest_islands(bm, min_faces=20)
    covered = cosmetic.covered_body_vertices(bm)
    H.fill_holes_near(bm, lambda c: abs(c.x) > EAR_X and c.z > BOTTOM_Z + 0.03)
    centre = H.head_centre()
    rx, ry, rz = _skull(surface, centre)
    edge = G.edge_distance(bm)
    for v in bm.verts:
        d = v.co - centre
        if v.co.z < centre.z:
            d.z = 0.0  # below the ears the bob hangs straight down
        if d.length < 1e-6:
            continue
        n = d.normalized()
        r = 1.0 / ((n.x / rx) ** 2 + (n.y / ry) ** 2 + (n.z / rz) ** 2) ** 0.5
        depth = G.smoothstep((centre.z + rz - v.co.z) / (centre.z + rz - BOTTOM_Z))
        ears = max(0.0, 1.0 - abs(v.co.z - 1.495) / 0.07) * G.smoothstep((abs(v.co.x) - 0.03) / 0.04)
        volume = VOLUME_TOP + (VOLUME_BOTTOM - VOLUME_TOP) * depth + (VOLUME_EARS - VOLUME_TOP) * ears
        target = Vector((centre.x, centre.y, v.co.z if v.co.z < centre.z else centre.z)) + n * (r + volume)
        # Below the bangs, the front of the hair stays near the cheeks (no flicks at the jaw).
        hug = FRONT_HUG * G.smoothstep((-v.co.y - 0.02) / 0.04) * (1.0 if v.co.z < BANGS_Z else 0.0)
        skin_side = surface.push_out(v.co, OFFSET + 0.006)
        v.co = surface.push_out(target.lerp(skin_side, hug), OFFSET)
    bm.normal_update()
    H.smooth_edges(bm, surface, iterations=8)
    ears = [surface.co[i] for i, c in enumerate(surface.co) if abs(c.x) > 0.07 and 1.43 < c.z < 1.56
            and -0.02 < c.y < 0.06]
    obj = G.finalize(bm, NAME, [("M_Hair_Bob", BLACK)], CAP_TRIS * 3, thickness=0.004, rim_only=True,
                     cover=ears, cover_gap=0.006)
    # Ears sit inside the hair: hide them with the scalp.
    ear_vertices = [i for i, c in enumerate(surface.co) if abs(c.x) > 0.07 and 1.42 < c.z < 1.57]
    obj[cosmetic.COVERED_PROP] = sorted(set(covered) | set(ear_vertices))
    return obj


def run(out_dir):
    obj = build()
    return [H.finish(obj, out_dir, G.material_info(obj), H.head_weights())]
