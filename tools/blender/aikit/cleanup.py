"""Clean raw generator meshes: join parts, merge duplicates, drop floaters, fix normals.

Generators duplicate vertices along UV seams, leave stray specks around the
model (floaters) and sometimes flip faces. clean() fixes those in place and
returns what it changed, so a run can be compared with the last one.

    from aikit import cleanup
    obj = cleanup.join(meshes, "Chair")
    print(cleanup.clean(obj))
"""
import bmesh
import bpy
import numpy as np

from . import meshutil

MERGE_DISTANCE = 1e-4     # seam duplicates sit exactly on top of each other
MIN_ISLAND_FACES = 20     # loose parts smaller than this are floaters


def join(objects, name):
    """Join `objects` into one mesh object called `name`, transforms baked, at identity."""
    meshutil.object_mode()
    objects = list(objects)
    if not objects:
        raise ValueError(f"{name}: nothing to join")
    for obj in objects:
        meshutil.apply_transform(obj)
    target = objects[0]
    if len(objects) > 1:
        with bpy.context.temp_override(active_object=target, object=target,
                                       selected_objects=objects, selected_editable_objects=objects):
            bpy.ops.object.join()
    target.name = name
    target.data.name = name
    return target


def _delete_faces(me, doomed):
    """Delete faces by index (numpy bool mask) and any vertices left without faces."""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.flatnonzero(doomed)], context="FACES")
    bm.to_mesh(me)
    bm.free()


def _drop_unused_materials(obj):
    me = obj.data
    if len(me.materials) <= 1:
        return 0
    index = np.empty(len(me.polygons), np.int64)
    me.polygons.foreach_get("material_index", index)
    used = np.unique(index)
    if len(used) == len(me.materials):
        return 0
    keep = [me.materials[i] for i in used]
    remap = np.zeros(len(me.materials), np.int64)
    remap[used] = np.arange(len(used))
    removed = len(me.materials) - len(keep)
    me.materials.clear()
    for mat in keep:
        me.materials.append(mat)
    me.polygons.foreach_set("material_index", remap[index].astype(np.int32))
    return removed


def clean(obj, merge_distance=MERGE_DISTANCE, min_island_faces=MIN_ISLAND_FACES):
    """Merge by distance, delete loose geometry and floaters, recalc normals outward.

    Also drops material slots no face uses. Returns counts of what changed
    (observations; nothing here judges the model).
    """
    meshutil.object_mode()
    meshutil.apply_transform(obj)
    me = obj.data
    verts_before, faces_before = len(me.vertices), len(me.polygons)

    bm = bmesh.new()
    bm.from_mesh(me)
    count = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=merge_distance)
    merged = count - len(bm.verts)
    loose_edges = [e for e in bm.edges if not e.link_faces]
    bmesh.ops.delete(bm, geom=loose_edges, context="EDGES")
    loose_verts = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose_verts, context="VERTS")
    bm.to_mesh(me)
    bm.free()

    labels, parts = meshutil.face_components(me)
    sizes = np.bincount(labels, minlength=parts) if parts else np.zeros(0, np.int64)
    small = sizes < min_island_faces
    # Never delete the whole model: a tiny accessory can be one small island.
    if parts and small.all():
        small[np.argmax(sizes)] = False
    doomed = small[labels] if parts else np.zeros(0, bool)
    if doomed.any():
        _delete_faces(me, doomed)

    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    me.update()
    return {
        "merged_vertices": merged,
        "loose_edges": len(loose_edges),
        "loose_vertices": len(loose_verts),
        "floaters_removed": int(small.sum()) if parts else 0,
        "floater_faces_removed": int(doomed.sum()),
        "unused_materials_removed": _drop_unused_materials(obj),
        "vertices": (verts_before, len(me.vertices)),
        "faces": (faces_before, len(me.polygons)),
    }
