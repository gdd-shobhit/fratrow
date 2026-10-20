"""Cosmetics from generated meshes (Tripo and other image-to-3D tools).

Hand-built items (items/f_top_offshoulder_crop.py) grow a garment out of the
master body. Generated items start from someone else's mesh, so the work runs
the other way: bring that mesh into the body's space, make it behave like a
charkit shell, then reuse the same bind, slider, mask and export steps as every
other item. Rigging never comes from the generator. An item takes SK_Human's
weights and all 16 body sliders from the skin under it, so it fits every body
preset and plays every Humanoid clip (design/art/ai-asset-pipeline.md).

Two stages:

1. prepare(src, asset): once per download. Imports the raw model
   (ArtSource/AI/<asset>/raw/, gitignored), cleans every part, fits the whole
   model onto the rest-pose body and saves the lean, fitted result as
   ArtSource/AI/<asset>/<asset>_clean.glb (committed). Generate from the body
   reference sheet (reference_sheet) so the model's body matches ours and the
   fit has bare skin to lock onto.
2. One item module per cosmetic (copy items/_generated_template.py) calls
   skinned_item / hair_item / rigid_item on named parts of that clean file. It
   rebuilds from the repo alone, like the hand-built items.

    import sys; sys.path.insert(0, r"<repo>/tools/blender")
    from charkit import ingest
    ingest.reference_sheet()                        # -> ArtSource/AI/_reference/
    ingest.prepare(r".../raw/model.glb", "F_Top_Corset")
"""
import math
import os
from collections import Counter

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

from aikit import cleanup, clean_glb, importer, meshutil, reduce, report, textures

from . import config as C
from . import cosmetic, review
from .bind import _attach, limit_and_normalize
from .export_fbx import REPO

AI_ROOT = os.path.join(REPO, "ArtSource", "AI")
REFERENCE_DIR = os.path.join(AI_ROOT, "_reference")

# --- prepare() ----------------------------------------------------------------
CLEAN_TRIS_CAP = 20000      # per part in the committed clean file (keeps it lean)
CLEAN_TEXTURE_SIZE = 1024
FIT_ITERATIONS = 200
FIT_SAMPLES = 4000          # points sampled per side (model and body) per fit iteration
FIT_KEEP = 0.4              # closest share of pairs trusted (bare skin, not fabric or hair)
FIT_TOLERANCE = 1e-6        # stop when an iteration moves less than this (m / scale / rad)

# --- items --------------------------------------------------------------------
INNER_DOT = -0.2            # face normal . skin normal below this = wall facing the body
OFFSET = 0.004              # fabric kept at least this far off the rest skin
THICKNESS = 0.003           # solidify depth for single-walled shells
MIN_GAP = 0.003             # same final clearance as hand-built items
SMOOTH_ITERATIONS = 2       # light relax of vertices the conform step moved
HAIR_OFFSET = 0.003


# --- Reference sheet ----------------------------------------------------------

REFERENCE_VIEWS = ("ref_front", "ref_left", "ref_back", "ref_right")


def reference_sheet(out_dir=REFERENCE_DIR, size=1024, preset=None):
    """Level orthographic renders of the bare rest-pose body for image-to-3D input.

    Feed these to the generator (multi-view: front/left/back/right) or paint an
    outfit over them first. A model generated on our own body in our own A-pose
    fits with millimetres to spare; one generated on a guessed body needs the
    conform step to fix centimetres. `left` is the character's left side.
    """
    review.reset_pose()
    review.set_sliders(C.BODY_PRESETS.get(preset, {}) if preset else {})
    objects = [bpy.data.objects[C.BODY], bpy.data.objects[C.EYES]]
    return review.render("SK_Human_Female", out_dir, views=REFERENCE_VIEWS, size=size, objects=objects)


# --- prepare(): raw download -> fitted, lean clean file -----------------------

def _bake(objects, matrix=Matrix.Identity(4)):
    """Apply `matrix` @ each object's world transform to its mesh data."""
    for obj in objects:
        obj.data.transform(matrix @ obj.matrix_world)
        obj.matrix_world = Matrix.Identity(4)
        obj.data.update()


def _world_points(objects):
    return np.array([v.co[:] for obj in objects for v in obj.data.vertices], dtype=np.float64)


def _similarity(src, dst, rotation=True):
    """Least-squares scale s, rotation R, translation t with dst ~ s R src + t (Umeyama)."""
    mu_s, mu_d = src.mean(axis=0), dst.mean(axis=0)
    xs, xd = src - mu_s, dst - mu_d
    var = (xs ** 2).sum() / len(src)
    if rotation:
        u, d, vt = np.linalg.svd(xd.T @ xs / len(src))
        fix = np.eye(3)
        if np.linalg.det(u) * np.linalg.det(vt) < 0:
            fix[2, 2] = -1.0
        rot = u @ fix @ vt
        scale = np.trace(np.diag(d) @ fix) / var
    else:
        rot = np.eye(3)
        scale = (xd * xs).sum() / (len(src) * var)
    return scale, rot, mu_d - scale * rot @ mu_s


def _matrix(scale, rot, t):
    m = Matrix.Identity(4)
    for i in range(3):
        for j in range(3):
            m[i][j] = scale * rot[i, j]
        m[i][3] = t[i]
    return m


def fit_to_body(objects, refine=True, rotation=True):
    """Move, scale (uniformly) and turn `objects` together onto the rest body.

    First the bounding boxes are matched (height, centre, feet on the floor),
    then a trimmed, two-way ICP refines it. Each round pairs sampled model
    points with their nearest skin point *and* sampled skin points with their
    nearest model point, then trusts only the closest FIT_KEEP of the pairs:
    on a dressed model those are the bare face, hands and legs, not fabric or
    hair. The skin-to-model half stops the fit sliding along the body, which
    one-way ICP does for hundreds of rounds. Returns observations: total
    scale, rotation in degrees, residual distances.
    """
    _bake(objects)
    surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])
    body = np.array([c[:] for c in surface.co])
    pts = _world_points(objects)
    lo, hi = pts.min(axis=0), pts.max(axis=0)
    blo, bhi = body.min(axis=0), body.max(axis=0)
    scale = (bhi[2] - blo[2]) / (hi[2] - lo[2])
    anchor = np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]])
    target = np.array([(blo[0] + bhi[0]) / 2, (blo[1] + bhi[1]) / 2, blo[2]])
    total = _matrix(scale, np.eye(3), target - scale * anchor)
    pts = pts * scale + (target - scale * anchor)

    step = max(1, len(pts) // FIT_SAMPLES)
    skin = body[::max(1, len(body) // FIT_SAMPLES)]
    rounds, kept_dist = 0, np.zeros(0)
    for rounds in range(1, (FIT_ITERATIONS if refine else 0) + 1):
        sample = pts[::step]
        tree = KDTree(len(sample))
        for i, p in enumerate(sample):
            tree.insert(p, i)
        tree.balance()
        near = np.array([surface.bvh.find_nearest(Vector(p))[0][:] for p in sample])
        back = np.array([sample[tree.find(p)[1]] for p in skin])
        src = np.concatenate([sample, back])
        dst = np.concatenate([near, skin])
        dist = np.linalg.norm(src - dst, axis=1)
        keep = np.argsort(dist)[:max(8, int(len(src) * FIT_KEEP))]
        kept_dist = dist[keep]
        s, rot, t = _similarity(src[keep], dst[keep], rotation)
        pts = (s * (rot @ pts.T)).T + t
        total = _matrix(s, rot, t) @ total
        angle = math.acos(max(-1.0, min(1.0, (np.trace(rot) - 1) / 2)))
        if abs(s - 1) < FIT_TOLERANCE and angle < FIT_TOLERANCE and np.linalg.norm(t) < FIT_TOLERANCE:
            break
    _bake(objects, total)

    sample = pts[::step]
    all_dist = np.array([surface.bvh.find_nearest(Vector(p))[3] for p in sample])
    rot_part = total.to_3x3().normalized()
    return {
        "scale": round(total.to_scale().x, 5),
        "rotation_deg": round(math.degrees(rot_part.to_quaternion().angle), 3),
        "icp_rounds": rounds,
        "trusted_median_mm": round(float(np.median(kept_dist)) * 1000, 2) if len(kept_dist) else None,
        "all_median_mm": round(float(np.median(all_dist)) * 1000, 2),
        "all_p90_mm": round(float(np.percentile(all_dist, 90)) * 1000, 2),
    }


def part_regions(obj, surface=None, top=3):
    """Body regions under a part, by share of its vertices (helps pick the parts of an item)."""
    surface = surface or cosmetic.BodySurface(bpy.data.objects[C.BODY])
    verts = obj.data.vertices
    step = max(1, len(verts) // 2000)
    counts = Counter(surface.regions[surface.bvh.find_nearest(verts[i].co)[2]]
                     for i in range(0, len(verts), step))
    total = sum(counts.values()) or 1
    return [(region, round(n / total, 2)) for region, n in counts.most_common(top)]


def _name_parts(objects, asset):
    """Deterministic part names, top to bottom then left to right: <asset>_part01..."""
    def key(obj):
        pts = [v.co for v in obj.data.vertices]
        centre = sum(pts, Vector()) / max(1, len(pts))
        return (-round(centre.z, 2), round(centre.x, 2))
    for i, obj in enumerate(sorted(objects, key=key), 1):
        obj.name = obj.data.name = f"{asset}_part{i:02d}"
    return sorted(objects, key=lambda o: o.name)


def prepare(src, asset, rotate_z=0.0, refine=True, rotation=True, tris_cap=CLEAN_TRIS_CAP,
            texture_size=CLEAN_TEXTURE_SIZE):
    """Raw generated model -> ArtSource/AI/<asset>/<asset>_clean.glb, fitted to the body.

    rotate_z turns the raw model about Z (degrees) before fitting, for a model
    that does not face -Y. Prints and returns per-part observations (tris,
    body regions) so the item modules can name the parts they use. Nothing is
    judged here: read the residuals and the review renders.
    """
    folder = os.path.join(AI_ROOT, asset)
    objects = importer.import_model(src)
    _bake(objects, Matrix.Rotation(math.radians(rotate_z), 4, "Z"))
    cleaned = {o.name: cleanup.clean(o) for o in objects}
    objects = [o for o in objects if len(o.data.polygons)]
    fit = fit_to_body(objects, refine=refine, rotation=rotation)
    objects = _name_parts(objects, asset)
    surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])
    parts = {}
    tex_dir = os.path.join(folder, "raw", "_clean_textures")
    for obj in objects:
        raw_tris = report.observe(obj)["tris"]
        tris = reduce.decimate_to(obj, tris_cap) if raw_tris > tris_cap else raw_tris
        textures.extract(obj, tex_dir, obj.name, size=texture_size)
        parts[obj.name] = {"tris": tris, "raw_tris": raw_tris, "regions": part_regions(obj, surface)}
    path = os.path.join(folder, f"{asset}_clean.glb")
    os.makedirs(folder, exist_ok=True)
    clean_glb.save(objects, path)
    renders = review.render(f"{asset}_fit", os.path.join(folder, "raw", "review"),
                            views=("front", "side", "back"), size=700,
                            objects=[bpy.data.objects[C.BODY], *objects])
    # The clean file is the result; the working parts would only clutter the master.
    for obj in objects:
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if data.users == 0:
            bpy.data.meshes.remove(data)
    result = {"clean": path, "fit": fit, "parts": parts, "cleanup": cleaned, "renders": renders}
    print(f"[ingest] {asset}: fit {fit}")
    for name, info in parts.items():
        print(f"[ingest]   {name}: {info['tris']} tris (raw {info['raw_tris']}), over {info['regions']}")
    return result


# --- Items from parts of a clean file -----------------------------------------

def _short(name):
    """SKM_F_Top_Corset -> F_Top_Corset (material M_ and texture T_ names)."""
    return name.split("_", 1)[1]


def _load_parts(source, parts, name):
    """Import `parts` (object names) of a clean file as one object `name` in the cosmetics collection."""
    path = source if os.path.isabs(source) else os.path.join(REPO, source)
    objects = importer.import_model(path)
    wanted = [o for o in objects if o.name.split(".")[0] in parts]
    missing = set(parts) - {o.name.split(".")[0] for o in wanted}
    for obj in objects:
        if obj not in wanted:
            data = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if data.users == 0:
                bpy.data.meshes.remove(data)
    if missing:
        raise ValueError(f"{name}: parts {sorted(missing)} not in {source}")
    # glTF splits vertices at UV seams and flat-shaded edges; weld them again or
    # every UV chart is a separate island to strip_inner and bind_to_body.
    for obj in wanted:
        cleanup.clean(obj)
    obj = cleanup.join(wanted, name)
    for coll in list(obj.users_collection):
        coll.objects.unlink(obj)
    cosmetic._collection().objects.link(obj)
    obj["charkit_item"] = name
    used = {m.name for m in obj.data.materials if m}
    if len(used) > 1:
        raise ValueError(f"{name}: parts use materials {sorted(used)}; "
                         "merge them into one texture first (one material per cosmetic)")
    if obj.data.materials:
        obj.data.materials[0].name = "M_" + _short(name)
    return obj


def strip_inner(obj, surface, threshold=INNER_DOT):
    """Delete faces that look at the skin: the inside wall of a closed garment volume.

    Generators return garments as closed solids over a guessed body. The wall
    facing the body would be pushed onto the outer wall by the conform step and
    z-fight; dropping it leaves a single-walled shell that solidify thickens
    like a hand-built item. Returns the number of faces removed.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    doomed = []
    for face in bm.faces:
        _, _, _, normal, _ = surface.nearest(face.calc_center_median())
        if face.normal.dot(normal) < threshold:
            doomed.append(face)
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    cosmetic.keep_largest_islands(bm, min_faces=12)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return len(doomed)


def conform(obj, surface, offset=OFFSET, max_gap=None, iterations=SMOOTH_ITERATIONS):
    """Push the item out to at least `offset` off the skin; with max_gap, pull loose fabric in.

    max_gap is for tight garments (tops, leggings) generated on a body that is
    not ours: fabric floating farther than max_gap is drawn back to it. Leave
    it None for skirts, sleeves and anything meant to hang. Moved vertices get
    a light relax so the correction does not leave dents. Returns how many moved.
    """
    mesh = obj.data
    moved = set()
    for v in mesh.vertices:
        _, _, loc, normal, _ = surface.nearest(v.co)
        gap = (v.co - loc).dot(normal)
        if gap < offset:
            v.co = loc + normal * offset
            moved.add(v.index)
        elif max_gap is not None and gap > max_gap:
            v.co = loc + normal * max_gap
            moved.add(v.index)
    if moved and iterations:
        neighbours = {i: [] for i in moved}
        for e in mesh.edges:
            a, b = e.vertices
            if a in neighbours:
                neighbours[a].append(b)
            if b in neighbours:
                neighbours[b].append(a)
        for _ in range(iterations):
            new = {}
            for i, nbrs in neighbours.items():
                if nbrs:
                    avg = sum((mesh.vertices[n].co for n in nbrs), Vector()) / len(nbrs)
                    new[i] = surface.push_out(mesh.vertices[i].co.lerp(avg, 0.5), offset)
            for i, co in new.items():
                mesh.vertices[i].co = co
    mesh.update()
    return len(moved)


def _textures(obj, name, out_dir, texture_size):
    """Write the item's textures next to its FBX; returns the sidecar material entry."""
    maps = textures.extract(obj, out_dir, _short(name), size=texture_size)
    entry = {"name": "M_" + _short(name), "color": [1.0, 1.0, 1.0], "recolorable": False}
    for key, field in (("BaseMap", "baseMap"), ("Normal", "normalMap"),
                       ("MetallicSmoothness", "metallicSmoothnessMap")):
        if maps.get(key):
            entry[field] = os.path.basename(maps[key])
    return entry


def _tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def _boundary_edges(obj):
    _, edges, _ = meshutil.corner_arrays(obj.data)
    return int((np.bincount(edges, minlength=len(obj.data.edges)) == 1).sum())


def _decimate_shell(obj, budget, thickness, symmetric):
    """Decimate a shell so that after solidify (2x faces + 2 rim tris per open edge) it fits `budget`."""
    if not thickness:
        return reduce.decimate_to(obj, budget, symmetry=symmetric)
    target = budget // 2
    for _ in range(3):
        reduce.decimate_to(obj, target, symmetry=symmetric)
        rim = 2 * _boundary_edges(obj)
        if 2 * _tris(obj) + rim <= budget:
            break
        target = max(64, (budget - rim) // 2)
    return _tris(obj)


def _provenance(source):
    """ArtSource/AI/<asset>/provenance.json for a clean file there (Steam AI-content disclosure)."""
    return os.path.join(os.path.dirname(source), "provenance.json").replace("\\", "/")


def region_vertices(regions):
    """Body vertices of the faces in `regions` (BR_* material slots)."""
    body = bpy.data.objects[C.BODY]
    wanted = {C.REGIONS.index(r) for r in regions}
    return sorted({v for p in body.data.polygons if p.material_index in wanted for v in p.vertices})


def skinned_item(name, slot, source, parts, budget, out_dir, *, offset=OFFSET, max_gap=None,
                 thickness=THICKNESS, inner=True, symmetric=True, texture_size=1024, regions=None):
    """A garment (Top, Bottom, FullBody, Legwear, Shoes) from parts of a clean file.

    strip inner wall -> decimate -> conform -> solidify -> clearance -> bind
    weights and sliders from the body -> skin mask -> FBX + textures + sidecar.
    `budget` is the final triangle count, solidify's second layer and rim
    included.

    Generated items have no cut lines to say which skin they grew from, so the
    skin mask is limited to the body `regions` they cover (default: every
    region holding at least 5% of the item), which stops the ray test hiding
    skin a sleeve merely hovers over in another region.
    """
    cosmetic.remove_item(name)
    surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])
    obj = _load_parts(source, parts, name)
    if regions is None:
        regions = [r for r, share in part_regions(obj, surface, top=len(C.REGIONS)) if share >= 0.05]
    obj[cosmetic.COVERED_PROP] = region_vertices(regions)
    stripped = strip_inner(obj, surface) if inner else 0
    _decimate_shell(obj, budget, thickness, symmetric)
    moved = conform(obj, surface, offset, max_gap)
    if thickness:
        cosmetic.solidify(obj, thickness)
    cosmetic.ensure_clearance(obj, MIN_GAP)
    obj.data.shade_smooth()
    material = _textures(obj, name, out_dir, texture_size)
    cosmetic.bind_to_body(obj)
    body = bpy.data.objects[C.BODY]
    hidden = cosmetic.skin_mask(obj)
    path = cosmetic.export(name, [obj], out_dir, {
        "slot": slot,
        "kind": "skinned",
        "hiddenBodyVertices": cosmetic.encode_mask(hidden, len(body.data.vertices)),
        "hiddenBodyVertexCount": len(hidden),
        "materials": [material],
        "source": source,
        "provenance": _provenance(source),
    })
    print(f"[{name}] {_tris(obj)} tris, inner faces stripped {stripped}, conformed {moved} vertices, "
          f"hides {len(hidden)} body vertices in {regions} -> {path}")
    return [path]


def hair_item(name, source, parts, budget, out_dir, *, offset=HAIR_OFFSET, texture_size=1024):
    """Helmet hair from parts of a clean file: 100% Head weights, no sliders, no skin mask.

    Generated hair is one dense closed volume; it is decimated as-is (no inner
    wall to strip, it is never seen from inside) and pushed off the scalp.
    Long hair moves rigidly with the head until spring bones arrive.
    """
    cosmetic.remove_item(name)
    surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])
    obj = _load_parts(source, parts, name)
    reduce.decimate_to(obj, budget)
    conform(obj, surface, offset)
    obj.data.shade_smooth()
    material = _textures(obj, name, out_dir, texture_size)
    obj.vertex_groups.clear()
    obj.vertex_groups.new(name="Head").add([v.index for v in obj.data.vertices], 1.0, "REPLACE")
    limit_and_normalize(obj)
    _attach(obj, bpy.data.objects[C.ARMATURE])
    body = bpy.data.objects[C.BODY]
    path = cosmetic.export(name, [obj], out_dir, {
        "slot": "Hair",
        "kind": "skinned",
        "hiddenBodyVertices": cosmetic.encode_mask([], len(body.data.vertices)),
        "hiddenBodyVertexCount": 0,
        "materials": [material],
        "source": source,
        "provenance": _provenance(source),
    })
    print(f"[{name}] {_tris(obj)} tris, Head-weighted -> {path}")
    return [path]


def rigid_item(name, slot, source, parts, socket, budget, out_dir, *, texture_size=1024):
    """A rigid accessory (Ears, Face, HeadTop, Hand, Back) from parts of a clean file.

    The clean file is already fitted to the rest body, so the part sits where
    it is worn; Unity keeps that placement relative to `socket`.
    """
    cosmetic.remove_item(name)
    obj = _load_parts(source, parts, name)
    reduce.decimate_to(obj, budget)
    obj.data.shade_smooth()
    material = _textures(obj, name, out_dir, texture_size)
    path = cosmetic.export(name, [obj], out_dir, {
        "slot": slot,
        "kind": "rigid",
        "attachments": [{"node": name, "socket": socket}],
        "materials": [material],
        "source": source,
        "provenance": _provenance(source),
    })
    print(f"[{name}] {_tris(obj)} tris on {socket} -> {path}")
    return [path]
