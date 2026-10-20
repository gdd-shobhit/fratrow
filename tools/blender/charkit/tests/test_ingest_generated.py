"""Headless end-to-end check of charkit.ingest on a stand-in for a Tripo download.

    "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b \
        ArtSource/Characters/SK_Human_Female.blend \
        --python tools/blender/charkit/tests/test_ingest_generated.py

No generator runs here. The stand-in is what a Tripo P2.0 "parts" download
looks like to the pipeline: a dressed character as separate dense, textured,
triangulated parts (body, a closed-volume top, helmet hair, an earring) at the
wrong scale, slightly turned and off-centre, exported as GLB. The test fits it
back onto the master body and builds a skinned, a hair and a rigid item from
it. Everything is written to a temp folder and the .blend is never saved.
Exits non-zero on the first failed check.
"""
import json
import math
import os
import sys
import tempfile
import traceback

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.abspath(os.path.join(HERE, "..", "..")))

from charkit import config as C          # noqa: E402
from charkit import cosmetic, ingest     # noqa: E402

RAW_SCALE = 0.6                          # generators normalise to their own unit box
RAW_TURN_DEG = 4.0
RAW_OFFSET = Vector((0.21, -0.13, 0.05))
CHECKS = []


def check(name, ok, detail=""):
    CHECKS.append((name, bool(ok), detail))
    print(f"[{'ok' if ok else 'FAIL'}] {name} {detail}")


def _texture(path, size=2048):
    """Base colour with fake baked shading (darker towards the bottom), saved as PNG."""
    y = np.linspace(0.35, 1.0, size)[:, None, None]
    x = np.linspace(0.0, 1.0, size)[None, :, None]
    rgb = np.concatenate([0.8 * y + 0 * x, 0.3 * y + 0.2 * x, 0.5 * y + 0 * x], axis=2)
    pixels = np.concatenate([rgb, np.ones((size, size, 1))], axis=2).astype(np.float32)
    img = bpy.data.images.new("stand_in_basecolor", size, size)
    img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def _material(img):
    mat = bpy.data.materials.new("Material_0")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = img
    links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def _part(name, bm, mat, subdivide=0):
    """bmesh -> triangulated, planar-UV'd, textured object (like a generator part)."""
    if subdivide:
        bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=subdivide, use_grid_fill=True)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    uv = bm.loops.layers.uv.verify()
    for face in bm.faces:
        for loop in face.loops:
            loop[uv].uv = (loop.vert.co.x + 0.5, loop.vert.co.z / 1.8)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def build_stand_in(folder):
    img = _texture(os.path.join(folder, "stand_in_basecolor.png"))
    mat = _material(img)
    surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])

    body = cosmetic.body_bmesh(set(C.REGIONS))
    body.verts.layers.int.remove(body.verts.layers.int[cosmetic.SOURCE_LAYER])
    parts = [_part("mesh_0", body, mat, subdivide=1)]

    # A top as a closed volume: chest + belly skin, pushed out 1-9 mm, solidified outward.
    top = cosmetic.body_bmesh({"Chest", "Belly"})
    top.verts.layers.int.remove(top.verts.layers.int[cosmetic.SOURCE_LAYER])
    for v in top.verts:
        v.co = surface.push_out(v.co, 0.006)
    obj = _part("mesh_1", top, mat, subdivide=1)
    mod = obj.modifiers.new("s", "SOLIDIFY")
    mod.thickness, mod.offset, mod.use_rim = 0.008, 1.0, True
    parts.append(obj)

    # Helmet hair: the top of a sphere around the head.
    head = [Vector(c) for c in surface.co if c.z > 1.45]
    centre = sum(head, Vector()) / len(head)
    radius = max((c - centre).length for c in head) + 0.012
    hair = bmesh.new()
    bmesh.ops.create_uvsphere(hair, u_segments=48, v_segments=24, radius=radius)
    bmesh.ops.delete(hair, geom=[v for v in hair.verts if v.co.z < -0.3 * radius], context="VERTS")
    bmesh.ops.translate(hair, verts=hair.verts, vec=centre + Vector((0, 0.01, 0.01)))
    parts.append(_part("mesh_2", hair, mat))

    # An earring at the left ear socket.
    arm = bpy.data.objects[C.ARMATURE]
    lobe = arm.matrix_world @ arm.data.bones["Socket_LeftEar"].head_local
    ring = bmesh.new()
    bmesh.ops.create_cone(ring, cap_ends=True, segments=16, radius1=0.006, radius2=0.006, depth=0.012)
    bmesh.ops.translate(ring, verts=ring.verts, vec=lobe - Vector((0, 0, 0.008)))
    parts.append(_part("mesh_3", ring, mat))

    raw = (Matrix.Translation(RAW_OFFSET) @ Matrix.Rotation(math.radians(RAW_TURN_DEG), 4, "Z")
           @ Matrix.Scale(RAW_SCALE, 4))
    for obj in parts:
        obj.matrix_world = raw
    for o in bpy.context.view_layer.objects:
        o.select_set(o in parts)
    bpy.context.view_layer.objects.active = parts[0]
    path = os.path.join(folder, "raw", "stand_in.glb")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True,
                              export_apply=True)
    for obj in parts:
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)
    return path


def run():
    tmp = tempfile.mkdtemp(prefix="charkit_ingest_")
    ingest.AI_ROOT = tmp
    asset = "Test_StandIn"
    raw = build_stand_in(os.path.join(tmp, asset))
    check("stand-in exported", os.path.getsize(raw) > 100_000, raw)

    result = ingest.prepare(raw, asset)
    fit = result["fit"]
    check("fit recovers the scale", abs(fit["scale"] - 1 / RAW_SCALE) / (1 / RAW_SCALE) < 0.005, str(fit))
    check("fit recovers the turn", abs(fit["rotation_deg"] - RAW_TURN_DEG) < 0.3, str(fit))
    check("bare skin lands on the body", fit["trusted_median_mm"] < 1.0, str(fit))
    check("clean file written", os.path.isfile(result["clean"]), result["clean"])
    check("clean file is lean", os.path.getsize(result["clean"]) < 8_000_000,
          f"{os.path.getsize(result['clean']) / 1e6:.1f} MB")

    def part_over(region, exclude=()):
        best = max((info for name, info in result["parts"].items() if name not in exclude),
                   key=lambda info: dict(info["regions"]).get(region, 0))
        return next(n for n, i in result["parts"].items() if i is best)

    parts = result["parts"]
    by_size = sorted(parts, key=lambda n: parts[n]["tris"])
    earring = by_size[0]
    body_part = by_size[-1]
    hair = part_over("Head", exclude=(earring, body_part))
    top = next(n for n in parts if n not in (earring, body_part, hair))
    check("top part sits over chest/belly",
          {"Chest", "Belly"} & {r for r, s in parts[top]["regions"] if s > 0.2}, str(parts[top]))

    out = os.path.join(tmp, "Cosmetics")
    os.makedirs(out, exist_ok=True)
    source = result["clean"]

    ingest.skinned_item("SKM_F_Top_StandIn", "Top", source, (top,), 1500, out)
    obj = bpy.data.objects["SKM_F_Top_StandIn"]
    side = json.load(open(os.path.join(out, "SKM_F_Top_StandIn.cosmetic.json")))
    keys = [k.name for k in obj.data.shape_keys.key_blocks[1:]]
    check("top within budget", ingest._tris(obj) <= 1500 * 1.05, str(ingest._tris(obj)))
    check("top carries all body sliders", keys == list(C.SHAPE_KEYS), str(len(keys)))
    check("top weighted to spine bones", {"Spine1", "Spine2"} <= set(g.name for g in obj.vertex_groups))
    check("top hides skin", side["hiddenBodyVertexCount"] > 50, str(side["hiddenBodyVertexCount"]))
    check("top sidecar names a base map", side["materials"][0].get("baseMap", "").endswith("_BaseMap.png"),
          str(side["materials"]))
    check("top base map written", os.path.isfile(os.path.join(out, side["materials"][0].get("baseMap", "-"))))
    check("top sidecar links provenance", side.get("provenance", "").endswith("provenance.json"))
    check("top fbx written", os.path.isfile(os.path.join(out, "SKM_F_Top_StandIn.fbx")))
    surface = cosmetic.BodySurface(bpy.data.objects[C.BODY])
    gap = min((v.co - loc).dot(normal) for v in obj.data.vertices
              for _, _, loc, normal, _ in [surface.nearest(v.co)])
    # ensure_clearance measures each vertex against its nearest face once; a
    # re-measure can land a fraction of a millimetre under MIN_GAP.
    check("top clears the skin", gap >= ingest.MIN_GAP - 5e-4, f"min gap {gap * 1000:.2f} mm")

    ingest.hair_item("SKM_F_Hair_StandIn", source, (hair,), 1500, out)
    obj = bpy.data.objects["SKM_F_Hair_StandIn"]
    check("hair is 100% Head", [g.name for g in obj.vertex_groups] == ["Head"])
    check("hair within budget", ingest._tris(obj) <= 1500 * 1.05, str(ingest._tris(obj)))

    ingest.rigid_item("SM_F_Ear_StandIn", "Ears", source, (earring,), "Socket_LeftEar", 200, out)
    side = json.load(open(os.path.join(out, "SM_F_Ear_StandIn.cosmetic.json")))
    check("earring attaches to its socket", side["attachments"] == [
        {"node": "SM_F_Ear_StandIn", "socket": "Socket_LeftEar"}])

    from charkit import review
    renders = review.render("ingest_standin", os.path.join(tmp, "renders"), views=("front", "three_quarter"),
                            size=700, objects=[bpy.data.objects[n] for n in
                                               (C.BODY, C.EYES, "SKM_F_Top_StandIn", "SKM_F_Hair_StandIn",
                                                "SM_F_Ear_StandIn")])
    review.apply_pose("arms_up")
    renders += review.render("ingest_standin_arms_up", os.path.join(tmp, "renders"), views=("front",),
                             size=700, objects=[bpy.data.objects[n] for n in
                                                (C.BODY, C.EYES, "SKM_F_Top_StandIn", "SKM_F_Hair_StandIn")])
    review.reset_pose()
    print("[renders]", *renders, sep="\n  ")
    print("[outputs]", tmp)


if __name__ == "__main__":
    try:
        run()
    except Exception:
        traceback.print_exc()
        CHECKS.append(("no exceptions", False, ""))
    failed = [c for c in CHECKS if not c[1]]
    print(f"\n{len(CHECKS) - len(failed)}/{len(CHECKS)} checks passed")
    sys.exit(1 if failed else 0)
