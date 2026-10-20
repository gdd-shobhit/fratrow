"""Procedural geometry for cosmetics that are not just a copy of the skin.

Tubes (ponytails, straps), bows, body cross-sections (skirts), convex-hull
starting shells (shoes) and ribbon nets (fishnet). Everything works on bmesh in
the master body's rest space (metres, feet on Z=0, facing -Y).
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from . import config as C


def tube(bm, points, radii, segments=8, material_index=0, caps=True):
    """Sweep a circle along a polyline (parallel-transport frames). Returns the new faces."""
    angles = [2 * math.pi * i / segments for i in range(segments)]
    t0 = (points[1] - points[0]).normalized()
    normal = t0.orthogonal().normalized()
    rings = []
    for i, p in enumerate(points):
        if i == 0:
            t = t0
        elif i == len(points) - 1:
            t = (points[-1] - points[-2]).normalized()
        else:
            t = (points[i + 1] - points[i - 1]).normalized()
        normal = (normal - t * normal.dot(t)).normalized()
        binormal = t.cross(normal)
        rings.append([bm.verts.new(p + (normal * math.cos(a) + binormal * math.sin(a)) * radii[i])
                      for a in angles])
    faces = []
    for a, b in zip(rings, rings[1:]):
        for j in range(segments):
            faces.append(bm.faces.new((a[j], a[(j + 1) % segments], b[(j + 1) % segments], b[j])))
    if caps:
        faces.append(bm.faces.new(list(reversed(rings[0]))))
        faces.append(bm.faces.new(rings[-1]))
    for f in faces:
        f.material_index = material_index
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def bow(bm, centre, along, outward, size, material_index=0):
    """A small bow: knot plus two flattened loops, lying in the plane facing `outward`."""
    along, outward = along.normalized(), outward.normalized()
    side = along.cross(outward).normalized()
    frame = Matrix((side, along, outward)).transposed().to_4x4()
    place = Matrix.Translation(centre) @ frame
    made = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=size * 0.35, matrix=place)
    faces = set(f for v in made["verts"] for f in v.link_faces)
    for lr in (-1, 1):
        loop = Matrix.Translation((lr * size * 0.75, 0, 0)) @ Matrix.Scale(0.45, 4, (0, 0, 1))
        made = bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=4, radius=size * 0.6,
                                         matrix=place @ loop @ Matrix.Scale(0.55, 4, (0, 1, 0)))
        faces |= set(f for v in made["verts"] for f in v.link_faces)
    for f in faces:
        f.material_index = material_index
        f.smooth = True
    return faces


def section(surface, z, exclude_regions=()):
    """Points where the body's edges cross height z (skipping polygons in exclude_regions)."""
    pts = []
    for poly, region in zip(surface.polys, surface.regions):
        if region in exclude_regions:
            continue
        n = len(poly)
        for k in range(n):
            a, b = surface.co[poly[k]], surface.co[poly[(k + 1) % n]]
            if (a.z - z) * (b.z - z) <= 0 and a.z != b.z:
                pts.append(a.lerp(b, (z - a.z) / (b.z - a.z)))
    return pts


def support(points, centre, direction):
    """Convex support distance of 2D points (x, y) from centre along direction."""
    return max((Vector((p.x - centre.x, p.y - centre.y)).dot(direction) for p in points), default=0.0)


def hull_shell(points, voxel):
    """A closed, evenly meshed shell around the convex hull of points (voxel remesh)."""
    bm = bmesh.new()
    verts = [bm.verts.new(p) for p in points]
    result = bmesh.ops.convex_hull(bm, input=verts)
    loose = list({g for g in result["geom_interior"] + result["geom_unused"] if isinstance(g, bmesh.types.BMVert)})
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    mesh = bpy.data.meshes.new("_hull")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("_hull", mesh)
    bpy.context.scene.collection.objects.link(obj)
    mod = obj.modifiers.new("_remesh", "REMESH")
    mod.mode = "VOXEL"
    mod.voxel_size = voxel
    depsgraph = bpy.context.evaluated_depsgraph_get()
    remeshed = obj.evaluated_get(depsgraph).to_mesh()
    out = bmesh.new()
    out.from_mesh(remeshed)
    obj.evaluated_get(depsgraph).to_mesh_clear()
    bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.meshes.remove(mesh)
    out.normal_update()
    return out


def ribbon_net(bm, ring_points, width, material_index=0):
    """Flat ribbons along a diamond lattice between consecutive rings (a fishnet).

    ring_points: list of rings, each a list of (point, outward normal), every
    ring rotated half a step further than the previous one, so point j links to
    the next ring's points j and j-1 (half a step either side): diamonds.
    """
    faces = []
    for ring, nxt in zip(ring_points, ring_points[1:]):
        n = len(ring)
        for j in range(n):
            for k in (j, (j - 1) % n):
                (a, na), (b, nb) = ring[j], nxt[k]
                d = (b - a).normalized()
                sa = d.cross(na).normalized() * (width / 2)
                sb = d.cross(nb).normalized() * (width / 2)
                quad = [bm.verts.new(a - sa), bm.verts.new(a + sa), bm.verts.new(b + sb), bm.verts.new(b - sb)]
                f = bm.faces.new(quad)
                if f.normal.dot(na) < 0:
                    f.normal_flip()
                f.material_index = material_index
                faces.append(f)
    return faces
