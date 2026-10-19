"""Black chunky combat boots (characters1, black bob)."""
from . import _footwear

NAME = "SKM_F_Shoes_ChunkyBoots"
SPEC = dict(top_z=0.22, offset=0.013, sole_height=0.035, sole_flare=1.1,
            upper=(0.11, 0.11, 0.12), sole=(0.16, 0.16, 0.17))


def build():
    return _footwear.build(NAME, **SPEC)


def run(out_dir):
    return _footwear.run(NAME, out_dir, **SPEC)
