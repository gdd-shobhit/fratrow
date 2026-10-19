"""Cosmetic items: build, fit, bind, mask and export garments and accessories.

Skinned items (tops, bottoms, shoes, hair) start as a shell copied from the
master body, cut with planes for clean hems, then relaxed like cloth: smoothed,
but never closer to the skin than an offset, so they bridge hollows (cleavage,
armpits) the way fabric does. They take the body's bone weights and every body
shape key from the skin under each vertex, so they follow animation and the
body sliders.

Each item also gets a skin mask: the body vertices it truly covers (rays cast
outward from the skin, minus a one-ring margin at the item's edges). Unity hides
body triangles whose three vertices are all masked, so skin never pokes through
and covered skin costs nothing. Body vertices are identified by the
BodyVertexId UV channel (body_prepare.add_vertex_ids), which survives Unity's
vertex reordering.

Rigid items (earrings, glasses, bags) are authored where they sit on the rest-pose
body; Unity attaches them to a Socket_* bone keeping that relative placement.

Every item exports as <name>.fbx plus <name>.cosmetic.json, which Unity's
importer turns into a CosmeticItem asset (Assets/Art/Characters/Editor).
"""
import base64
import hashlib
import json
import os
from collections import defaultdict

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

from . import config as C
from .bind import _attach, limit_and_normalize

COLLECTION = "Cosmetics_Female"
SIDECAR_SCHEMA = 1
SOURCE_LAYER = "body_vertex"   # bmesh vert layer set by body_bmesh
COVERED_PROP = "charkit_covered"


# --- Body queries -------------------------------------------------------------

def body_topology_hash(body):
    """Stable id of the body's topology; masks are only valid for this topology."""
    h = hashlib.sha1(len(body.data.vertices).to_bytes(4, "little"))
    for poly in body.data.polygons:
        h.update(",".join(map(str, poly.vertices)).encode() + b";")
    return h.hexdigest()[:16]


class BodySurface:
    """The rest-pose master body: nearest-point lookups with barycentric weights."""

    def __init__(self, body):
        mesh = body.data
        keys = mesh.shape_keys.key_blocks if mesh.shape_keys else []
        basis = keys[0] if keys else None
        self.co = [(basis.data[i].co if basis else v.co).copy() for i, v in enumerate(mesh.vertices)]
        self.polys = [tuple(p.vertices) for p in mesh.polygons]
        self.regions = [C.REGIONS[p.material_index] for p in mesh.polygons]
        self.bvh = BVHTree.FromPolygons(self.co, self.polys)
        names = {g.index: g.name for g in body.vertex_groups}
        self.weights = [{names[g.group]: g.weight for g in v.groups} for v in mesh.vertices]
        self.key_deltas = {k.name: [k.data[i].co - self.co[i] for i in range(len(self.co))]
                           for k in keys[1:]}
        self.normals = [v.normal.copy() for v in mesh.vertices]

    def nearest(self, p):
        """(polygon vertices, barycentric weights, surface point, normal, distance)."""
        loc, normal, index, dist = self.bvh.find_nearest(p)
        verts = self.polys[index]
        bary = poly_3d_calc([self.co[v] for v in verts], loc)
        return verts, bary, loc, normal, dist

    def push_out(self, p, offset):
        """p moved outward so it is at least `offset` from the skin."""
        _, _, loc, normal, _ = self.nearest(p)
        if (p - loc).dot(normal) < offset:
            return loc + normal * offset
        return p


# --- Shell construction -------------------------------------------------------

def _collection():
    coll = bpy.data.collections.get(COLLECTION)
    if coll is None:
        coll = bpy.data.collections.new(COLLECTION)
        bpy.context.scene.collection.children.link(coll)
    return coll


def remove_item(name):
    for obj in [o for o in bpy.data.objects if o.name == name or o.get("charkit_item") == name]:
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if isinstance(data, bpy.types.Mesh) and data.users == 0:
            bpy.data.meshes.remove(data)


def body_bmesh(regions):
    """A bmesh of the rest body restricted to `regions`, without skin data."""
    body = bpy.data.objects[C.BODY]
    surface = BodySurface(body)
    bm = bmesh.new()
    source = bm.verts.layers.int.new(SOURCE_LAYER)  # body vertex index + 1; 0 = made by a cut
    vmap = {}
    for i, co in enumerate(surface.co):
        vmap[i] = bm.verts.new(co)
        vmap[i][source] = i + 1
    uv_src = body.data.uv_layers.get("UVMap")
    uv = bm.loops.layers.uv.new("UVMap")
    for poly, region in zip(body.data.polygons, surface.regions):
        if region not in regions:
            continue
        face = bm.faces.new([vmap[v] for v in poly.vertices])
        for loop, li in zip(face.loops, poly.loop_indices):
            loop[uv].uv = uv_src.data[li].uv
    for v in [v for v in bm.verts if not v.link_faces]:
        bm.verts.remove(v)
    bm.normal_update()
    return bm


def cut(bm, point, normal, geom_filter=None):
    """Bisect with a plane and delete everything on the +normal side."""
    geom = [e for e in bm.edges] + [f for f in bm.faces] + [v for v in bm.verts]
    if geom_filter:
        geom = [g for g in geom if geom_filter(g)]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=point, plane_no=normal, clear_outer=True)
    for v in [v for v in bm.verts if not v.link_faces]:
        bm.verts.remove(v)


def covered_body_vertices(bm):
    """Body vertices that survived the cuts: exactly the skin this shell was grown from.

    Call after cutting and before relaxing. Any body triangle whose three
    vertices are all in this set lies inside the item's cut lines, so it is
    under fabric no matter how the shell is relaxed or puffed afterwards.

    Vertices made by a cut inherit a neighbour's tag (bmesh interpolates the
    layer), possibly from the far side of the cut, so a tag only counts when the
    vertex still sits exactly on that body vertex.
    """
    body_co = BodySurface(bpy.data.objects[C.BODY]).co
    layer = bm.verts.layers.int.get(SOURCE_LAYER)
    return sorted(v[layer] - 1 for v in bm.verts
                  if v[layer] > 0 and (v.co - body_co[v[layer] - 1]).length < 1e-6)


def keep_largest_islands(bm, min_faces):
    """Drop disconnected scraps smaller than min_faces left over from cutting."""
    seen, islands = set(), []
    for f in bm.faces:
        if f in seen:
            continue
        stack, island = [f], []
        seen.add(f)
        while stack:
            cur = stack.pop()
            island.append(cur)
            for e in cur.edges:
                for nb in e.link_faces:
                    if nb not in seen:
                        seen.add(nb)
                        stack.append(nb)
        islands.append(island)
    doomed = [f for isl in islands if len(isl) < min_faces for f in isl]
    if doomed:
        bmesh.ops.delete(bm, geom=doomed, context="FACES")


def relax(bm, surface, offset, iterations, factor=0.5, pinned=None):
    """Cloth-like relax: Laplacian smoothing, then keep every vertex >= offset off the skin.

    Boundary vertices are smoothed only along the boundary, so hems stay clean
    curves instead of shrinking inward. `pinned` vertices do not move.
    """
    pinned = pinned or set()
    neighbours = {}
    for v in bm.verts:
        if v.is_boundary:
            neighbours[v] = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
        else:
            neighbours[v] = [e.other_vert(v) for e in v.link_edges]
    for v in bm.verts:
        v.co = surface.push_out(v.co + v.normal * offset, offset)
    for _ in range(iterations):
        new = {}
        for v, nbrs in neighbours.items():
            if v in pinned or not nbrs:
                continue
            avg = sum((n.co for n in nbrs), Vector()) / len(nbrs)
            new[v] = surface.push_out(v.co.lerp(avg, factor), offset)
        for v, co in new.items():
            v.co = co
    bm.normal_update()


def to_object(bm, name, material):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(material)
    mesh.shade_smooth()
    obj = bpy.data.objects.new(name, mesh)
    obj["charkit_item"] = name
    _collection().objects.link(obj)
    return obj


def decimate(obj, target_tris):
    """Collapse-decimate a shell (mirrored on X) to roughly target_tris before solidifying."""
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris <= target_tris:
        return tris
    mod = obj.modifiers.new("_decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = target_tris / tris
    mod.use_symmetry = True
    mod.symmetry_axis = "X"
    depsgraph = bpy.context.evaluated_depsgraph_get()
    baked = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    obj.modifiers.remove(mod)
    old = obj.data
    obj.data = baked
    bpy.data.meshes.remove(old)
    baked.name = obj.name
    return sum(len(p.vertices) - 2 for p in baked.polygons)


def solidify(obj, thickness):
    """Give the shell thickness inward (rim included) and bake it into the mesh."""
    mod = obj.modifiers.new("_solidify", "SOLIDIFY")
    mod.thickness = thickness
    mod.offset = -1.0
    mod.use_rim = True
    # Even offset divides by the corner angle and threw a vertex 50 m out on a
    # degenerate face; clamping keeps the inner layer from overshooting.
    mod.use_even_offset = False
    mod.thickness_clamp = 1.0
    depsgraph = bpy.context.evaluated_depsgraph_get()
    baked = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    obj.modifiers.remove(mod)
    old = obj.data
    obj.data = baked
    bpy.data.meshes.remove(old)
    baked.name = obj.name


def ensure_clearance(obj, min_gap):
    """Push any vertex closer than min_gap to the rest skin back out.

    Decimation and solidify can pull the inner layer almost onto the skin
    (0.2 mm was seen at the bust), which then pokes through at any slider value.
    """
    surface = BodySurface(bpy.data.objects[C.BODY])
    moved = 0
    for v in obj.data.vertices:
        p = surface.push_out(v.co, min_gap)
        if p != v.co:
            v.co = p
            moved += 1
    obj.data.update()
    return moved


def material(name, color, roughness=0.6, metallic=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    if mat.node_tree is None:
        mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    mat.diffuse_color = (*color, 1.0)
    return mat


# --- Skinning, sliders, mask --------------------------------------------------

MAX_SKIN_DISTANCE = 0.15  # no vertex of a body-grown item should be farther than this
SLIDER_MIN_GAP = 0.004    # at full slider, keep fabric at least this far off the shaped skin


def bind_to_body(obj):
    """Copy bone weights and every body shape key from the skin under each vertex."""
    body = bpy.data.objects[C.BODY]
    arm = bpy.data.objects[C.ARMATURE]
    surface = BodySurface(body)
    mesh = obj.data
    stray = [v.index for v in mesh.vertices if surface.nearest(v.co)[4] > MAX_SKIN_DISTANCE]
    if stray:
        raise ValueError(f"{obj.name}: {len(stray)} vertices more than {MAX_SKIN_DISTANCE} m "
                         f"from the skin (first: {stray[:5]}); the shell geometry is broken")
    if mesh.shape_keys:
        obj.shape_key_clear()
    obj.vertex_groups.clear()
    groups = {}
    samples = []
    for v in mesh.vertices:
        verts, bary, *_ = surface.nearest(v.co)
        samples.append((verts, bary))
        mixed = defaultdict(float)
        for bv, b in zip(verts, bary):
            for name, w in surface.weights[bv].items():
                mixed[name] += b * w
        for name, w in mixed.items():
            if w > 1e-4:
                group = groups.get(name) or obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
                groups[name] = group
                group.add([v.index], w, "REPLACE")
    limit_and_normalize(obj)
    obj.shape_key_add(name="Basis", from_mix=False)
    rest_gap = [surface.nearest(v.co)[4] for v in mesh.vertices]
    for name in C.SHAPE_KEYS:
        deltas = surface.key_deltas[name]
        key = obj.shape_key_add(name=name, from_mix=False)
        key.slider_min, key.slider_max = 0.0, 1.0
        # Where a slider swells skin that is not straight under a vertex (e.g. the
        # bust under fabric bridging the cleavage), following the nearest skin is
        # not enough: test against the shaped body and push sunken vertices out.
        shaped = [c + d for c, d in zip(surface.co, deltas)]
        shaped_bvh = BVHTree.FromPolygons(shaped, surface.polys)
        for v, (verts, bary) in zip(mesh.vertices, samples):
            p = v.co + sum((deltas[bv] * b for bv, b in zip(verts, bary)), Vector())
            loc, normal, _, _ = shaped_bvh.find_nearest(p)
            keep = min(rest_gap[v.index], SLIDER_MIN_GAP)
            if (p - loc).dot(normal) < keep:
                p = loc + normal * keep
            key.data[v.index].co = p
    for mod in [m for m in obj.modifiers if m.type == "ARMATURE"]:
        obj.modifiers.remove(mod)
    obj.parent = None
    _attach(obj, arm)


def skin_mask(obj, reach=0.025, margin_rings=0):
    """Body vertex indices this item covers (for hiding skin under it).

    A vertex is covered when a ray along its normal hits the item within
    `reach`, i.e. the fabric sits tight over it. Skin under loose fabric (a
    puffed sleeve) stays visible: it can be seen through the gap at the
    fabric's edge, and it cannot poke through anyway.

    Items grown from the body (shell + cuts) also store their exact source
    vertices in obj[COVERED_PROP] (see covered_body_vertices); the mask is then
    limited to those, so rays cannot catch e.g. a sleeve rising over a bare
    shoulder.

    Unity hides a body triangle only when all three of its vertices are
    covered, so triangles crossing an item's edge stay visible; that is the
    margin. `margin_rings` erodes further (one ring is ~4 cm on this body).
    """
    body = bpy.data.objects[C.BODY]
    allowed = set(obj[COVERED_PROP]) if COVERED_PROP in obj else None
    surface = BodySurface(body)
    mesh = obj.data
    keys = mesh.shape_keys.key_blocks if mesh.shape_keys else None
    co = [(keys[0].data[i].co if keys else v.co) for i, v in enumerate(mesh.vertices)]
    item_bvh = BVHTree.FromPolygons(co, [tuple(p.vertices) for p in mesh.polygons])
    covered = set()
    for i, (p, n) in enumerate(zip(surface.co, surface.normals)):
        if allowed is not None and i not in allowed:
            continue
        hit = item_bvh.ray_cast(p + n * 1e-4, n, reach)
        if hit[0] is not None:
            covered.add(i)
    return sorted(_erode(body, covered, margin_rings))


def _erode(body, covered, rings):
    neighbours = defaultdict(set)
    for e in body.data.edges:
        a, b = e.vertices
        neighbours[a].add(b)
        neighbours[b].add(a)
    for _ in range(rings):
        covered = {i for i in covered if neighbours[i] <= covered}
    return covered


def encode_mask(indices, count):
    bits = bytearray((count + 7) // 8)
    for i in indices:
        bits[i >> 3] |= 1 << (i & 7)
    return base64.b64encode(bytes(bits)).decode("ascii")


# --- Export -------------------------------------------------------------------

def export(name, objects, folder, sidecar):
    """Export `objects` with the armature to <folder>/<name>.fbx and write the sidecar."""
    from . import export_fbx
    arm = bpy.data.objects[C.ARMATURE]
    for obj in objects:
        if obj.data.shape_keys:
            for key in obj.data.shape_keys.key_blocks[1:]:
                key.value = 0.0
    path = export_fbx.export([arm, *objects], os.path.join(folder, name + ".fbx"))
    body = bpy.data.objects[C.BODY]
    data = {
        "schema": SIDECAR_SCHEMA,
        "id": name,
        "model": name + ".fbx",
        "body": {"vertexCount": len(body.data.vertices), "topologyHash": body_topology_hash(body)},
        **sidecar,
    }
    with open(os.path.join(folder, name + ".cosmetic.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    return path
