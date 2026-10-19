"""Rebuild the master female character from the base mesh, end to end.

    from charkit import build
    build.run(hbm_blend=r"...\\human_base_meshes_bundle.blend")

Order matters: the skeleton is measured on the prepared body, binding needs the
skeleton, and the shape keys read bone axes from it.
"""
from . import bind, body_prepare, body_shapes, review, skeleton, validate


def run(hbm_blend=None):
    body_prepare.run(hbm_blend)
    skeleton.run()
    bind.run()
    body_shapes.run()
    review.reset_pose()
    return validate.run()
