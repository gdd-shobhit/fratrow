"""White low-top sneakers (characters1, redhead)."""
from . import _footwear

NAME = "SKM_F_Shoes_Sneakers"
SPEC = dict(top_z=0.105, offset=0.010, sole_height=0.022, sole_flare=1.06,
            upper=(0.95, 0.95, 0.94), sole=(0.72, 0.72, 0.75))


def build():
    return _footwear.build(NAME, **SPEC)


def run(out_dir):
    return _footwear.run(NAME, out_dir, **SPEC)
