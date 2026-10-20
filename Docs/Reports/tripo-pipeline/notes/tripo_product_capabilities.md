# Tripo AI product capabilities for game-asset creation (as of 2026-10-19)

Method note: tripo3d.ai blocks automated fetchers (HTTP 403 to WebFetch). The official pages below were therefore downloaded directly with a browser user agent on 2026-10-19 and read as text. API facts come from three places: the official developer-portal bundle (developers.tripo3d.ai), the official SDK repos under github.com/VAST-AI-Research, and their changelogs. Blog dates are each page's `datePublished` value.

"Studio" means the Tripo Studio web app. "API" means the Tripo developer API. The two share models but price them differently. Searching stopped early because of the coordinator's usage limit, so some items are marked as gaps.

## 1. Current model and generation versions, input modes, polycount and topology

### Takeaway
As of October 2026 Tripo runs two model families:

- **H series** ("HD Model"; current version **H3.1**, API id `v3.1-20260211`): dense, high-detail meshes up to about 2M polygons.
- **P series** ("Smart Mesh"): low-poly real-time meshes.
  - **P1.0** (March 2026): triangles only, 500–20K faces.
  - **P2.0** (published 2026-08-19): adds native quads (tri 500–50K, quad 500–25K faces), auto-separated parts, 4-view input, up to 4 polycount variants per run, and regional "Mesh Edit".

The older v3.0 (Standard/Ultra, 2025), v2.5 and v1.4 snapshots can still be called through the API.

### Cited Findings
**Version timeline**
- **v3.0 (2025):**
  - Two modes. "Standard Mode: optimized topology for industrial-grade use. Perfect for game assets…" and "Ultra Mode… delivers assets with up to 2 million polygons".
  - The same release added sketch/image-to-3D, Flux / GPT-4o image generation, "T-Pose Models + Auto-Rigging", Magic Brush 2.0, Smart Part Segmentation and model privacy controls.
  - Source: [Tripo blog: v3.0 Ultra / 3.0 Standard](https://www.tripo3d.ai/blog/introducing-tripo-new-algorithm3)
- **v3.0 launch date:** September 2025 (press release dated Sept 2, 2025, seen via search). The API snapshot is `v3.0-20250812`. — [Yahoo Finance press release](https://finance.yahoo.com/news/tripo-frontrunner-3d-ai-boom-151500448.html); [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **March 25, 2026 announcement:**
  - $50M in funding "backed by Alibaba and Baidu Ventures".
  - **Tripo H3.1:** "high-fidelity geometry and visual precision".
  - **Tripo P1.0:** "optimized for real-time graphics and interactive environments", trained on native polygon mesh data, and said to bypass retopology.
  - **Tripo W1.0:** an early world-model project.
  - Claimed scale: more than 6.5M creators, 90,000 developers and about 100M assets generated.
  - Source: [PR Newswire, 2026-03-25](https://www.prnewswire.com/news-releases/tripo-ai-announces-50-million-in-funding-and-new-models-for-production-ready-3d-generation-302724894.html)
- **GDC 2026:** P1.0 was previewed as "native 3D diffusion". — [PR Newswire GDC 2026 release](https://www.prnewswire.com/news-releases/tripo-ai-debuts-production-grade-native-3d-diffusion-at-gdc-2026-302708371.html) (seen in search results only)
- **Smart Mesh P1.0 blog (2026/03/12):**
  - Produces "clean low-poly meshes with organized edge flow" in "just 2 seconds".
  - Positions H3.1 (HD Model) for "hero assets… high-poly geometry" and P1.0 for "real-time assets, games… clean low-poly topology".
  - At launch it was Studio-only: "API access will also be available soon".
  - Source: [Tripo blog: Smart Mesh P1.0](https://www.tripo3d.ai/blog/introducing-smart-mesh-v1)
- **P2.0 blog (2026-08-19):**
  - "generates clean quad topology natively".
  - "automatically separates body, clothing, and accessories into independent pieces during generation".
  - "triangle topology from 500 to 50,000 faces and quad topology from 500 to 25,000 faces". P1.0 was "Triangle: 500 - 20K" with no quads.
  - Multi-view input takes front, left, right and back images.
  - "up to 4 variants at different face counts in a single pass, for the same credit cost".
  - "Mesh Edit" regenerates only a selected region.
  - Generation "typically takes 10-40 seconds".
  - P1.0 "remains available to all users".
  - Source: [Tripo blog: P2.0 Official](https://www.tripo3d.ai/blog/tripo-p2-0-preview)
- **HD Model page:** "Create High-Poly 3D Model with Tripo H3.1 — Up to 2 million polygons", aimed at "3D printing and cinematic visual art". — [Tripo: High Detail Model](https://www.tripo3d.ai/features/high-detail-model)
- **Game-dev page:** Smart Mesh makes "game-ready meshes at 5K-20K faces for real-time rendering"; HD Model goes "up to 2M polygons". — [Tripo: AI 3D model generator for game developers](https://www.tripo3d.ai/use-cases/ai-3d-model-generator-for-game-developers)
- **Smart Mesh limits (Tripo's own FAQ):** "it does not yet fully replace Tripo's dedicated Retopology function for every use case; retopology improvements are coming soon." — [Tripo: Smart Mesh feature page](https://www.tripo3d.ai/features/smart-mesh)

**Input modes**
- **API generation endpoints:** `POST /v3/generation/text-to-model`, `/image-to-model` and `/multiview-to-model`.
  - Multiview order is front, left, back, right. Only the front view is required.
  - The API can also make multiview images from one image (`/image-to-multiview`, `/edit-multiview`), and do text-to-image and image-to-image.
  - Sources: [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md); [SDK CHANGELOG 1.0.0](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/CHANGELOG.md)
- **Model ids accepted on apiv3** for text/image/multiview generation: `v3.1-20260211`, `v3.0-20250812`, `v2.5-20250123`, `P1-20260311` and `P2-20260801`. `v1.4-20240625` is still accepted; `v2.0-20240919` is no longer available. — [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **Generation parameters in the SDK:** `face_limit`, `texture`, `pbr`, `texture_quality` (standard/detailed), `geometry_quality` (standard/detailed), `auto_size`, `quad`, `compress`, `generate_parts`, `smart_low_poly`, `export_uv`, `texture_alignment` and `orientation`. — [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **API face-limit ranges (developer docs):**
  - `P1-20260311`: 50–20,000.
  - `P2-20260801`: triangle 48–50,000, or 48–25,000 with `quad=true`.
  - Recommended minimums: "simple models start from 150 faces, complex models start from 250 faces".
  - P1 "does not support quad, smart_low_poly, generate_parts, or geometry_quality".
  - `geometry_quality: "detailed"` works only with model >= v3.0.
  - Source: [Tripo developer portal (developers.tripo3d.ai, generation docs)](https://developers.tripo3d.ai/)
- **Studio concept-image generation:** paid tiers get Nano Banana, Nano Banana Pro, Nano Banana 2, GPT Image 2 and Midjourney. Free gets "Nano Banana only". — [Tripo pricing](https://www.tripo3d.ai/pricing)

**Independent observation (competitor source)**
- Neural4D, a competitor, reports a default text-to-3D chair from Tripo 3.1/H3.1 at "965,873 faces / 498,150 vertices". It calls H3.1 topology "chaotic triangles on complex shapes" and P1.0 "organized edge flow". No test date is given; image uploads are dated 2026/06. — [Neural4D blog (competitor)](https://blog.neural4d.com/comparisons/tripo-3-1-model-review/)

### Inferences
- For a Unity indie pipeline, P2.0 is the main generator: quad or tri meshes within engine budgets, with parts already split. H3.1 output (about 1M faces by default) needs retopology or decimation before runtime use, or should serve only as a bake source.
- The `P2-20260801` API id suggests a model snapshot dated 2026-08-01, before the 2026-08-19 blog post.
- Variants give 4 face counts for one credit cost, so they can serve as a rough LOD chain from one generation. Tripo itself suggests "use different variants for different LOD levels". They are separate generations, though, not guaranteed-consistent LODs.

### Gaps
- No official 2026 number for H3.1's default face count, or for Standard vs Ultra counts, beyond "up to 2 million". The ~966K default comes from a competitor.
- No release notes separating H3.1 from v3.0 were found; the PR release gives no specs.
- Unclear whether a Studio text prompt can drive P2.0 directly. The P2.0 how-to describes only image or multi-view upload, but the API P-series pricing card lists "Text to 3D 30–40 credits".

## 2. Texturing: PBR maps, resolution, re-texturing, Magic Brush, styles, baked lighting

### Takeaway
- **PBR maps:** Base Color, Normal, Roughness and Metallic. One game-dev page also lists AO.
- **Resolution (API):** standard is the default, `detailed` is about 4K, and `extreme` is 8K.
- **Resolution (Studio):** 4K on Free, 8K on Max/Team, plus AI upscaling from 1K to 4K or 8K.
- **Baked lighting:** controlled by a "Remove Lighting" (delight) toggle in texture model v3.5 (`v3.5-20260815`). It is on by default for Smart Mesh.
- **Local edits:** the Magic Brush repaints selected areas.
- **Styles:** global style transfer exists in Studio. API stylization (cartoon, voxel and similar presets) was removed in October 2026.

### Cited Findings
- **Map types:** "Generate physically based textures instantly—Base Color, Normal, Roughness, and more". The FAQ adds "You can create Base Color, Roughness, Metallic, Normal, and other standard PBR maps." FBX, OBJ or GLB can be uploaded for re-texturing. — [Tripo: AI Texturing](https://www.tripo3d.ai/features/ai-texturing)
- **AO claim:** a game-dev marketing line says "automatic 4K PBR texture maps—diffuse, normal, metallic, roughness, and ambient occlusion included". — [Tripo: game developers page](https://www.tripo3d.ai/use-cases/ai-3d-model-generator-for-game-developers)
- **Remove Lighting, feature page:**
  - "Toggle Remove Lighting on for clean base colors that relight naturally across scenes, or off to preserve your reference's original lighting and style."
  - "For HD Model and the Texture module, it's a toggle in your Texture settings. For Smart Mesh, it's on by default when you add texture."
  - "It's included in the standard texture generation cost, and it only applies to new generations."
  - Source: [Tripo: AI Texturing](https://www.tripo3d.ai/features/ai-texturing)
- **Remove Lighting, Oct 2026 pipeline post:** "Remove Lighting On: gives you a clean base color that works consistently across different engine lighting setups. Remove Lighting Off: keeps the original lighting and shadow of your reference". — [Tripo blog: End-to-End 3D Pipeline (2026-10-10)](https://www.tripo3d.ai/blog/3d-game-pipeline)
- **API texture task** (`POST /v3/models/texture`):
  - Model versions: `v2.5-20250123`, `v3.0-20250812` and `v3.5-20260815`.
  - Options: `texture_quality` (standard, detailed, or fast on v3.5 only), `texture_alignment`, `part_names` (texture specific parts), `bake`, `text_prompt`, `image_prompt`, `style_image`, and `delight` (a v3.5 option).
  - Sources: [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md); [SDK CHANGELOG](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/CHANGELOG.md)
- **API quality tiers (developer docs):**
  - "`standard` — balanced quality and speed. `detailed` — higher fidelity, slower generation. `extreme` — 8K textures, highest fidelity. Costs additional credits compared to detailed."
  - The textured-model tips add: "texture_quality: 'detailed' adds ~90s but delivers 4K textures; 'extreme' produces 8K textures".
  - Detailed and extreme "work only with model versions >= v3.0".
  - "Textures are embedded in the GLB file."
  - PBR generation is "significantly slower (~120 s vs. ~20 s)".
  - Source: [Tripo developer portal](https://developers.tripo3d.ai/)
- **Studio resolution by tier:** Free includes "Up to 4k Texture Generation & Edit". Max adds "Unlimited Access to 8K Textures". The compare table marks 8K Texture as "Limited Access" below Max/Team. The page headline is "Industry-first True 8K Textures". — [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Upscaling:** "Texture Upscaling to take maps from 1K up to 4K or 8K". — [Tripo: Image to 3D Model](https://www.tripo3d.ai/features/image-to-3d-model)
- **Tripo's guidance on 8K:** "An 8K source is not automatically an 8K runtime requirement… Scale it down to 4K or 2K once the art pass is done." — [Tripo blog: game character workflow with P2.0 (2026-10-14)](https://www.tripo3d.ai/blog/mesh-game-character-p-2)
- **Magic Brush:** "Correct flaws, enhance details, or repaint small areas with a simple brush stroke". Magic Brush 2.0, in the v3.0 release, added edge feathering and brush strength. — [Tripo: AI Texturing](https://www.tripo3d.ai/features/ai-texturing); [Tripo v3.0 blog](https://www.tripo3d.ai/blog/introducing-tripo-new-algorithm3)
- **Paid-tier texture features:** "Part-based Texture Edit", "Texture Edit with Magic Brush" and "Single-view to Multi-view". — [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Styles:**
  - Studio advertises "global style transfer, so you can apply realistic, stylized, or artistic looks in one click", and still links a "Model Stylization" feature.
  - The API no longer supports it: "Refine and stylize are no longer supported: `refine_model` / `stylize_model` raise an error".
  - ComfyUI-Tripo 2.0.0 (2026-10-15) deprecated its Refine and Stylize nodes.
  - Sources: [Tripo: AI Texturing](https://www.tripo3d.ai/features/ai-texturing); [Tripo: Segmentation feature page](https://www.tripo3d.ai/features/ai-model-segmentation); [tripo-python-sdk README](https://github.com/VAST-AI-Research/tripo-python-sdk); [ComfyUI-Tripo README](https://github.com/VAST-AI-Research/ComfyUI-Tripo)
- **Press coverage:** a June 2026 trade report covered 8K texture support alongside H3.1 and P1.0. — [AMPulse (search result only)](https://www.ampulse.online/news/tripo-ai-unveils-next-generation-3d-generation-model-and-platform-features-g908f)

### Inferences
- For URP/HDRP lit materials, turn Remove Lighting on (the default for Smart Mesh) so shadows don't get baked into albedo. Turn it off for hand-painted or unlit styles.
- Don't rely on preset style filters (voxel, LEGO, cartoon) for API automation; the API stylize task is gone as of October 2026. Style has to come from the reference image or prompt, or from `style_image` / `text_prompt` on the texture task.

### Gaps
- Not documented: exact map filenames, channel packing (for example metallic-roughness packed per glTF, or separate maps in FBX), and whether AO or emissive maps are emitted for each format.
- The exact resolution of the "standard" texture tier was not found. Only detailed ≈ 4K and extreme = 8K are documented.
- The current Studio style-preset catalog was not found.

## 3. Smart retopology, low-poly, quad remesh, UVs

### Takeaway
There are three routes to an engine-budget mesh:

1. **Generate directly** with Smart Mesh P1.0 or P2.0.
2. **Smart Low Poly (retopology) on any existing mesh.** In the API, v3.0 outputs tri 500–20K or quad 500–10K faces, with optional Smart UV in the same task.
3. **Studio "HD Retopology + Normal Map Baking"**, built on P1.0, which turns an H-series mesh into low-poly plus a baked normal map.

Smart UV is an AI UV unwrapper (about October 2026) that makes "large, clean, editable UV islands" and shows UV utilization. A quad remesher and a `quad` option at convert time also exist.

### Cited Findings
- **API retopology** (`POST /v3/mesh/decimate`, SDK `smart_lowpoly`): versions `v1.0`, `v2.0` and `v3.0`, with v2.0 as the server default. "face limit: 500–20000 (quad ≤ 10000) for v2.0/v3.0. `smart_uv` needs v3.0." — [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **Retopology v3.0 pricing:** "newest retopology, with optional UV unwrap in the same task. Priced by output topology: 35 credits for a triangle mesh, 40 for a quad mesh (quad=true)". `face_limit` is "Triangle: 500–20,000; Quad: 500–10,000. Adaptive if omitted". — [Tripo developer portal (mesh-decimate docs)](https://developers.tripo3d.ai/)
- **API Smart UV** (`POST /v3/mesh/uv`, 20 credits):
  - Returns `model_url` and `uv_render_url`.
  - "the input must not exceed 80,000 equivalent triangles".
  - ComfyUI notes that "a quad counts as two" and that fresh generations are usually larger than the limit, so Smart UV should run after Smart low poly.
  - Sources: [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md); [ComfyUI-Tripo README](https://github.com/VAST-AI-Research/ComfyUI-Tripo)
- **Studio Smart UV and HD Retopology:**
  - Smart UV "places seams and unwraps your model like a professional in seconds. UV utilization is visible in real time".
  - Clicking a UV island highlights the matching faces.
  - Smart UV costs 20 credits; a Smart UV retry costs 10.
  - "HD Retopology + Normal Map Baking: Retopologize HD models into real-time-ready low-poly meshes while preserving high-poly detail through normal map baking. Built on P1.0 algorithms."
  - Source: [Tripo blog: End-to-End 3D Pipeline (2026-10-10)](https://www.tripo3d.ai/blog/3d-game-pipeline)
- **Smart UV layouts:** "Smart UV produces large UV islands and clean, editable layouts, packing densely with structure-aligned seams via smart atlases… You get free retries for three more UV layouts." — [Tripo blog: game character workflow with P2.0](https://www.tripo3d.ai/blog/mesh-game-character-p-2)
- **Quad remeshing:** Studio lists "Smart Mesh or Quad Remesher to convert triangles into clean quad topology". Separate "AI Quad Remesher" and "Quadrilateral Remesh" feature pages exist. — [Tripo: Image to 3D Model](https://www.tripo3d.ai/features/image-to-3d-model); [Tripo: Smart Mesh](https://www.tripo3d.ai/features/smart-mesh)
- **Convert options:** `quad=True`, `force_symmetry`, `face_limit`, `pack_uv` and `bake`. — [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **Tier gating:** "Smart Low Poly" and "Part-based Retopology" are listed as paid features. — [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Smart Mesh exports** claim to "include clean UVs and PBR texture maps". — [Tripo: Smart Mesh](https://www.tripo3d.ai/features/smart-mesh)

### Inferences
- Retopology caps out lower (≤20K tri, ≤10K quad) than P2.0 generation (50K tri, 25K quad). Assets that need 10–25K quads should be generated with P2.0 rather than retopologized.
- A plausible Unity prop chain: P2.0 generate, then Smart UV, then texture with Remove Lighting on, then FBX/GLB export. The HD route: H3.1, then HD Retopology with normal bake.

### Gaps
- No independent 2026 evaluation of UV quality after retopology (seams, texel density, stretch) was found. Only Tripo's own claims and a testimonial exist.
- The difference between Studio's "Quad Remesher" and Smart Low Poly v3.0 quad output is not documented.

## 4. Part segmentation, parts generation, part completion

### Takeaway
- **Segmentation v2** (2026/05/27) splits a mesh into semantic parts at three precision tiers: Simple 3–6 parts, Balanced 6–15, Detailed 15+. It works on Tripo-generated or imported models.
- **Pre-segmented generation:** models can come out already split ("Generate in Parts" in Studio, `generate_parts` in the API). P2.0 auto-separates body, clothing and accessories.
- **Closing cut surfaces:** "Fill Parts" offers a free Quick Cap, or AI Completion, which rebuilds watertight surfaces.
- **Per-part tools:** retexture, retopology and transform work per part.

### Cited Findings
- **Segmentation v2:**
  - "segments any 3D model into clean, named parts in under a minute, with three precision tiers and semantic boundary detection".
  - "Simple (3–6 parts)… Balanced (6–15 parts) is the default… Detailed (15+ parts) gives every limb, accessory, and joint its own piece."
  - "turn on Generate in Parts in the Model tab before you generate. The model comes out pre-segmented".
  - Imported models qualify "as long as it is in your Tripo library".
  - Parts can be renamed, and boundaries moved in a 3D editor.
  - Source: [Tripo blog: Segmentation v2 (2026/05/27)](https://www.tripo3d.ai/blog/tripo-segmentation-v2)
- **Fill Parts:** "Quick Cap seals openings instantly by closing the mesh boundary. It's free for everyone and consumes no credits. AI Completion rebuilds the open surface into a clean, watertight mesh… It's included with any paid plan and consumes no credits." — [Tripo blog: Segmentation v2](https://www.tripo3d.ai/blog/tripo-segmentation-v2)
- **Free-tier access:** "Your first try is free. Subscribe to unlock more." The pricing table marks Smart Segmentation "Limited Access" on Free and "Unlimited Access" on paid tiers. — [Tripo blog: Segmentation v2](https://www.tripo3d.ai/blog/tripo-segmentation-v2); [Tripo pricing](https://www.tripo3d.ai/pricing)
- **P2.0 parts:** P2.0 "automatically separates body, clothing, and accessories into independent pieces during generation", for "material assignment, LOD management, or modular assembly". — [Tripo blog: P2.0](https://www.tripo3d.ai/blog/tripo-p2-0-preview)
- **API:**
  - `POST /v3/mesh/segment` (model `v2.0-20260430`; `segmentation_granularity` is simple, balanced or detailed).
  - `POST /v3/mesh/complete` (`completion_mode` is "ai_completion" or "quick_cap"). "`mesh_completion` requires a `mesh_segmentation` task as input."
  - API price: "Segmentation & Completion 40–50 credits".
  - Sources: [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md); [Tripo developer portal pricing](https://developers.tripo3d.ai/)
- **Paid-tier part tools:** "Part-based Transform", "Part-based Retopology" and "Part-based Texture Edit". — [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Related open research:** HoloPart ("Generative 3D Part Amodal Segmentation", April 2025, MIT) completes occluded part geometry. — [HoloPart GitHub](https://github.com/VAST-AI-Research/HoloPart)

### Inferences
- Segmentation output is aimed at 3D printing and rigging prep as much as games. For Unity, the main uses are separate materials or submeshes (hair, clothing) and swappable accessories.
- Tripo's own character workflow generates the head and body in separate passes. That buys polygon and texture budget per part, at the cost of alignment work.

### Gaps
- How parts appear in FBX/GLB exports is not documented: separate meshes vs submeshes, naming, and whether parts share UV/texture sets.
- The Studio credit cost of segmentation beyond the free first try was not found.

## 5. Auto-rigging and animation (feature facts only)

### Takeaway
- **Humanoid rigger:** rebuilt and released 2026-10-13 in Studio; API rig model `v3.0-20260909`. It rigs full body plus hand bones.
- **Five engine skeleton presets:** UE5 Mannequin, Mixamo, VRM 1.0, Unity Humanoid and ActorCore.
- **Animation:** Text to Motion makes 1–10 s clips. Multi-stage Motion chains up to 5 segments. A preset library of 100+ motions is free in Studio.
- **Non-humanoids** (quadruped, hexapod, octopod, serpentine, aquatic, "others") use the v2.5 rigger.
- **Export:** rigged models export as FBX or GLB with animations.

### Cited Findings
- **Studio Rigging & Animation (2026-10-13):**
  - "auto-rigging your character with full-body and hand bones".
  - "5 Engine-Ready Skeletons: UE5 Mannequin, Mixamo, VRM 1.0, Unity Humanoid, ActorCore".
  - "Text to Motion… animations lasting 1–10 seconds".
  - "Multi-stage Motion… combine up to five motion segments".
  - "Rigging supports humanoid models up to 100,000 triangle faces. Characters should be in T-pose or A-pose."
  - "Rigging costs 20 credits per character. Text to Motion costs 20 credits per animation. Preset animations from the library are free."
  - "Non-humanoid models use the v2.5 rigging algorithm, which works without engine-specific presets."
  - Source: [Tripo blog: Rigging & Animation](https://www.tripo3d.ai/blog/rigging-animation)
- **Pipeline post:** "Select the preset that matches your engine. Auto-rig. Export FBX. Drag into your engine, and the skeleton maps correctly, no re-mapping needed." Multi-stage Motion also lets you "Drag waypoints on the XY plane to control movement direction". — [Tripo blog: End-to-End 3D Pipeline](https://www.tripo3d.ai/blog/3d-game-pipeline)
- **API rigging:**
  - `rig_check` is free and returns `riggable` and `rig_type` (for example "biped").
  - `rig` takes model `v1.0-20240301` (default), `v2.5-20260210` or `v3.0-20260909`.
  - `spec`: `tripo`, `mixamo` and `actorcore` work with every version. `unity`, `unreal` and `vrm` need v2.5 or v3.0.
  - `retarget` takes "At most 5 animations per task".
  - Measured compatibility (2026-10-15):

    | Rig version | Skeleton types | Humanoid presets | Serpentine/aquatic presets | Quadruped/hexapod/octopod presets |
    | --- | --- | --- | --- | --- |
    | v3.0 | all | yes | yes | no |
    | v2.5 | all | no | yes | yes |
    | v1.0 | bipeds only | — | — | — |

  - Sources: [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md); [ComfyUI-Tripo README](https://github.com/VAST-AI-Research/ComfyUI-Tripo)
- **API pricing card:** "Rig Check Free · Auto Rig 25 credits · Animation Retarget 10 credits / animation · *100+ preset motions, batch up to 5". — [Tripo developer portal](https://developers.tripo3d.ai/)
- **API docs tips:**
  - "rig_type: 'biped' is for humanoids; use 'quadruped' for four-legged creatures".
  - "Retarget supports both preset animations and custom FBX animation file_tokens".
  - "Each animation in the array creates a separate output file — 5 presets = 5 GLBs".
  - Retargeting an unrigged task fails with error 2006.
  - Source: [Tripo developer portal (animated-scene cookbook)](https://developers.tripo3d.ai/)
- **Feature page:** rigs "Humans, quadrupeds, stylized characters, and even mechanical creatures"; exports "FBX or GLB with skeletons included". — [Tripo: AI Auto Rigging](https://www.tripo3d.ai/features/ai-auto-rigging)
- **Export options:** convert supports `with_animation`, `animate_in_place` and `fbx_preset` (default "blender"). — [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **v3.0 (2025):** characters could export "directly in T-Pose, with skeleton binding included… ready for animation in tools like Mixamo". — [Tripo v3.0 blog](https://www.tripo3d.ai/blog/introducing-tripo-new-algorithm3)
- **Related open-source:** UniRig (SIGGRAPH 2025, MIT) and SkinTokens (MIT, 2026) predict skeletons and skinning. — [UniRig GitHub](https://github.com/VAST-AI-Research/UniRig); [VAST-AI-Research org](https://github.com/VAST-AI-Research)

### Inferences
- For Unity, the "Unity Humanoid" preset (API `spec="unity"`, which needs rig v2.5 or v3.0) or the `mixamo` spec should map to Mecanim Humanoid avatars. Rig quality and Mecanim mapping are covered by the other researcher.
- Studio and API price rigging differently: 20 vs 25 credits per rig, and free library animations in Studio vs 10 credits per retarget in the API.

### Gaps
- Bone names and finger joint counts per preset were not documented in the sources read. "Hand bones" is stated; per-finger chain depth is not.
- Text to Motion looks Studio-only for now. No text-to-motion route appears in the SDK 1.0.0 route map, and the SDK's 2026-10-14 commit history includes "Remove text_to_motion for now". — [tripo-python-sdk commits](https://github.com/VAST-AI-Research/tripo-python-sdk/commits/master)

## 6. Export formats and options

### Takeaway
- **Formats:** GLB/glTF, FBX, OBJ, USDZ, STL and 3MF. Some pages also cite USD.
- **API convert options:** face limit, quad conversion, texture size (default 4096) and format (default JPEG), scale factor, pivot-to-center-bottom, orientation, UV packing, baking, vertex colors, animation inclusion and in-place animation.
- **Delivery:** GLB embeds textures. API download URLs expire after 5 minutes.

### Cited Findings
- **Convert call:** `convert_model(…, "FBX")` supports `GLTF | USDZ | FBX | OBJ | STL | 3MF`. Options: `quad`, `force_symmetry`, `face_limit`, `flatten_bottom`, `flatten_bottom_threshold`, `texture_size=4096`, `texture_format="JPEG"`, `scale_factor=1.0`, `pivot_to_center_bottom`, `with_animation`, `pack_uv`, `bake`, `part_names`, `export_vertex_colors`, `fbx_preset="blender"`, `export_orientation="+x"` and `animate_in_place`. — [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **Developer-portal notes:**
  - FAQ formats: "GLB, GLTF, FBX, OBJ, STL, USDZ and 3MF".
  - "STL discards all color/material data — if you need colors, use OBJ + MTL".
  - "Model URLs expire after 5 minutes — download immediately after task success".
  - "Textures are embedded in the GLB file".
  - apiv3 returns a single `model_url`.
  - Sources: [Tripo developer portal](https://developers.tripo3d.ai/); [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **Import:** `POST /v3/models/import` accepts GLB, OBJ, FBX and STL. Uploads above 50 MB use a presigned PUT; the upload limit is 150 MB. — [SDK CHANGELOG](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/CHANGELOG.md); [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **Studio export:** "Export as USD, FBX, OBJ, STL, GLB, or 3MF". Smart Mesh models export "in… FBX, GLB, OBJ, and USDZ". — [Tripo: Image to 3D Model](https://www.tripo3d.ai/features/image-to-3d-model); [Tripo: game developers page](https://www.tripo3d.ai/use-cases/ai-3d-model-generator-for-game-developers)
- **Export caps:** Free allows "Exports 15 (H2.5 only)". Paid tiers get unlimited exports and "Unlimited Batch Exports". — [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Unity Bridge default import settings:** "Generate Colliders: false · Import Materials: true · Import Textures: true · Pivot: Center Bottom · Texture Resolution: Current Maximum". — [Tripo blog: DCC Bridge for Unity](https://www.tripo3d.ai/blog/tripo-dcc-bridge-for-unity)

### Inferences
- Axis defaults differ: convert uses `fbx_preset` "blender" and `export_orientation` "+x", while the Blender add-on faces models +Y by default. The team should set orientation, scale and pivot explicitly in API exports and check the axis on Unity import (Unity is Y-up, +Z forward).
- Free-tier exports are capped at 15 and limited to the older "H2.5" model, so production assumes a paid tier.

### Gaps
- The default FBX unit scale (m vs cm) and what `auto_size` maps to in real units are not documented.
- Whether Studio exports expose the same texture-size, format and pivot options as the API convert task was not verified.

## 7. API: endpoints, pricing, SDKs, rate limits

### Takeaway
- **Version:** the current API is **apiv3** (`https://openapi.tripo3d.ai`). **The v2 API shuts down on 2026-10-31**, and SDK 0.x and ComfyUI-Tripo 1.x stop working then. Use `tripo3d>=1.0.0`.
- **Billing:** pay-as-you-go at **100 credits = $1.00**. Credits don't expire and failed tasks aren't charged.
- **Limits:** 3 parallel tasks per category by default; usage tier raises the limit.
- **Tooling:** official SDKs for Python, JS, Go, Java and Rust; a Node CLI (`tripo-cli`) aimed at AI coding agents; and webhooks.
- **Studio and API billing are separate.** A Studio subscription does not include API access or API credits. Each needs its own purchase, and API credits are a separate pay-as-you-go balance.

### Cited Findings
- **Studio vs API billing:** the developer-portal FAQ asks "If I have a Tripo Studio subscription, can I use the API service?" and answers "No. Tripo API and Tripo Studio operate under separate billing systems and require separate purchases." The API billing page says "Tripo3D currently operates on a pay-as-you-go credit model. Top up your balance to start generating". New API accounts may get trial credits ("Includes {{credits}} trial credits, expiring {{date}}"); the amount is filled in at runtime. — [Tripo developer portal FAQ / billing](https://developers.tripo3d.ai/)
- **SDK pinning:**
  - Install with `pip install "tripo3d>=1.0.0,<2.0.0"`.
  - "1.x will not make breaking changes, and deprecated items are removed only in 2.0.0."
  - ComfyUI-Tripo 2.0.0 requires that range.
  - The SDK's 1.0.0 merge (2026-10-14) includes a commit titled "Remove text_to_motion for now", so text-to-motion is not exposed in the API SDK yet.
  - Sources: [SDK CHANGELOG](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/CHANGELOG.md); [ComfyUI-Tripo README](https://github.com/VAST-AI-Research/ComfyUI-Tripo); [tripo-python-sdk commits](https://github.com/VAST-AI-Research/tripo-python-sdk/commits/master)
- **v2 shutdown:** "The v2 API is shut down on 2026-10-31 and 0.x releases stop working then." SDK 1.0.0 (2026-10) moves to apiv3. The China endpoint is `https://openapi.tripo3d.com`. — [tripo-python-sdk README](https://github.com/VAST-AI-Research/tripo-python-sdk); [SDK CHANGELOG](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/CHANGELOG.md)
- **apiv3 routes:**

  | Area | Routes |
  | --- | --- |
  | Generation | `/v3/generation/text-to-model`, `image-to-model`, `multiview-to-model`, `text-to-image`, `image-to-image`, `image-to-multiview`, `edit-multiview`, `image-to-splat`, `image-to-depth` |
  | Models | `/v3/models/texture`, `convert`, `import` |
  | Animations | `/v3/animations/rig-check`, `rig`, `retarget` |
  | Mesh | `/v3/mesh/segment`, `complete`, `decimate`, `uv` |
  | Tasks | `GET /v3/tasks/{task_id}`, `POST /v3/tasks/list` |
  | Files | `POST /v3/files`, `/v3/files/presign` |

  Refine and stylize are removed. — [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **Docs coverage:** the developer portal also has pages for webhooks, the CLI and a "codex-plugin". — [Tripo developer portal](https://developers.tripo3d.ai/)
- **API pricing cards** (developer portal, fetched 2026-10-19):

  | Category | Prices |
  | --- | --- |
  | Image Generation | Text to Image 5 credits; Generate Image 5–10; Image to Multiview 10; Edit Multiview 5 per image |
  | 3D Generation, H series | Text to 3D 10–20; Image to 3D 20–30; Multiview to 3D 20–30; "Advanced Setup From 5 credits" |
  | 3D Generation, P series | Text to 3D 30–40; Image to 3D 40–50; Multiview to 3D 40–50 |
  | Model Processing | Texture 10–30; Format Convert 5–10; Retopology 10–30; Segmentation & Completion 40–50 |
  | Animation | Rig Check free; Auto Rig 25; Animation Retarget 10 per animation |

  — [Tripo developer portal](https://developers.tripo3d.ai/)
- **More API pricing details:**
  - "P2 base is 100 credits; texture add-ons match P1 (standard +10)".
  - Retopology v3.0 costs 35 credits for triangles and 40 for quads.
  - Smart UV costs 20 credits.
  - Worked example: "v3.1-20260211… texture_quality detailed… geometry_quality detailed… ~120s, ~50 credits".
  - Worked example: a P1 character at 5,000 faces with a rig and 3 animations takes "~105s, ~85 credits".
  - Sources: [Tripo developer portal](https://developers.tripo3d.ai/); [tripo-python-sdk docs/API.md](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/docs/API.md)
- **Billing terms:**
  - "100 credits = $1.00 USD". The minimum top-up is $1, and Stripe auto-recharge is available.
  - "Our credits do not expire and remain valid indefinitely."
  - "Failed tasks will not be charged."
  - Enterprise volume pricing is available via business@tripo3d.ai.
  - "We do not use any customer data, including inputs or generated models, for model training."
  - "Tripo API and Tripo Studio are powered by the same underlying models and algorithms."
  - Source: [Tripo developer portal](https://developers.tripo3d.ai/)
- **Rate limits:**
  - "Rate limits ensure fair and reliable access to the API by placing specific caps on parallel requests. Your usage tier determines how high these limits are set."
  - The concurrency table lists "3D Generation — H Series: 3 parallel tasks", "P Series: 3 parallel tasks" and "Animation (shared with rigging/retarget): 3 parallel tasks".
  - The portal advertises "Independent concurrency quotas for each task type".
  - Source: [Tripo developer portal](https://developers.tripo3d.ai/)
- **SDK behavior:** `GET` requests are retried up to 3 times on 429/502/503/504. Tasks report `credits_consumed`. — [SDK CHANGELOG](https://github.com/VAST-AI-Research/tripo-python-sdk/blob/master/CHANGELOG.md)
- **Official SDK repos:** `tripo-python-sdk` (MIT, updated 2026-10-14); `tripo-js-sdk`, `tripo-go-sdk` and `tripo-rust-sdk` (MIT, 2026-09-18); `tripo-java-sdk` (MIT, 2026-07-13). — [VAST-AI-Research GitHub org](https://github.com/VAST-AI-Research)
- **Tripo CLI:** install with "`npm install -g tripo-cli`", which "Requires Node.js 20 or later". The portal pitches it with "Copy the prompt into Codex, Claude Code or Cursor". A DeepSeek Harness plugin wraps tripo-cli. — [Tripo developer portal](https://developers.tripo3d.ai/); [Tripo3D-Plugin-dsh](https://github.com/VAST-AI-Research/Tripo3D-Plugin-dsh); [Studio CLI page](https://studio.tripo3d.ai/cli)

### Inferences
- At $0.01 per credit, an API P2.0 quad character costs about 100 credits for the mesh, about 10 for texture, 25 for the rig and 10 per animation clip. That is roughly $1.35–$1.75 per animated character before retries. H-series props cost roughly $0.20–$0.50 each.
- Any tutorial or plugin older than October 2026 that targets the v2 endpoints breaks after 2026-10-31. Two official integrations look exposed (see section 8):
  - The Blender add-on vendors an SDK commit from 2025-10-23, a 0.x / v2-era release.
  - tripo-mcp depends on an unbounded `tripo3d>=0.2.0`.
- A team that subscribes to Studio for artists and also automates through the API pays twice: a Studio plan plus API top-ups at $0.01 per credit. API credits never expire. Studio subscription credits reset every billing cycle (section 9).

### Gaps
- The usage-tier thresholds for concurrency above 3 were not found.
- Exact add-on costs were not found for detailed geometry, `extreme` 8K textures, `generate_parts`, and quad on the H series. The docs say only "From 5 credits" and "Costs additional credits compared to detailed".
- The amount of free trial credits for new API accounts was not found.

## 8. Plugins and integrations

### Takeaway
- **Studio "DCC Bridge":** sends models from the browser to Blender, 3ds Max, **Unity**, Unreal, Maya, Cocos, **Godot**, ZBrush, Roblox and SketchUp. MetaTailor is also listed. It requires a paid tier.
- **Unity Bridge:** a UPM package for Unity 2021.3 LTS+ that auto-detects Built-in, URP or HDRP.
- **API-key integrations:**
  - official Blender add-on
  - official ComfyUI nodes (v2.0.0, on apiv3)
  - alpha official MCP server
  - Tripo as a generator in mcp-for-blender (formerly blender-mcp)

### Cited Findings
- **Bridge targets and tier:** the pricing page lists "DCC Bridge: Blender, 3ds Max, Unity, Unreal, Maya, Cocos, Godot, ZBrush" as a Pro+ feature ("Unlimited Batch Exports · DCC Bridge"). The footer lists plugins for Blender, 3ds Max, Unity, Unreal, Maya, Cocos, Godot, ZBrush, MetaTailor and Roblox. — [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Downloads:** the Unity bridge zip is at `tripo-public.tripo3d.ai/plugins/unity-bridge/Tripo3d_Unity_Bridge-latest.zip`. Godot, UE, Maya, 3ds Max, Cocos, ZBrush and Roblox bridges sit on the same host. — [Unity Bridge zip](https://tripo-public.tripo3d.ai/plugins/unity-bridge/Tripo3d_Unity_Bridge-latest.zip); link list from the [Tripo pricing page HTML](https://www.tripo3d.ai/pricing)
- **Unity Bridge (guide dated 2026/03/05):**
  - Requirements: "OS: Windows, Mac · Unity: 2021.3 LTS or newer (recommended 2022.3 LTS+ or Unity6 LTS+) · Browser: Google Chrome 116+, Edge 116+, Opera 102+".
  - Install via Package Manager → "Add package from disk…" → package.json. The panel is under Tools → Tripo Bridge.
  - "the Render Pipeline is automatically detected… Standard (Built-in Render Pipeline), URP, HDRP".
  - Animations made in Studio can be selected at export and sent.
  - "model transfer is only supported in Unity Editor mode".
  - "The connection only recognizes the first Unity launched."
  - Purple materials mean a render-pipeline mismatch.
  - Source: [Tripo blog: DCC Bridge for Unity](https://www.tripo3d.ai/blog/tripo-dcc-bridge-for-unity)
- **Other bridge guides:** Unreal and 3ds Max guides are dated 2026/03/05; a SketchUp guide is dated 2026/10/15. — [Tripo blog: Smart Mesh P1.0 related posts](https://www.tripo3d.ai/blog/introducing-smart-mesh-v1); [Tripo blog announcements](https://www.tripo3d.ai/blog?tab=announcement)
- **Official Blender add-on** (MIT; repo last pushed 2025-11-06):
  - Needs Blender 3.0+ and a Tripo API key.
  - Text-, image- and multiview-to-model, with texture quality, PBR, face limit and style options.
  - v0.7.0 added concurrent generation; v0.6.0 made models face +Y by default.
  - Source: [tripo-3d-for-blender](https://github.com/VAST-AI-Research/tripo-3d-for-blender)
- **ComfyUI-Tripo changelog:**
  - 2.0.0 (20261015): API v3, a Smart UV node, Rig v2.5/v3.0 with more skeleton specs, texture v3.5 (`fast`, `delight`), and smart low poly v3.0.
  - Segmentation v2.0: 20260630. Model import: 20260528.
  - Source: [ComfyUI-Tripo](https://github.com/VAST-AI-Research/ComfyUI-Tripo)
- **TripoSplat** ships an official ComfyUI workflow template. — [TripoSplat GitHub](https://github.com/VAST-AI-Research/TripoSplat)
- **Official "Tripo MCP Server"** (MIT; last pushed 2025-04-14): "This project is in alpha. Currently, it supports Tripo Blender Addon integration." Installed with `uvx tripo-mcp`. — [tripo-mcp](https://github.com/VAST-AI-Research/tripo-mcp)
- **tripo-mcp dependencies:** `pyproject.toml` is version 0.1.2 and requires `"tripo3d>=0.2.0"` with no upper bound. `src/server.py` imports `TripoClient` from `tripo3d` and talks to the Blender add-on over a socket. — [tripo-mcp pyproject.toml](https://github.com/VAST-AI-Research/tripo-mcp/blob/main/pyproject.toml); [tripo-mcp src/server.py](https://github.com/VAST-AI-Research/tripo-mcp/blob/main/src/server.py)
- **Blender add-on SDK pin:** the add-on vendors `tripo-python-sdk` as a submodule at commit `067bfe89` ("fix version", 2025-10-23). That is a 0.x SDK from before the 2026-10-14 move to apiv3, and 0.x stops working when v2 shuts down on 2026-10-31. — [tripo-3d-for-blender repo contents](https://github.com/VAST-AI-Research/tripo-3d-for-blender); [tripo-python-sdk commits](https://github.com/VAST-AI-Research/tripo-python-sdk/commits/master); [tripo-python-sdk README](https://github.com/VAST-AI-Research/tripo-python-sdk)
- **mcp-for-blender** (formerly blender-mcp) lists "AI-generated 3D models via Tripo, Hyper3D Rodin and Hunyuan3D". Its Premium tier lets you "Generate AI 3D models (Hunyuan3D, Tripo, Hyper3D Rodin) straight into Blender without bringing your own API keys." — [mcp-for-blender README](https://github.com/ahujasid/mcp-for-blender)
- **Agent-built game guide:** on 2026/10/16 Tripo published "How to Build a 3D Game with Opus 5.5, Astra, and Tripo", using Tripo, Blender, Unity and AI coding agents. — [Tripo blog](https://www.tripo3d.ai/blog/how-to-build-an-explorable-3d-game-with-tripo-and-ai)

### Inferences
- **Risk after 2026-10-31:** the official Blender add-on's vendored 0.x SDK will likely stop working once v2 shuts down, unless VAST ships an update. Its repo was last pushed 2025-11-06. tripo-mcp would pull SDK 1.x on a fresh install; 1.x keeps the 0.x method names, so it may keep working, but this is untested. The Studio DCC Bridges are browser-to-app and don't depend on the public API version.
- No Unity Asset Store listing was found. The Unity integration is the browser-to-Editor bridge, which needs a Studio session and a paid tier; it is not an API-key Editor tool. Automated Unity pipelines should call the API (SDK or CLI) and import the files with Unity's importer.
- The team's existing mcp-for-blender setup can already call Tripo through `generate_3d`. That goes through mcp-for-blender's own premium/key path, though, not necessarily the team's Tripo subscription credits.

### Gaps
- No version history or changelog was found for the Unity Bridge package, and no documentation of how it maps PBR maps into URP/HDRP Lit materials.
- The Studio-side Blender DCC Bridge page (`/integrations/blender`) was not read in detail. The Godot bridge requirements were not read.

## 9. Pricing tiers, credits, per-operation cost, commercial licensing

### Takeaway
Studio tiers as of 2026-10-19:

| Tier | Price | Credits/month | Concurrent tasks | Rights |
| --- | --- | --- | --- | --- |
| Free | $0 | 200 | 1 | Public models, non-commercial |
| Pro | $20/mo billed annually ($240/yr) | 3,000 | 10 | Private models, commercial use |
| Max | $90/mo billed annually ($1,080/yr) | 25,000 | 100 | Private models, commercial use |
| Team | $55/seat/mo annual, $110/seat monthly | 90,000 | 200 | Private models, commercial use |

Credit packs are sold separately. Studio per-operation costs (Oct 2026): P2.0 mesh 100, Mesh Edit 50, Smart UV 20 (retry 10), Texture 20, Rigging 20, Text to Motion 20, preset animations free.

Terms of Service (last updated July 11, 2025):
- **Free users:** Tripo "retains all rights" to their inputs and outputs.
- **Paid users:** they "generally have all rights" to outputs, and grant Tripo a license to use and display them for providing the service.

### Cited Findings
- **Free:**
  - "$0 · 200 Monthly Credits ≈ 13 Models · 1 Concurrent Task · Standard Queue · Public Models · Non-Commercial Use · Limited Edit History, Storage & Exports".
  - Includes "HD Model Access, Up to 4k Texture Generation & Edit, Retopology".
  - The compare table adds 1-day history, 3 free retries, storage for 10 models, and "Exports 15 (H2.5 only)".
  - Source: [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Pro:**
  - "$20.00 / Month billed annually as: $240.00 / year · 3000 Monthly Credits ≈ 200 Models ($0.16 Per Model)".
  - "10 Concurrent Tasks · High Queue Priority · Private Models · Commercial Use · Batch Generation · 7-day History · More Free Retries · Unlimited Storage · Unlimited Batch Exports · DCC Bridge".
  - Plus "HD Model Access, Smart Mesh Access, Multi-view to 3D, Smart Segmentation, Ultra Mesh Quality & More".
  - Source: [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Max:**
  - "$90.00 / Month billed annually as: $1080.00 / year · 25000 Monthly Credits ≈ 1660 Models ($0.09 Per Model)".
  - "100 Concurrent Tasks · Dedicated Queue Priority · Permanent Edit History · Unlimited Free Retries & Storage · 3 Free Pro Refine · Unlimited Access to 8K Textures · Early Access to Beta Features".
  - Source: [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Team:**
  - "$110.00 → $55.00 / Mo / seat billed annually as: $1980.00 / year · 90000 Monthly Credits ≈ 6000 Models · 200 Concurrent Tasks · Shared Workspace & Assets · Centralized Billing".
  - The compare table shows "Monthly: $330.00 · Yearly: $1980.00".
  - Source: [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Pricing-page FAQ (embedded JSON, fetched 2026-10-19):**
  - "Credits are the usage currency on Tripo. Each model generation and editing consume credits depending on complexity and settings… Plans include monthly credits, and you can always purchase extra credits as needed."
  - "New users receive free credits to try AI 3D generation".
  - "Subscription credits reset based on billing cycle, not calendar month. If you upgrade mid-cycle, you'll only be charged the difference… prorated for the remaining time."
  - "Refunds are available for accidental purchases if you submit a request within 24 hours and no credits have been used."
  - Payment by Visa, Mastercard, Amex and Alipay, with PayPal invoices also covered.
  - Plan names: "upgrade to the Professional/Max/Team Plan… for commercial use, private assets, and higher-quality outputs". "Pro" is the Professional plan.
  - Enterprise and high-volume plans go through business@tripo3d.ai.
  - Source: [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Separate from API billing:** Studio plan credits do not fund the API ("separate billing systems and require separate purchases"). — [Tripo developer portal FAQ](https://developers.tripo3d.ai/)
- **Credit packs:** $10 for 1,000 credits; $100 for 10,000 + 2,000; $500 for 50,000 + 12,000; $1,000 for 100,000 + 30,000. — [Tripo pricing](https://www.tripo3d.ai/pricing)
- **Studio per-step costs:** "P2 Official (Mesh Generate) 100 · Mesh Edit 50 per edit · Smart UV 20 · Smart UV Retry 10 · Texture 20 · Rigging 20 · Text to Motion 20 · Full pipeline (one character, no edits/retries): ~180 credits / $1.8". "All users get free trials on P2 and Smart UV." — [Tripo blog: End-to-End 3D Pipeline (2026-10-10)](https://www.tripo3d.ai/blog/3d-game-pipeline)
- **Mesh Edit and P2.0 quotas:** "Pro users get 3 free edits per model. Max and Team users get unlimited edits. After the free tier, each edit costs 50 credits." Every user gets 2 free single-image P2.0 generations; multi-view P2.0 is subscribers-only. — [Tripo blog: P2.0](https://www.tripo3d.ai/blog/tripo-p2-0-preview)
- **Terms of Service** ("Last updated: July 11, 2025"; operator Holymolly Ltd):
  - §5.2.1: "For Users who access and use the Services free of charge ('Free Users'), Tripo retains all rights, including without limitation the rights to use, copy, reproduce, modify… license… the Inputs and Outputs submitted or generated by Free Users, as well as all Intellectual Property rights arising therefrom."
  - §5.2.2: "Paid Users generally have all rights (including… use, copy, reproduce, modify… derive revenue…) of the Inputs and Outputs… subject to… the consent and authorization to authorize the Company to use and display the Inputs and Outputs as may be necessary for Service provision on royalty-free, perpetual, irrevocable, worldwide, non-exclusive basis".
  - Also §5.2.2: "Company will not use Inputs and Outputs as training data to train, validate, test, or improve any AI Technology."
  - Outputs placed in "public" areas "may be retained in perpetuity".
  - Disputes go to HKIAC arbitration in Hong Kong.
  - Source: [Tripo Terms of Service](https://www.tripo3d.ai/terms)

### Inferences
- **Tier-name conflict:** the live pricing page shows Free/Pro/Max/Team, but the P2.0 and Smart Mesh FAQs name "Starter, Pro, Advanced, Max, Team" as multi-view tiers. Starter and Advanced may be legacy, regional or unlisted plans; the team should check which plan its account is actually on.
- **Per-model math conflict:** the "≈ models" estimates work out to about 15 credits per model, which can't reflect P2.0 at 100 credits a mesh. At the per-step Studio costs, Pro's 3,000 credits cover about 16 full P2.0 characters a month (~180 credits each).
- **Team seats:** Team appears to bill a 3-seat minimum ($1,980/yr = $55 × 12 × 3; $330/mo = $110 × 3).
- **Monthly prices:** the page shows the same Pro and Max figure in both billing columns, beside a "New User 50% Off First Month" banner. The true month-to-month price may differ from the annual-equivalent shown.
- **Licensing for a commercial game:** every shipped asset must be generated on a paid account. Read literally, §5.2.1 means outputs made while on Free belong to Tripo even after an upgrade; this is an inference from the wording.

### Gaps
- The Studio credit cost of a standard H3.1 generation (with or without texture) was not found.
- No evidence that the free tier is currently CC BY 4.0. The current Terms say Tripo retains all rights and contain no CC license language.
- Unclear whether unused monthly subscription credits roll over. The FAQ says only that they "reset based on billing cycle". Whether purchased Studio credit packs expire is also unstated; API credits explicitly never expire.
- The Studio per-operation credit table that the FAQ mentions ("see the table below") loads at runtime. It is not in the static HTML or the page's JS chunks, so the Studio cost of an H3.1 generation is still unverified.

## 10. TripoSR and later open-source Tripo / VAST releases

### Takeaway
- **TripoSR** (Tripo AI + Stability AI; technical report March 2024; MIT for code and weights) is a fast LRM-style single-image reconstructor. It runs in under 0.5 s on an A100 and needs about 6 GB VRAM. Output is vertex colors by default, with an optional texture bake. It is far below the commercial platform: no PBR, quads, rigging or parts.
- **Later open releases from VAST-AI-Research:**
  - **TripoSG** (Mar 2025): 1.5B-parameter rectified-flow image-to-shape model; MIT; ≥8 GB VRAM.
  - **TripoSF / SparseFlex** (Mar 2025): a VAE for 1024³ arbitrary-topology shapes; MIT; ≥12 GB VRAM.
  - **HoloPart** (Apr 2025): amodal part segmentation and completion; MIT.
  - **UniRig** (SIGGRAPH 2025): auto-rigging; MIT.
  - **TripoSplat** (2026): single image to up to 262,144 Gaussians; MIT.
- None of these is the commercial H3.1, P1.0 or P2.0 model.

### Cited Findings
- **TripoSR:**
  - "a state-of-the-art open-source model for fast feedforward 3D reconstruction from a single image, collaboratively developed by Tripo AI and Stability AI".
  - "high-quality 3D models in less than 0.5 seconds on an NVIDIA A100 GPU".
  - "released under the MIT license, which includes the source code, pretrained models".
  - "The default options takes about 6GB VRAM for a single image input."
  - Vertex colors by default; `--bake-texture` with `--texture-resolution` gives a texture instead.
  - Technical report: arXiv 2403.02151. The repo was created 2024-02-07 and last pushed 2026-06-04.
  - Sources: [TripoSR GitHub](https://github.com/VAST-AI-Research/TripoSR); [TripoSR technical report](https://arxiv.org/abs/2403.02151); [VAST-AI-Research org](https://github.com/VAST-AI-Research)
- **TripoSG:**
  - "[2025-03] Release of TripoSG 1.5B parameter rectified flow model and VAE trained on 2048 latent tokens".
  - "[2025-04] Release TripoSG-scribble… 512 token model for fast shape prototyping from scribble+prompt".
  - "CUDA-enabled GPU with at least 8GB VRAM".
  - Face limit via `--faces 5000`; SDF-based VAE; MIT; weights at huggingface.co/VAST-AI/TripoSG.
  - Sources: [TripoSG GitHub](https://github.com/VAST-AI-Research/TripoSG); [TripoSG on Hugging Face](https://huggingface.co/VAST-AI/TripoSG)
- **TripoSF (SparseFlex):** "Ultra-high resolution mesh modeling (up to 1024³)" with arbitrary topology. "[2025-03] Initial Release: Pretrained VAE model weights (1024³ reconstruction)". Needs "≥12GB VRAM for 1024³ resolution". MIT. — [TripoSF GitHub](https://github.com/VAST-AI-Research/TripoSF)
- **TripoSplat:** "converts a single 2D image into high-quality and variable number of 3D Gaussians… Arbitrary Gaussian count (up to 262,144)". "TripoSplat code and weight models are released under the MIT License". It has an official ComfyUI template. The repo was created 2026-06-01 and last pushed 2026-08-13. — [TripoSplat GitHub](https://github.com/VAST-AI-Research/TripoSplat)
- **HoloPart:** "Generative 3D Part Amodal Segmentation"; initial release April 2025; MIT. — [HoloPart GitHub](https://github.com/VAST-AI-Research/HoloPart)
- **UniRig:** "[SIGGRAPH 2025] One Model to Rig Them All: Diverse Skeleton Rigging". Skeleton and skinning checkpoints are released; MIT. Training the skin model needs "at least 60GB memory on a single gpu". — [UniRig GitHub](https://github.com/VAST-AI-Research/UniRig)
- **Other VAST-AI-Research repos** (as of 2026-10-19):

  | Repo | Description | License | Last push |
  | --- | --- | --- | --- |
  | SkinTokens | — | MIT | 2026-05 |
  | AniGen | SIGGRAPH 2026, animatable 3D asset generation | no SPDX license detected | 2026-07 |
  | DetailGen3D | — | MIT | — |
  | MIDI-3D | CVPR 2025, image-to-scene | Apache-2.0 | — |
  | TriplaneGaussian | — | Apache-2.0 | 2024 |
  | GeoSAM2 | CVPR 2026, 3D part segmentation | Apache-2.0 | — |
  | SeqTex | SIGGRAPH Asia 2025, texture generation | no SPDX license detected | — |
  | Mira-Scene | — | MIT | 2026-10 |
  | EdenLive | real-time generative rendering | Apache-2.0 | 2026-10-19 |

  — [VAST-AI-Research GitHub org](https://github.com/VAST-AI-Research)
- **Commercial comparison points:** H3.1 up to 2M polygons; P2.0 native quads with tri up to 50K and quad up to 25K; PBR textures up to 8K; built-in rigging and animation. — [Tripo: High Detail Model](https://www.tripo3d.ai/features/high-detail-model); [Tripo blog: P2.0](https://www.tripo3d.ai/blog/tripo-p2-0-preview); [Tripo developer portal](https://developers.tripo3d.ai/)

### Inferences
- In 2026, TripoSR is mainly useful as a free, offline, MIT-licensed blockout or placeholder generator on a ~6 GB GPU. Its vertex-colored, untopologized meshes are not game-ready.
- TripoSG and TripoSF produce geometry only; their READMEs describe shape synthesis and no texture output was found. They need 8–12 GB+ VRAM and a separate texturing step.
- Tripo has no open-weights equivalent of the commercial P-series (native mesh, quads) or H3.1.

### Gaps
- Hugging Face model cards were not opened directly. License and VRAM figures come from the GitHub READMEs.
- No official statement on open-sourcing H3.1, P1.0 or P2.0 was found.
- TripoSplat's public release date is unknown beyond the repo's creation on 2026-06-01. Its Gaussian output needs a splat renderer to be usable in Unity.
