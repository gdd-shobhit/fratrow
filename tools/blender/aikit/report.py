"""Numbers about a generated model, for a person or a skill to read. Never a verdict.

The baked-lighting figures answer "did the generator paint its own lighting
into the albedo?" without knowing the UV layout: base colour is sampled
through the UVs and compared between surfaces facing up and facing down. A
painted top light makes up-facing texels brighter (up_down_ratio well above 1,
positive normal_z_correlation). Whether a number is too high is for the reader
to decide against the art direction; this module only measures.

    from aikit import report
    print(report.observe(obj))
"""
import numpy as np

from . import meshutil, textures

LUMA_SAMPLES = 20000     # face corners sampled for the luminance stats
LUMA_TEXTURE = 256       # base colour is read from a copy this size


def _edge_use(me):
    _, edges, _ = meshutil.corner_arrays(me)
    return np.bincount(edges, minlength=len(me.edges)) if len(me.edges) else np.zeros(0, np.int64)


def _images(obj):
    seen = {}
    for mat in obj.data.materials:
        if mat is None or mat.node_tree is None:
            continue
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image is not None:
                seen[node.image.name] = list(node.image.size)
    return [{"name": n, "size": s} for n, s in seen.items()]


def luminance(obj):
    """Base-colour luminance stats sampled through the active UVs, split by facing."""
    me = obj.data
    mats = [m for m in me.materials if m is not None]
    bsdf = textures._principled(mats[0]) if mats else None
    image = textures.source(bsdf.inputs["Base Color"])[0] if bsdf is not None else None
    if image is None or me.uv_layers.active is None or not len(me.loops):
        return None
    tex = textures.pixels(image, textures.target_size(image, LUMA_TEXTURE))
    h, w = tex.shape[:2]
    uv = np.empty(len(me.loops) * 2, np.float64)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    totals = np.empty(len(me.polygons), np.int64)
    me.polygons.foreach_get("loop_total", totals)
    normals = np.empty(len(me.polygons) * 3, np.float64)
    me.polygons.foreach_get("normal", normals)
    nz = np.repeat(normals.reshape(-1, 3)[:, 2], totals)
    pick = np.linspace(0, len(uv) - 1, min(LUMA_SAMPLES, len(uv))).astype(np.int64)
    x = np.clip(((uv[pick, 0] % 1.0) * (w - 1)).astype(np.int64), 0, w - 1)
    y = np.clip(((uv[pick, 1] % 1.0) * (h - 1)).astype(np.int64), 0, h - 1)
    lum = tex[y, x, :3] @ textures.LUMA
    nz = nz[pick]
    up, down = lum[nz > 0.5], lum[nz < -0.5]
    corr = float(np.corrcoef(lum, nz)[0, 1]) if lum.std() > 1e-6 and nz.std() > 1e-6 else 0.0
    return {
        "mean": round(float(lum.mean()), 4),
        "std": round(float(lum.std()), 4),
        "up_mean": round(float(up.mean()), 4) if len(up) else None,
        "down_mean": round(float(down.mean()), 4) if len(down) else None,
        "up_down_ratio": round(float(up.mean() / down.mean()), 3) if len(up) and len(down) and down.mean() > 1e-6 else None,
        "normal_z_correlation": round(corr, 3),
    }


def observe(obj):
    """Triangles, topology, materials, textures, size and luminance of `obj` (a dict of numbers)."""
    me = obj.data
    use = _edge_use(me)
    _, parts = meshutil.face_components(me)
    lo, hi = meshutil.bounds(obj)
    return {
        "tris": meshutil.tri_count(obj),
        "verts": len(me.vertices),
        "faces": len(me.polygons),
        "boundary_edges": int((use == 1).sum()),
        "nonmanifold_edges": int((use > 2).sum()),
        "loose_parts": int(parts),
        "materials": [m.name for m in me.materials if m is not None],
        "uv_layers": [uv.name for uv in me.uv_layers],
        "textures": _images(obj),
        "dimensions_m": [round(float(d), 4) for d in hi - lo],
        "base_color_luminance": luminance(obj),
    }
