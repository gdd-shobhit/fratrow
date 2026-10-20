"""Character recipes in Blender: the same *.recipe.json files Unity imports
(Assets/Art/Characters/Female/Recipes), used to paint each character's face and
eye textures (charkit.faces) and to preview the dressed characters side by side.

A recipe names a base model, height (whole-body scale), skin colour, body
slider values, face/eye textures and the worn items with optional colours per
material slot (sRGB hex; empty keeps the item's default). The "face" and
"iris" blocks are painting parameters for charkit.faces; Unity ignores them.

    from charkit import recipes
    recipes.run()   # paint every recipe's textures, render the line-up evidence
"""
import glob
import json
import os

import bmesh
import bpy
from mathutils import Matrix

from . import config as C
from . import cosmetic, export_fbx, faces, review

RECIPE_DIR = os.path.join(export_fbx.FEMALE_DIR, "Recipes")
TEXTURE_DIR = os.path.join(export_fbx.FEMALE_DIR, "Textures")
EVIDENCE_DIR = os.path.join(export_fbx.REPO, "production", "qa", "evidence", "m4-trio")


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def all_recipes():
    return [load(p) for p in sorted(glob.glob(os.path.join(RECIPE_DIR, "*.recipe.json")))]


def item_objects(item_id):
    """The Blender objects of an item: the object itself, or one per socket for rigid items."""
    obj = bpy.data.objects.get(item_id)
    if obj is not None:
        return [obj]
    found = [o for o in bpy.data.objects if o.name.startswith(item_id + "_") and o.type == "MESH"]
    if not found:
        raise KeyError(f"item {item_id} is not built in this file (run its charkit.items module)")
    return found


def exported_mask(item_id):
    """Body vertices an exported item hides, decoded from its sidecar (what Unity uses)."""
    import base64
    path = os.path.join(export_fbx.FEMALE_DIR, "Cosmetics", item_id + ".cosmetic.json")
    bits = base64.b64decode(load(path).get("hiddenBodyVertices") or "")
    return {i for i in range(len(bits) * 8) if bits[i >> 3] & (1 << (i & 7))}


def _textured_material(name, image, roughness):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    if mat.node_tree is None:
        mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    tex = next((n for n in nodes if n.type == "TEX_IMAGE"), None) or nodes.new("ShaderNodeTexImage")
    tex.image = image
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = roughness
    nodes.active = tex
    return mat


def _item_materials(recipe_id, item, obj):
    colors = item.get("colors") or []
    mats = []
    for k, src in enumerate(obj.data.materials):
        color = faces.hex_to_srgb(colors[k]) if k < len(colors) else cosmetic.material_srgb(src)
        mats.append(cosmetic.material(f"_pv_{recipe_id}_{src.name}", color, roughness=0.7))
    return mats


def lineup(recipes, spacing=0.8, paint=True):
    """Static dressed copies of each recipe, side by side (as Unity draws them:
    sliders, skin mask, height, colours, face and eye textures). Returns the
    objects; delete them with review.clear_previews()."""
    review.clear_previews()
    coll = bpy.data.collections.new("_preview")
    bpy.context.scene.collection.children.link(coll)
    body, eyes = bpy.data.objects[C.BODY], bpy.data.objects[C.EYES]
    eyes_hidden = eyes.hide_get()
    eyes.hide_set(False)
    keyed = [o for o in bpy.data.objects if o.type == "MESH" and o.data.shape_keys]
    saved = {o.name: {k.name: k.value for k in o.data.shape_keys.key_blocks[1:]} for o in keyed}
    made = []
    try:
        for n, recipe in enumerate(recipes):
            rid = recipe["id"]
            if paint:
                face_img, eye_img = faces.paint(recipe, TEXTURE_DIR)
            else:
                face_img, eye_img = bpy.data.images[f"_pv_{recipe['faceTexture']}"], bpy.data.images[recipe["eyeTexture"]]
            worn = [(item, obj) for item in recipe["items"] for obj in item_objects(item["id"])]
            skinned = [obj for _, obj in worn if obj.data.shape_keys]
            hidden = set()
            for item in recipe["items"]:
                hidden |= exported_mask(item["id"])
            review.set_sliders({s["name"]: s["value"] for s in recipe["sliders"]}, [o.name for o in skinned])
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            place = Matrix.Translation(((n - (len(recipes) - 1) / 2) * spacing, 0, 0)) @ Matrix.Scale(recipe["height"], 4)
            skin = _textured_material(f"_pv_{rid}_skin", face_img, 0.6)
            iris = _textured_material(f"_pv_{rid}_eyes", eye_img, 0.25)
            parts = [(body, [skin]), (eyes, [iris])] + [(obj, _item_materials(rid, item, obj)) for item, obj in worn]
            for src, mats in parts:
                mesh = bpy.data.meshes.new_from_object(src.evaluated_get(depsgraph))
                if src is body and hidden:
                    bm = bmesh.new()
                    bm.from_mesh(mesh)
                    bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(v.index in hidden for v in f.verts)],
                                     context="FACES_ONLY")
                    bm.to_mesh(mesh)
                    bm.free()
                mesh.materials.clear()
                for mat in mats:
                    mesh.materials.append(mat)
                if len(mats) == 1:
                    for poly in mesh.polygons:
                        poly.material_index = 0
                obj = bpy.data.objects.new(f"_preview_{rid}_{src.name}", mesh)
                obj.matrix_world = place @ src.matrix_world
                coll.objects.link(obj)
                made.append(obj)
    finally:
        for o in keyed:
            for k in o.data.shape_keys.key_blocks[1:]:
                k.value = saved[o.name][k.name]
        eyes.hide_set(eyes_hidden)
    return made


def run(out_dir=EVIDENCE_DIR):
    """Paint every recipe's textures and render the line-up plus face close-ups."""
    recipes = all_recipes()
    made = lineup(recipes)
    # Renders include everything not hidden from rendering: leave out the master and its items.
    saved = {o.name: o.hide_render for o in bpy.data.objects}
    for o in bpy.data.objects:
        if o not in made and o.type in ("MESH", "ARMATURE"):
            o.hide_render = True
    try:
        written = review.render("trio_lineup", out_dir, views=("front", "three_quarter", "back"), size=1400,
                                objects=made, color_type="TEXTURE")
        spacing = 0.8
        for n, recipe in enumerate(recipes):
            x = (n - (len(recipes) - 1) / 2) * spacing
            z = 1.52 * recipe["height"]
            written += review.render(f"{recipe['id']}_face", out_dir, views=("front", "three_quarter"), size=700,
                                     objects=made, focus=((x, -0.02, z), 0.32), color_type="TEXTURE")
    finally:
        for o in bpy.data.objects:
            o.hide_render = saved.get(o.name, o.hide_render)
        review.clear_previews()
    return written
