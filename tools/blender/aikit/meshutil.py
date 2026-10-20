"""Mesh helpers shared by the aikit modules: counts, transforms, connectivity.

Everything here reads meshes through foreach_get into numpy, so it stays fast
on raw generator output (0.5-2M triangles), where per-element Python loops
take minutes.
"""
import bpy
import numpy as np
from mathutils import Matrix


def object_mode():
    """Leave edit/sculpt/paint mode; most mesh data writes need object mode."""
    obj = bpy.context.object
    if obj is not None and obj.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")


def mesh_of(obj_or_mesh):
    """The Mesh datablock of a mesh object (or the mesh itself)."""
    if isinstance(obj_or_mesh, bpy.types.Object):
        if obj_or_mesh.type != "MESH":
            raise TypeError(f"{obj_or_mesh.name} is a {obj_or_mesh.type}, not a mesh")
        return obj_or_mesh.data
    return obj_or_mesh


def tri_count(obj_or_mesh):
    """Triangles the mesh renders as (an n-gon counts n - 2)."""
    me = mesh_of(obj_or_mesh)
    totals = np.empty(len(me.polygons), np.int64)
    me.polygons.foreach_get("loop_total", totals)
    return int((totals - 2).sum())


def edge_vertices(me):
    """(n, 2) vertex indices of every edge."""
    arr = np.empty(len(me.edges) * 2, np.int64)
    me.edges.foreach_get("vertices", arr)
    return arr.reshape(-1, 2)


def corner_arrays(me):
    """(vertex index, edge index) of every face corner, plus each face's loop_start."""
    verts = np.empty(len(me.loops), np.int64)
    edges = np.empty(len(me.loops), np.int64)
    me.loops.foreach_get("vertex_index", verts)
    me.loops.foreach_get("edge_index", edges)
    starts = np.empty(len(me.polygons), np.int64)
    me.polygons.foreach_get("loop_start", starts)
    return verts, edges, starts


def vertex_positions(obj, world=True):
    """(n, 3) vertex positions, in world space by default."""
    me = mesh_of(obj)
    co = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    if world and isinstance(obj, bpy.types.Object):
        m = np.array(obj.matrix_world, np.float64)
        co = co @ m[:3, :3].T + m[:3, 3]
    return co


def bounds(obj, world=True):
    """(min xyz, max xyz) as numpy arrays; zeros for an empty mesh."""
    co = vertex_positions(obj, world)
    if len(co) == 0:
        return np.zeros(3), np.zeros(3)
    return co.min(axis=0), co.max(axis=0)


def _components(n, a, b):
    """Connected components of n nodes joined by edges (a[i], b[i]).

    Union-find in numpy: hook each edge's larger root onto the smaller one,
    then pointer-jump until every node points at its root; repeat until no
    edge spans two roots. Returns (label per node, component count).
    """
    parent = np.arange(n, dtype=np.int64)
    if n == 0:
        return parent, 0
    while len(a):
        pa, pb = parent[a], parent[b]
        lo, hi = np.minimum(pa, pb), np.maximum(pa, pb)
        spanning = lo != hi
        if not spanning.any():
            break
        np.minimum.at(parent, hi[spanning], lo[spanning])
        while True:
            jumped = parent[parent]
            if np.array_equal(jumped, parent):
                break
            parent = jumped
    roots, labels = np.unique(parent, return_inverse=True)
    return labels, len(roots)


def vertex_components(obj_or_mesh):
    """(label per vertex, count) of parts connected through edges."""
    me = mesh_of(obj_or_mesh)
    ev = edge_vertices(me)
    return _components(len(me.vertices), ev[:, 0], ev[:, 1])


def face_components(obj_or_mesh):
    """(label per face, count) of loose parts; faces sharing a vertex are one part."""
    me = mesh_of(obj_or_mesh)
    labels, _ = vertex_components(me)
    verts, _, starts = corner_arrays(me)
    if len(starts) == 0:
        return np.zeros(0, np.int64), 0
    face_labels = labels[verts[starts]]
    uniq, face_labels = np.unique(face_labels, return_inverse=True)
    return face_labels, len(uniq)


def apply_transform(obj):
    """Bake the object's world transform into its mesh; the object ends at identity.

    Clears the parent first (keeping the world transform). A negative scale
    would turn the mesh inside out, so faces are flipped back in that case.
    """
    me = mesh_of(obj)
    world = obj.matrix_world.copy()
    if obj.parent is not None:
        obj.parent = None
    if world == Matrix.Identity(4):
        obj.matrix_world = world
        return obj
    if me.users > 1:
        obj.data = me = me.copy()
    me.transform(world, shape_keys=True)
    if world.determinant() < 0:
        me.flip_normals()
    obj.matrix_world = Matrix.Identity(4)
    me.update()
    return obj


def replace_mesh(obj, new_mesh):
    """Give obj new_mesh (keeping obj's mesh name) and free the old mesh if unused."""
    old = obj.data
    name = old.name
    obj.data = new_mesh
    if old.users == 0:
        bpy.data.meshes.remove(old)
    new_mesh.name = name
    return obj
