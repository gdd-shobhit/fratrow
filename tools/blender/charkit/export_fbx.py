"""Export characters and cosmetics to FBX for Unity.

Settings give Unity scale 1, no -90 degree root rotation and the character
facing +Z: Blender -Y forward, Z up -> FBX forward -Z, up Y, transforms applied.
The A-pose bind is exported as-is; Unity's Humanoid avatar gets its T-pose
reference from the importer (Assets/Art/Characters/Editor). Shape keys and
socket bones are kept; leaf bones are not added.

    from charkit import export_fbx
    export_fbx.run()                 # master -> Assets/Art/Characters/Female/SK_Female_Base.fbx
    export_fbx.verify(path)          # re-import into a scratch scene and compare
"""
import os

import bpy

from . import config as C

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
FEMALE_DIR = os.path.join(REPO, "Assets", "Art", "Characters", "Female")
BASE_FBX = "SK_Female_Base.fbx"

FBX_SETTINGS = dict(
    use_selection=True,
    object_types={"ARMATURE", "MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=True,
    use_mesh_modifiers=False,     # keeps shape keys; the armature becomes the skin cluster
    mesh_smooth_type="FACE",
    use_tspace=False,
    add_leaf_bones=False,
    primary_bone_axis="Y",
    secondary_bone_axis="X",
    use_armature_deform_only=False,  # sockets are non-deforming but must ship
    armature_nodetype="NULL",
    bake_anim=False,
    use_custom_props=False,
    path_mode="AUTO",
    embed_textures=False,
)


def export(objects, path):
    """Export exactly `objects` (armature first) to `path`."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    view_layer = bpy.context.view_layer
    for obj in view_layer.objects:
        obj.select_set(False)
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
    view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=path, **FBX_SETTINGS)
    return path


def run():
    from . import review
    review.reset_pose()
    arm = bpy.data.objects[C.ARMATURE]
    body = bpy.data.objects[C.BODY]
    eyes = bpy.data.objects[C.EYES]
    for key in body.data.shape_keys.key_blocks[1:]:
        key.value = 0.0
    path = export([arm, body, eyes], os.path.join(FEMALE_DIR, BASE_FBX))
    print(f"[export_fbx] wrote {path} ({os.path.getsize(path) // 1024} KB)")
    return path


def verify(path):
    """Re-import the FBX into a temporary scene and compare it with the master."""
    master_arm = bpy.data.objects[C.ARMATURE]
    master_body = bpy.data.objects[C.BODY]
    scene = bpy.data.scenes.new("_fbx_verify")
    window_scene = bpy.context.window.scene
    bpy.context.window.scene = scene
    problems = []
    try:
        bpy.ops.import_scene.fbx(filepath=path, axis_forward="-Z", axis_up="Y",
                                 automatic_bone_orientation=False, ignore_leaf_bones=False)
        objs = list(scene.objects)
        arms = [o for o in objs if o.type == "ARMATURE"]
        # Names collide with the master's, so Blender suffixes them (".001").
        meshes = {o.name.split(".")[0]: o for o in objs if o.type == "MESH"}
        if len(arms) != 1:
            problems.append(f"expected 1 armature, got {len(arms)}")
        else:
            got = {b.name for b in arms[0].data.bones}
            want = {b.name for b in master_arm.data.bones}
            if got != want:
                problems.append(f"bones differ: missing {sorted(want - got)}, extra {sorted(got - want)}")
        body = meshes.get(C.BODY)
        if body is None:
            problems.append(f"{C.BODY} missing; meshes: {sorted(meshes)}")
        else:
            keys = [k.name for k in body.data.shape_keys.key_blocks[1:]] if body.data.shape_keys else []
            if keys != list(C.SHAPE_KEYS):
                problems.append(f"shape keys differ: {keys}")
            tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
            want_tris = sum(len(p.vertices) - 2 for p in master_body.data.polygons)
            if tris != want_tris:
                problems.append(f"body tris {tris} != {want_tris}")
            bpy.context.view_layer.update()
            zs = [(body.matrix_world @ v.co).z for v in body.data.vertices]
            height = max(zs) - min(zs)
            if abs(height - C.TARGET_HEIGHT) > 0.01:
                problems.append(f"round-trip height {height:.3f} m")
            groups = {g.name for g in body.vertex_groups}
            if len(groups) != len(master_body.vertex_groups):
                problems.append(f"vertex groups {len(groups)} != {len(master_body.vertex_groups)}")
        print(f"[export_fbx] verify {os.path.basename(path)}: objects {sorted(o.name for o in objs)}")
    finally:
        bpy.context.window.scene = window_scene
        for obj in list(scene.objects):
            data = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if data is not None and data.users == 0:
                if isinstance(data, bpy.types.Mesh):
                    bpy.data.meshes.remove(data)
                elif isinstance(data, bpy.types.Armature):
                    bpy.data.armatures.remove(data)
        bpy.data.scenes.remove(scene)
        bpy.data.orphans_purge(do_recursive=True)
    for p in problems:
        print("[export_fbx] PROBLEM:", p)
    if not problems:
        print("[export_fbx] round-trip matches the master")
    return problems
