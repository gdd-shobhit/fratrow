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


def preview_variants(variants, spacing=0.75):
    """Bake each {name: slider values (+ "height")} into a static copy, side by side.

    Returns the created objects; delete them with clear_previews(). The rig and
    the master body are left at their current slider values afterwards.
    """
    body = bpy.data.objects[C.BODY]
    eyes = bpy.data.objects[C.EYES]
    keys = body.data.shape_keys.key_blocks
    saved = {k.name: k.value for k in keys[1:]}
    coll = bpy.data.collections.get("_preview") or bpy.data.collections.new("_preview")
    if coll.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(coll)
    made = []
    try:
        for n, (label, values) in enumerate(variants.items()):
            for k in keys[1:]:
                k.value = values.get(k.name, 0.0)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            x = (n - (len(variants) - 1) / 2) * spacing
            for src in (body, eyes):
                mesh = bpy.data.meshes.new_from_object(src.evaluated_get(depsgraph))
                obj = bpy.data.objects.new(f"_preview_{label}_{src.name}", mesh)
                obj.matrix_world = src.matrix_world
                obj.scale = Vector((1, 1, 1)) * values.get("height", 1.0)
                obj.location.x += x
                coll.objects.link(obj)
                made.append(obj)
    finally:
        for k in keys[1:]:
            k.value = saved[k.name]
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


def render(name, out_dir, views=("front", "three_quarter"), size=900, objects=None, focus=None):
    """Workbench render of the character from each view to <out_dir>/<name>_<view>.png.

    focus=(centre, extent) frames a close-up instead of the whole character.
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
    try:
        scene.camera = cam
        try:
            scene.render.engine = "BLENDER_WORKBENCH"
        except TypeError:
            pass
        shading = scene.display.shading
        shading.light = "STUDIO"
        shading.color_type = "MATERIAL"
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
        bpy.data.objects.remove(cam, do_unlink=True)
        bpy.data.cameras.remove(cam_data)
