"""Bring a mesh down to a triangle budget without losing its UVs.

Collapse decimation keeps UV coordinates (it interpolates them), so the
generator's texture still fits afterwards: for static props this replaces
retopology and rebaking. A collapse ratio lands near, not on, the target, so
a second pass trims any overshoot.

    from aikit import reduce
    reduce.decimate_to(obj, 4000)
"""
import bpy

from . import meshutil

TOLERANCE = 1.02   # accept up to 2% over the budget before a second pass


def _collapse(obj, ratio, symmetry):
    mod = obj.modifiers.new("_aikit_decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = max(1e-4, min(1.0, ratio))
    mod.use_collapse_triangulate = True
    if symmetry:
        mod.use_symmetry = True
        mod.symmetry_axis = "X"
    depsgraph = bpy.context.evaluated_depsgraph_get()
    baked = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    obj.modifiers.remove(mod)
    meshutil.replace_mesh(obj, baked)


def decimate_to(obj, target_tris, symmetry=False):
    """Collapse-decimate `obj` in place to about `target_tris`; returns the resulting count.

    symmetry keeps the mesh mirrored across X (the character's left/right).
    Meshes already under budget are left untouched. Shape keys do not survive.
    """
    meshutil.object_mode()
    meshutil.apply_transform(obj)
    for _ in range(2):
        tris = meshutil.tri_count(obj)
        if tris <= target_tris * TOLERANCE:
            break
        _collapse(obj, target_tris / tris, symmetry)
    return meshutil.tri_count(obj)
