"""propkit: static props (furniture, cups, kegs, signage...) from AI-generated models to Unity.

    import sys; sys.path.insert(0, r"<repo>/tools/blender")
    from propkit import ingest
    ingest.run(r"ArtSource/AI/RedCup/raw/red_cup.glb", "RedCup", height=0.12, budget="small")

Builds on tools/blender/aikit. See tools/blender/propkit/README.md.
"""
