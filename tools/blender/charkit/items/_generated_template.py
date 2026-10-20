"""Template for a cosmetic made from a generated (Tripo) model. Copy, rename, fill in.

1. Generate on the body reference sheet (ingest.reference_sheet) and record it:
   tools/tripo/tripo_gen.py (API) or `register` (a Studio download).
2. ingest.prepare(<raw model>, ASSET) once: fits it to the body and writes
   ArtSource/AI/<ASSET>/<ASSET>_clean.glb, printing each part's body regions.
3. List the parts this item uses in PARTS, pick the builder below, run(out_dir).

See design/art/ai-asset-pipeline.md. The leading underscore keeps this file out
of any "build every item" loop.
"""
from .. import ingest

ASSET = "F_Top_Example"                     # folder under ArtSource/AI/
SOURCE = f"ArtSource/AI/{ASSET}/{ASSET}_clean.glb"
NAME = "SKM_F_Top_Example"                  # SKM_ skinned, SM_ rigid (character-pipeline.md naming)
SLOT = "Top"
PARTS = (f"{ASSET}_part03",)                # from prepare()'s printout
BUDGET = 1500                               # final triangles (Top + Bottom + Shoes <= 3,000)


def run(out_dir):
    return ingest.skinned_item(NAME, SLOT, SOURCE, PARTS, BUDGET, out_dir,
                               max_gap=None)   # e.g. 0.012 for a tight top on a guessed body
    # Hair:   ingest.hair_item(NAME, SOURCE, PARTS, BUDGET, out_dir)
    # Rigid:  ingest.rigid_item(NAME, "HeadTop", SOURCE, PARTS, "Socket_HeadTop", BUDGET, out_dir)
