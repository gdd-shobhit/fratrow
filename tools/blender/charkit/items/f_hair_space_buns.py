"""Blonde space buns with two loose face-framing strands (characters1, blonde)."""
import math

import bmesh
from mathutils import Matrix, Vector

from .. import cosmetic, shapes
from . import _garment as G
from . import _hair as H

NAME = "SKM_F_Hair_SpaceBuns"
HAIRLINE_Z = 1.585
OFFSET = 0.007
VOLUME = 0.012       # cap puffed out on top, fading to the hairline
CAP_TRIS = 700
BUN_RADIUS = 0.042
BUN_DIRECTION = (0.55, 0.15, 1.0)   # from the head centre, per side (x mirrored)
STRAND_FROM = (0.78, -0.55, 0.05)   # temple direction from the head centre
STRAND_LENGTH = 0.15
STRAND_RADII = (0.0075, 0.007, 0.0062, 0.0054, 0.0045, 0.0034, 0.0025)
STRAND_CURL = 0.009   # sideways S-curve of the strand
BLONDE = (0.97, 0.84, 0.5)


def _bun(bm, centre, radius):
    """A twisted bun: UV sphere with a spiral groove."""
    made = bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=7, radius=radius,
                                     matrix=Matrix.Translation(centre))
    for v in made["verts"]:
        d = v.co - centre
        angle = math.atan2(d.y, d.x)
        height = d.z / radius
        groove = 0.08 * math.sin(3 * angle + 5 * height)
        v.co = centre + d * (1.0 + groove)
    for f in {f for v in made["verts"] for f in v.link_faces}:
        f.smooth = True


def _strand(bm, surface, sign):
    """A loose lock hanging from the temple: a smooth curve kept off the face."""
    start, _ = H.surface_point(bm, Vector((STRAND_FROM[0] * sign, STRAND_FROM[1], STRAND_FROM[2])))
    n = len(STRAND_RADII)
    pts = [start + Vector((sign * (0.004 * t + STRAND_CURL * math.sin(math.pi * t * 1.5)), -0.006 * t,
                           -STRAND_LENGTH * t))
           for t in (k / (n - 1) for k in range(n))]
    for _ in range(4):
        pts = [surface.push_out(p, OFFSET * 0.5 + r + 0.001) for p, r in zip(pts, STRAND_RADII)]
        pts = [pts[0]] + [(a + b * 2 + c) / 4 for a, b, c in zip(pts, pts[1:], pts[2:])] + [pts[-1]]
    shapes.tube(bm, pts, list(STRAND_RADII), segments=6)


def build():
    bm, covered, surface = H.cap(HAIRLINE_Z, OFFSET)
    H.inflate(bm, VOLUME)
    for sign in (1, -1):
        d = Vector((BUN_DIRECTION[0] * sign, BUN_DIRECTION[1], BUN_DIRECTION[2]))
        hit, normal = H.surface_point(bm, d)
        _bun(bm, hit + normal * BUN_RADIUS * 0.55, BUN_RADIUS)
        _strand(bm, surface, sign)
    obj = G.finalize(bm, NAME, [("M_Hair_SpaceBuns", BLONDE)], CAP_TRIS * 3, thickness=0.004, rim_only=True)
    obj[cosmetic.COVERED_PROP] = covered
    return obj


def run(out_dir):
    obj = build()
    return [H.finish(obj, out_dir, G.material_info(obj), H.head_weights())]
