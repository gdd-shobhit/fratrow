"""Long orange-red high ponytail with bangs and a big red bow (characters1, redhead).

The ponytail is a tapered tube from the crown, arcing back and hanging down the
back, kept clear of the body. Its lower half blends from the head to the upper
spine so it does not stay glued to the head when she turns.
"""
import math

import bmesh
from mathutils import Vector

from .. import cosmetic, shapes
from . import _garment as G
from . import _hair as H

NAME = "SKM_F_Hair_HighPonytail"
HAIRLINE_Z = 1.556   # at the brows: the cap's front edge is the bangs
OFFSET = 0.007
VOLUME = 0.01
CAP_TRIS = 700
BASE_DIRECTION = (0.0, 0.55, 0.85)   # crown, slightly back
TAIL_END_Z = 1.06                    # hangs to about the waist
TAIL_RADIUS = (0.042, 0.052, 0.05, 0.045, 0.04, 0.034, 0.028, 0.022, 0.016, 0.01, 0.004)
BACK_GAP = 0.02
BOW_SIZE = 0.065
BANGS = dict(depth=0.014, count=4, spread=0.9)   # soft points along the front edge
ORANGE = (0.95, 0.45, 0.17)
BOW_RED = (0.82, 0.08, 0.13)


def _tail_points(base, normal, surface):
    """Up and back out of the crown, then down behind the back, clear of the body."""
    n = len(TAIL_RADIUS)
    pts = []
    for k in range(n):
        t = k / (n - 1)
        rise = 0.05 * math.sin(math.pi * min(t * 2.5, 1.0))
        back = 0.07 * math.sin(math.pi * min(t * 1.2, 1.0) / 2) + 0.02 * t
        z = base.z + rise - (base.z - TAIL_END_Z) * t ** 1.3
        sway = 0.012 * math.sin(t * 5.0)
        pts.append(Vector((base.x + sway, base.y + 0.01 + back, z)))
    pts[0] = base - normal * 0.01
    for _ in range(4):
        pts = [pts[0]] + [surface.push_out(p, r + BACK_GAP) for p, r in zip(pts[1:], TAIL_RADIUS[1:])]
        pts = [pts[0]] + [(a + b * 2 + c) / 4 for a, b, c in zip(pts, pts[1:], pts[2:])] + [pts[-1]]
    return pts


def build():
    bm, covered, surface = H.cap(HAIRLINE_Z, OFFSET)
    H.inflate(bm, VOLUME)
    base, normal = H.surface_point(bm, Vector(BASE_DIRECTION))
    H.scallop(bm, surface, gap=OFFSET, **BANGS)
    shapes.tube(bm, _tail_points(base, normal, surface), list(TAIL_RADIUS), segments=8)
    up = Vector((0, 0, 1))
    shapes.bow(bm, base + normal * 0.022, up.cross(normal).cross(normal).normalized() * -1, normal,
               BOW_SIZE, material_index=1)
    obj = G.finalize(bm, NAME, [("M_Hair_HighPonytail", ORANGE), ("M_Hair_HighPonytail_Bow", BOW_RED)],
                     CAP_TRIS * 3, thickness=0.004, rim_only=True)
    obj[cosmetic.COVERED_PROP] = covered
    return obj


def run(out_dir):
    obj = build()
    return [H.finish(obj, out_dir, G.material_info(obj), H.head_weights(blend_to="Spine2"))]
