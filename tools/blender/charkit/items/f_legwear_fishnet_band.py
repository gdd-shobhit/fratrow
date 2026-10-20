"""Black fishnet showing at the waist between crop top and cargo pants (characters1, black bob).

A diamond net of thin ribbons hugging the waist; it hides no skin.
"""
import math

import bmesh
import bpy
from mathutils import Vector

from .. import config as C
from .. import cosmetic, shapes
from . import _garment as G

NAME = "SKM_F_Legwear_FishnetBand"
BOTTOM_Z = 0.982     # starts just above the cargo waistband (0.975)
TOP_Z = 1.105        # tucked under the crop top's hem (1.10)
ROWS = 10
SEGMENTS = 26
OFFSET = 0.0025
WIDTH = 0.0016
BLACK = (0.05, 0.05, 0.06)
ARMS = ("UpperArm", "Forearm", "Hand")


def build():
    cosmetic.remove_item(NAME)
    surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])
    mid = shapes.section(surface, (BOTTOM_Z + TOP_Z) / 2, ARMS)
    centre = Vector((0.0, sum(p.y for p in mid) / len(mid), 0.0))
    rings = []
    for r in range(ROWS + 1):
        z = BOTTOM_Z + (TOP_Z - BOTTOM_Z) * r / ROWS
        pts = shapes.section(surface, z, ARMS)
        ring = []
        for j in range(SEGMENTS):
            a = 2 * math.pi * (j + 0.5 * r) / SEGMENTS  # half a step further each row: diamonds
            d = Vector((math.cos(a), math.sin(a)))
            radius = shapes.support(pts, centre, d) + OFFSET
            ring.append((Vector((centre.x + d.x * radius, centre.y + d.y * radius, z)), Vector((d.x, d.y, 0.0))))
        rings.append(ring)
    bm = bmesh.new()
    shapes.ribbon_net(bm, rings, WIDTH)
    obj = cosmetic.to_object(bm, NAME, cosmetic.material("M_Legwear_Fishnet", BLACK, roughness=0.6))
    return obj


def run(out_dir):
    obj = build()
    return [cosmetic.finish(obj, out_dir, "Legwear", G.material_info(obj), mask=False)]
