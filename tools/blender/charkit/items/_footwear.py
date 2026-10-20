"""Shared builder for shoes and boots.

Each foot: convex hull of the foot and lower leg up to the shoe's top, voxel
remeshed, shrunk onto the foot (so the toe box bridges the toes), opened at
the top, flat sole with its own material. Both feet go into one mesh.
"""
import bmesh
import bpy
from mathutils import Vector

from .. import config as C
from .. import cosmetic, shapes

FOOT_REGIONS = {"Feet", "Calf"}


RIM_MARGIN = 0.04  # skin within this of the opening stays visible (no hole when looking in)


def build(name, top_z, offset, sole_height, sole_flare, upper, sole, shell_tris=2000,
          voxel=0.02, thickness=0.004, min_gap=0.003, toe_lift=0.0):
    """Returns the shoe object (both feet) with obj[COVERED_PROP] set."""
    cosmetic.remove_item(name)
    body = bpy.data.objects[C.BODY]
    surface = cosmetic.BodySurface(body)
    in_region = [False] * len(surface.co)
    for poly, region in zip(surface.polys, surface.regions):
        if region in FOOT_REGIONS:
            for v in poly:
                in_region[v] = True

    combined = bmesh.new()
    covered = []
    for sign in (1, -1):
        pts = [c for i, c in enumerate(surface.co)
               if in_region[i] and c.x * sign > 0.0 and c.z < top_z + 0.02]
        covered += [i for i, c in enumerate(surface.co)
                    if in_region[i] and c.x * sign > 0.0 and c.z < top_z - RIM_MARGIN]
        foot = shapes.hull_shell(pts, voxel)
        cosmetic.shrink(foot, surface, offset, iterations=18, pull=0.35, smooth_iterations=20)
        inside = [p for p in pts if p.z < top_z - 0.01]  # not the leg above the opening
        cosmetic.cover_points(foot, inside, gap=0.004)  # toe tips must not poke through
        cosmetic.cut(foot, Vector((0, 0, top_z)), Vector((0, 0, 1)))
        centre = sum((v.co for v in foot.verts), Vector()) / len(foot.verts)
        for v in foot.verts:
            if v.co.z < sole_height:
                # Flat floor and a slightly wider sole rim.
                k = 1.0 + (sole_flare - 1.0) * (1.0 - max(v.co.z, 0.0) / sole_height)
                v.co.x = centre.x + (v.co.x - centre.x) * k
                v.co.y = centre.y + (v.co.y - centre.y) * k
                v.co.z = max(v.co.z, -0.003)
            if toe_lift and v.co.y < centre.y - 0.06 and v.co.z < 0.02:
                v.co.z += toe_lift * (centre.y - 0.06 - v.co.y) / 0.08
        cosmetic.add_edge_loop(foot, sole_height)  # straight sole/upper colour line
        mesh = bpy.data.meshes.new("_foot")
        foot.to_mesh(mesh)
        foot.free()
        combined.from_mesh(mesh)
        bpy.data.meshes.remove(mesh)

    mats = [cosmetic.material(f"M_{name[4:]}_Upper", upper, roughness=0.55),
            cosmetic.material(f"M_{name[4:]}_Sole", sole, roughness=0.8)]
    obj = cosmetic.to_object(combined, name, mats)
    cosmetic.decimate(obj, shell_tris)  # a no-op unless the coarse remesh overshoots
    cosmetic.solidify(obj, thickness, rim_only=True)
    cosmetic.ensure_clearance(obj, min_gap)
    cosmetic.assign_materials(obj, lambda c, n: 1 if c.z < sole_height else 0)
    obj.data.shade_smooth()
    obj[cosmetic.COVERED_PROP] = sorted(set(covered))
    return obj


def run(name, out_dir, upper, sole, **kwargs):
    obj = build(name, upper=upper, sole=sole, **kwargs)
    mats = [(m.name, cosmetic.material_srgb(m), True) for m in obj.data.materials]
    return [cosmetic.finish(obj, out_dir, "Shoes", mats)]
