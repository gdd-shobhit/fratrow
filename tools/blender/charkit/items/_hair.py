"""Shared hair building: a scalp cap grown from the head skin, plus helpers.

The hairline is one slanted plane through a point on the forehead, facing
forward and down: it removes the face and the lower neck, keeps the sides and
the nape. Hair rides the head (Head 100%); long parts can blend toward the spine.
"""
import bmesh
import bpy
from mathutils import Vector

from .. import config as C
from .. import cosmetic
from . import _garment as G

FACE = (27, 28, 37, 45, 46, 47, 48, 49)   # eyelids, nose, mouth, inside the mouth
EARS = (29,)
HAIRLINE_NORMAL = (0.0, -0.8, -0.6)       # forward and down


def head_centre():
    head, top = G.bone_axis("Head")
    return head.lerp(top, 0.45)


def cap(hairline_z, offset, extra_cuts=(), exclude=FACE + EARS, hairline_normal=HAIRLINE_NORMAL,
        relax_iterations=15):
    """Scalp shell above the hairline. Returns (bm, covered body vertices, surface)."""
    point = (0.0, -0.09, hairline_z)
    bm, covered, surface = G.shell({"Head", "Neck"}, [(point, hairline_normal, None), *extra_cuts], offset,
                                   relax_iterations=relax_iterations, exclude_face_sets=exclude)
    smooth_edges(bm, surface, gap=offset)
    return bm, covered, surface


def inflate(bm, amount, gather=0.035):
    """Give the cap volume: push away from the head centre by `amount`, fading
    to nothing within `gather` of the hairline so the edge still hugs the skin."""
    centre = head_centre()
    edge = G.edge_distance(bm)
    for v in bm.verts:
        d = (v.co - centre).normalized()
        v.co += d * amount * G.smoothstep(edge.get(v, 1.0) / gather)
    bm.normal_update()


def smooth_edges(bm, surface, iterations=6, factor=0.5, gap=0.006):
    """Straighten jagged open edges (hairline, bangs, cut ends): boundary vertices
    move toward their boundary neighbours only, staying off the skin."""
    for _ in range(iterations):
        moves = {}
        for v in bm.verts:
            if not v.is_boundary:
                continue
            nbrs = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(nbrs) == 2:
                avg = (nbrs[0].co + nbrs[1].co) / 2
                moves[v] = surface.push_out(v.co.lerp(avg, factor), gap)
        for v, co in moves.items():
            v.co = co
    bm.normal_update()


def scallop(bm, surface, depth, count, gap, spread=1.1):
    """Bangs from the cap itself: the front edge pulled down into `count` soft
    points (`depth` long) across `spread` radians of the forehead, kept off the skin."""
    import math
    centre = head_centre()
    edge = G.edge_distance(bm)
    for v in bm.verts:
        d = v.co - centre
        if d.y > -0.02:
            continue
        a = math.atan2(d.x, -d.y)
        if abs(a) > spread:
            continue
        tips = (0.5 + 0.5 * math.cos(count * math.pi * (a / spread))) ** 2
        near_edge = 1.0 - G.smoothstep(edge.get(v, 1.0) / 0.025)
        fade = 1.0 - G.smoothstep((abs(a) - spread * 0.75) / (spread * 0.25))
        v.co = surface.push_out(v.co - Vector((0, 0, depth * tips * near_edge * fade)), gap)
    bm.normal_update()


def hairline_roots(bm, count, min_z, max_abs_x):
    """`count` evenly spaced points along the front hairline edge (for draped bangs)."""
    edge = sorted((v.co.copy() for v in bm.verts if v.is_boundary and v.co.y < -0.04
                   and v.co.z > min_z and abs(v.co.x) < max_abs_x), key=lambda c: c.x)
    if len(edge) < 2:
        return []
    return [edge[round(k * (len(edge) - 1) / (count - 1))] for k in range(count)]


def fringe(bm, surface, count, spread, length, root_offset, radius=0.011, lift=0.006, tip=0.2, roots=None):
    """Bangs: `count` short tapered locks hanging from the front hairline over the forehead.

    spread: half-angle (radians) across the forehead; length: how far each
    lock hangs; roots sit where rays from the head centre leave the cap; tip:
    lock radius at the end relative to the root (1.0 = blunt-cut bangs).
    """
    import math
    from . import _garment  # noqa: F401  (keeps import order explicit)
    from .. import shapes
    for k in range(count):
        a = -spread + 2 * spread * k / max(1, count - 1)
        if roots:
            root = roots[k] + Vector((0, 0, radius * 0.5))
        else:
            d = Vector((math.sin(a), -math.cos(a), 0.62)).normalized()
            root, normal = surface_point(bm, d)
            if root is None:
                continue
            root = root - normal * root_offset
        tip_len = length * (1.0 - 0.25 * abs(a) / max(spread, 1e-6))
        pts = []
        for j in range(5):
            t = j / 4
            p = root + Vector((0.004 * math.sin(a) * t, -0.006 * t, -tip_len * t))
            pts.append(surface.push_out(p, lift + radius * (1 - t)))
        shapes.tube(bm, pts, [radius * (1 - (1 - tip) * j / 4) for j in range(5)], segments=5)


def fill_holes_near(bm, accept):
    """Fill boundary loops whose centre passes accept(centre) (e.g. the ear holes)
    with a fan around a new centre vertex, wound like the faces next to it.
    (bmesh holes_fill silently skips some loops; the centre vertex also lets a
    later reshape dome the patch instead of leaving it flat.)"""
    edges = {e for e in bm.edges if e.is_boundary}
    loops = []
    while edges:
        stack, loop = [edges.pop()], []
        while stack:
            e = stack.pop()
            loop.append(e)
            for v in e.verts:
                for nb in v.link_edges:
                    if nb in edges:
                        edges.discard(nb)
                        stack.append(nb)
        loops.append(loop)
    for loop in loops:
        centre = sum((v.co for e in loop for v in e.verts), Vector()) / (2 * len(loop))
        if not accept(centre):
            continue
        hub = bm.verts.new(centre)
        for e in loop:
            a, b = e.verts
            face_loop = e.link_loops[0]
            if face_loop.vert is a:   # the face runs a -> b, so the patch runs b -> a
                a, b = b, a
            bm.faces.new((a, b, hub))
    bm.normal_update()


def surface_point(bm, direction, origin=None):
    """Where a ray from the head centre along direction leaves the shell."""
    from mathutils.bvhtree import BVHTree
    origin = origin or head_centre()
    bvh = BVHTree.FromBMesh(bm)
    d = Vector(direction).normalized()
    hit = bvh.ray_cast(origin + d * 0.4, -d, 0.5)
    return hit[0], hit[1]


def head_weights(blend_to=None, top_z=1.45, bottom_z=1.20):
    """Weights: Head, optionally fading to `blend_to` (e.g. Spine2) below top_z."""
    def at(co):
        if blend_to is None:
            return {"Head": 1.0}
        k = G.smoothstep((co.z - bottom_z) / (top_z - bottom_z))
        return {"Head": k, blend_to: 1.0 - k}
    return at


def finish(obj, out_dir, materials, weights_at):
    return cosmetic.finish(obj, out_dir, "Hair", materials, weights_at=weights_at)
