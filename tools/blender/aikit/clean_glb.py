"""Save the lean, cleaned copy of a generated model that the repo keeps.

Raw downloads stay out of git (LFS is disabled on this repository); this GLB
is what rebuilds read: decimated meshes, resized textures embedded, no Draco
(Blender and Unity both read plain glTF without extra decoders).

    from aikit import clean_glb
    clean_glb.save([obj], r"ArtSource/AI/Chair/Chair_clean.glb")
"""
import os

import bpy

from . import meshutil


def save(objects, path):
    """Export exactly `objects` to `path` as a GLB with embedded textures; returns the path."""
    meshutil.object_mode()
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    view_layer = bpy.context.view_layer
    for obj in view_layer.objects:
        obj.select_set(False)
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
    view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(filepath=os.path.abspath(path), export_format="GLB", use_selection=True,
                              export_apply=True, export_yup=True,
                              export_draco_mesh_compression_enable=False)
    return path
