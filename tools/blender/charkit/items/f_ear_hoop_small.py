"""Small gold hoop earrings (characters1, blonde). Rigid: one hoop per ear socket."""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from .. import config as C
from .. import cosmetic

NAME = "SM_F_Ear_HoopSmall"
SLOT = "Ears"
MATERIAL = "M_Metal_Gold"
COLOR = (0.93, 0.74, 0.36)
RADIUS = 0.010
WIRE = 0.0016


def build():
    cosmetic.remove_item(NAME)
    mat = cosmetic.material(MATERIAL, COLOR, roughness=0.3, metallic=1.0)
    arm = bpy.data.objects[C.ARMATURE]
    objects = []
    for side, suffix in (("Left", "L"), ("Right", "R")):
        lobe = arm.data.bones[f"Socket_{side}Ear"].head_local
        # Hoop hangs from the lobe, flat against the side of the head (axis along X).
        place = Matrix.Translation(lobe - Vector((0, 0, RADIUS))) @ Matrix.Rotation(math.pi / 2, 4, "Y")
        objects.append(_torus(f"{NAME}_{suffix}", place, mat))
    return objects


def _torus(name, place, mat):
    """Torus built point by point: 14 x 5 segments."""
    major, minor, nu, nv = RADIUS, WIRE, 14, 5
    bm = bmesh.new()
    rings = []
    for i in range(nu):
        a = 2 * math.pi * i / nu
        centre = Vector((math.cos(a) * major, math.sin(a) * major, 0))
        out = Vector((math.cos(a), math.sin(a), 0))
        ring = []
        for j in range(nv):
            b = 2 * math.pi * j / nv
            p = centre + out * (math.cos(b) * minor) + Vector((0, 0, math.sin(b) * minor))
            ring.append(bm.verts.new(place @ p))
        rings.append(ring)
    for i in range(nu):
        for j in range(nv):
            a, b = rings[i][j], rings[(i + 1) % nu][j]
            c, d = rings[(i + 1) % nu][(j + 1) % nv], rings[i][(j + 1) % nv]
            bm.faces.new((a, b, c, d))
    bm.normal_update()
    obj = cosmetic.to_object(bm, name, mat)
    obj["charkit_item"] = NAME
    return obj


def run(out_dir):
    objects = build()
    path = cosmetic.export(NAME, objects, out_dir, {
        "slot": SLOT,
        "kind": "rigid",
        "attachments": [
            {"node": f"{NAME}_L", "socket": "Socket_LeftEar"},
            {"node": f"{NAME}_R", "socket": "Socket_RightEar"},
        ],
        "materials": [{"name": MATERIAL, "color": list(COLOR), "recolorable": False}],
    })
    print(f"[{NAME}] {sum(len(o.data.polygons) * 2 for o in objects)} tris -> {path}")
    return [path]
