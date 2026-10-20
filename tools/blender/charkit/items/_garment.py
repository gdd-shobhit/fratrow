"""Shared steps for garments grown from the body (tops, pants).

shell -> item-specific shaping -> finalize -> cosmetic.finish. See
f_top_offshoulder_crop.py for the same steps written out by hand.
"""
import math

import bpy
from mathutils import Vector

from .. import config as C
from .. import cosmetic


def bone_axis(name):
    bone = bpy.data.objects[C.ARMATURE].data.bones[name]
    return bone.head_local.copy(), bone.tail_local.copy()


def side_bone(co, name):
    """The Left/Right variant of a bone name for the side co is on."""
    return name if co.x >= 0 else name.replace("Left", "Right")


def smoothstep(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def shell(regions, cuts, offset, relax_iterations=20, exclude_face_sets=(), min_island=20):
    """Body shell with plane cuts, relaxed. Returns (bm, covered body vertices, surface).

    cuts: [(point, normal, geom_filter or None)] — everything on +normal is removed.
    """
    surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])
    bm = cosmetic.body_bmesh(regions, exclude_face_sets)
    for point, normal, geom_filter in cuts:
        cosmetic.cut(bm, Vector(point), Vector(normal), geom_filter=geom_filter)
    cosmetic.keep_largest_islands(bm, min_faces=min_island)
    covered = cosmetic.covered_body_vertices(bm)
    cosmetic.relax(bm, surface, offset, relax_iterations)
    return bm, covered, surface


def edge_distance(bm):
    """Approximate distance of each vertex to the nearest open edge."""
    boundary = [v.co.copy() for v in bm.verts if v.is_boundary]
    return {v: min((v.co - b).length for b in boundary) for v in bm.verts} if boundary else {}


def limb_scale(bm, bone, factor_at, select=lambda v: True):
    """Scale vertices radially away from a limb bone's axis (both sides, mirrored).

    factor_at(v, t) -> multiplier, with t the parameter along the bone (may be
    outside 0..1 beyond the joints).
    """
    for v in bm.verts:
        if not select(v):
            continue
        head, tail = bone_axis(side_bone(v.co, bone))
        axis = tail - head
        t = (v.co - head).dot(axis) / axis.length_squared
        foot = head + axis * t
        v.co = foot + (v.co - foot) * factor_at(v, t)
    bm.normal_update()


def finalize(bm, name, materials, shell_tris, thickness=0.003, min_gap=0.003, cover=None, cover_gap=0.004,
             rim_only=False):
    """bmesh -> object: decimate, no-poke-through, solidify, clearance.

    materials: [(name, colour)]. cover: body points that must stay under the
    garment (e.g. the covered skin) — after decimation the shell is coarser than
    the skin and bust/elbow points would otherwise poke between its vertices.
    """
    cosmetic.remove_item(name)
    mats = [cosmetic.material(n, c, roughness=0.75) for n, c in materials]
    obj = cosmetic.to_object(bm, name, mats)
    cosmetic.decimate(obj, shell_tris)
    if cover:
        import bmesh as _bmesh
        shell_bm = _bmesh.new()
        shell_bm.from_mesh(obj.data)
        cosmetic.cover_points(shell_bm, cover, gap=cover_gap)
        shell_bm.to_mesh(obj.data)
        shell_bm.free()
    cosmetic.solidify(obj, thickness, rim_only=rim_only)
    cosmetic.ensure_clearance(obj, min_gap)
    obj.data.shade_smooth()
    return obj


def material_info(obj, recolorable=True):
    return [(m.name, cosmetic.material_srgb(m), recolorable) for m in obj.data.materials]
