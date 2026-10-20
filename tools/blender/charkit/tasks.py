"""One-call pipeline tasks for driving the master file (through the Blender MCP).

The stable entry points for scripted sessions: each call does one job on the
open master (ArtSource/Characters/SK_Human_Female.blend) and prints a compact
report. Item names are module names (f_hair_bob) or item ids (SKM_F_Hair_Bob).

    from charkit import tasks
    tasks.status()                         # body hash, items built/exported/stale, recipe textures
    tasks.build("f_hair_bob")              # rebuild one item in the file (unbound until exported)
    tasks.export(["f_hair_bob"])           # build + bind + mask + FBX/sidecar; None = every item
    tasks.slider_spikes()                  # shape keys that move vertices far from the skin they follow
    tasks.focus(tasks.HEAD)                # then mcp__blender__look(mode="angles", target=["_focus"])
    tasks.isolate(["f_hair_bob"]); tasks.show_all()
    tasks.item_sheet("f_hair_bob")         # renders to production/qa/evidence/items/
    tasks.paint_faces(); tasks.lineup()    # recipe textures; M4 line-up evidence
    tasks.save()                           # tidy previews and helpers, save the master

After editing charkit code, tasks.reload() picks it up. build, export,
paint_faces and lineup reload on their own first, edits to this file included.
"""
import ast
import glob
import importlib
import json
import os
import sys
import traceback

import bpy
import numpy as np
from mathutils.kdtree import KDTree

from . import config as C
from . import cosmetic, export_fbx, faces, recipes, review

HERE = os.path.dirname(os.path.abspath(__file__))
COSMETICS_DIR = os.path.join(export_fbx.FEMALE_DIR, "Cosmetics")
ITEM_SHEET_DIR = os.path.join(export_fbx.REPO, "production", "qa", "evidence", "items")

# An exported item is stale when its FBX is older than its module or one of
# these (only the ones the item imports, directly or not).
STALE_SOURCES = ("cosmetic", "shapes", "items._garment", "items._hair", "items._footwear")

# slider_spikes: skin a key moves by more than SPIKE_SKIN_MOVE is "the key's
# region"; item vertices it moves farther than SPIKE_REACH from that region are spikes.
SPIKE_SKIN_MOVE = 0.001
SPIKE_REACH = cosmetic.MAX_SKIN_DISTANCE   # no item vertex may be farther than this from the skin it follows

# Region centres for focus().
HEAD = (0.0, 0.0, 1.53)
FACE = (0.0, -0.02, 1.5)
FEET = (0.0, -0.03, 0.08)


# --- Modules --------------------------------------------------------------------

def _source(name):
    """File of a charkit module, by its name relative to the package ("items._hair")."""
    return os.path.join(HERE, *name.split(".")) + ".py"


def _imports(name):
    """charkit modules `name` imports, including imports inside functions."""
    with open(_source(name), encoding="utf-8-sig") as f:
        tree = ast.parse(f.read())
    package = name.split(".")[:-1]
    found = set()
    for node in ast.walk(tree):
        if not isinstance(node, ast.ImportFrom) or not node.level:
            continue
        base = package[:len(package) - (node.level - 1)]
        if node.module:
            found.add(".".join(base + node.module.split(".")))
        else:
            found |= {".".join(base + [alias.name]) for alias in node.names}
    return {n for n in found if os.path.exists(_source(n))}


def _deps(name):
    """Every charkit module `name` depends on, directly or not."""
    seen, stack = set(), [name]
    while stack:
        for dep in _imports(stack.pop()):
            if dep not in seen:
                seen.add(dep)
                stack.append(dep)
    return seen


def _modules():
    """Every charkit module, each after the modules it imports; tasks last."""
    core = sorted(f[:-3] for f in os.listdir(HERE)
                  if f.endswith(".py") and f not in ("__init__.py", "tasks.py"))
    helpers = sorted("items." + f[:-3] for f in os.listdir(os.path.join(HERE, "items"))
                     if f.startswith("_") and f.endswith(".py") and f != "__init__.py")
    order = []

    def visit(name, path=()):
        if name in order or name in path:
            return
        for dep in sorted(_imports(name)):
            visit(dep, path + (name,))
        order.append(name)

    for name in core + helpers + ["items." + n for n in item_names()]:
        visit(name)
    return order + ["tasks"]


def reload():
    """importlib.reload every charkit module (dependencies first). Returns their names."""
    names = _modules()
    for name in names:
        full = f"{__package__}.{name}"
        if full in sys.modules:
            importlib.reload(sys.modules[full])
        else:
            importlib.import_module(full)
    return names


def item_names():
    """Item module names in charkit/items (helpers start with "_")."""
    return sorted(f[:-3] for f in os.listdir(os.path.join(HERE, "items"))
                  if f.endswith(".py") and not f.startswith("_"))


def _module(name):
    """An item module by module name (f_hair_bob) or item id (SKM_F_Hair_Bob)."""
    names = item_names()
    if name not in names:
        by_id = {importlib.import_module(f"{__package__}.items.{n}").NAME: n for n in names}
        if name not in by_id:
            raise KeyError(f"no item {name!r}; items: {', '.join(names)}")
        name = by_id[name]
    return importlib.import_module(f"{__package__}.items.{name}")


def _find(name):
    """Objects for an object name, an item id or an item module name."""
    obj = bpy.data.objects.get(name)
    if obj is not None:
        return [obj]
    if name in item_names():
        name = _module(name).NAME
    return recipes.item_objects(name)


def _tris(objects):
    return sum(len(p.vertices) - 2 for obj in objects for p in obj.data.polygons)


# --- Build and export -------------------------------------------------------------

# Tasks that reload hand over to a private _impl looked up after the reload, so
# an edit to this file takes effect in the same call (the caller's function
# object is the old one).

def build(name):
    """Reload, then rebuild one item in the file with its module's build().

    Skinned items come out unbound (no weights, no sliders) until export()
    runs the full build; rigid items are complete.
    """
    reload()
    return _build(name)


def _build(name):
    mod = _module(name)
    made = mod.build()
    objects = list(made) if isinstance(made, (list, tuple)) else [made]
    result = {"object": objects[0].name if len(objects) == 1 else mod.NAME,
              "objects": [o.name for o in objects], "tris": _tris(objects)}
    print(f"[tasks] built {result['object']}: {result['tris']} tris")
    return result


def export(names=None):
    """Reload, then build, bind, mask and export items (all when None) to Cosmetics/.

    Keeps going past failures. Returns [{"item", "ok", "tris", "error"}].
    """
    reload()
    return _export(names)


def _export(names):
    review.reset_pose()
    if isinstance(names, str):
        names = [names]
    results = []
    for name in item_names() if names is None else names:
        item = name
        try:
            mod = _module(name)
            item = mod.NAME
            mod.run(COSMETICS_DIR)
            results.append({"item": item, "ok": True, "tris": _tris(recipes.item_objects(item)), "error": None})
        except Exception as e:
            print(traceback.format_exc())
            results.append({"item": item, "ok": False, "tris": None, "error": f"{type(e).__name__}: {e}"})
    print(f"{'item':<28}{'ok':<6}{'tris':>6}  error")
    for r in results:
        tris = r["tris"] if r["tris"] is not None else "-"
        print(f"{r['item']:<28}{'ok' if r['ok'] else 'FAIL':<6}{tris:>6}  {r['error'] or ''}")
    return results


# --- Viewing ----------------------------------------------------------------------

def focus(centre, size=0.26, name="_focus"):
    """A box around `centre` for the look tool to frame:
    mcp__blender__look(mode="angles", target=["_focus"]). Replaces an earlier one.

    The box is hidden (look frames hidden targets, so it never shows in the
    images) and never rendered; isolate() and show_all() leave it alone.
    """
    unfocus(name)
    orphan = bpy.data.meshes.get(name)
    if orphan is not None and orphan.users == 0:
        bpy.data.meshes.remove(orphan)
    h = size / 2
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([(x, y, z) for x in (-h, h) for y in (-h, h) for z in (-h, h)], [], [])
    obj = bpy.data.objects.new(name, mesh)
    obj.location = centre
    obj.display_type = "BOUNDS"
    obj.hide_render = True
    obj.hide_select = True
    bpy.context.scene.collection.objects.link(obj)
    obj.hide_set(True)
    return obj


def unfocus(name="_focus"):
    obj = bpy.data.objects.get(name)
    if obj is None:
        return
    mesh = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    if mesh is not None and mesh.users == 0:
        bpy.data.meshes.remove(mesh)


def isolate(names, with_body=True, with_armature=True):
    """Hide every mesh in the viewport except `names` (items or objects) and the body.
    with_armature=False also hides the bones. Undo with show_all()."""
    if isinstance(names, str):
        names = [names]
    keep = {o.name for n in names for o in _find(n)}
    if with_body:
        keep.add(C.BODY)
    for obj in bpy.context.view_layer.objects:
        if obj.type == "MESH" and not obj.name.startswith("_focus"):
            obj.hide_set(obj.name not in keep)
            if obj.name in keep:
                obj.hide_viewport = False
        elif obj.type == "ARMATURE":
            obj.hide_set(not with_armature)
    return sorted(keep)


def show_all():
    """The master's default visibility: every mesh but the eyes, and the armature."""
    for obj in bpy.context.view_layer.objects:
        if obj.type in ("MESH", "ARMATURE") and not obj.name.startswith("_focus"):
            obj.hide_viewport = False
            obj.hide_set(obj.name == C.EYES)


def viewport_colors(mode="MATERIAL"):
    """Solid-shading colour source in every 3D viewport (MATERIAL, TEXTURE for the
    face and eye images, OBJECT, ...). Returns the previous mode."""
    valid = [i.identifier for i in bpy.types.View3DShading.bl_rna.properties["color_type"].enum_items]
    if mode not in valid:
        raise ValueError(f"color_type {mode!r} not in {valid}")
    previous = None
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            for space in area.spaces:
                if space.type == "VIEW_3D":
                    previous = previous or space.shading.color_type
                    space.shading.color_type = mode
    return previous


# --- Checks -----------------------------------------------------------------------

def _key_co(key):
    co = np.empty(len(key.data) * 3)
    key.data.foreach_get("co", co)
    return co.reshape(-1, 3)


def _to_world(obj, co):
    m = np.array(obj.matrix_world)
    return co @ m[:3, :3].T + m[:3, 3]


def slider_spikes(names=None, threshold=0.005):
    """Shape keys of skinned items that move vertices where the body key does not.

    Prints each item's max displacement per key (mm). A key is flagged when it
    moves an item vertex by more than `threshold` m and that vertex rests
    farther than SPIKE_REACH from every body vertex the same key moves by more
    than SPIKE_SKIN_MOVE. Returns [(item, key, max move of the flagged vertices)].
    """
    body =bpy.data.objects[C.BODY]
    body_keys = body.data.shape_keys.key_blocks
    body_rest = _key_co(body_keys[0])
    body_world = _to_world(body, body_rest)
    regions = {}
    for name in C.SHAPE_KEYS:
        if name not in body_keys:
            continue
        moved = np.nonzero(np.linalg.norm(_key_co(body_keys[name]) - body_rest, axis=1) > SPIKE_SKIN_MOVE)[0]
        regions[name] = None
        if len(moved):
            regions[name] = KDTree(len(moved))
            for i in moved:
                regions[name].insert(body_world[i], int(i))
            regions[name].balance()

    if isinstance(names, str):
        names = [names]
    wanted = {o.name for n in names for o in _find(n)} if names else None
    items = sorted((o for o in bpy.data.objects
                    if o.type == "MESH" and o.name.startswith("SKM_F_") and o.name not in (C.BODY, C.EYES)
                    and o.data.shape_keys and (wanted is None or o.name in wanted)), key=lambda o: o.name)
    flagged, rows, details = [], [], []
    for obj in items:
        keys = obj.data.shape_keys.key_blocks
        rest = _key_co(keys[0])
        world = _to_world(obj, rest)
        row = {}
        for key in keys[1:]:
            move = np.linalg.norm(_key_co(key) - rest, axis=1)
            stray = {}  # vertex -> distance to the nearest skin the key moves (None: it moves none)
            if key.name in regions:
                tree = regions[key.name]
                for i in np.nonzero(move > threshold)[0]:
                    dist = tree.find(world[i])[2] if tree is not None else None
                    if dist is None or dist > SPIKE_REACH:
                        stray[int(i)] = dist
            row[key.name] = (float(move.max()) if len(move) else 0.0, bool(stray))
            if stray:
                worst = max(stray, key=lambda i: move[i])
                reach = "no skin moves" if stray[worst] is None else f"{stray[worst]:.3f} m from moved skin"
                flagged.append((obj.name, key.name, float(move[worst])))
                details.append(f"  {obj.name} {key.name}: {len(stray)} vertices, worst #{worst} moves "
                               f"{move[worst] * 1000:.1f} mm at ({', '.join(f'{c:.3f}' for c in world[worst])}), "
                               f"{reach}")
        rows.append((obj.name, row))

    keys = list(C.SHAPE_KEYS)
    print("[slider_spikes] max move per key, mm ('!' = moves vertices away from the skin the key moves)")
    print("  " + "  ".join(f"{k + 1} {n}" for k, n in enumerate(keys)))
    print(f"{'item':<22}" + "".join(f"{k + 1:>6}" for k in range(len(keys))))
    for name, row in rows:
        cells = "".join(f"{row[k][0] * 1000:5.1f}{'!' if row[k][1] else ' '}" if k in row else "    - "
                        for k in keys)
        print(f"{name.replace('SKM_F_', ''):<22}{cells}")
    print(f"[slider_spikes] {len(flagged)} flagged key(s)" + (":" if flagged else ""))
    for line in details:
        print(line)
    return flagged


def _mtime(path):
    return os.path.getmtime(path) if os.path.exists(path) else None


def status():
    """Print and return the state of the body, every item and every recipe."""
    body =bpy.data.objects[C.BODY]
    topology = cosmetic.body_topology_hash(body)
    report = {"body": {"vertices": len(body.data.vertices), "topologyHash": topology}, "items": [], "recipes": []}
    print(f"[status] {C.BODY}: {len(body.data.vertices)} vertices, topology {topology}")
    print(f"{'item':<28}{'in file':<10}{'fbx':<5}{'sidecar':<9}{'mask':<10}state")
    exported = set()
    for module in item_names():
        mod = _module(module)
        try:
            objects = recipes.item_objects(mod.NAME)
        except KeyError:
            objects = []
        skinned = any(o.data.shape_keys for o in objects)
        bound = skinned and all(any(m.type == "ARMATURE" for m in o.modifiers) for o in objects)
        built = "no" if not objects else ("bound" if bound else "rigid" if mod.NAME.startswith("SM_") else "unbound")
        fbx = os.path.join(COSMETICS_DIR, mod.NAME + ".fbx")
        sidecar = os.path.join(COSMETICS_DIR, mod.NAME + ".cosmetic.json")
        mask = "-"
        if os.path.exists(sidecar):
            with open(sidecar, encoding="utf-8") as f:
                mask = "ok" if json.load(f).get("body", {}).get("topologyHash") == topology else "MISMATCH"
        state = "ok"
        if not os.path.exists(fbx):
            state = "not exported"
        else:
            name = "items." + module
            sources = [name] + [d for d in sorted(_deps(name)) if d in STALE_SOURCES]
            newer = [s for s in sources if _mtime(_source(s)) > _mtime(fbx)]
            if newer:
                state = "stale (" + ", ".join(s.split(".")[-1] + ".py" for s in newer) + " newer)"
            else:
                exported.add(mod.NAME)
        entry = {"item": mod.NAME, "module": module, "inFile": built, "fbx": os.path.exists(fbx),
                 "sidecar": os.path.exists(sidecar), "mask": mask, "state": state}
        report["items"].append(entry)
        print(f"{mod.NAME:<28}{built:<10}{'yes' if entry['fbx'] else 'no':<5}"
              f"{'yes' if entry['sidecar'] else 'no':<9}{mask:<10}{state}")

    print(f"{'recipe':<16}{'face':<7}{'eyes':<7}items")
    for path in sorted(glob.glob(os.path.join(recipes.RECIPE_DIR, "*.recipe.json"))):
        recipe = recipes.load(path)
        newest = max(_mtime(_source("faces")), _mtime(path))
        cells = []
        for key in ("faceTexture", "eyeTexture"):
            t = _mtime(os.path.join(recipes.TEXTURE_DIR, recipe[key] + ".png"))
            cells.append("no" if t is None else "stale" if t < newest else "yes")
        missing = [i["id"] for i in recipe["items"] if i["id"] not in exported]
        items = "ok" if not missing else "not exported/stale: " + ", ".join(missing)
        report["recipes"].append({"id": recipe["id"], "face": cells[0], "eyes": cells[1], "items": missing})
        print(f"{recipe['id']:<16}{cells[0]:<7}{cells[1]:<7}{items}")
    return report


# --- Recipes and renders ------------------------------------------------------------

def _recipes(ids):
    found = recipes.all_recipes()
    if ids is None:
        return found
    if isinstance(ids, str):
        ids = [ids]
    by_id = {r["id"]: r for r in found}
    picked = []
    for i in ids:
        recipe = by_id.get(i) or by_id.get("CH_F_" + i)
        if recipe is None:
            raise KeyError(f"no recipe {i!r}; recipes: {', '.join(by_id)}")
        picked.append(recipe)
    return picked


def paint_faces(ids=None):
    """Reload, then paint face and eye textures for every recipe (or `ids`). Returns the PNG paths."""
    reload()
    return _paint_faces(ids)


def _paint_faces(ids):
    written = []
    for recipe in _recipes(ids):
        faces.paint(recipe, recipes.TEXTURE_DIR)
        written += [os.path.join(recipes.TEXTURE_DIR, recipe[k] + ".png") for k in ("faceTexture", "eyeTexture")]
    return written


def lineup():
    """Reload, then paint every recipe and render the M4 line-up evidence. Returns the paths."""
    reload()
    return recipes.run()


def item_sheet(name, out_dir=None, views=("front", "side", "back", "three_quarter"), focus=None, size=900):
    """Render one item worn on the body (body, eyes and the item; everything else is
    left out of the render) to <out_dir>/<item id>_<view>.png. focus=(centre, extent)
    frames a close-up, e.g. (tasks.HEAD, 0.4). Returns the paths."""
    objects = _find(name)
    item = objects[0].get("charkit_item", objects[0].name)
    shown = [bpy.data.objects[C.BODY], bpy.data.objects[C.EYES], *objects]
    saved = {o.name: o.hide_render for o in bpy.data.objects}
    try:
        for obj in bpy.data.objects:
            if obj.type not in ("CAMERA", "LIGHT"):
                obj.hide_render = obj not in shown
        return review.render(item, out_dir or ITEM_SHEET_DIR, views=views, size=size, objects=shown, focus=focus)
    finally:
        for obj in bpy.data.objects:
            if obj.name in saved:
                obj.hide_render = saved[obj.name]


# --- Save ---------------------------------------------------------------------------

def _tidy():
    """Remove previews, generated images, unused preview materials and focus boxes."""
    review.clear_previews()
    for image in [i for i in bpy.data.images if i.name.startswith(("_pv_", "T_F_"))]:
        bpy.data.images.remove(image)
    for mat in [m for m in bpy.data.materials if m.name.startswith("_pv_") and m.users == 0]:
        bpy.data.materials.remove(mat)
    for obj in [o for o in bpy.data.objects if o.name.startswith("_focus")]:
        unfocus(obj.name)


def _write():
    bpy.ops.wm.save_mainfile(compress=True)


def save():
    """Tidy the scene, restore default visibility and save the master. Returns (path, bytes)."""
    path = bpy.data.filepath
    if not path:
        raise RuntimeError("the open file has never been saved; open the master first")
    _tidy()
    show_all()
    _write()
    size = os.path.getsize(path)
    print(f"[tasks] saved {path} ({size / 1e6:.1f} MB)")
    return path, size
