"""White chunky ankle boots (characters1, blonde)."""
from . import _footwear

NAME = "SKM_F_Shoes_AnkleBoots"
SPEC = dict(top_z=0.30, offset=0.011, sole_height=0.03, sole_flare=1.07,
            upper=(0.96, 0.96, 0.95), sole=(0.84, 0.84, 0.85), voxel=0.025)


def build():
    return _footwear.build(NAME, **SPEC)


def run(out_dir):
    return _footwear.run(NAME, out_dir, **SPEC)
