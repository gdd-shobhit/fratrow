"""Check the master rig against the pipeline contract.

Prints observations and returns a list of problems; it does not fix anything.
Run after any rebuild, and before exporting.

    from charkit import validate
    problems = validate.run()
"""
import bpy

from . import config as C
from .bind import MAX_INFLUENCES
from .skeleton import FINGERS, HIERARCHY, mirror_name

UNITY_REQUIRED = ("Hips", "Spine", "Head", "LeftUpLeg", "LeftLeg", "LeftFoot",
                  "LeftArm", "LeftForeArm", "LeftHand")


def expected_bones():
    names = []
    for name in HIERARCHY:
        names.append(name)
        if "Left" in name:
            names.append(mirror_name(name))
    return names


def run():
    problems = []
    arm = bpy.data.objects.get(C.ARMATURE)
    body = bpy.data.objects.get(C.BODY)
    eyes = bpy.data.objects.get(C.EYES)
    if not (arm and body and eyes):
        return [f"missing objects: armature={bool(arm)} body={bool(body)} eyes={bool(eyes)}"]
    bones = arm.data.bones

    expected = expected_bones()
    missing = [b for b in expected if b not in bones]
    extra = [b.name for b in bones if b.name not in expected]
    if missing or extra:
        problems.append(f"bone names: missing {missing}, unexpected {extra}")
    for name in UNITY_REQUIRED + tuple(mirror_name(n) for n in UNITY_REQUIRED if "Left" in n):
        if name in bones and not bones[name].use_deform:
            problems.append(f"{name} must deform")
    sockets = [b for b in bones if b.name.startswith("Socket_")]
    if any(b.use_deform for b in sockets):
        problems.append("socket bones must not deform")

    # Symmetry: every Left bone mirrors its Right twin.
    for b in bones:
        if "Left" in b.name:
            twin = bones.get(mirror_name(b.name))
            for a, c in ((b.head_local, twin.head_local), (b.tail_local, twin.tail_local)):
                if abs(a.x + c.x) > 1e-4 or abs(a.y - c.y) > 1e-4 or abs(a.z - c.z) > 1e-4:
                    problems.append(f"{b.name} is not mirrored by {twin.name}")
                    break

    # Weights.
    deform = {b.name for b in bones if b.use_deform}
    for obj in (body, eyes):
        if not any(m.type == "ARMATURE" and m.object == arm for m in obj.modifiers):
            problems.append(f"{obj.name} has no Armature modifier on {arm.name}")
        names = {g.index: g.name for g in obj.vertex_groups}
        stray = [n for n in names.values() if n not in deform]
        if stray:
            problems.append(f"{obj.name} has groups for non-deform bones: {stray}")
        over, unweighted, unnormalised = 0, 0, 0
        for v in obj.data.vertices:
            ws = [g.weight for g in v.groups if names.get(g.group) in deform]
            over += len(ws) > MAX_INFLUENCES
            unweighted += not ws
            unnormalised += bool(ws) and abs(sum(ws) - 1.0) > 1e-3
        if over or unweighted or unnormalised:
            problems.append(f"{obj.name}: {over} verts over {MAX_INFLUENCES} influences, "
                            f"{unweighted} unweighted, {unnormalised} not normalised")
    body_bones = {g.name for g in body.vertex_groups}
    no_weights = sorted(deform - body_bones - {"LeftEye", "RightEye"})
    if no_weights:
        problems.append(f"deform bones with no body weights: {no_weights}")

    # Mesh contract.
    mats = [m.name for m in body.data.materials]
    if mats != [C.REGION_PREFIX + r for r in C.REGIONS]:
        problems.append(f"body material slots {mats} != regions {list(C.REGIONS)}")
    ids = body.data.uv_layers.get(C.VERTEX_ID_UV)
    if ids is None or [uv.name for uv in body.data.uv_layers][:2] != ["UVMap", C.VERTEX_ID_UV]:
        problems.append(f"body UV channels must be ['UVMap', '{C.VERTEX_ID_UV}'] (Unity uv, uv2)")
    elif any(int(ids.data[l.index].uv.x) != l.vertex_index for l in body.data.loops):
        problems.append(f"{C.VERTEX_ID_UV} does not match vertex indices; re-run body_prepare.add_vertex_ids")
    keys = [k.name for k in body.data.shape_keys.key_blocks[1:]] if body.data.shape_keys else []
    if keys != list(C.SHAPE_KEYS):
        problems.append(f"shape keys {keys} != config.SHAPE_KEYS")
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    eye_tris = sum(len(p.vertices) - 2 for p in eyes.data.polygons)
    budget = sum(t for _, t in C.DECIMATE_BUDGETS)
    if tris > budget * 1.05:
        problems.append(f"body {tris} tris over budget {budget}")
    height = max(v.co.z for v in body.data.vertices) - min(v.co.z for v in body.data.vertices)
    if abs(height - C.TARGET_HEIGHT) > 0.005:
        problems.append(f"body height {height:.3f} m != {C.TARGET_HEIGHT}")
    if arm.matrix_world != arm.matrix_world.Identity(4):
        problems.append("armature object transform must be identity (bake it before export)")

    print(f"[validate] bones {len(bones)} ({len(deform)} deform, {len(sockets)} sockets), "
          f"body {tris} tris / {len(body.data.vertices)} verts, eyes {eye_tris} tris, "
          f"height {height:.3f} m, {len(keys)} shape keys, {len(FINGERS) * 2} finger chains")
    for p in problems:
        print("[validate] PROBLEM:", p)
    if not problems:
        print("[validate] no problems found")
    return problems
