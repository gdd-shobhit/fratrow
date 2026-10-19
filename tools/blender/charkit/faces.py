"""Per-character face and eye textures, painted procedurally from a recipe.

A face texture covers the body's UV atlas (config.UV_ATLAS) and is white except
for the painted features: brows, lash line and wing, eyeshadow, lips, blush and
freckles. Unity multiplies it by the skin colour (CharacterAppearance tints the
body), so each texel stores feature colour / skin colour, painted for the
recipe's skin tone. Features are placed in 3D around landmarks measured on the
body (the eye openings and the mouth line), so they follow the mesh, not its
UV layout, and survive a re-unwrap.

The eye texture is an iris in the eyes' front-projected UVs (pupil at 0.5, 0.5,
see body_prepare.build_eyes); both eyes share it.

Colours are sRGB hex strings, as in the recipes. Run inside Blender:

    from charkit import faces
    faces.paint(recipe)   # recipe dict from charkit.recipes.load()
"""
import math
import os

import bmesh
import bpy
import numpy as np

from . import config as C

LID_RIM, EYE_SOCKET = 28, 37          # face sets: eyelid rim, socket behind the eyeball
FACE_SKIN, JAW = 44, 46
MOUTH_INSIDE = (47, 48)
HIDDEN_FACE_SETS = (EYE_SOCKET, 47, 48, 49)   # never seen: left unpainted (white)
FACE_SIZE = 1024
EYE_SIZE = 256
IRIS_RADIUS = 0.0125                  # metres; the eye openings are ~0.031 tall
SCLERA = (0.96, 0.96, 0.95)

FACE_DEFAULTS = {
    "brows": "#6B4A35", "browThickness": 1.0, "browArch": 1.0,
    "lashes": "#2A1D1D", "wing": 0.3,
    "eyeshadow": "#C99A8C", "eyeshadowAlpha": 0.0,
    "lips": "#D9777F", "lipAlpha": 0.85,
    "blush": "#F0A0A0", "blushAlpha": 0.0,
    "freckles": 0.0, "freckleColor": "#B0674A",
}


# --- Colour helpers -------------------------------------------------------------

def hex_to_srgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def _to_linear(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _to_srgb(c):
    c = np.clip(np.asarray(c, dtype=np.float64), 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def _smoothstep(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# --- Landmarks ------------------------------------------------------------------

def _face_sets(mesh):
    sets = [0] * len(mesh.polygons)
    mesh.attributes[C.FACE_SET_ATTR].data.foreach_get("value", sets)
    return sets


def _chain(edges):
    """Order the vertices of a closed edge loop."""
    nbrs = {}
    for e in edges:
        a, b = e.verts
        nbrs.setdefault(a, []).append(b)
        nbrs.setdefault(b, []).append(a)
    start = next(iter(nbrs))
    loop, prev, cur = [start], None, start
    while True:
        nxt = next((v for v in nbrs[cur] if v is not prev), None)
        if nxt is None or nxt is start:
            return loop
        loop.append(nxt)
        prev, cur = cur, nxt


def _smooth(line, iterations=3):
    """Chaikin corner cutting, endpoints kept: takes the decimation wobble out of a lid line."""
    for _ in range(iterations):
        if len(line) < 3:
            return line
        q = 0.75 * line[:-1] + 0.25 * line[1:]
        r = 0.25 * line[:-1] + 0.75 * line[1:]
        line = np.vstack([line[:1], np.column_stack([q, r]).reshape(-1, 2)[1:-1], line[-1:]])
    return line


def _split_lids(loop):
    """Split an eye-opening contour [(x, z)] into upper lid (inner corner to the outer
    flick), outer edge (flick down to the outer corner) and lower lid (inner to outer corner)."""
    n = len(loop)
    ax = [abs(p[0]) for p in loop]
    i_in = min(range(n), key=ax.__getitem__)
    i_flick = max(range(n), key=ax.__getitem__)
    forward = [(i_in + k) % n for k in range((i_flick - i_in) % n + 1)]
    backward = [(i_in - k) % n for k in range((i_in - i_flick) % n + 1)]
    mean_z = lambda path: np.mean([loop[i][1] for i in path])
    upper, rest = (forward, backward) if mean_z(forward) > mean_z(backward) else (backward, forward)
    rest = rest[::-1]  # flick -> inner along the bottom
    k = 1
    while k < len(rest) - 1 and ax[rest[k]] > ax[i_flick] - 0.004:
        k += 1
    edge, lower = rest[:k], rest[k - 1:][::-1]
    pts = lambda path: np.array([loop[i] for i in path])
    return _smooth(pts(upper)), pts(edge), _smooth(pts(lower))


def landmarks():
    """Eye openings (lid lines as (x, z), inner to outer) and the mouth line."""
    body = bpy.data.objects[C.BODY]
    sets = _face_sets(body.data)
    bm = bmesh.new()
    bm.from_mesh(body.data)

    def border(a, b):
        out = []
        for e in bm.edges:
            if len(e.link_faces) != 2:
                continue
            fs = {sets[f.index] for f in e.link_faces}
            if fs & a and fs & b and len(fs) == 2:
                out.append(e)
        return out

    eyes = {}
    rim = border({LID_RIM}, {EYE_SOCKET})
    for sign in (1, -1):
        loop = [(v.co.x, v.co.z) for v in _chain([e for e in rim if e.verts[0].co.x * sign > 0])]
        upper, edge, lower = _split_lids(loop)
        eyes[sign] = {"upper": upper, "edge": edge, "lower": lower}

    mouth = border({FACE_SKIN, JAW}, set(MOUTH_INSIDE))
    pts = [v.co for e in mouth for v in e.verts if v.co.y < -0.07]
    bm.free()
    centre = min(pts, key=lambda p: abs(p.x))
    corner = max(pts, key=lambda p: abs(p.x))
    return {"eyes": eyes, "mouth": {"z": centre.z, "corner_z": corner.z, "half_width": abs(corner.x)}}


# --- Texel positions ------------------------------------------------------------

def texel_map(size, min_z=1.3):
    """3D rest position, normal and face set of each texel of the body UV (above min_z).

    Returns (pos (size, size, 3), normal (size, size, 3), face_set (size, size),
    hit (size, size)); rows are v (row 0 = v 0), padded a few texels past island
    edges for filtering.
    """
    me = bpy.data.objects[C.BODY].data
    sets = np.array(_face_sets(me))
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    tri_loops = np.zeros(n * 3, np.int64)
    tri_verts = np.zeros(n * 3, np.int64)
    tri_polys = np.zeros(n, np.int64)
    me.loop_triangles.foreach_get("loops", tri_loops)
    me.loop_triangles.foreach_get("vertices", tri_verts)
    me.loop_triangles.foreach_get("polygon_index", tri_polys)
    uv = np.zeros(len(me.loops) * 2)
    me.uv_layers["UVMap"].data.foreach_get("uv", uv)
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    nor = np.zeros(len(me.vertices) * 3)
    me.vertex_normals.foreach_get("vector", nor)
    uv, co, nor = uv.reshape(-1, 2) * size, co.reshape(-1, 3), nor.reshape(-1, 3)
    tri_loops, tri_verts = tri_loops.reshape(-1, 3), tri_verts.reshape(-1, 3)

    pos = np.zeros((size, size, 3), np.float32)
    normal = np.zeros((size, size, 3), np.float32)
    face_set = np.full((size, size), -1, np.int32)
    hit = np.zeros((size, size), bool)
    for loops, verts, poly in zip(tri_loops, tri_verts, tri_polys):
        if co[verts, 2].max() < min_z:
            continue
        p = uv[loops]
        x0, y0 = np.floor(p.min(0)).astype(int)
        x1, y1 = np.ceil(p.max(0)).astype(int)
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, size - 1), min(y1, size - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        q = np.stack([xs.ravel() + 0.5, ys.ravel() + 0.5], 1)
        m = np.array([p[1] - p[0], p[2] - p[0]]).T
        if abs(np.linalg.det(m)) < 1e-12:
            continue
        b12 = np.linalg.solve(m, (q - p[0]).T).T
        bary = np.column_stack([1 - b12.sum(1), b12])
        inside = (bary >= -1e-4).all(1)
        if not inside.any():
            continue
        bary = bary[inside]
        rows, cols = ys.ravel()[inside], xs.ravel()[inside]
        pos[rows, cols] = bary @ co[verts]
        nrm = bary @ nor[verts]
        normal[rows, cols] = nrm / np.linalg.norm(nrm, axis=1, keepdims=True)
        face_set[rows, cols] = sets[poly]
        hit[rows, cols] = True

    for _ in range(4):  # pad: copy a neighbour into empty texels next to an island
        grown = hit.copy()
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            src = np.roll(np.roll(hit, dy, 0), dx, 1) & ~grown
            for channel in (pos, normal, face_set):
                channel[src] = np.roll(np.roll(channel, dy, 0), dx, 1)[src]
            grown |= src
        hit = grown
    return pos, normal, face_set, hit


# --- Painting -------------------------------------------------------------------

def _polyline(q, line):
    """Distance from points q (N, 2) to a polyline (M, 2), and the arc position 0..1 of the nearest point."""
    a, b = line[:-1], line[1:]
    ab = b - a
    seg = np.maximum((ab ** 2).sum(1), 1e-12)
    t = np.clip((((q[:, None, :] - a[None]) * ab[None]).sum(-1)) / seg[None], 0.0, 1.0)
    d = np.linalg.norm(q[:, None, :] - (a[None] + t[..., None] * ab[None]), axis=-1)
    k = d.argmin(1)
    r = np.arange(len(q))
    lengths = np.sqrt(seg)
    cum = np.concatenate([[0.0], np.cumsum(lengths)])
    return d[r, k], (cum[k] + t[r, k] * lengths[k]) / cum[-1]


def _stroke(d, half_width, soft=0.0005):
    return _smoothstep((half_width - d) / soft + 0.5)


def _brow(sign, upper, thickness, arch):
    """Brow centre line (inner to outer) above an upper lid line."""
    inner, outer = upper[0], upper[-1]
    top = upper[:, 1].max()
    p0 = np.array([inner[0] - sign * 0.002, top + 0.012])
    p2 = np.array([outer[0] + sign * 0.005, top + 0.009])
    peak = np.array([inner[0] + (outer[0] - inner[0]) * 0.62, top + 0.012 + 0.0065 * arch])
    ctrl = 2 * peak - (p0 + p2) / 2
    t = np.linspace(0, 1, 24)[:, None]
    line = (1 - t) ** 2 * p0 + 2 * (1 - t) * t * ctrl + t ** 2 * p2
    width = 0.0042 * thickness * (1.0 - 0.72 * np.linspace(0, 1, 24) ** 1.6)
    return line, width


def _features(pos, normal, spec, marks, skin_lin):
    """Composite the features over the skin colour; returns linear RGB (N, 3)."""
    x, y, z = pos[:, 0].astype(np.float64), pos[:, 1].astype(np.float64), pos[:, 2].astype(np.float64)
    front = _smoothstep((-normal[:, 1] - 0.05) / 0.35) * (y < -0.03)
    out = np.tile(skin_lin, (len(pos), 1))

    def layer(color, alpha):
        nonlocal out
        a = np.clip(alpha, 0.0, 1.0)[:, None]
        out = out * (1 - a) + _to_linear(hex_to_srgb(color))[None] * a

    q = np.column_stack([x, z])
    mouth = marks["mouth"]

    # Blush: soft ellipses on the cheeks.
    if spec["blushAlpha"] > 0:
        a = np.zeros(len(pos))
        for sign in (1, -1):
            r = np.sqrt(((x - sign * 0.052) / 0.021) ** 2 + ((z - 1.489) / 0.012) ** 2)
            a = np.maximum(a, 1 - _smoothstep(r))
        layer(spec["blush"], a * spec["blushAlpha"] * front)

    # Freckles: fixed-seed dots across the nose bridge and cheeks.
    if spec["freckles"] > 0:
        rng = np.random.default_rng(7)
        dots = np.column_stack([rng.uniform(-0.066, 0.066, 90), rng.uniform(1.474, 1.503, 90)])
        dots = dots[np.abs(dots[:, 0]) > 0.006]
        radii = rng.uniform(0.0007, 0.0012, len(dots))
        a = np.zeros(len(pos))
        near = (np.abs(x) < 0.075) & (z > 1.465) & (z < 1.51)
        for (dx, dz), rad in zip(dots, radii):
            d = np.hypot(x[near] - dx, z[near] - dz)
            a[near] = np.maximum(a[near], _stroke(d, rad, 0.0004))
        layer(spec["freckleColor"], a * 0.55 * spec["freckles"] * front)

    # Eyeshadow, lashes, brows.
    shadow, lashes, brows = np.zeros(len(pos)), np.zeros(len(pos)), np.zeros(len(pos))
    for sign, eye in marks["eyes"].items():
        upper, edge, lower = eye["upper"], eye["edge"], eye["lower"]
        near = (np.abs(x - upper[:, 0].mean()) < 0.06) & (np.abs(z - upper[:, 1].mean()) < 0.045)
        qn = q[near]

        d_up, t_up = _polyline(qn, upper)
        lid_z = np.interp(qn[:, 0] * sign, upper[:, 0] * sign, upper[:, 1])
        # The lid rim just below the contour is visible above the eyeball: the line starts there.
        above = (qn[:, 1] > lid_z - 0.0022) | (qn[:, 0] * sign > upper[-1, 0] * sign)
        if spec["eyeshadowAlpha"] > 0:
            shadow[near] = np.maximum(shadow[near], (1 - _smoothstep(d_up / 0.009)) * above)

        # Upper lash line: thin at the inner corner, bold at the outer flick, then the wing.
        wing = spec["wing"]
        tip = upper[-1] + np.array([sign * 0.010, 0.0025]) * wing
        lid = np.vstack([upper, tip[None]]) if wing > 0 else upper
        d_lid, t_lid = _polyline(qn, lid)
        cum = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(lid, axis=0), axis=1))])
        at = t_lid * cum[-1]
        lid_end = cum[len(upper) - 1]
        half = np.where(at <= lid_end,
                        0.001 + 0.002 * np.clip(at / lid_end, 0, 1) ** 1.2,
                        0.003 * np.clip(1 - (at - lid_end) / max(cum[-1] - lid_end, 1e-6), 0.1, 1))
        lashes[near] = np.maximum(lashes[near], _stroke(d_lid, half, 0.0006) * (above | (at > lid_end)))
        # A soft lower lash line on the outer half.
        d_low, t_low = _polyline(qn, lower)
        lashes[near] = np.maximum(lashes[near], _stroke(d_low, 0.0005) * 0.5 * _smoothstep((t_low - 0.4) / 0.35))

        line, width = _brow(sign, upper, spec["browThickness"], spec["browArch"])
        d_b, t_b = _polyline(qn, line)
        brows[near] = np.maximum(brows[near], _stroke(d_b, np.interp(t_b, np.linspace(0, 1, 24), width) / 2, 0.0007))
    if spec["eyeshadowAlpha"] > 0:
        layer(spec["eyeshadow"], shadow * spec["eyeshadowAlpha"] * front)

    # Lips: upper lip with a cupid's bow, fuller lower lip, darker parting line.
    a_w = mouth["half_width"] + 0.0008
    u = np.clip(np.abs(x) / a_w, 0, 1)
    zm = mouth["z"] + (mouth["corner_z"] - mouth["z"]) * u ** 2
    zu = zm + 0.0062 * (1 - u ** 2) ** 0.6 * (1 - 0.28 * np.exp(-(x / 0.0045) ** 2))
    zl = zm - 0.0078 * (1 - u ** 2) ** 0.55
    edge = np.minimum(z - zl, zu - z)
    lips = _smoothstep(edge / 0.0009) * _smoothstep((a_w - np.abs(x)) / 0.0025) * (y < -0.06)
    layer(spec["lips"], lips * spec["lipAlpha"])
    parting = _stroke(np.abs(z - zm), 0.00035) * (np.abs(x) < a_w) * (y < -0.06)
    dark = "#" + "".join(f"{int(c * 255 * 0.55):02X}" for c in hex_to_srgb(spec["lips"]))
    layer(dark, parting * 0.8)

    layer(spec["brows"], brows * 0.95 * front)
    layer(spec["lashes"], lashes)
    return out


def paint_face(recipe, size=FACE_SIZE):
    """(texture, preview) as sRGB float arrays (size, size, 3): the Unity texture
    (features / skin colour) and what it looks like on the recipe's skin."""
    spec = dict(FACE_DEFAULTS, **recipe.get("face", {}))
    skin_lin = _to_linear(hex_to_srgb(recipe["skin"]))
    pos, normal, face_set, hit = texel_map(size)
    paint = hit & ~np.isin(face_set, HIDDEN_FACE_SETS)
    final = np.tile(skin_lin, (size, size, 1))
    final[paint] = _features(pos[paint], normal[paint], spec, landmarks(), skin_lin)
    texture = _to_srgb(np.clip(final / skin_lin[None, None], 0, 1))
    return texture, _to_srgb(final)


def paint_iris(color, size=EYE_SIZE):
    """sRGB float array (size, size, 3): sclera with an anime-style iris in the middle."""
    radius = max(_eye_radius(), 1e-6)
    v, u = np.mgrid[0:size, 0:size]
    dx = ((u + 0.5) / size - 0.5) * 2 * radius / IRIS_RADIUS
    dz = ((v + 0.5) / size - 0.5) * 2 * radius / IRIS_RADIUS
    rho = np.hypot(dx, dz)
    px = 2 * radius / IRIS_RADIUS / size  # one texel in iris radii
    c = _to_linear(hex_to_srgb(color))
    g = np.clip((1 - dz) / 2, 0, 1)[..., None]           # 0 at the top of the iris, 1 at the bottom
    iris = c * 0.42 * (1 - g) + np.minimum(c * 1.3, 1) * g
    iris = iris * (1 - _smoothstep((rho - 0.78) / 0.22)[..., None] * 0.65)   # dark rim
    pupil = 1 - _smoothstep((rho - 0.40) / px)
    iris = iris * (1 - pupil[..., None]) + (c * 0.08) * pupil[..., None]
    inside = (1 - _smoothstep((rho - 1) / px + 0.5))[..., None]
    out = iris * inside + _to_linear(SCLERA) * (1 - inside)
    for (hx, hz, hr) in ((-0.36, 0.36, 0.24), (0.34, -0.32, 0.1)):
        h = 1 - _smoothstep((np.hypot(dx - hx, dz - hz) - hr) / px + 0.5)
        out = out * (1 - h[..., None]) + h[..., None]
    return _to_srgb(out)


def _eye_radius():
    eyes = bpy.data.objects[C.EYES]
    xs = [v.co.x for v in eyes.data.vertices if v.co.x > 0]
    return (max(xs) - min(xs)) / 2


# --- Output ---------------------------------------------------------------------

def image(name, rgb, path=None):
    """Blender image from an sRGB float array (rows = v); saved as PNG when path is given."""
    h, w, _ = rgb.shape
    img = bpy.data.images.get(name)
    if img is not None and (img.size[0] != w or img.size[1] != h):
        bpy.data.images.remove(img)
        img = None
    img = img or bpy.data.images.new(name, w, h, alpha=False)
    rgba = np.concatenate([rgb, np.ones((h, w, 1))], axis=2).astype(np.float32)
    img.pixels.foreach_set(rgba.ravel())
    img.update()
    if path:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
    return img


def paint(recipe, texture_dir):
    """Write the recipe's face and eye textures; returns the preview images (face, eyes)."""
    face, face_preview = paint_face(recipe)
    eyes = paint_iris(recipe["iris"])
    image(recipe["faceTexture"], face, os.path.join(texture_dir, recipe["faceTexture"] + ".png"))
    image(recipe["eyeTexture"], eyes, os.path.join(texture_dir, recipe["eyeTexture"] + ".png"))
    print(f"[faces] {recipe['id']}: {recipe['faceTexture']}.png, {recipe['eyeTexture']}.png")
    return image(f"_pv_{recipe['faceTexture']}", face_preview), bpy.data.images[recipe["eyeTexture"]]
