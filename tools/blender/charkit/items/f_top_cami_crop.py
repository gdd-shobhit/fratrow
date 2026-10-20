"""Red spaghetti-strap cami crop top (characters1, redhead)."""
import bmesh
import bpy
from mathutils import Vector

from .. import config as C
from .. import cosmetic, shapes
from . import _garment as G

NAME = "SKM_F_Top_CamiCrop"
NECKLINE_Z = 1.222   # straight across, under the arms
HEM_Z = 1.09
OFFSET = 0.007
SHELL_TRIS = 420
STRAP_X = 0.075      # strap position either side of the centre line
STRAP_SHOULDER_X = 0.095  # where the strap crosses the top of the shoulder
STRAP_RADIUS = 0.0028
STRAP_LIFT = 0.004   # strap distance over the skin
RED = (0.85, 0.12, 0.15)


def _strap(bm, surface, sign):
    """A thin tube from the front neckline over the shoulder to the back neckline."""
    neck_base = G.bone_axis("Neck")[0].z
    shoulder_top = max((c for c in surface.co if abs(c.x - sign * STRAP_SHOULDER_X) < 0.015
                        and abs(c.y - 0.02) < 0.035 and c.z < neck_base),  # not the head/ears above
                       key=lambda c: c.z)
    z = NECKLINE_Z - 0.006
    # Horizontal rays at the neckline height, so both ends meet the top edge exactly.
    front = surface.bvh.ray_cast(Vector((sign * STRAP_X, -0.4, z)), Vector((0, 1, 0)), 1.0)[0]
    back = surface.bvh.ray_cast(Vector((sign * STRAP_X, 0.4, z)), Vector((0, -1, 0)), 1.0)[0]
    # Quadratic Bezier front -> over the shoulder -> back, then only push points
    # that are inside or too close to the skin (projecting every point can flip
    # it to the wrong side of the curved shoulder).
    control = 2 * shoulder_top - (front + back) / 2 + Vector((0, 0, 0.015))
    gap = STRAP_LIFT + STRAP_RADIUS
    pts = []
    for k in range(17):
        t = k / 16
        p = (1 - t) ** 2 * front + 2 * (1 - t) * t * control + t ** 2 * back
        pts.append(surface.push_out(p, gap))
    shapes.tube(bm, pts, [STRAP_RADIUS] * len(pts), segments=5)


def build():
    torso = lambda g: all(abs(v.co.x) < 0.2 for v in (g.verts if hasattr(g, "verts") else [g]))
    bm, covered, surface = G.shell(
        {"Chest", "Belly"},
        [((0, 0, NECKLINE_Z), (0, 0, 1), torso), ((0, 0, HEM_Z), (0, 0, -1), torso)],
        OFFSET)
    obj = G.finalize(bm, NAME, [("M_Top_CamiCrop", RED)], SHELL_TRIS,
                     cover=[surface.co[i] for i in covered])
    straps = bmesh.new()
    for sign in (1, -1):
        _strap(straps, surface, sign)
    straps.from_mesh(obj.data)
    straps.to_mesh(obj.data)
    straps.free()
    obj.data.shade_smooth()
    obj[cosmetic.COVERED_PROP] = covered
    return obj


def run(out_dir):
    obj = build()
    return [cosmetic.finish(obj, out_dir, "Top", G.material_info(obj))]
