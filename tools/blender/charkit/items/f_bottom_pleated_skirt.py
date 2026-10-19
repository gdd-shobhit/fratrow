"""White pleated mini skirt (characters1, blonde).

Procedural, not grown from the skin: a skirt hangs across both legs. Each ring
follows the body's convex outline at its height (spanning both thighs below the
crotch), flares toward the hem and alternates in/out for the pleats. The
waistband takes the skin's weights; lower down the skirt blends between the hips
and whichever leg it is over, so it swings with a stride without tearing.
"""
import math

import bmesh
import bpy
from mathutils import Vector

from .. import config as C
from .. import cosmetic, shapes
from . import _garment as G

NAME = "SKM_F_Bottom_PleatedSkirt"
TOP_Z = 1.00
HEM_Z = 0.70
SEGMENTS = 36        # pleat faces (alternating in/out)
RINGS = 8
OFFSET = 0.009
FLARE = 0.075        # extra radius at the hem
PLEAT = (0.004, 0.012)  # pleat depth at the waist, at the hem
LEG_FOLLOW = 0.55    # at the hem, how much the skirt follows the leg under it (rest: hips)
WHITE = (0.97, 0.97, 0.96)
ARMS = ("UpperArm", "Forearm", "Hand")


def _rings(surface):
    centre = Vector((0.0, sum(p.y for p in shapes.section(surface, TOP_Z, ARMS)) /
                     max(1, len(shapes.section(surface, TOP_Z, ARMS))), 0.0))
    rings = []
    for r in range(RINGS + 1):
        depth = r / RINGS
        z = TOP_Z + (HEM_Z - TOP_Z) * depth
        pts = shapes.section(surface, z, ARMS)
        ring = []
        for j in range(SEGMENTS):
            a = 2 * math.pi * j / SEGMENTS
            d = Vector((math.cos(a), math.sin(a)))
            base = shapes.support(pts, centre, d) + OFFSET
            pleat = (PLEAT[0] + (PLEAT[1] - PLEAT[0]) * depth) * (1 if j % 2 == 0 else -1)
            radius = base + FLARE * depth ** 1.3 + pleat
            ring.append(Vector((centre.x + d.x * radius, centre.y + d.y * radius, z)))
        rings.append(ring)
    return rings


def _weights(surface):
    def at(co):
        depth = G.smoothstep((TOP_Z - co.z) / (TOP_Z - HEM_Z))
        skin = cosmetic.skin_weights_at(surface, co)
        left = G.smoothstep(0.5 + co.x / 0.12)
        k = LEG_FOLLOW * depth
        legs = {"Hips": 1.0 - k, "LeftUpLeg": k * left, "RightUpLeg": k * (1.0 - left)}
        blend = depth ** 0.7
        out = {b: w * (1.0 - blend) for b, w in skin.items()}
        for b, w in legs.items():
            out[b] = out.get(b, 0.0) + w * blend
        return out
    return at


def build():
    surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])
    bm = bmesh.new()
    rows = [[bm.verts.new(p) for p in ring] for ring in _rings(surface)]
    for upper, lower in zip(rows, rows[1:]):
        for j in range(SEGMENTS):
            k = (j + 1) % SEGMENTS
            f = bm.faces.new((upper[j], lower[j], lower[k], upper[k]))
            f.smooth = False  # crisp pleats
    bm.normal_update()
    for f in bm.faces:
        if (f.calc_center_median() - Vector((0, 0, f.calc_center_median().z))).dot(f.normal) < 0:
            f.normal_flip()
    obj = G.finalize(bm, NAME, [("M_Bottom_PleatedSkirt", WHITE)], shell_tris=10_000, thickness=0.003)
    for p in obj.data.polygons:
        p.use_smooth = False
    # Skin under the waistband/hips only: below that the skirt hangs loose.
    co = surface.co
    obj[cosmetic.COVERED_PROP] = [i for i, c in enumerate(co) if HEM_Z + 0.05 < c.z < TOP_Z - 0.01
                                  and abs(c.x) < 0.25]
    return obj, surface


def run(out_dir):
    obj, surface = build()
    return [cosmetic.finish(obj, out_dir, "Bottom", G.material_info(obj), weights_at=_weights(surface))]
