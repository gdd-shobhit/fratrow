"""Bind the body and eyes to SK_Human.

- Body: Blender's automatic (heat) weights for the big joints, then the hands
  are overwritten with weights taken from the finger-segment face sets, which
  is cleaner than heat weights on low-poly fingers.
- Eyes: each eyeball 100% on its eye bone.
- Every mesh is limited to MAX_INFLUENCES bones per vertex and normalised.

    from charkit import bind
    bind.run()
"""
from collections import defaultdict

import bpy

from . import config as C
from .skeleton import FINGERS, PALM, mirror_name

MAX_INFLUENCES = 4  # Unity's default "Skin Weights: 4 bones"
HAND_SMOOTH_ITERATIONS = 2  # spreads each knuckle hinge over a neighbouring ring
HAND_SMOOTH_FACTOR = 0.5


def _select_only(objects, active):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for obj in bpy.context.view_layer.objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = active


def _unbind(obj):
    obj.vertex_groups.clear()
    for mod in [m for m in obj.modifiers if m.type == "ARMATURE"]:
        obj.modifiers.remove(mod)
    obj.parent = None


def _attach(obj, arm):
    """Parent with an Armature modifier, keeping existing vertex groups."""
    obj.parent = arm
    obj.matrix_parent_inverse = arm.matrix_world.inverted()
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm


def auto_weights(body, arm):
    """Heat weights from every deform bone except the eyes."""
    _unbind(body)
    eye_bones = [b for b in arm.data.bones if b.name.endswith("Eye")]
    for bone in eye_bones:
        bone.use_deform = False
    try:
        _select_only([body, arm], arm)
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    finally:
        for bone in eye_bones:
            bone.use_deform = True
    missing = [b.name for b in arm.data.bones
               if b.use_deform and not b.name.endswith("Eye") and b.name not in body.vertex_groups]
    if missing:
        raise RuntimeError(f"heat weighting gave no weights to: {missing}")


def hand_weights(body):
    """Overwrite palm + finger weights with face-set based ones.

    A vertex inside one finger segment follows that segment's bone; a vertex on
    the seam between two segments is split evenly. Vertices that also touch the
    forearm keep their heat weights, so the wrist stays smooth.
    """
    mesh = body.data
    sets = [0] * len(mesh.polygons)
    mesh.attributes[C.FACE_SET_ATTR].data.foreach_get("value", sets)
    bone_of = {PALM: "LeftHand"}
    for finger, segments in FINGERS.items():
        for k, fs in enumerate(segments):
            bone_of[fs] = f"LeftHand{finger}{k + 1}"
    bones_at = defaultdict(set)
    outside = set()
    for poly in mesh.polygons:
        fs = sets[poly.index]
        for v in poly.vertices:
            if fs in bone_of:
                bones_at[v].add(bone_of[fs])
            else:
                outside.add(v)
    groups = body.vertex_groups
    changed = 0
    for v, bones in bones_at.items():
        if v in outside:
            continue
        if mesh.vertices[v].co.x < 0:
            bones = {mirror_name(b) for b in bones}
        for g in mesh.vertices[v].groups:
            groups[g.group].remove([v])
        for b in bones:
            group = groups.get(b) or groups.new(name=b)
            group.add([v], 1.0 / len(bones), "REPLACE")
        changed += 1
    hand = {v for v in bones_at if v not in outside}
    _smooth_weights(body, hand, HAND_SMOOTH_ITERATIONS, HAND_SMOOTH_FACTOR)
    return changed


def _smooth_weights(obj, verts, iterations, factor):
    """Blend each vertex's weights toward its neighbours' average (verts only)."""
    mesh = obj.data
    neighbours = defaultdict(set)
    for e in mesh.edges:
        a, b = e.vertices
        neighbours[a].add(b)
        neighbours[b].add(a)
    groups = obj.vertex_groups
    for _ in range(iterations):
        current = {v.index: {g.group: g.weight for g in v.groups} for v in mesh.vertices}
        for v in verts:
            avg = defaultdict(float)
            for n in neighbours[v]:
                for g, w in current[n].items():
                    avg[g] += w / len(neighbours[v])
            mixed = defaultdict(float)
            for g, w in current[v].items():
                mixed[g] += (1.0 - factor) * w
            for g, w in avg.items():
                mixed[g] += factor * w
            for g in current[v]:
                groups[g].remove([v])
            for g, w in mixed.items():
                if w > 1e-4:
                    groups[g].add([v], w, "REPLACE")


def eye_weights(eyes, arm):
    _unbind(eyes)
    left = eyes.vertex_groups.new(name="LeftEye")
    right = eyes.vertex_groups.new(name="RightEye")
    for v in eyes.data.vertices:
        (left if v.co.x > 0 else right).add([v.index], 1.0, "REPLACE")
    _attach(eyes, arm)


def limit_and_normalize(obj, max_influences=MAX_INFLUENCES):
    """Keep the strongest `max_influences` weights per vertex and normalise them."""
    groups = obj.vertex_groups
    unweighted = 0
    for v in obj.data.vertices:
        weights = sorted(((g.weight, g.group) for g in v.groups if g.weight > 1e-4), reverse=True)
        for w, gi in weights[max_influences:]:
            groups[gi].remove([v.index])
        for g in list(v.groups):
            if g.weight <= 1e-4:
                groups[g.group].remove([v.index])
        kept = weights[:max_influences]
        total = sum(w for w, _ in kept)
        if total <= 0:
            unweighted += 1
            continue
        for w, gi in kept:
            groups[gi].add([v.index], w / total, "REPLACE")
    # Drop groups that ended up empty.
    used = {g.group for v in obj.data.vertices for g in v.groups}
    for group in [g for g in groups if g.index not in used]:
        groups.remove(group)
    return unweighted


def run():
    arm = bpy.data.objects[C.ARMATURE]
    body = bpy.data.objects[C.BODY]
    eyes = bpy.data.objects[C.EYES]
    auto_weights(body, arm)
    hand_verts = hand_weights(body)
    eye_weights(eyes, arm)
    unweighted = {o.name: limit_and_normalize(o) for o in (body, eyes)}
    _select_only([], arm)
    print(f"[bind] hand vertices re-weighted: {hand_verts}; unweighted vertices: {unweighted}; "
          f"body groups: {len(body.vertex_groups)}")
    return unweighted
