"""Shrink a generated model's textures and write them in Unity URP Lit's layout.

Generators ship 2K-8K maps; a prop or cosmetic needs 512-1024. extract()
resizes every image the model's first material uses (never upscaling, always a
power of two), points the material at the resized copies, and writes the maps
Unity reads next to the export:

    T_<name>_BaseMap.png            base colour (sRGB)
    T_<name>_Normal.png             tangent-space normal, OpenGL (+Y) like glTF and Unity
    T_<name>_MetallicSmoothness.png URP packing: metallic in R, smoothness (1 - roughness) in A

glTF packs metallic (B) and roughness (G) into one image; that image is
resized and packed into the .blend, not written, so a lean GLB saved
afterwards keeps glTF's own layout. Nodes are found by type and links, never
by name (node names are localized).

    from aikit import textures
    maps = textures.extract(obj, out_dir, "Chair")   # {"BaseMap": path, ...}
"""
import os

import bpy
import numpy as np

DEFAULT_SIZE = 1024
LUMA = np.array([0.2126, 0.7152, 0.0722])


def _pow2_floor(n):
    return 1 << (max(1, int(n)).bit_length() - 1)


def target_size(image, size=DEFAULT_SIZE):
    """(w, h): at most `size` on the long side, powers of two, never larger than the source."""
    w, h = image.size
    long_side = min(_pow2_floor(size), _pow2_floor(max(w, h)))
    if w >= h:
        return long_side, max(1, _pow2_floor(h * long_side / w))
    return max(1, _pow2_floor(w * long_side / h)), long_side


def _principled(mat):
    if mat is None or not mat.use_nodes or mat.node_tree is None:
        return None
    return next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)


def source(socket, depth=6):
    """(image, channel) feeding `socket` through Normal Map / Separate Color / mix nodes.

    channel is "R", "G", "B" or "A" when a channel was split off, None for the
    whole colour; (None, None) when no image feeds the socket.
    """
    if not socket.is_linked or depth == 0:
        return None, None
    link = socket.links[0]
    node = link.from_node
    if node.type == "TEX_IMAGE":
        return node.image, ("A" if link.from_socket.identifier == "Alpha" else None)
    if node.type in ("SEPARATE_COLOR", "SEPRGB", "SEPARATE_RGB"):
        image, _ = source(node.inputs[0], depth - 1)
        index = list(node.outputs).index(link.from_socket)
        return image, "RGB"[index] if index < 3 else None
    for inp in node.inputs:   # Normal Map, Mix, Gamma, vertex-colour multiplies...
        if inp.type in ("RGBA", "VECTOR") and inp.is_linked:
            found = source(inp, depth - 1)
            if found[0] is not None:
                return found
    return None, None


def pixels(image, size):
    """(h, w, 4) float32 pixels of a resized copy of `image` (the source is untouched)."""
    copy = image.copy()
    w, h = size
    if tuple(copy.size) != (w, h):
        copy.scale(w, h)
    arr = np.empty(w * h * 4, np.float32)
    copy.pixels.foreach_get(arr)
    bpy.data.images.remove(copy)
    return arr.reshape(h, w, 4)


def _new_image(name, arr, non_color):
    # A same-named image may be the one being replaced (a re-run); Blender suffixes this one.
    # No alpha plane when every texel is opaque: the PNG is a quarter smaller.
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=bool((arr[..., 3] < 0.999).any()))
    if non_color:
        img.colorspace_settings.is_data = True
    img.pixels.foreach_set(arr.ravel())
    return img


def _save(name, arr, path, non_color=False):
    img = _new_image(name, arr, non_color)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def extract(obj, out_dir, name, size=DEFAULT_SIZE):
    """Resize the first material's textures, repoint it, write T_<name>_* maps.

    Returns {"BaseMap": path, "Normal": path, "MetallicSmoothness": path},
    holding only the maps the material actually has. A model with no textured
    material (e.g. vertex colours only) returns {}. Only the first material
    is processed; others are reported and left as they are.
    """
    mats = [m for m in obj.data.materials if m is not None]
    if len(mats) > 1:
        print(f"[aikit.textures] {obj.name}: {len(mats)} materials, only {mats[0].name} is processed")
    bsdf = _principled(mats[0]) if mats else None
    if bsdf is None:
        return {}
    base, _ = source(bsdf.inputs["Base Color"])
    normal, _ = source(bsdf.inputs["Normal"])
    metal, metal_ch = source(bsdf.inputs["Metallic"])
    rough, rough_ch = source(bsdf.inputs["Roughness"])
    if base is None and normal is None and metal is None and rough is None:
        return {}
    os.makedirs(out_dir, exist_ok=True)
    written, resized = {}, {}

    def resized_copy(image, label, path=None, non_color=False):
        if image.name in resized:
            return resized[image.name]
        arr = pixels(image, target_size(image, size))
        img_name = f"T_{name}_{label}"
        if path:
            img = _save(img_name, arr, path, non_color)
        else:
            img = _new_image(img_name, arr, non_color)
            img.pack()
        resized[image.name] = img
        return img

    if base is not None:
        path = os.path.join(out_dir, f"T_{name}_BaseMap.png")
        resized_copy(base, "BaseMap", path)
        written["BaseMap"] = path
    if normal is not None:
        path = os.path.join(out_dir, f"T_{name}_Normal.png")
        resized_copy(normal, "Normal", path, non_color=True)
        written["Normal"] = path
    if metal is not None or rough is not None:
        ref = metal if metal is not None else rough
        shape = target_size(ref, size)
        out = np.zeros((shape[1], shape[0], 4), np.float32)
        channel = {"R": 0, "G": 1, "B": 2, "A": 3}
        out[..., 0] = (pixels(metal, shape)[..., channel.get(metal_ch, 0)] if metal is not None
                       else bsdf.inputs["Metallic"].default_value)
        out[..., 3] = 1.0 - (pixels(rough, shape)[..., channel.get(rough_ch, 0)] if rough is not None
                             else bsdf.inputs["Roughness"].default_value)
        path = os.path.join(out_dir, f"T_{name}_MetallicSmoothness.png")
        bpy.data.images.remove(_save(f"T_{name}_MetallicSmoothness", out, path, non_color=True))
        written["MetallicSmoothness"] = path
        for image, label in ((metal, "Metallic"), (rough, "Roughness")):
            if image is not None:
                resized_copy(image, f"{label}_packed", non_color=True)

    for node in mats[0].node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image is not None and node.image.name in resized:
            node.image = resized[node.image.name]
    return written
