"""Import an AI-generated model (GLB/GLTF/FBX/OBJ) and keep only its meshes.

Generators wrap the mesh in node hierarchies (empties carrying the scale and
an axis fix), sometimes add cameras or lights, and rigged outputs bring an
armature. import_model() deletes everything but the meshes (the armature too,
unless asked to keep it) and clears parenting so each mesh keeps its world
transform at the scene root. glTF is +Y up; Blender's importer converts it to
Z up, so nothing is rotated here.

    from aikit import importer
    meshes = importer.import_model(r"ArtSource/AI/Chair/raw/chair.glb")
"""
import os

import bpy

from . import meshutil

# Object types an import may bring in that a mesh pipeline never wants.
DISCARD_TYPES = {"EMPTY", "CAMERA", "LIGHT", "LIGHT_PROBE", "SPEAKER", "CURVE", "FONT"}

# bpy.data collections whose orphaned datablocks are freed after discarding objects.
DATA_COLLECTIONS = ("cameras", "lights", "armatures", "curves", "lightprobes", "speakers")

# File extension -> import operator (called with filepath=...) and its options.
IMPORTERS = {
    ".glb": ("import_scene.gltf", {"merge_vertices": False}),   # cleanup.clean merges
    ".gltf": ("import_scene.gltf", {"merge_vertices": False}),
    ".fbx": ("import_scene.fbx", {}),
    ".obj": ("wm.obj_import", {}),
}


def _operator(path_name):
    group, name = path_name.split(".")
    return getattr(getattr(bpy.ops, group), name)


def _remove_data(data):
    """Free an orphaned camera/light/armature/... datablock left by a deleted object."""
    for attr in DATA_COLLECTIONS:
        collection = getattr(bpy.data, attr)
        if collection.get(data.name) == data:
            collection.remove(data)
            return


def import_model(path, keep_armature=False):
    """Import `path` and return the mesh objects it created.

    Empties, cameras, lights (and armatures unless keep_armature) brought in
    by the file are deleted; meshes are unparented keeping their world
    transforms. Without the armature, Armature modifiers and animation data
    are removed too, so the meshes stay in their rest shape. With
    keep_armature the armature objects stay in the scene, unparented; find
    them through the meshes' Armature modifiers.
    """
    path = os.path.abspath(path)
    ext = os.path.splitext(path)[1].lower()
    if ext not in IMPORTERS:
        raise ValueError(f"{path}: unsupported format {ext!r}; expected one of {sorted(IMPORTERS)}")
    if not os.path.isfile(path):
        raise FileNotFoundError(path)
    meshutil.object_mode()

    before = set(bpy.data.objects)
    op_name, options = IMPORTERS[ext]
    result = _operator(op_name)(filepath=path, **options)
    if "FINISHED" not in result:
        raise RuntimeError(f"{path}: {op_name} returned {result}")
    new = [o for o in bpy.data.objects if o not in before]

    # Unparent everything, with world transforms captured before any change.
    bpy.context.view_layer.update()
    world = {o: o.matrix_world.copy() for o in new}
    for obj in new:
        if obj.parent is not None:
            obj.parent = None
            obj.matrix_parent_inverse.identity()
    for obj, matrix in world.items():
        obj.matrix_world = matrix

    discard = set(DISCARD_TYPES)
    if not keep_armature:
        discard.add("ARMATURE")
    meshes = []
    for obj in new:
        if obj.type in discard:
            data = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if data is not None and data.users == 0:
                _remove_data(data)
        elif obj.type == "MESH":
            meshes.append(obj)
    if not keep_armature:
        for obj in meshes:
            for mod in [m for m in obj.modifiers if m.type == "ARMATURE"]:
                obj.modifiers.remove(mod)
            obj.animation_data_clear()
    bpy.context.view_layer.update()
    tris = sum(meshutil.tri_count(o) for o in meshes)
    print(f"[aikit.importer] {os.path.basename(path)}: {len(new)} objects imported, "
          f"{len(meshes)} meshes kept, {tris} tris")
    return meshes
