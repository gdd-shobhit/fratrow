"""Put a model in the pipeline's frame: metres, Z up, standing on Z=0, centred, facing -Y.

Generators normalise to their own unit box and pick their own front. A prop
needs its real-world size (a cup is 0.12 m whatever the file says) and its
origin at the bottom centre so it can be dropped on a floor.

    from aikit import normalize
    normalize.rotate_z(obj, 90)          # only if it does not face -Y
    normalize.to_ground(obj, height=0.12)
"""
import math

from mathutils import Matrix, Vector

from . import meshutil

FACING = "-Y"   # charkit's convention: Blender -Y is the front (FBX export turns it into Unity +Z)


def to_ground(obj, height=None, facing=FACING):
    """Bake transforms; scale uniformly to `height` metres (if given); bottom centre to the origin.

    `facing` documents the convention and is checked, not applied: turn a
    model with rotate_z first. Returns the scale used and the final dimensions.
    """
    if facing != FACING:
        raise ValueError(f"facing must be {FACING!r} (rotate the model with rotate_z first)")
    meshutil.apply_transform(obj)
    lo, hi = meshutil.bounds(obj)
    extent = hi[2] - lo[2]
    if height is not None and extent <= 0:
        raise ValueError(f"{obj.name}: flat along Z, cannot scale to a height")
    scale = height / extent if height is not None else 1.0
    anchor = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]))
    obj.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(-anchor))
    obj.data.update()
    lo, hi = meshutil.bounds(obj)
    return {"scale": float(scale), "dimensions": [round(float(d), 5) for d in hi - lo]}


def rotate_z(obj, degrees):
    """Turn the mesh about Z around its bounding-box centre (for a model that does not face -Y)."""
    meshutil.apply_transform(obj)
    lo, hi = meshutil.bounds(obj)
    centre = Vector((float((lo[0] + hi[0]) / 2), float((lo[1] + hi[1]) / 2), 0.0))
    turn = Matrix.Translation(centre) @ Matrix.Rotation(math.radians(degrees), 4, "Z") @ Matrix.Translation(-centre)
    obj.data.transform(turn)
    obj.data.update()
    return obj
