# AI-generated modular cosmetics (clothing, hair, shoes, accessories, heads) for a shared skeleton + base body — state as of 2026-10-19

Context for the report writer: the target pipeline is Unity URP, one Mixamo-named Humanoid skeleton (SK_Human), one ~6k-tri female base body with 16 shape keys, cosmetics fitted to the body and given bone weights + shape keys copied from the body by Blender Python, plus a per-item "skin mask" of covered body vertices. Source-quality legend used below: **[vendor]** = the tool maker's own docs/blog/marketing; **[docs]** = official API/manual pages (verified behavior as documented); **[forum]** = practitioner anecdote; **[paper]** = research paper; **[listing]** = third-party add-on store page (seller claims). Pages I could not fetch in full (only search snippets) are marked **[snippet]**.

## 1. Can Tripo (or competitors) generate clothing as separate parts, or a garment alone? How well do separated parts hold up?

### Takeaway
Yes, but only as *post-hoc segmentation of a single generated mesh*, not as garments authored on your body: Tripo (Segmentation v1/v2 + Part Completion API), Rodin (BANG auto/manual parts), Hunyuan3D-Part (P3-SAM + X-Part) and Meshy (Auto Split, print-oriented) all split a mesh into parts, and Tripo/Hunyuan can AI-fill the hidden geometry exposed by the cut. No source documents garment-grade results (inner faces, uniform thickness, single-sided cloth) for clothing split off a body; independent quality evidence is essentially absent and most claims are vendor-authored.

### Cited Findings
**Tripo (verified API behavior)**
- Tripo's segmentation endpoint is `POST https://openapi.tripo3d.com/v3/mesh/segment`; models are `v1.0-20250506` (default, geometry-based) and `v2.0-20260430` (semantic segmentation with labeling, **Beta**). v2-only params: `segmentation_granularity` = `simple` / `balanced` (default) / `detailed`; `split_by_connectivity` (default true); `ref_image` (a reference image steering the breakdown — when supplied, granularity and connectivity are ignored). Inputs: GLB, GLTF, FBX, OBJ, STL up to 150 MB; output is a GLB `model_url` plus a preview render; example shows 40 credits. The docs do not say whether parts come back as separate meshes/nodes or what face-count limits apply. [docs] — [Tripo API: Mesh Segmentation](https://developers.tripo3d.com/en/docs/mesh-segment.md)
- Tripo's completion endpoint `POST /v3/mesh/complete` takes a finished segment task ID, optional `part_names` (omit = complete all parts) and `completion_mode` = `ai_completion` (default; "AI diffusion" generates geometry for missing/hidden regions) or `quick_cap` (just seals open boundaries, no AI). Only model `v1.0-20250506` is listed. [docs] — [Tripo API: Mesh Complete](https://developers.tripo3d.com/en/docs/mesh-complete.md)
- Scenario's integration write-up (28 Jul 2026) describes v1 as parameterless geometry analysis that finds "natural split points… where limbs meet the torso or where accessories attach", suited to "game assets whose topology already follows part boundaries"; v2 (preview) uses semantic analysis for "sharper, cleaner" boundaries where topology does not match part structure; listed use cases include swapping "armor pieces, clothing items, and accessories". Suggested chain: generate with Tripo 3.1 / 3.1 Multi View → segment → texture → rig (Uthana rigging for bipeds). The article does not discuss whether parts are hollow or closed. [vendor-partner] — [Scenario blog](https://www.scenario.com/blog/tripo-segmentation-3d-mesh-editable-parts-scenario)
- Tripo marketing says its splitter separates "heads, limbs, clothing, and accessories", that a precision brush can add/remove vertex areas so "clothing or armor can be separated cleanly from the body", and that Part Completion fills geometry exposed by a cut so pieces aren't hollow. [vendor, snippet] — [Tripo 3D Model Splitter](https://www.tripo3d.ai/3d-tools/3d-model-splitter); [Tripo game-dev editing guide](https://www.tripo3d.ai/game-development/master-manual-editing-and-refining-workflows-for-ai-generated-3d-game-assets); [Tripo blog: auto part segmentation](https://www.tripo3d.ai/blog/ai-3d-auto-part-segmentation)
- A press write-up on Segmentation v2 notes fine details (e.g., text on a t-shirt) can still struggle at lower input resolutions. [press, snippet] — [Quasa](https://quasa.io/media/tripo-releases-segmentation-v2-smarter-cleaner-part-separation-for-ai-generated-3d-models)
- Tripo P1.0 and H3.1 were released 9 Mar 2026; P1.0 is described as a native 3D diffusion model outputting low-poly, engine-ready assets in ~2 s. [aggregator timeline] — [Cinevva AI 3D timeline 2026](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)

**Rodin / Hyper3D**
- Rodin Gen-2 introduced automatic "BANG"-based part decomposition, "widely used", but users wanted more control; Gen-2.5 adds "Manual BANG to Parts" and "Part Refine" (regenerate/clean one area). The CTO explicitly names separate "armor, clothing, weapons, accessories, or hair" as the game need this addresses, but the article gives no detail on whether parts are closed/complete. Smart Low-Poly (autoregressive, artist-style tri/quad output) is still beta. Characters are handled by a separate product (ChatAvatar). Article dated 3 Jun 2026. [vendor interview] — [80.lv: Rodin Gen-2.5](https://80.lv/articles/how-hyper3d-rodin-gen-2-5-is-bringing-production-level-control-to-ai-3d-generation)
- Gen-2.5 dated 26 May 2026 (raw tris, quads or Smart Low-Poly output); Gen-2 dated 1 Oct 2025 as "recursive, part-based generation at 10B parameters" — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026); other sources give Gen-2 launch dates of 5 Aug 2025 or Oct 2025, so treat the date as unverified — [Rodin review (francisokafor)](https://www.francisokafor.com/tools/hyper3d-rodin); [gaga.art review](https://gaga.art/blog/rodin-gen-2-review/)

**Hunyuan3D (open weights)**
- Hunyuan3D-Part = P3-SAM (native 3D part segmentation of any mesh → semantic features, part masks, bounding boxes) + X-Part (generates "complete parts", recommended input: scanned or AI-generated meshes such as Hunyuan3D 2.5/3.0). The public X-Part is a "light version"; the "full-blood" version is only in Hunyuan3D-Studio. Papers: P3-SAM arXiv 2509.06784, X-Part arXiv 2509.08643 (Sept 2025). Page does not address watertightness or clothing. [docs] — [Hugging Face: tencent/Hunyuan3D-Part](https://huggingface.co/tencent/Hunyuan3D-Part); [X-Part paper](https://arxiv.org/pdf/2509.08643)
- Timeline lists Hunyuan3D-Part (28 Sep 2025, open weights, Tencent Community License), PartCrafter (13 Jul 2025, multiple parts from one image, MIT) and NVIDIA PartPacker (11 Jun 2025, non-commercial). — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)

**Meshy**
- Meshy "Auto Split" divides a model into printable parts in ~40 s and automatically seals each cut so parts are watertight; it is presented as a 3D-printing feature and works only on untextured draft models. [vendor tutorial, snippet] — [Meshy tutorial (ES)](https://www.meshy.ai/es/tutorials/auto-split-3d-model-into-parts); [Meshy tutorial (RU)](https://www.meshy.ai/ru/tutorials/auto-split-3d-model-into-parts)
- I found no Meshy feature for semantic clothing segmentation; Meshy 2026 releases listed are Meshy 6 GA (18 Jan 2026, adds Low Poly Mode), 3D Agent beta (Jun 2026), multiview upgrade (7 Aug 2026; one main + up to 3 auxiliary views). — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)

**What's under the cloth**
- Meshy's own article on photo-to-character notes the body under a coat or skirt is "a plausible guess" and guess quality falls with every added layer. [vendor] — [Meshy: Can a selfie become a 3D character?](https://www.meshy.ai/topics/can-a-selfie-become-a-3d-character)
- Tripo's hair article states AI tools typically produce "one dense, watertight mesh" rather than the thin card geometry real-time content needs (same issue applies to cloth). [vendor, snippet] — [Tripo: converting hair cards for games](https://www.tripo3d.ai/blog/explore/ai-3d-model-generator-converting-hair-cards-for-games)

**Garment-alone generation (dedicated garment tools, not general 3D generators)**
- Garment3DGen: deforms a base garment mesh to match a single image and generates textures; outputs are claimed drape/simulate-ready on bodies. [paper] — [arXiv 2403.18816](https://arxiv.org/pdf/2403.18816)
- ChatGarment: estimates a sewing pattern from one image, drapes it on a body, accepts text edits (e.g., sleeve length). [paper] — [arXiv 2412.17811](https://arxiv.org/html/2412.17811v2)
- Tailor (text → parameterized avatar + matched garment templates deformed to fit the body with topology-preserving losses, multi-view diffusion texturing, mesh output "simulation-ready"); v3 revised 19 Jan 2026. Rigging not mentioned in abstract. [paper] — [arXiv 2503.12052](https://arxiv.org/abs/2503.12052)
- Style3D "AI Garment": front-view image → 2D patterns + 3D garment with sewing relationships (trial activation required). [vendor] — [Style3D help](https://help.style3d.com/studio/en/c2c8/7cd2/b61b/)
- Marvelous Designer: AI Pattern Drafter (2025.1) takes keywords or an image and drafts a basic pattern — [MD support](https://support.marvelousdesigner.com/hc/en-us/articles/49694260744089); MD 2026.0 (Apr 2026) added 3D Pencil (draw pattern lines on the avatar) and Lacing; 2026.1 focused on fabric behavior (fold brush, Seamline Rip). No text-to-garment found. — [Digital Production, 15 Apr 2026](https://digitalproduction.com/2026/04/15/marvelous-designer-2026-0-adds-3d-pencil-and-lacing/); [Textalks on 2026.1](https://textalks.com/?p=85142)

### Inferences
- For this team, "segment a dressed character" produces garment *volumes* (closed shells whose inside surface is AI-guessed and whose underside was fused to a guessed body). Expect to delete the inner shell, re-thicken with Solidify, or treat the closed shell as a thick rigid-ish garment. This is consistent with the vendor statements about watertight meshes and hidden-geometry completion, but no source tested it on clothing.
- `ref_image` in Tripo Seg v2 is the most promising lever for "split exactly top / bottom / shoes / hair / earrings" — a reference image with color-coded regions could steer the split. Untested in any source found.
- Generating a garment *alone* (e.g., an "invisible mannequin" image of a jacket fed to Tripo/Meshy image-to-3D) is plausible since they accept any object, but I found no source evaluating it; the dedicated garment tools above (pattern-based) are the ones that produce true thin, sewable cloth.

### Gaps
- No independent test of Tripo Seg v2 / Rodin BANG / Hunyuan X-Part on clothing (inner faces, thickness, whether completion fabricates skin under clothes). Reddit (r/aigamedev, r/blender) was not reachable from the search tool (domain blocked), so practitioner reports are missing.
- Whether Tripo returns each part as a separate GLB node/mesh is not documented.
- Meshy segmentation for non-print use, and Meshy "Meshy 7" status (mentioned on a pose page) are unverified.

## 2. Fitting an AI-generated garment onto the existing base body in Blender, then retopo, weight transfer, shape-key transfer and slider-proof clipping control

### Takeaway
Community practice is a layered pipeline: rough-place → conform (Shrinkwrap for tight/single-sided, sculpt Grab/proportional edit for thick items, or cloth-sim against a shrunken body) → retopo to a deformation-friendly low-poly → copy weights from the body (Data Transfer face-interpolated as a baseline, Robust Weight Transfer to fix armpits/crotch/skirts) → project body shape-key deltas onto the garment (Surface Deform-based or barycentric-projection add-ons) → check clipping across shape keys and poses, with an offset/push-out pass and body hiding under the garment. Several 2026 Blender add-ons (RadFit, Outfit Refit, MorphBridge) now package exactly these steps.

### Cited Findings
**Conforming / fitting**
- Blender Artists thread (Oct 14–15; year not shown, thread ID suggests 2025): Shrinkwrap for single-sided clothing; proportional editing / Grab-brush sculpting for thick clothing; a cloth-sim trick — shrink the character via a shape key, let cloth collide, animate the key back to full size so the cloth ends up fitted; Marvelous Designer autofit is closest to quick but "still requires manual work"; check single-sidedness with the Face Orientation overlay. Consensus order: base fit → rigging/weight transfer → cloth sim if needed. No one-click autofit exists in Blender for custom bodies. [forum] — [Blender Artists: make clothes fit a character](https://blenderartists.org/t/how-to-make-clothes-fit-a-character-instead-of-the-character-fitting-the-clothes/1615158)
- Tight-clothing guide: Shrinkwrap targeting the body with a small offset to avoid z-fighting; keep thickness low. [blog] — [Seeles: tight-fitting clothes guide 2026](https://www.seeles.ai/resources/blogs/clothes-tight-3d-modeling-guide)
- Polycount: Blender cloth "doesn't understand thick garments" — it treats each side as separate pieces that fall into each other; Surface Deform is the Blender analogue of Max/Maya Skin Wrap for driving a retopo mesh from a sim, but binding failed with "invalid or convex cases" and a Solidify cage tore; suggestion: simulate a simplified single-sided proxy and drive the detailed mesh from it. [forum] — [Polycount thread](https://polycount.com/discussion/comment/2779129); [Polycount reply](https://polycount.com/discussion/comment/2779164)
- Cloth-sim → retopo → bake high-poly detail into maps is an established game workflow. [tutorial, 2020] — [BlenderNation: cloth retopology for game assets](https://www.blendernation.com/2020/05/26/cloth-retopology-for-game-asset-creation-promoted/)

**Weight transfer**
- Robust Weight Transfer (Sent From Space; free; GPL-3.0 code) is a Blender add-on implementing "Robust Skin Weights Transfer via Weight Inpainting" (Abdrashitov, Raichstat, Monsen, Hill — SIGGRAPH Asia 2023 Technical Communications, DOI 10.1145/3610543.3626180); it aims at one-click body→clothing transfer that avoids manual smoothing between legs, chest and armpits; its fork allows flipped normals and works around an inpaint failure on disconnected meshes. [docs] — [GitHub: sentfromspacevr/robust-weight-transfer](https://github.com/sentfromspacevr/robust-weight-transfer); [GitHub: rin-23/RobustSkinWeightsTransferCode](https://github.com/rin-23/RobustSkinWeightsTransferCode); [80.lv, 1 Apr 2025](https://80.lv/articles/grab-this-one-click-blender-tool-for-effortless-weight-transfers/)
- Mechanism as described in coverage: first transfers like Blender Data Transfer "Face Interpolated", rejects matches using Max Distance and Max Normal Difference, then inpaints the rejected vertices; Point and Surface modes, Point default as "generally more robust". [snippet] — [Digital Production, Apr 2025](https://digitalproduction.com/2025/04/23/weight-no-more-streamlining-blenders-weight-painting-with-robust-weight-transfer/); [CGDive tool page](https://cgdivetest.super.site/tools/robust-weight-transfer)
- The reference research repo's own "body to garment transfer" FBX workflow is still marked "Coming soon". [docs] — [rin-23 repo](https://github.com/rin-23/RobustSkinWeightsTransferCode)
- Alternatives named: Quick Attach (attach e.g. a jacket to a body) — [CGDive Quick Attach](https://cgdivetest.super.site/tools/quick-attach); WeightFlow Pro (paid) — [Superhive](https://superhivemarket.com/products/weightflow-pro/docs). Unity forum (Jul 2023): artist workflow = bind to the shared skeleton, copy weights from a close-fitting model, hand-fix. [forum] — [Unity Discussions: hundreds of clothing pieces](https://discussions.unity.com/t/how-do-you-manage-hundreds-of-different-armor-clothing-pieces-on-a-3d-character-on-the-art-side/923955)

**Shape-key (body slider) transfer to garments**
- Surface Deform is the native route but errors on some meshes. [forum] — [Blender Artists: transferring shape keys to clothing](https://blenderartists.org/t/transferring-shape-keys-from-a-base-mesh-to-clothing-armor/698063)
- ShapeKeyWrap (free) uses Surface Deform internally; most common failure: source mesh has "edges with more than two faces"; coincident vertices also break binding (fix: disable modifiers / nudge vertices). [listing] — [ShapeKeyWrap](https://squeezypixels.gumroad.com/l/shapekeywrap)
- Shape Transfer (Blender 3.6/4.x): any target topology, but the source body must avoid >2-face edges, overlapping vertices and collinear-edge faces. [listing] — [Shape Transfer](https://bartoszstyperek.gumroad.com/l/shape_transfer)
- MorphBridge (Blender 4.2–5.2): projects each target vertex onto the source surface, takes barycentric coordinates on the closest triangle and interpolates shape-key deltas; options: Strength, Max Distance (default 1.0), Laplacian Smoothing (1–5 iters recommended), Influence vertex group, selected-only; use cases explicitly include clothing, hair cards, accessories; limitations: poor results with very different poses or non-overlapping surfaces, one source→one target. [listing] — [MorphBridge docs](https://superhivemarket.com/products/shapekey-transfer-addon/docs)
- Daz forum: distortion during transfer can come from the Armature modifier; build keys on an unrigged copy and then apply. [forum] — [Daz forum](https://forum.daz3d.com/forums/discussion/comment/8030126/)
- Unity forum: copying body blend shapes to clothing so outfits follow body sliders is a known approach (video + Invector add-on by mbussidk). [forum] — [Unity Discussions: character customization woes](https://discussions.unity.com/t/character-customization-woes/828871)

**All-in-one fit/refit add-ons (2026)**
- RadFit (Blender 4.2–5.2, ~$14): not a cloth sim ("no draping or gravity"); pushes cloth out of skin to a default 3 mm gap while keeping folds, with a protect group for straps/buttons/belts; refits outfits across bodies/rigs; copies body weights where cloth lies on skin and smooths skirts/hoods/capes; for every body shape key that moves skin it creates a same-named garment key; checks clearance across up to 8 test poses; "masks covered body skin to reduce triangles"; 4 bones/vertex for Unity. Requires clothes already roughly placed; humanoid rig only. [listing] — [RadFit](https://superhivemarket.com/products/radfit--fit-any-outfit-to-any-body)
- Outfit Refit: pushes cloth out of the body, covers skin poke-through, reports before/after clipping counts per garment. [listing, snippet] — [Outfit Refit](https://superhivemarket.com/products/outfit-refit)

**Retopology**
- AI meshes commonly arrive dense (hundreds of thousands of faces) and without animation-suited topology; typical character budgets cited 5k–15k faces. [articles, snippet] — [levelup.gitconnected](https://levelup.gitconnected.com/3d-artists-arent-being-replaced-by-ai-not-yet-in-real-game-pipelines-cf75a6ee3947); [3D AI Studio guide](https://3daistudio.com/3d-generator-ai-comparison-alternatives-guide/can-ai-generate-game-ready-3d-models)
- Generator-side low-poly options now exist: Meshy 6 Low Poly Mode (Jan 2026), Tripo P1.0 (Mar 2026), Rodin Smart Low-Poly (beta) — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026); [80.lv Rodin](https://80.lv/articles/how-hyper3d-rodin-gen-2-5-is-bringing-production-level-control-to-ai-3d-generation). Tripo claims its Smart Mesh mode gives clean edge flow in <5 s (vendor claim, unverified). [snippet] — [renderu.com](https://renderu.com/en/news/post/28459)

### Inferences
- The team's existing Blender scripts (copy weights + shape keys from body, compute skin mask) are already the industry pattern; RadFit's feature list is almost a 1:1 commercial version, which validates the design. The AI garment only changes the *front* of the pipeline: you need a fit/conform + retopo stage before the existing scripts run.
- Recommended order for AI garments: (1) align to base body in rest A-pose; (2) delete AI inner shell / fused body remnants; (3) retopo to target budget (Quad Remesher/manual or generator low-poly mode) — do this *before* weights and shape keys, because all transfer methods operate per-vertex; (4) conform with Shrinkwrap (offset) + sculpt; (5) bake AI high-poly detail to normal map; (6) run existing weight/shape-key transfer; (7) push-out pass evaluated at every shape-key extreme (e.g., each key at 1.0 and combos) and a few poses; (8) compute skin mask.
- Surface Deform needs the *body* (source) to be clean manifold-ish geometry (no >2-face edges, no overlapping verts); a 6k-tri game base body is usually fine, so the existing scripts are low-risk; problems arise if binding to AI meshes as the source.

### Gaps
- Mesh Deform cage workflows for AI garments, Auto-Rig Pro Smart/Remap for this use, and the "Mesh Data Transfer" add-on: no sources found in this session.
- Blender manual pages for Data Transfer/Surface Deform returned 403; their exact option lists are cited only indirectly via add-on docs.
- No source quantifies how much retopo time AI garments save versus modeling from scratch.

## 3. Alternative: generate a full dressed character in A-pose matching the base body, then segment and transfer to the base skeleton — does it work in practice?

### Takeaway
Tooling now makes it *possible* (Meshy and others offer A/T-pose locked generation; Hunyuan3D-Omni accepts a skeleton/pose condition; Tripo/Rodin/Hunyuan segment parts), and vendors promote "generate → segment → rig" chains — but every source rigs the *generated* character's own body; none shows the garments being re-seated onto a different, pre-existing base body with matching proportions. Treat it as a way to get garment blockouts, not a drop-in.

### Cited Findings
- Meshy lets you choose T-pose, A-pose or custom pose before generation (pose locked afterwards); A/T-pose option integrated in Meshy 6 Preview; pose page mentions per-joint custom control and "Meshy 7" (version status unverified); offset bone placement in auto-rig usually comes from a non-standard input pose. [vendor docs] — [Meshy tutorial: A-pose/T-pose from one image](https://www.meshy.ai/tutorials/generate-a-pose-or-t-pose-3d-character-from-a-single-image); [Meshy Pose Control](https://www.meshy.ai/features/custom-pose); [Meshy rigging docs](https://docs.meshy.ai/en/webapp/guides/3d-model/rigging)
- Hunyuan3D-Omni (Sept 2025) accepts skeletal pose as a control signal (plus point cloud, voxel, bbox); training biases toward the harder pose signal; ≥10 GB VRAM; nothing specific on A-pose. [paper] — [arXiv 2509.21245](https://arxiv.org/html/2509.21245v1); [SourcePulse summary](https://www.sourcepulse.org/projects/15781289)
- Tripo blog workflow: generate body, head and hair as separate parts from a T-pose reference "with consistent clothing", assemble and rig in Blender. [vendor, snippet] — [Tripo blog](https://www.tripo3d.ai/blog/gpt-6-astra-3d-character-workflow)
- Tripo's VRChat guide says clothing fitting and weight transfer remain Blender/Unity tasks even when AI produces the starting asset. [vendor, snippet] — [Tripo: add clothes to VRChat avatar](https://www.tripo3d.ai/blog/vrchat-how-to-add-clothes-to-avatar-easy)
- Body under clothing in generated characters is a guess that degrades per layer (supports re-seating onto a known body rather than trusting the generated one). [vendor] — [Meshy](https://www.meshy.ai/topics/can-a-selfie-become-a-3d-character)
- Reallusion community workflow for AI meshes into CC4: generate in Hunyuan, send a CC base character to ZBrush via GoZ, append the AI mesh as a SubTool and fit it there; use Hide Mesh and layer ordering against poke-through. [forum/guide] — [Reallusion discussions](https://discussions.reallusion.com/t/how-to-convert-an-ai-generated-3d-mesh-into-character-creator-4-cc4/14624)

### Inferences
- The most reliable variant: render the *existing base body* in its A-pose to an image, paint/generate the outfit over it in 2D (img2img with the body as control), feed that image (multi-view if possible) to Tripo/Meshy, then segment. Proportions will be close but not identical, so a conform step (Section 2) is still needed. This is an inference; no source documents it.
- Skeleton transfer is the easy part (the team owns SK_Human; weights come from the body, not from the AI auto-rig). Ignore AI auto-rigs for cosmetics.

### Gaps
- No practitioner report found of re-seating AI-segmented garments onto a different base body at production scale, nor measured success rates.

## 4. Tools and add-ons in common use (Blender, garment design, Unity-side systems, CC4)

### Takeaway
Blender side: Robust Weight Transfer (free) for weights; Surface Deform-based (ShapeKeyWrap) or projection-based (MorphBridge) shape-key transfer; RadFit/Outfit Refit as 2026 one-stop fit/refit tools; Marvelous Designer (plus 2025.1 AI Pattern Drafter) or pattern-AI tools (Style3D, ChatGarment) for real cloth. Unity side: shared skeleton + toggled/merged skinned meshes, UMA for slot/wardrobe systems, Synty-style mask tinting. CC4 offers vertex-level conform and hide-mesh but has no AI-specific path.

### Cited Findings
- Robust Weight Transfer, Quick Attach, WeightFlow Pro, ShapeKeyWrap, Shape Transfer, MorphBridge, RadFit, Outfit Refit — see Section 2 citations.
- Unity forum (Jul 2023): shared base skeleton is "standard"; extend it only by adding bones without renaming/re-parenting existing ones; Skyrim/Oblivion/Second Life use one skeleton with optional extra bones; suggestion to merge a specially-named armature into the base skeleton with an AssetPostprocessor; bone names/topology matter more than small proportion differences. [forum] — [Unity Discussions](https://discussions.unity.com/t/how-do-you-manage-hundreds-of-different-armor-clothing-pieces-on-a-3d-character-on-the-art-side/923955)
- Unity forum: simplest runtime approach is to export all clothing options and toggle them; advanced approach merges skinned meshes at runtime; UMA recommended (handles limb length; building an equivalent takes months). [forum] — [Unity Discussions: character customization woes](https://discussions.unity.com/t/character-customization-woes/828871)
- CC2UMAConverter (experimental) converts a clothed character into UMA wardrobe slots. [repo] — [GitHub](https://github.com/valentinwinkelmann/CC2UMAConverter)
- Reallusion CC: clothing conforms while the body is morphed; hard-surface items can ignore distortion; conform configurable per vertex. [vendor] — [Reallusion custom outfits](https://cdn-www.reallusion.com/Character-Creator/custom-outfit.html)
- Daz Genesis is the classic shared-base model: morphs derived from the base mesh so clothing/hair/morphs "change with the characters". — [Wikipedia: Daz 3D](https://en.wikipedia.org/wiki/Daz_3D)
- Marvelous Designer remains the game/entertainment garment tool; AI Pattern Drafter (2025.1); 2026.0/2026.1 features (Section 1). — [MD support](https://support.marvelousdesigner.com/hc/en-us/articles/49694260744089); [Digital Production](https://digitalproduction.com/2026/04/15/marvelous-designer-2026-0-adds-3d-pencil-and-lacing/)
- MetaHuman outfit packs ship a body mask that hides body geometry under clothing for optimization and clipping prevention (same concept as the team's skin mask). [listing] — [Fab listing](https://www.fab.com/listings/445b4519-a04f-4517-a580-498ce208a782)

### Inferences
- The team's custom Blender scripts + Unity runtime already cover what UMA/CC4 would provide; adopting RWT (weights) and a MorphBridge-style barycentric projection (shape keys) inside their scripts is likely higher-value than switching systems.

### Gaps
- Auto-Rig Pro "Smart"/Remap for cosmetics, Synty's internal workflow, and "Mesh Data Transfer" add-on — not found in this session.

## 5. AI-generated hair: helmet/card hair vs strands for games

### Takeaway
AI generators output hair as a single dense, watertight sculpt ("helmet" volume); the accepted use is as a blockout/shape reference that is either kept as stylized helmet hair (after retopo + texture) or rebuilt as cards with baked textures. No source shows AI generators producing usable hair cards or strands directly.

### Cited Findings
- AI hair needs conversion: tools produce one dense watertight mesh; pipeline = isolate hair, rebuild into cards, bake textures, handle alpha clipping and LODs. [vendor, snippet] — [Tripo: converting hair cards](https://www.tripo3d.ai/blog/explore/ai-3d-model-generator-converting-hair-cards-for-games)
- AI hair "often works best as a solid mass" later converted to cards or groomed strands; AI for base topology, manual edge flow/UV after. [vendor, snippet] — [Tripo: cloth and hair geometry](https://www.tripo3d.ai/blog/explore/ai-3d-model-generator-generating-cloth-and-hair-geometry); [Tripo: hair-card topology](https://www.tripo3d.ai/blog/explore/smart-mesh-topology-for-hair-cards-assets)
- A game-dev experiment with image-to-3D notes a scalp-shaped hair cap under cards for short styles. [artist blog, snippet] — [Mariza Torska ArtStation blog](https://marizatorska.artstation.com/blog/category/6)
- Separate (non-AI-3D) tools generate hair-card textures: Pixa hair card generator, Fiber Card Gen. — [Pixa](https://www.pixa.com/create/hair-card-generator); [Fiber Card Gen](https://eperson.itch.io/fiber-card-gen)
- Tailor (research) outputs hair as strands alongside meshes. [paper] — [arXiv 2503.12052](https://arxiv.org/abs/2503.12052)
- Shape keys can be transferred to hair cards via projection (MorphBridge use case). — [MorphBridge docs](https://superhivemarket.com/products/shapekey-transfer-addon/docs)

### Inferences
- For a ~6k-tri stylized/toon body, retopologized AI "helmet" hair with a toon/anisotropic shader is the cheapest path; hair cards add alpha-sorting cost in URP. Hair should be weighted to head/neck bones and given head-related shape keys only (body sliders rarely affect it).

### Gaps
- No independent comparison of AI hair quality across Tripo/Meshy/Rodin; no source on AI strand generation usable in Unity.

## 6. Heads/faces: unique NPC heads on a shared body (neck seam, welding, facial blend shapes)

### Takeaway
Seam work is primarily a *normals* problem: keep identical neck-ring vertex positions, copy the body's normals onto the head's border, weight those border verts identically (100% to the same neck/spine bone), and export custom normals. Facial animation for AI heads can now be automated: OmniFaceRig (Jun 2026 paper) builds up to 155 FACS blendshapes + mouth interior from static generated meshes; Blender add-ons generate/map ARKit-52 keys.

### Cited Findings
- inZOI modding guide: much of a "visible seam" is normal/tangent mismatch; export from Blender with custom normals ("Normals Only") so the engine uses transferred normals. [guide] — [inZOI forum](https://forum.playinzoi.com/t/topic/13339)
- Polycount: copy the body's normals onto the head and weight seam verts 100% to one bone (top of spine); or build head+body as one mesh then split; or design around the seam (chokers, collars). [forum] — [Polycount](https://polycount.com/discussion/comment/759808); [Polycount](https://polycount.com/discussion/comment/1475690/)
- Unity forum: averaged normals looked seamless in Maya but broke after re-export (export-settings issue). [forum] — [Unity Discussions](https://discussions.unity.com/t/character-model-problems-with-seams-and-normals/662469)
- Unreal forum: normal seams appear on moving modular skeletal meshes; workarounds described as an "astronomical amount of work" (unresolved). [forum] — [Unreal forums](https://forums.unrealengine.com/t/how-do-you-create-a-segmented-model-with-no-normal-seams/337171)
- OmniFaceRig (arXiv 2606.08043, 6 Jun 2026; Meta authors): static surface-only character mesh → up to 155 FACS blendshapes, procedurally fitted teeth/gums/tongue, repacked UVs; humans, humanoids and animals; no manual landmarks; releases Omni-Bench (1,000 bipeds). Results reported on "screened" inputs; some teeth–face penetration remains; pipeline can fail on segmentation errors or unstable template fitting. Face fusion step: boolean-cut old face, weld new border to remaining boundary. [paper] — [arXiv abs](https://arxiv.org/abs/2606.08043); [arXiv html](https://arxiv.org/html/2606.08043v1)
- Blender ARKit tooling: ShapeKeyGen generates all 52 ARKit keys (Auto-Rig Pro, Rigify, custom rigs); ARKomposeIt auto-maps ARKit keys to custom rigs; Random Head Generator can append 51 ARKit keys to a head. [listings] — [ShapeKeyGen](https://superhivemarket.com/products/shapekeygen); [ARKomposeIt](https://superhivemarket.com/products/arkomposeit); [Random Head Generator](https://superhivemarket.com/products/random-head-generator)
- CC4 + AI head/mesh path goes through ZBrush GoZ fitting onto a CC base. [guide] — [Reallusion discussions](https://discussions.reallusion.com/t/how-to-convert-an-ai-generated-3d-mesh-into-character-creator-4-cc4/14624)

### Inferences
- Most robust approach for this team: do not attach raw AI heads. Instead keep one *head topology* (cut from the base body at a fixed neck ring) and *wrap/project* it onto each AI-generated head (shrinkwrap/sculpt or a wrap tool), so every NPC head shares vertex order → facial blend shapes, neck-ring vertices, UV layout and weights are reused; the AI output becomes a sculpt target + texture source. This mirrors the Daz/CC shared-topology model. No source documents this exact AI flow.
- If raw AI heads are used, the OmniFaceRig-style "cut and weld border to a fixed ring" is the documented pattern; normals must be copied from the body at the ring.

### Gaps
- No source on Unity-specific handling (e.g., Unity's import normals setting for split head/body) beyond the general "export custom normals" advice; OmniFaceRig code/weights availability not confirmed.

## 7. Keeping a consistent art style across many AI-generated cosmetics

### Takeaway
Advice converges on locking "constants" (palette, proportions, material treatment, detail level), always conditioning on the same style reference images, using a fixed prompt template, generating style sheets first, and budgeting a manual consistency pass. For 3D specifically, the strongest lever is to discard AI textures and re-texture everything into the team's own shared palette/shading (toon or hand-painted), treating AI output as geometry only.

### Cited Findings
- Sloyd "Custom Style": a reference image acts as the style guide while text covers content; keep separate saved styles for characters/props/environments. [vendor] — [Sloyd: how to create 3D assets in a consistent style](https://www.sloyd.ai/en/blog/how-to-style-consistent)
- Fix constants (palette, proportions, material treatment, detail level) and vary only the explored variable; use per-asset-type anchors (silhouette sheets for characters). [blog] — [Virtuall: AI game assets that match your art style](https://virtuall.pro/blog/how-to-create-ai-game-assets-that-match-your-art-style)
- Generate style reference sheets/palettes first; a consistency/cleanup pass is still needed. [blog] — [Relish Games: generative AI for 2D game art](https://relishgames.com/journal/generative-ai-for-2d-game-art-tools-and-techniques/)
- Midjourney --sref helps but has no project-level memory; references must be fed into every generation. [blog] — [Makko: 2D game art consistency 2026](https://blog.makko.ai/6-best-ai-tools-for-2d-game-art-consistency-in-2026/)
- Rodin Gen-2.5: Creative mode recommended default, Faithful mode for physically consistent real-world images. [vendor] — [80.lv](https://80.lv/articles/how-hyper3d-rodin-gen-2-5-is-bringing-production-level-control-to-ai-3d-generation)

### Inferences
- Practical template for this team: (a) one "style anchor" image set (base body render in A-pose + 3–5 approved outfits) attached to every image-gen step; (b) a fixed prompt skeleton ("[item], [material], [palette refs], toon/hand-painted, flat lighting, front + back views, on base body"); (c) generate concept images first and only send approved images to 3D; (d) bake geometry detail to normals and author albedo through a shared palette/gradient map so colors come from the game, not the generator.

### Gaps
- No source found on re-texturing AI meshes to a shared palette/atlas or LUT-based palette snapping in 3D; no studio case study on style drift across dozens of AI cosmetics.

## 8. Texture strategy for tintable cosmetics (grayscale/mask textures, atlases, materials per cosmetic)

### Takeaway
Standard practice for player-tintable cosmetics is desaturated/grayscale albedo plus a mask (single-channel or RGB "material ID") that the shader uses to apply per-region colors; commercial packs expose ~3 tint regions per item; Synty-style packs use a shader with color sections, with texture-baking alternatives for shader independence. AI textures need to be converted into this form.

### Cited Findings
- Desaturate the customizable areas, paint a white-on-black mask, and tint only white areas in the shader. [forum] — [Polycount](https://polycount.com/discussion/comment/2576731); [Polycount](https://polycount.com/discussion/comment/2576847/)
- Per-part mask images driving shader color, or swapping specific RGB values from a reference image. [forum] — [Unity Discussions](https://discussions.unity.com/t/how-to-dynamically-apply-a-color-to-one-portion-of-a-uv-map/167520)
- Commercial outfit pack: each part has 3 independently colorable regions via a Material ID map. [listing] — [Fab listing](https://www.fab.com/listings/4ea9c74a-8aec-423a-a71f-32a81069adb5)
- Synty Modular Fantasy Heroes uses a shader with preset color sections; a third-party Colour Changer writes colors into the texture so it works with other shaders (HDRP, toon). [listing] — [Modular Heroes Colour Changer](https://timps.itch.io/modular-heroes-colour-changer)
- Example modular pack with 147 swappable parts and "easy to change colors". [listing] — [BundleSmith devlog](https://bundlesmith.itch.io/modular-male-character/devlog/1309388/modular-character-released)

### Inferences
- For AI cosmetics: after retopo + UV, bake AI albedo → convert to luminance (keeps folds/shading detail) → generate an RGB region mask (Tripo Seg v2 part labels or vertex-color regions can seed it) → URP Shader Graph multiplies luminance by up to 3 tint colors. One shared shader/material template per cosmetic slot keeps SRP Batcher compatibility; atlasing is optional because a modular character usually renders as a few skinned meshes regardless.

### Gaps
- No source on URP SRP-Batcher/material-count tradeoffs for modular characters specifically, or on automatic mask extraction from AI textures.

## 9. Studio/indie examples of this exact approach, and reported failure modes

### Takeaway
I found no documented studio or indie postmortem using AI generators (Tripo/Meshy/Rodin/Hunyuan) to feed a shared-skeleton modular wardrobe; the modular-character examples found are hand-made packs. Failure modes are therefore assembled from adjacent evidence: dense/tangled topology, fused or guessed geometry under clothing, Surface Deform bind errors, thick-garment cloth-sim collapse, seams at the neck, and clipping across body morphs.

### Cited Findings
- Non-AI modular references: Quaternius CC0 fantasy outfits — 12 outfits from 62 modular parts on a shared "Universal Base Characters" body; v2.0 changelog: "a lot less clipping combining random parts". — [Quaternius](https://quaternius.itch.io/modular-character-outfits-fantasy); Camfrae Minimalistic Character Builder ($60, Mixamo-rigged, replaceable clothing) — [Gumroad](https://camfrae.gumroad.com/l/minimalistic-character-builder)
- Search for AI-outfit Unity devlogs returned only non-AI modular systems. — [Unity Asset Store: Character Customizer Modern Clothes](https://assetstore.unity.com/packages/3d/characters/humanoids/character-customizer-modern-clothes-pack-328606); [itch.io Godot modular system](https://imp0str.itch.io/modular-3d-character-system)
- Reported failure modes:
  - Dense meshes, poor edge flow for deformation, distorted hands/faces without retopo. — [levelup.gitconnected](https://levelup.gitconnected.com/3d-artists-arent-being-replaced-by-ai-not-yet-in-real-game-pipelines-cf75a6ee3947); [Nilo: common Rosebud AI 3D issues](https://nilo.io/articles/common-rosebud-ai-3d-issues)
  - "Tangled mesh, hard-to-edit materials, inconsistent geometry" from raw Tripo output (vendor's own framing). — [renderu.com](https://renderu.com/en/news/post/28459)
  - Guessed body under layered clothing. — [Meshy](https://www.meshy.ai/topics/can-a-selfie-become-a-3d-character)
  - Surface Deform bind errors (>2-face edges, coincident verts, "invalid or convex cases"). — [ShapeKeyWrap](https://squeezypixels.gumroad.com/l/shapekeywrap); [Polycount](https://polycount.com/discussion/comment/2779129)
  - Blender cloth collapsing thick (two-sided) garments. — [Polycount](https://polycount.com/discussion/comment/2779125)
  - Shape-key transfer failing when poses differ or surfaces don't overlap. — [MorphBridge docs](https://superhivemarket.com/products/shapekey-transfer-addon/docs)
  - Normal seams on modular skinned meshes in motion. — [Unreal forums](https://forums.unrealengine.com/t/how-do-you-create-a-segmented-model-with-no-normal-seams/337171)
  - Body mask trade-off: masked body regions must be disabled/rebuilt if those skin areas need to show. — [Fab MetaHuman listing](https://www.fab.com/listings/445b4519-a04f-4517-a580-498ce208a782)
  - Linked/referenced base skeleton files breaking imports when the base changes (keep a frozen copy). — [Unity Discussions](https://discussions.unity.com/t/how-do-you-manage-hundreds-of-different-armor-clothing-pieces-on-a-3d-character-on-the-art-side/923955)
- Research dataset analog: CLOTH3D had to remove inner-leg faces and Laplacian-smooth stretched faces when registering skirts/dresses to SMPL bodies. [paper] — [arXiv 1912.02792](https://arxiv.org/pdf/1912.02792)

### Inferences
- The highest-risk failure for this specific system is *slider clipping*: a garment fitted at default shape-key values will clip at bust/hips maxima unless the push-out pass is run per shape key (RadFit's same-named key + clearance approach) and the skin mask is computed against the *union* of all key extremes, not the rest shape.
- Vendor-authored blogs dominate the evidence; the report should flag that the AI-to-modular-wardrobe workflow is not yet independently documented at production scale as of Oct 2026.

### Gaps
- No studio/indie case study, GDC/80.lv talk, or postmortem on AI-generated wardrobes for a shared body was found. Reddit (r/aigamedev, r/blender, r/Unity3D) could not be searched (domain blocked for the search tool), which is the most likely place for anecdotes.
- YouTube 2025–2026 tutorials on "AI clothing → rigged Blender character" were not located/verified in this session.
