"""Deformation test poses and review renders.

Poses rotate pose bones in their local space. Every bone's local Z faces
forward (limbs, spine) or out of the back of the hand (fingers), so:
+X rotation swings a limb forward, -X curls a finger into the palm.

    from charkit import review
    review.apply_pose("squat"); review.render("squat", out_dir)
    review.reset_pose()
"""
import math
import os

import bpy
from mathutils import Euler, Quaternion, Vector

from . import config as C
from .skeleton import FINGERS


def _rot(x=0.0, y=0.0, z=0.0):
    return Euler((math.radians(x), math.radians(y), math.radians(z)), "XYZ").to_quaternion()


def _both(name, x=0.0, y=0.0, z=0.0, mirror_y=True, mirror_z=True):
    """Same rotation on Left and Right bones (twist/side-bend flipped for the mirror)."""
    return {f"Left{name}": _rot(x, y, z),
            f"Right{name}": _rot(x, -y if mirror_y else y, -z if mirror_z else z)}


def _fist(amount=1.0):
    pose = {}
    for finger in FINGERS:
        curl = (10, 40, 45) if finger == "Thumb" else (70, 90, 60)
        for k in range(3):
            pose.update(_both(f"Hand{finger}{k + 1}", x=-curl[k] * amount))
    return pose


POSES = {
    "rest": {},
    # +Z raises a limb sideways (abduction). Unity's avatar setup forces T-pose.
    "t_pose": {**_both("Arm", z=52)},
    # Overhead reach lifts the collarbone too, as real motion and mocap clips do.
    "arms_up": {**_both("Shoulder", z=25), **_both("Arm", z=110), **_both("ForeArm", z=10)},
    "arms_forward": {**_both("Arm", x=80), **_both("ForeArm", x=60)},
    "squat": {
        "Hips": ("loc", Vector((0, -0.33, 0))),
        "Spine": _rot(x=25), "Spine1": _rot(x=10),
        **_both("UpLeg", x=105), **_both("Leg", x=-120), **_both("Foot", x=20),
        **_both("Arm", x=70), **_both("ForeArm", x=20),
    },
    "walk": {
        "LeftUpLeg": _rot(x=30), "LeftLeg": _rot(x=-15),
        "RightUpLeg": _rot(x=-20), "RightLeg": _rot(x=-35), "RightFoot": _rot(x=-15),
        "LeftArm": _rot(x=-25), "RightArm": _rot(x=30), "RightForeArm": _rot(x=30),
        "Spine": _rot(y=8),
    },
    "fists": {**_fist(), **_both("Arm", x=60), **_both("ForeArm", x=90)},
    "twist": {"Spine": _rot(y=15), "Spine1": _rot(y=15), "Neck": _rot(y=20), "Head": _rot(y=30, x=-10)},
}


def reset_pose():
    arm = bpy.data.objects[C.ARMATURE]
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
        pb.scale = Vector((1, 1, 1))


def apply_pose(name):
    reset_pose()
    arm = bpy.data.objects[C.ARMATURE]
    for bone, value in POSES[name].items():
        pb = arm.pose.bones[bone]
        if isinstance(value, tuple) and value[0] == "loc":
            pb.location = value[1]
        else:
            pb.rotation_quaternion = value
    bpy.context.view_layer.update()


def set_sliders(values, items=()):
    """Set body slider values {name: 0..1} on the body and on worn items (same key names)."""
    for obj in [bpy.data.objects[C.BODY]] + [bpy.data.objects[n] for n in items]:
        if obj.data.shape_keys:
            for key in obj.data.shape_keys.key_blocks[1:]:
                key.value = values.get(key.name, 0.0)


def preview_variants(variants, spacing=0.75, items=(), pose=None):
    """Bake each {name: slider values (+ "height")} into a static copy, side by side.

    `items` are worn cosmetics (their sliders follow the body) and their skin
    masks are applied to the body copy, as Unity draws it; `pose` is a POSES
    name applied while baking. Returns the created objects; delete them with
    clear_previews(). Sliders and pose are restored afterwards.
    """
    import bmesh
    from . import cosmetic
    hidden = set()
    for name in items:
        hidden |= set(cosmetic.skin_mask(bpy.data.objects[name]))
    body = bpy.data.objects[C.BODY]
    sources = [body, bpy.data.objects[C.EYES]] + [bpy.data.objects[n] for n in items]
    keyed = [o for o in sources if o.data.shape_keys]
    saved = {o.name: {k.name: k.value for k in o.data.shape_keys.key_blocks[1:]} for o in keyed}
    coll = bpy.data.collections.get("_preview") or bpy.data.collections.new("_preview")
    if coll.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(coll)
    made = []
    try:
        if pose:
            apply_pose(pose)
        for n, (label, values) in enumerate(variants.items()):
            set_sliders(values, items)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            x = (n - (len(variants) - 1) / 2) * spacing
            for src in sources:
                mesh = bpy.data.meshes.new_from_object(src.evaluated_get(depsgraph))
                if src is body and hidden:
                    bm = bmesh.new()
                    bm.from_mesh(mesh)
                    bmesh.ops.delete(bm, context="FACES_ONLY", geom=[
                        f for f in bm.faces if all(v.index in hidden for v in f.verts)])
                    bm.to_mesh(mesh)
                    bm.free()
                obj = bpy.data.objects.new(f"_preview_{label}_{src.name}", mesh)
                obj.matrix_world = src.matrix_world
                obj.scale = Vector((1, 1, 1)) * values.get("height", 1.0)
                obj.location.x += x
                coll.objects.link(obj)
                made.append(obj)
    finally:
        for o in keyed:
            for k in o.data.shape_keys.key_blocks[1:]:
                k.value = saved[o.name][k.name]
        if pose:
            reset_pose()
    return made


def mask_preview(item_names, offset_x=0.8):
    """Static copies of the body with the items' skin masks applied, plus the items.

    What Unity will draw: look at it from every side to catch see-through gaps.
    Returns the created objects; delete them with clear_previews().
    """
    import bmesh
    from . import cosmetic
    clear_previews()
    body = bpy.data.objects[C.BODY]
    hidden = set()
    for name in item_names:
        hidden |= set(cosmetic.skin_mask(bpy.data.objects[name]))
    coll = bpy.data.collections.new("_preview")
    bpy.context.scene.collection.children.link(coll)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(body.evaluated_get(depsgraph))
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(v.index in hidden for v in f.verts)],
                     context="FACES_ONLY")
    bm.to_mesh(mesh)
    bm.free()
    made = [bpy.data.objects.new("_preview_masked_body", mesh)]
    for name in item_names:
        src = bpy.data.objects[name]
        made.append(bpy.data.objects.new(f"_preview_{name}",
                                         bpy.data.meshes.new_from_object(src.evaluated_get(depsgraph))))
    for obj in made:
        obj.location.x = offset_x
        coll.objects.link(obj)
    return made


def clear_previews():
    coll = bpy.data.collections.get("_preview")
    if coll is None:
        return
    for obj in list(coll.objects):
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)
    bpy.data.collections.remove(coll)


VIEWS = {
    "front": Vector((0, -1, 0.05)),
    "side": Vector((1, 0, 0.05)),
    "back": Vector((0, 1, 0.05)),
    "three_quarter": Vector((0.7, -0.7, 0.25)),
}


STAMP_METADATA = ("use_stamp_date", "use_stamp_time", "use_stamp_render_time", "use_stamp_memory",
                  "use_stamp_hostname", "use_stamp_filename", "use_stamp_frame", "use_stamp_frame_range")


def render(name, out_dir, views=("front", "three_quarter"), size=900, objects=None, focus=None,
           color_type="MATERIAL"):
    """Workbench render of the character from each view to <out_dir>/<name>_<view>.png.

    focus=(centre, extent) frames a close-up instead of the whole character;
    color_type="TEXTURE" shows image textures (face and eyes).
    """
    scene = bpy.context.scene
    objects = objects or [o for o in bpy.data.collections[C.COLLECTION].all_objects if o.type == "MESH"]
    if focus:
        centre, extent = Vector(focus[0]), focus[1]
    else:
        depsgraph = bpy.context.evaluated_depsgraph_get()
        pts = []
        for obj in objects:
            ev = obj.evaluated_get(depsgraph)
            mesh = ev.to_mesh()
            pts += [ev.matrix_world @ v.co for v in mesh.vertices]
            ev.to_mesh_clear()
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        centre, extent = (lo + hi) / 2, max(hi - lo)

    cam_data = bpy.data.cameras.new("_review_cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = extent * 1.15
    cam = bpy.data.objects.new("_review_cam", cam_data)
    scene.collection.objects.link(cam)
    saved = (scene.camera, scene.render.engine, scene.render.resolution_x, scene.render.resolution_y,
             scene.render.filepath, scene.render.film_transparent)
    # No date/time metadata in the PNGs: unchanged renders stay byte-identical in git.
    stamps = {a: getattr(scene.render, a) for a in STAMP_METADATA if hasattr(scene.render, a)}
    for attr in stamps:
        setattr(scene.render, attr, False)
    try:
        scene.camera = cam
        try:
            scene.render.engine = "BLENDER_WORKBENCH"
        except TypeError:
            pass
        shading = scene.display.shading
        shading.light = "STUDIO"
        shading.color_type = color_type
        shading.show_cavity = True
        scene.render.resolution_x = scene.render.resolution_y = size
        scene.render.film_transparent = False
        os.makedirs(out_dir, exist_ok=True)
        written = []
        for view in views:
            direction = VIEWS[view].normalized()
            cam.location = centre + direction * (extent * 3)
            cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
            path = os.path.join(out_dir, f"{name}_{view}.png")
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            written.append(path)
        return written
    finally:
        (scene.camera, scene.render.engine, scene.render.resolution_x, scene.render.resolution_y,
         scene.render.filepath, scene.render.film_transparent) = saved
        for attr, value in stamps.items():
            setattr(scene.render, attr, value)
        bpy.data.objects.remove(cam, do_unlink=True)
        bpy.data.cameras.remove(cam_data)
