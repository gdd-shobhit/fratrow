"""Headless end-to-end test of propkit.ingest on a stand-in for a Tripo download.

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/propkit/tests/test_ingest.py [-- --out <dir>]

There is no real Tripo output in the repo, so the test builds one that fails
the same ways: a ~190k-triangle chair (six closed, noise-displaced, fully
triangulated parts) with a 2048 base-color atlas carrying fake baked light
(tops bright, bottoms dark), a 2048 glTF metallic-roughness texture (steel
legs) and a normal map; five sub-20-face floaters, cracked seams (duplicate
vertices), a non-manifold fin, all under an empty that scales it x37 and turns
it 90 degrees (front facing +X, as Tripo exports), plus a camera and a light.

Everything is written to a temp folder, never into Assets/. Exits 1 on any
failed check (and prints every check either way).
"""
import json
import math
import os
import struct
import sys
import tempfile
import traceback

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector, noise

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))

from propkit import ingest  # noqa: E402

NAME = "TestChair"
HEIGHT = 0.9          # m
BUDGET = "small"      # 1500 tris
RAW_SCALE = 37.0
RAW_TURN_DEG = 90.0   # front -Y -> +X
TEX = 2048
GRID = 6              # atlas cells per side: part x direction
FLOATERS = 5
# name, size (x, y, z), centre, subdivision cuts, sRGB colour, roughness, metallic
PARTS = (
    ("seat", (0.9, 0.9, 0.12), (0.0, 0.0, 0.90), 70, (0.62, 0.40, 0.22), 0.6, 0.0),
    ("back", (0.9, 0.12, 1.0), (0.0, 0.39, 1.465), 70, (0.55, 0.35, 0.20), 0.6, 0.0),
    # Parts sit 5 mm apart so merge-by-distance cannot fuse them into one island.
    ("leg_fl", (0.1, 0.1, 0.835), (-0.38, -0.38, 0.4175), 36, (0.70, 0.70, 0.72), 0.3, 1.0),
    ("leg_fr", (0.1, 0.1, 0.835), (0.38, -0.38, 0.4175), 36, (0.70, 0.70, 0.72), 0.3, 1.0),
    ("leg_bl", (0.1, 0.1, 0.835), (-0.38, 0.38, 0.4175), 36, (0.70, 0.70, 0.72), 0.3, 1.0),
    ("leg_br", (0.1, 0.1, 0.835), (0.38, 0.38, 0.4175), 36, (0.70, 0.70, 0.72), 0.3, 1.0),
)
# Baked light per face direction (+X, -X, +Y, -Y, +Z, -Z), multiplied into the sRGB albedo.
SHADE = (0.80, 0.55, 0.70, 0.65, 1.00, 0.35)

CHECKS = []


def check(label, ok, detail=""):
    CHECKS.append((label, bool(ok)))
    suffix = "" if isinstance(detail, str) and not detail else f" -- {detail}"
    print(f"[test_ingest] {'ok  ' if ok else 'FAIL'} {label}{suffix}")
    return ok


# --- Stand-in Tripo download ----------------------------------------------------

def _cell_rect(part, direction):
    cell = part * 6 + direction
    cx, cy = cell % GRID, cell // GRID
    margin = 0.06
    return (cx + margin) / GRID, (cy + margin) / GRID, (1 - 2 * margin) / GRID


def cell_centre_uv(part, direction):
    u0, v0, s = _cell_rect(part, direction)
    return u0 + s / 2, v0 + s / 2


def _direction(normal):
    axis = max(range(3), key=lambda i: abs(normal[i]))
    return axis * 2 + (0 if normal[axis] > 0 else 1)


def _image(name, pixels, data):
    h, w = pixels.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels.foreach_set(pixels.astype(np.float32).ravel())
    img.pack()
    if data:
        img.colorspace_settings.is_data = True
    return img


def build_textures():
    base = np.full((TEX, TEX, 4), (0.05, 0.05, 0.05, 1.0), np.float32)
    mr = np.full((TEX, TEX, 4), (1.0, 0.8, 0.0, 1.0), np.float32)       # glTF: R occl, G rough, B metal
    nrm = np.full((TEX, TEX, 4), (0.5, 0.5, 1.0, 1.0), np.float32)
    ys, xs = np.mgrid[0:TEX, 0:TEX]
    grain = 0.92 + 0.08 * np.sin(xs * 0.35 + np.sin(ys * 0.05) * 3.0)   # wood-ish streaks
    for p, (_, _, _, _, colour, rough, metal) in enumerate(PARTS):
        for d in range(6):
            u0, v0, s = _cell_rect(p, d)
            x0, y0, n = int(u0 * TEX), int(v0 * TEX), int(s * TEX)
            region = (slice(y0, y0 + n), slice(x0, x0 + n))
            gradient = np.linspace(0.85, 1.0, n)[:, None] if d < 4 else 1.0   # darker toward the floor
            for c in range(3):
                base[region + (c,)] = colour[c] * SHADE[d] * grain[region] * gradient
            mr[region + (1,)] = rough
            mr[region + (2,)] = metal
    nrm[..., 0] += 0.04 * np.sin(xs * 0.2)
    return (_image("raw_basecolor", base, False), _image("raw_metalrough", mr, True),
            _image("raw_normal", nrm, True))


def build_material():
    base, mr, nrm = build_textures()
    mat = bpy.data.materials.new("tripo_material")
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tb = nt.nodes.new("ShaderNodeTexImage")
    tb.image = base
    nt.links.new(tb.outputs["Color"], bsdf.inputs["Base Color"])
    tm = nt.nodes.new("ShaderNodeTexImage")
    tm.image = mr
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(tm.outputs["Color"], sep.inputs[0])
    nt.links.new(sep.outputs[1], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs[2], bsdf.inputs["Metallic"])
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = nrm
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def build_chair_bmesh():
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for p, (_, size, centre, cuts, *_rest) in enumerate(PARTS):
        before = set(bm.faces)
        made = bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.LocRotScale(Vector(centre), None, Vector(size)))
        edges = list({e for v in made["verts"] for e in v.link_edges})
        bmesh.ops.subdivide_edges(bm, edges=edges, cuts=cuts, use_grid_fill=True)
        lo = np.array(centre) - np.array(size) / 2
        hi = np.array(centre) + np.array(size) / 2
        bm.normal_update()
        for f in [f for f in bm.faces if f not in before]:   # planar UVs into this part's atlas cells
            d = _direction(f.normal)
            a, b = [i for i in range(3) if i != d // 2]
            u0, v0, s = _cell_rect(p, d)
            for loop in f.loops:
                co = loop.vert.co
                loop[uv].uv = (u0 + s * (co[a] - lo[a]) / (hi[a] - lo[a]),
                               v0 + s * (co[b] - lo[b]) / (hi[b] - lo[b]))
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    # Organic noise like a generated surface (UVs are already assigned).
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * 0.004 * noise.noise(v.co * 9.0)
    # Cracked seams: split a strip of the seat's top -> coincident duplicate vertices.
    seat_top = [e for e in bm.edges if all(abs(v.co.z - 0.96) < 0.01 for v in e.verts)
                and abs(e.verts[0].co.x - e.verts[1].co.x) < 1e-3 and -0.2 < e.verts[0].co.x < 0.2]
    bmesh.ops.split_edges(bm, edges=seat_top[:200])
    # Non-manifold fin on the seat's front edge: one quad sharing an existing edge.
    front = min((e for e in bm.edges if all(abs(v.co.y + 0.45) < 0.01 and abs(v.co.z - 0.96) < 0.01 for v in e.verts)),
                key=lambda e: abs(e.verts[0].co.x), default=None)
    if front is not None:
        a, b = front.verts
        c = bm.verts.new(b.co + Vector((0, -0.05, 0.03)))
        d = bm.verts.new(a.co + Vector((0, -0.05, 0.03)))
        bm.faces.new((a, b, c, d))
    return bm


def build_floaters_bmesh():
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    spots = ((0.7, -0.6, 1.2), (-0.8, 0.5, 0.3), (0.2, -0.9, 0.05), (0.9, 0.9, 1.8), (-0.6, -0.7, 1.5))
    for i, spot in enumerate(spots):
        if i < 3:   # cube crumbs, 12 tris each
            bmesh.ops.create_cube(bm, size=0.03, matrix=Matrix.Translation(spot))
        else:       # single-triangle flakes
            vs = [bm.verts.new(Vector(spot) + Vector(o)) for o in ((0, 0, 0), (0.02, 0, 0), (0, 0.02, 0.01))]
            bm.faces.new(vs)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    return bm


def build_standin(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mat = build_material()
    scene = bpy.context.scene
    root = bpy.data.objects.new("TripoRoot", None)
    scene.collection.objects.link(root)
    root.scale = (RAW_SCALE,) * 3
    root.rotation_euler = (0, 0, math.radians(RAW_TURN_DEG))
    for name, bm in (("tripo_mesh", build_chair_bmesh()), ("tripo_debris", build_floaters_bmesh())):
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(mat)
        me.shade_smooth()
        obj = bpy.data.objects.new(name, me)
        scene.collection.objects.link(obj)
        obj.parent = root
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    light = bpy.data.objects.new("Light", bpy.data.lights.new("Light", "POINT"))
    for o in (cam, light):
        scene.collection.objects.link(o)
        o.location = (3, -3, 2)
    tris = sum(len(p.vertices) - 2 for o in scene.objects if o.type == "MESH" for p in o.data.polygons)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_cameras=True, export_lights=True)
    print(f"[test_ingest] stand-in: {tris} tris, {os.path.getsize(path) // 1024} KB -> {path}")
    return tris


# --- Checks ---------------------------------------------------------------------

def png_info(path):
    """(width, height, colour type) from a PNG header (2 = RGB, 6 = RGBA)."""
    with open(path, "rb") as f:
        head = f.read(26)
    w, h = struct.unpack(">II", head[16:24])
    return w, h, head[25]


def pixel(path, uv):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * img.channels, np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, img.channels)
    value = px[int(uv[1] * h), int(uv[0] * w)]
    bpy.data.images.remove(img)
    return value


def world_bounds(obj):
    co = np.array([(obj.matrix_world @ v.co)[:] for v in obj.data.vertices])
    return co.min(axis=0), co.max(axis=0), co


def main():
    out = None
    if "--" in sys.argv:
        args = sys.argv[sys.argv.index("--") + 1:]
        if "--out" in args:
            out = args[args.index("--out") + 1]
    out = os.path.abspath(out or tempfile.mkdtemp(prefix="propkit_test_"))
    os.makedirs(out, exist_ok=True)
    src = os.path.join(out, "raw", "tripo_chair.glb")
    os.makedirs(os.path.dirname(src), exist_ok=True)
    raw_tris = build_standin(src)
    check("stand-in is dense", raw_tris > 150_000, raw_tris)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    props_root = os.path.join(out, "Assets", "Art", "Props")
    clean_root = os.path.join(out, "ArtSource", "AI")
    result = ingest.run(src, NAME, height=HEIGHT, budget=BUDGET, out_root=props_root,
                        clean_root=clean_root, rotate_z=-RAW_TURN_DEG,
                        provenance={"generator": "test stand-in"})
    budget = ingest.BUDGETS[BUDGET]
    lods = result["lods"]

    # Triangle budgets and LOD names.
    names = [ingest.lod_name(NAME, i) for i in range(3)]
    check("LOD names", [l["name"] for l in lods] == names and all(bpy.data.objects.get(n) for n in names),
          [l["name"] for l in lods])
    check("LOD0 tris <= budget", lods[0]["tris"] <= budget, f"{lods[0]['tris']} / {budget}")
    check("LOD0 uses most of the budget", lods[0]["tris"] >= 0.9 * budget, lods[0]["tris"])
    for i, ratio in enumerate(ingest.LOD_RATIOS, start=1):
        check(f"LOD{i} tris <= {ratio:.0%} of LOD0", lods[i]["tris"] <= round(lods[0]["tris"] * ratio),
              f"{lods[i]['tris']} vs {lods[0]['tris']}")
    check("observe reports LOD0 tris", result["observe"].get("tris") == lods[0]["tris"], result["observe"].get("tris"))

    # Cleanup removed exactly the floaters and kept every chair part.
    cleaned = result["cleanup"]
    print("[test_ingest] cleanup:", cleaned)
    lod0 = bpy.data.objects[names[0]]
    from aikit import meshutil
    _, parts = meshutil.face_components(lod0)
    check("chair parts kept (6 loose parts in LOD0)", parts == len(PARTS), parts)
    removed = cleaned.get("floaters_removed")
    check("floaters removed", removed == FLOATERS, f"{removed} of {FLOATERS}")
    merged = next((v for k, v in cleaned.items() if "merge" in k), 0)
    check("duplicate vertices merged", merged and merged > 0, merged)

    # Scale, grounding, centring and facing.
    lo, hi, co = world_bounds(lod0)
    check("height to 1 mm", abs((hi[2] - lo[2]) - HEIGHT) <= 1e-3, f"{hi[2] - lo[2]:.5f} m")
    check("bottom on Z=0", abs(lo[2]) <= 1e-4, f"{lo[2]:.6f}")
    centre = (lo[:2] + hi[:2]) / 2
    check("centred on X/Y to 1 mm", bool(np.all(np.abs(centre) <= 1e-3)), f"({centre[0]:.5f}, {centre[1]:.5f})")
    check("transforms applied", lod0.matrix_world == Matrix.Identity(4))
    upper = co[co[:, 2] > 0.75 * HEIGHT]
    check("front faces -Y (backrest at +Y)", len(upper) and upper[:, 1].mean() > 0.1 * HEIGHT
          and abs(upper[:, 0].mean()) < 0.05 * HEIGHT, f"backrest centre x={upper[:, 0].mean():.3f} y={upper[:, 1].mean():.3f}")

    # Textures: written, 1024, URP packing.
    tex = result["textures"]
    check("texture keys", set(tex) == {"BaseMap", "Normal", "MetallicSmoothness"}, sorted(tex))
    for key, path in tex.items():
        w, h, colour = png_info(path)
        check(f"{os.path.basename(path)} is 1024x1024", (w, h) == (1024, 1024) and os.path.dirname(path) == os.path.join(props_root, NAME),
              f"{w}x{h} type {colour}")
        check(f"{os.path.basename(path)} named T_{NAME}_{key}.png", os.path.basename(path) == f"T_{NAME}_{key}.png")
    if "MetallicSmoothness" in tex:
        for p, d in ((2, 0), (0, 4)):   # a steel leg side, the wooden seat top
            _, _, _, _, _, rough, metal = PARTS[p]
            r, g, b, a = pixel(tex["MetallicSmoothness"], cell_centre_uv(p, d))[:4]
            check(f"MetallicSmoothness {PARTS[p][0]}: R=metallic, A=1-roughness",
                  abs(r - metal) < 0.02 and abs(a - (1 - rough)) < 0.02, f"R={r:.3f} A={a:.3f} (want {metal}, {1 - rough:.2f})")
    if "BaseMap" in tex:
        top = pixel(tex["BaseMap"], cell_centre_uv(0, 4))[:3]
        expect = np.array(PARTS[0][4]) * SHADE[4] * 0.92   # mean of the grain streaks
        check("BaseMap keeps the albedo texels", np.all(np.abs(top - expect) < 0.08), f"{top} vs {expect}")
    lum = result["observe"].get("base_color_luminance") or result["observe"].get("luminance") or {}
    print("[test_ingest] luminance observations:", json.dumps(lum, default=str)[:600])

    # Files and sidecar.
    folder = os.path.join(props_root, NAME)
    fbx, glb, sidecar = result["fbx"], result["glb"], result["sidecar"]
    check("FBX path", fbx == os.path.join(folder, f"SM_{NAME}.fbx") and os.path.isfile(fbx), fbx)
    check("clean GLB path", glb == os.path.join(clean_root, NAME, f"{NAME}_clean.glb") and os.path.isfile(glb), glb)
    with open(sidecar, encoding="utf-8") as f:
        data = json.load(f)
    check("sidecar", data["id"] == NAME and data["model"] == f"SM_{NAME}.fbx"
          and data["observe"]["tris"] == lods[0]["tris"] and [l["tris"] for l in data["lods"]] == [l["tris"] for l in lods],
          os.path.basename(sidecar))
    check("nothing written to the repo's Assets/", not os.path.exists(os.path.join(ingest.PROPS_ROOT, NAME)))
    sizes = {os.path.basename(p): f"{os.path.getsize(p) / 1024:.0f} KB" for p in [fbx, glb, sidecar, *tex.values()]}
    print("[test_ingest] file sizes:", sizes)

    # FBX round trip: same objects, same triangles, same height.
    want = {l["name"]: l["tris"] for l in lods}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx, axis_forward="-Z", axis_up="Y")
    got = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bpy.context.scene.objects if o.type == "MESH"}
    check("FBX re-import: names and tris", got == want, f"{got} vs {want}")
    bpy.context.view_layer.update()
    if names[0] in bpy.data.objects:
        lo, hi, _ = world_bounds(bpy.data.objects[names[0]])
        check("FBX re-import: height to 1 mm, bottom on 0", abs((hi[2] - lo[2]) - HEIGHT) <= 1e-3 and abs(lo[2]) <= 1e-3,
              f"{hi[2] - lo[2]:.5f} m, bottom {lo[2]:.5f}")
        check("FBX re-import: scale 1", all(abs(s - 1) < 1e-6 for s in bpy.data.objects[names[0]].scale),
              bpy.data.objects[names[0]].scale[:])

    # Clean GLB round trip: LOD0 only, textures embedded.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=glb)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    tris = sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons)
    check("clean GLB holds LOD0", len(meshes) == 1 and tris == lods[0]["tris"], f"{len(meshes)} meshes, {tris} tris")
    sizes = sorted(tuple(i.size) for i in bpy.data.images)
    check("clean GLB embeds 1024 textures", sizes and all(s == (1024, 1024) for s in sizes)
          and all(i.packed_file is not None for i in bpy.data.images), sizes)

    failed = [label for label, ok in CHECKS if not ok]
    print(f"[test_ingest] {len(CHECKS) - len(failed)}/{len(CHECKS)} checks passed; outputs in {out}")
    return not failed


if __name__ == "__main__":
    try:
        ok = main()
    except Exception:
        traceback.print_exc()
        ok = False
    sys.exit(0 if ok else 1)
