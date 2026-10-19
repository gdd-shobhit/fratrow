"""Build the master female body from the Human Base Meshes source.

Steps: import -> regions -> proportions -> decimate -> height -> eyes -> cleanup.
Each step is a function so it can be re-run on its own while tuning
config.py. Run inside Blender (see tools/blender/charkit/README.md):

    from charkit import body_prepare
    body_prepare.run(hbm_blend=r"...\\human_base_meshes_bundle.blend")
"""
import bmesh
import bpy
from mathutils import Matrix, Vector

from . import config as C


def _collection():
    coll = bpy.data.collections.get(C.COLLECTION)
    if coll is None:
        coll = bpy.data.collections.new(C.COLLECTION)
        bpy.context.scene.collection.children.link(coll)
    return coll


def _remove_object(name):
    obj = bpy.data.objects.get(name)
    if obj is None:
        return
    data = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    if isinstance(data, bpy.types.Mesh) and data.users == 0:
        bpy.data.meshes.remove(data)


def _material(name, color, roughness):
    """Get or create a Principled material with the given base colour."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    if mat.node_tree is None:
        mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    mat.diffuse_color = (*color, 1.0)
    return mat


def _bbox(points):
    lo = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return lo, hi


def _tris(mesh):
    return sum(len(p.vertices) - 2 for p in mesh.polygons)


def import_source(hbm_blend):
    """Append the CC0 body and eyes, bake transforms, centre on X/Y, feet on Z=0."""
    for name in (C.BODY, C.EYES, C.HBM_BODY, *C.HBM_EYES):
        _remove_object(name)
    with bpy.data.libraries.load(hbm_blend, link=False) as (_src, dst):
        dst.objects = [C.HBM_BODY, *C.HBM_EYES]
    coll = _collection()
    for obj in dst.objects:
        coll.objects.link(obj)
        obj.parent = None
    body = bpy.data.objects[C.HBM_BODY]
    eyes = [bpy.data.objects[n] for n in C.HBM_EYES]
    for eye in eyes:
        eye.location.x += C.HBM_EYE_OFFSET_X
    bpy.context.view_layer.update()  # appended objects keep a stale matrix_world until this
    for obj in (body, *eyes):
        obj.data.transform(obj.matrix_world)
        obj.matrix_world = Matrix.Identity(4)
    lo, hi = _bbox([v.co for v in body.data.vertices])
    to_origin = Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
    for obj in (body, *eyes):
        obj.data.transform(to_origin)
    body.name = body.data.name = C.BODY
    return body, eyes


def assign_regions(body):
    """One material slot per body region, from the source's sculpt face sets."""
    mesh = body.data
    # Read face sets before touching materials: that invalidates attribute refs.
    face_sets = [0] * len(mesh.polygons)
    mesh.attributes[".sculpt_face_set"].data.foreach_get("value", face_sets)
    # Keep a copy under a public name: decimation and cleanup drop the sculpt
    # one, and skeleton.py places joints on the seams between face sets.
    keep = mesh.attributes.get(C.FACE_SET_ATTR) or mesh.attributes.new(C.FACE_SET_ATTR, "INT", "FACE")
    keep.data.foreach_set("value", face_sets)
    mesh.materials.clear()
    for region in C.REGIONS:
        mesh.materials.append(_material(C.REGION_PREFIX + region, C.SKIN_TONE, 0.55))
    by_face_set = {fs: region for region, sets in C.REGION_FACE_SETS.items() for fs in sets}
    slot = {region: i for i, region in enumerate(C.REGIONS)}
    unknown = set()
    for poly in mesh.polygons:
        region = by_face_set.get(face_sets[poly.index])
        if region is None:
            unknown.add(face_sets[poly.index])
            continue
        if region == "Pelvis" and poly.center.z < C.PELVIS_THIGH_SPLIT_Z:
            region = "Thigh"
        poly.material_index = slot[region]
    if unknown:
        raise ValueError(f"face sets with no region in config.REGION_FACE_SETS: {sorted(unknown)}")
    counts = {r: 0 for r in C.REGIONS}
    for poly in mesh.polygons:
        counts[C.REGIONS[poly.material_index]] += 1
    return counts


def _smoothstep(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def _proportion(co, rigid_lift):
    """Map a source-space point through the leg stretch and head scale."""
    x, y, z = co
    z0, z1 = C.LEG_STRETCH_Z
    lift = (z1 - z0) * (C.LEG_STRETCH - 1.0)
    if rigid_lift or z >= z1:
        nz = z + lift
    elif z > z0:
        nz = z0 + (z - z0) * C.LEG_STRETCH
    else:
        nz = z
    t = _smoothstep((z - C.HEAD_BLEND_Z[0]) / (C.HEAD_BLEND_Z[1] - C.HEAD_BLEND_Z[0]))
    if t > 0.0:
        s = 1.0 + (C.HEAD_SCALE - 1.0) * t
        px, py, pz = C.HEAD_PIVOT
        pz += lift
        x, y, nz = px + (x - px) * s, py + (y - py) * s, pz + (nz - pz) * s
    return Vector((x, y, nz))


def proportion_pass(body, eyes):
    """Smaller head, longer legs. Arms move rigidly so hands keep their shape."""
    mesh = body.data
    arm_slots = {C.REGIONS.index(r) for r in C.ARM_REGIONS}
    arm_verts = {v for p in mesh.polygons if p.material_index in arm_slots for v in p.vertices}
    for v in mesh.vertices:
        v.co = _proportion(v.co, v.index in arm_verts)
    for eye in eyes:
        for v in eye.data.vertices:
            v.co = _proportion(v.co, False)
    mesh.update()


def _region_tris(mesh, slots):
    return sum(len(p.vertices) - 2 for p in mesh.polygons if p.material_index in slots)


def _decimate_slots(body, slots, target):
    """Collapse only the vertices whose faces all lie in `slots`, to `target` tris there.

    The Decimate vertex group is an on/off mask (not a soft weight) and its
    ratio is global, so the ratio is computed from the whole mesh. Border
    vertices are left out of the mask, so neighbouring regions never tear.
    """
    mesh = body.data
    inside = [True] * len(mesh.vertices)
    touched = [False] * len(mesh.vertices)
    for poly in mesh.polygons:
        in_group = poly.material_index in slots
        for v in poly.vertices:
            touched[v] = touched[v] or in_group
            inside[v] = inside[v] and in_group
    group = body.vertex_groups.new(name="_decimate_mask")
    group.add([i for i in range(len(mesh.vertices)) if inside[i] and touched[i]], 1.0, "REPLACE")
    total, current = _tris(mesh), _region_tris(mesh, slots)
    mod = body.modifiers.new("_decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = min(1.0, (total - current + target) / total)
    mod.use_symmetry = True
    mod.symmetry_axis = "X"
    mod.vertex_group = group.name
    depsgraph = bpy.context.evaluated_depsgraph_get()
    reduced = bpy.data.meshes.new_from_object(body.evaluated_get(depsgraph))
    body.modifiers.remove(mod)
    old = body.data
    body.data = reduced
    bpy.data.meshes.remove(old)
    body.vertex_groups.remove(body.vertex_groups[group.name])
    reduced.name = C.BODY


def decimate(body):
    """Collapse-decimate each region group to its budget (config.DECIMATE_BUDGETS)."""
    listed = {r for regions, _ in C.DECIMATE_BUDGETS for r in regions if r != "*"}
    report = {}
    for regions, target in C.DECIMATE_BUDGETS:
        names = [r for r in C.REGIONS if r not in listed] if regions == ("*",) else regions
        slots = {C.REGIONS.index(r) for r in names}
        _decimate_slots(body, slots, target)
        report["+".join(regions)] = _region_tris(body.data, slots)
    return _tris(body.data), report


def scale_to_height(body, eyes, height=None):
    height = height or C.TARGET_HEIGHT
    top = max(v.co.z for v in body.data.vertices)
    scale = Matrix.Scale(height / top, 4)
    for obj in (body, *eyes):
        obj.data.transform(scale)
    return height / top


def build_eyes(source_eyes):
    """Replace the dense source eyes with one low-poly mesh, pupils facing -Y.

    UVs are a front projection (pupil at 0.5, 0.5) so an iris texture can be
    added later without re-unwrapping.
    """
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    face_forward = Matrix.Rotation(1.5707963, 4, "X")  # +Z pole -> -Y
    for src in source_eyes:
        lo, hi = _bbox([v.co for v in src.data.vertices])
        centre = (lo + hi) / 2
        radius = max(hi.x - lo.x, hi.z - lo.z) / 2
        made = bmesh.ops.create_uvsphere(bm, u_segments=C.EYE_SEGMENTS, v_segments=C.EYE_RINGS,
                                         radius=radius, matrix=Matrix.Translation(centre) @ face_forward)
        new_verts = set(made["verts"])
        for face in {f for v in new_verts for f in v.link_faces}:
            for loop in face.loops:
                d = loop.vert.co - centre
                loop[uv_layer].uv = (0.5 + d.x / (2 * radius), 0.5 + d.z / (2 * radius))
    for src in source_eyes:
        _remove_object(src.name)
    mesh = bpy.data.meshes.new(C.EYES)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(_material(C.EYE_MATERIAL, C.EYE_COLOR, 0.2))
    mesh.shade_smooth()
    eyes = bpy.data.objects.new(C.EYES, mesh)
    _collection().objects.link(eyes)
    return eyes


def cleanup(body):
    """Drop sculpt-only data the game never needs; keep UVs and seams."""
    mesh = body.data
    for name in [a.name for a in mesh.attributes]:
        if name in (".sculpt_mask", ".sculpt_face_set", "crease_edge", "crease_vert"):
            mesh.attributes.remove(mesh.attributes[name])
    mesh.shade_smooth()


def run(hbm_blend=None):
    hbm_blend = hbm_blend or C.HBM_BLEND
    if not hbm_blend:
        raise ValueError("pass hbm_blend= or set CHARKIT_HBM_BLEND to the bundle .blend")
    body, source_eyes = import_source(hbm_blend)
    regions = assign_regions(body)
    proportion_pass(body, source_eyes)
    tris, budgets = decimate(body)
    scale = scale_to_height(body, source_eyes)
    eyes = build_eyes(source_eyes)
    cleanup(body)
    print(f"[body_prepare] body {tris} tris {budgets}, eyes {_tris(eyes.data)} tris, height scale {scale:.4f}")
    print(f"[body_prepare] source faces per region: {regions}")
    return body, eyes
