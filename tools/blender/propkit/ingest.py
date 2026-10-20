"""Static prop ingest: AI-generated model -> Unity FBX with LODs, URP textures and a record.

Static props skip retopology: decimation keeps the generator's UVs and
texture, so the pipeline is import -> join -> clean -> scale to a real height
and ground -> decimate to a triangle budget -> LODs -> textures -> export
(Docs/Reports/tripo-pipeline/notes/practitioner_pipelines.md, sections 1 and 4).

    from propkit import ingest
    ingest.run(r"ArtSource/AI/RedCup/raw/red_cup.glb", "RedCup", height=0.12, budget="small")

Writes, for name "RedCup":

    Assets/Art/Props/RedCup/SM_RedCup.fbx          SM_RedCup_LOD0/1/2 (Unity builds the LODGroup)
    Assets/Art/Props/RedCup/T_RedCup_*.png         BaseMap / Normal / MetallicSmoothness, as present
    Assets/Art/Props/RedCup/SM_RedCup.prop.json    observed numbers (never verdicts)
    ArtSource/AI/RedCup/RedCup_clean.glb           LOD0 + textures, the lean committed source

Unity's Assets/Art/Props/Editor/PropModelImporter.cs applies the import rules
and builds M_RedCup.mat (URP Lit) from the T_ files. Every LOD is decimated
from the cleaned full-density mesh, not from the LOD above it.

The front of the prop must face -Y in Blender (charkit's convention; it becomes
+Z forward in Unity). Generators often face +X: pass rotate_z=-90 for that.
"""
import json
import math
import os
import re

import bpy
from mathutils import Matrix

from aikit import clean_glb, cleanup, importer, normalize, reduce, report, textures

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
PROPS_ROOT = os.path.join(REPO, "Assets", "Art", "Props")
CLEAN_ROOT = os.path.join(REPO, "ArtSource", "AI")

# LOD0 triangle budgets by prop size; pass a tier name or a number to run().
BUDGETS = {"small": 1500, "medium": 4000, "large": 10000}
LOD_RATIOS = (0.5, 0.25)      # LOD1, LOD2... as a share of LOD0's triangles
TEXTURE_SIZE = 1024
SHARP_ANGLE_DEG = 45.0        # edges sharper than this stay hard after decimation
SIDECAR_SCHEMA = 1
SIDECAR_SUFFIX = ".prop.json"
COLLECTION_PREFIX = "PROP_"   # Blender collection holding a prop's LOD objects
NAME_PATTERN = re.compile(r"[A-Za-z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)*")

# Mirrors charkit.export_fbx.FBX_SETTINGS (Unity scale 1, no root rotation,
# +Z forward), restricted to static meshes. Texture paths are stripped to file
# names so the FBX never records a machine path; Unity remaps the material anyway.
FBX_SETTINGS = dict(
    use_selection=True,
    object_types={"MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=True,
    use_mesh_modifiers=True,
    mesh_smooth_type="FACE",
    use_tspace=False,
    use_custom_props=False,
    add_leaf_bones=False,
    bake_anim=False,
    path_mode="STRIP",
    embed_textures=False,
)


def resolve_budget(budget):
    """(tier name or None, LOD0 triangle budget) from "small"/"medium"/"large" or a number."""
    if isinstance(budget, str):
        key = budget.strip().lower()
        if key not in BUDGETS:
            raise ValueError(f"unknown budget tier {budget!r}; expected one of {sorted(BUDGETS)} or a number")
        return key, BUDGETS[key]
    if isinstance(budget, (int, float)) and not isinstance(budget, bool) and budget >= 1:
        return None, int(budget)
    raise ValueError(f"budget must be a tier name {sorted(BUDGETS)} or a positive number, got {budget!r}")


def lod_name(name, level):
    """Object, mesh and FBX node name of LOD `level`: SM_<name>_LOD<level>."""
    return f"SM_{name}_LOD{level}"


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def _rel(path):
    """Repo-relative POSIX path when inside the repo, else the absolute path."""
    path = os.path.abspath(path)
    try:
        rel = os.path.relpath(path, REPO)
    except ValueError:      # another drive
        return path.replace("\\", "/")
    return path.replace("\\", "/") if rel.startswith("..") else rel.replace("\\", "/")


def _collection(name):
    coll = bpy.data.collections.get(COLLECTION_PREFIX + name)
    if coll is None:
        coll = bpy.data.collections.new(COLLECTION_PREFIX + name)
        bpy.context.scene.collection.children.link(coll)
    return coll


def _move_to(obj, coll):
    for other in list(obj.users_collection):
        if other != coll:
            other.objects.unlink(obj)
    if coll not in obj.users_collection:
        coll.objects.link(obj)


def _remove_object(obj):
    data = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    if isinstance(data, bpy.types.Mesh) and data.users == 0:
        bpy.data.meshes.remove(data)


def remove_previous(name):
    """Delete a previous run's objects and material so names come out exact (no .001)."""
    prefix = f"SM_{name}_"
    coll = bpy.data.collections.get(COLLECTION_PREFIX + name)
    doomed = set(coll.objects) if coll else set()
    doomed |= {o for o in bpy.data.objects if o.name.startswith(prefix)}
    for obj in doomed:
        _remove_object(obj)
    if coll is not None and not coll.objects:
        bpy.data.collections.remove(coll)
    for mat in [m for m in bpy.data.materials if m.name == f"M_{name}" or m.name.startswith(f"M_{name}.")]:
        if mat.users == 0:
            bpy.data.materials.remove(mat)
        else:
            mat.name = f"M_{name}_prev"


def _name_materials(obj, name):
    """M_<name> for the first material slot, M_<name>_<i> for any others."""
    for i, slot in enumerate(obj.material_slots):
        if slot.material is not None:
            slot.material.name = f"M_{name}" if i == 0 else f"M_{name}_{i}"


def shade(obj, angle_deg=SHARP_ANGLE_DEG):
    """Smooth shading with hard edges above angle_deg; returns the sharp edge count.

    Decimation leaves the source's custom normals and sharp-edge marks
    meaningless, so both are reset from the decimated geometry.
    """
    me = obj.data
    if me.has_custom_normals:
        attr = me.attributes.get("custom_normal")
        if attr is not None:
            me.attributes.remove(attr)
        if me.has_custom_normals:
            with bpy.context.temp_override(object=obj, active_object=obj,
                                           selected_objects=[obj], selected_editable_objects=[obj]):
                bpy.ops.mesh.customdata_custom_splitnormals_clear()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(angle_deg))
    sharp = me.attributes.get("sharp_edge")
    if sharp is None:
        return 0
    flags = [False] * len(me.edges)
    sharp.data.foreach_get("value", flags)
    return sum(flags)


def _make_lod(dense, name, level, target, coll):
    obj = dense.copy()
    obj.data = dense.data.copy()
    obj.name = obj.data.name = lod_name(name, level)
    coll.objects.link(obj)
    bpy.context.view_layer.update()
    tris = reduce.decimate_to(obj, target)
    obj.data.name = obj.name
    # Collapse decimation places merged vertices at optimal positions, which can
    # dip a fraction of a millimetre below the grounded box: re-seat on Z = 0.
    zs = [v.co.z for v in obj.data.vertices]
    shift = -min(zs) if zs else 0.0
    if shift:
        obj.data.transform(Matrix.Translation((0.0, 0.0, shift)))
        obj.data.update()
    sharp = shade(obj)
    return obj, {"name": obj.name, "target": int(target), "tris": int(tris),
                 "verts": len(obj.data.vertices), "sharp_edges": sharp, "groundShift": round(shift, 6)}


def export_fbx(objects, path):
    """Export exactly `objects` to `path` with FBX_SETTINGS."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    view_layer = bpy.context.view_layer
    view_layer.update()   # drop entries of objects deleted earlier in the run
    for obj in view_layer.objects:
        if obj is not None:
            obj.select_set(False)
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
    view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=path, **FBX_SETTINGS)
    return path


def run(src, name, height, budget, lods=LOD_RATIOS, texture_size=TEXTURE_SIZE, out_root=PROPS_ROOT,
        clean_root=CLEAN_ROOT, rotate_z=0.0, provenance=None, keep_dense=False):
    """Ingest one static prop; returns paths, per-LOD counts and the observations.

    src: raw model (GLB/GLTF/FBX/OBJ), normally ArtSource/AI/<name>/raw/...
    name: PascalCase asset name, no prefix ("RedCup" -> SM_RedCup, T_RedCup_*, M_RedCup).
    height: real-world height in metres (bounding-box Z after grounding).
    budget: LOD0 triangles, a BUDGETS tier name or a number.
    lods: LOD1.. triangle shares of LOD0 (descending, each in (0, 1)).
    out_root / clean_root: where the Unity folder and the clean GLB go (tests redirect them).
    rotate_z: degrees about Z after grounding, to bring the prop's front to -Y.
    provenance: optional dict (generator, model version, reference image...) copied
        into the sidecar for the Steam AI-content disclosure.
    keep_dense: leave the cleaned full-density mesh in the scene (named SM_<name>_dense).
    """
    if not NAME_PATTERN.fullmatch(name or "") or name.split("_")[0] in ("SM", "T", "M", "SK", "SKM"):
        raise ValueError(f"name {name!r}: use PascalCase letters/digits without a type prefix, e.g. 'RedCup'")
    if not height or height <= 0:
        raise ValueError(f"height must be a positive number of metres, got {height!r}")
    tier, budget_tris = resolve_budget(budget)
    lods = tuple(float(r) for r in lods)
    if any(not 0 < r < 1 for r in lods) or list(lods) != sorted(lods, reverse=True):
        raise ValueError(f"lods must be descending shares in (0, 1), got {lods}")

    folder = os.path.join(out_root, name)
    os.makedirs(folder, exist_ok=True)
    remove_previous(name)
    coll = _collection(name)

    meshes = importer.import_model(src)
    if not meshes:
        raise ValueError(f"{src}: no meshes imported")
    raw_tris = sum(tri_count(o) for o in meshes)
    dense = cleanup.join(meshes, f"SM_{name}_dense")
    _move_to(dense, coll)
    cleaned = cleanup.clean(dense)
    grounded = normalize.to_ground(dense, height=height)
    if rotate_z:
        normalize.rotate_z(dense, rotate_z)
    _name_materials(dense, name)
    maps = textures.extract(dense, folder, name, size=texture_size)
    _name_materials(dense, name)
    source = report.observe(dense)

    lod_objects, lod_info = [], []
    for level, ratio in enumerate((1.0,) + lods):
        target = budget_tris if level == 0 else max(4, round(lod_info[0]["tris"] * ratio))
        obj, info = _make_lod(dense, name, level, target, coll)
        info["ratio"] = ratio
        lod_objects.append(obj)
        lod_info.append(info)
    if not keep_dense:
        _remove_object(dense)

    fbx_path = export_fbx(lod_objects, os.path.join(folder, f"SM_{name}.fbx"))
    glb_path = os.path.join(clean_root, name, f"{name}_clean.glb")
    os.makedirs(os.path.dirname(glb_path), exist_ok=True)
    clean_glb.save([lod_objects[0]], glb_path)
    observed = report.observe(lod_objects[0])

    sidecar = {
        "schema": SIDECAR_SCHEMA,
        "id": name,
        "model": os.path.basename(fbx_path),
        "source": {"file": _rel(src), "clean": _rel(glb_path), "objects": len(meshes), "tris": raw_tris},
        "provenance": dict(provenance or {}),
        "height": float(height),
        "rotateZ": float(rotate_z),
        "budget": {"tier": tier, "tris": budget_tris},
        "lods": lod_info,
        "textures": {key: os.path.basename(path) for key, path in maps.items()},
        "cleanup": cleaned,
        "normalize": grounded,
        "observeSource": source,
        "observe": observed,
        "blender": bpy.app.version_string,
    }
    sidecar_path = os.path.join(folder, f"SM_{name}{SIDECAR_SUFFIX}")
    with open(sidecar_path, "w", encoding="utf-8") as f:
        json.dump(sidecar, f, indent=2, default=_json_default)
        f.write("\n")

    sizes = {os.path.basename(p): os.path.getsize(p) // 1024 for p in [fbx_path, glb_path, *maps.values()]}
    print(f"[propkit] {name}: raw {raw_tris} tris -> LODs {[i['tris'] for i in lod_info]} "
          f"(budget {budget_tris}), height {height} m; files KB {sizes}")
    return {
        "name": name,
        "fbx": fbx_path,
        "glb": glb_path,
        "sidecar": sidecar_path,
        "textures": maps,
        "lods": lod_info,
        "objects": [o.name for o in lod_objects],
        "cleanup": cleaned,
        "normalize": grounded,
        "observe": observed,
    }


def _json_default(value):
    """numpy scalars/arrays, tuples and Blender vectors in aikit's dicts -> JSON."""
    if hasattr(value, "tolist"):
        return value.tolist()
    if hasattr(value, "__len__"):
        return list(value)
    if hasattr(value, "item"):
        return value.item()
    return str(value)
