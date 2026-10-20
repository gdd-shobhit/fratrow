"""aikit: shared Blender ingest toolkit for AI-generated models (Tripo and similar).

Generators hand over one dense, textured mesh at an arbitrary scale and facing,
often with floaters, duplicate vertices and non-manifold bits. These modules
turn that into something a pipeline can export:

    importer   import_model(path, keep_armature=False) -> [mesh objects]
    cleanup    join(objects, name), clean(obj, ...), shade(obj, ...)
    normalize  to_ground(obj, height=None, facing="-Y"), rotate_z(obj, degrees)
    reduce     decimate_to(obj, target_tris, symmetry=False) -> tris
    textures   extract(obj, out_dir, name, size=1024) -> {map: path}
    report     observe(obj) -> dict of numbers (observations, never verdicts)
    clean_glb  save(objects, path)
    meshutil   counts, transforms and connectivity shared by the above

Used by tools/blender/propkit (static props) and charkit's cosmetic ingest.
See tools/blender/aikit/README.md. Blender 5.0; 1 Blender unit = 1 m, Z up,
the front of a model faces -Y (charkit's convention).
"""
