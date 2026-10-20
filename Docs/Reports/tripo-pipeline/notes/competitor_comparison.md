# Tripo vs. the other leading AI 3D generators for game assets (state as of 2026-10-19)

Scope note for the report writer: nearly every 2026 "comparison" article is published by a vendor, a reseller that sells credits for several of these models, or a site with referral links. Each finding below says who published it. The few tests that run against the publisher's own interest are the most credible and are flagged that way. Reddit and r/aigamedev could not be fetched (fetch blocked, search returned no threads), so practitioner sentiment is secondhand (see the "practitioners" key question).

Model versions current as of Oct 2026, used throughout: **Tripo H3.1** (flagship, snapshot v3.1-20260211) plus **Tripo P1.0** (low-poly "Smart Mesh"); **Meshy 7 / 7.1**; **Rodin Gen-2.5**; **Hunyuan 3D 3.1** (hosted only) and **Hunyuan3D 2.1** (latest open weights); **TRELLIS.2-4B**; **Hitem3D / Hi3D v3.0**.

---

## 1. Geometry, topology, texture quality and fit by asset type (characters, hard-surface props, environment; stylized vs realistic)

### Takeaway
No tool wins every dimension. Tripo is consistently strong on raw "white mesh" geometry, texture preference votes and speed. That includes a test by a Meshy employee, which ran against his own employer's interest. Tripo's weak spot is dense or messy triangle topology on complex hard-surface shapes at H3.1 defaults. In both the editorial and the retopology tests, Hunyuan 3D gives the cleanest, most consistent geometry. Rodin is the topology and hard-surface pick. Meshy is often preferred for textured single-image output, but its topology tests the worst. Hitem3D has the most raw detail but outputs dense triangles.

### Cited Findings
**Tripo**
- Tripo H3.1 (v3.1-20260211) is Tripo's default flagship. Its face limit is adaptive up to 2,000,000 faces, it outputs PBR materials, takes about 40 s without texture and about 120 s with texture, accepts text, single-image and multiview input, and offers optional quad mesh output (forced to FBX) plus a "smart low-poly" option. — [Tripo Developers: H3.1](https://developers.tripo3d.ai/en/models/v3-1) (vendor)
- Tripo P1.0 ("Smart Mesh") and H3.1 launched 9 Mar 2026. P1.0 is pitched as native 3D diffusion that generates low-poly meshes "in as little as two seconds" (vendor claim). — [Cinevva 2026 release timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026); press release: [Yahoo Finance / Tripo P1.0 launch](https://finance.yahoo.com/sectors/technology/articles/tripo-ai-launches-smart-mesh-162600191.html); [Technology.org on H3.1 (2026-03-26)](https://www.technology.org/2026/03/26/tripo-ai-introduces-h3-1-advancing-high-fidelity-ai-3d-generation-for-production/)
- On reseller 3D AI Studio, Tripo P1 tops out at about 20K faces, takes 60–90 s, comes with PBR and UVs, and is described as usable in Unity, Unreal or Godot "without retopology". The article includes no test methodology and the publisher sells credits for the model. — [3D AI Studio, Rodin 2.5 vs Tripo P1 vs Hunyuan 3D (2026-05-22)](https://www.3daistudio.com/blog/rodin-2-5-vs-tripo-vs-hunyuan-3d-comparison)
- **Independent-ish, against interest:** a Meshy technical artist tested Meshy 6, Tripo v3.1 (build 20260211) and Rodin Gen-2.5 through their APIs in Apr–May 2026, using internal blind voting. In white-mesh geometry, Tripo beat Rodin 77.9% single-view and 62.7% multi-view. Rodin beat Meshy 55.7% single-view and 75.0% multi-view. In textured single-view output, Meshy beat Rodin 83.9%. His conclusion: Tripo leads on white-mesh geometry and topology, while Meshy leads textured single-view but "struggles with complex geometry and part separation". Face count did not predict quality. On one representative prompt: Tripo 13,482 faces / $0.18, Meshy 17,438 / $0.20, Rodin 20,054 / $0.25. Median image-to-3D latency was Tripo 99 s, Meshy 195 s, Rodin 190 s. — [HackerNoon, Marcus Chen (Meshy), 2026-06-03](https://hackernoon.com/how-i-stress-tested-3-ai-3d-generators-on-the-same-inputs-what-the-numbers-actually-show)
- **Competitor review (Neural4D):** in a one-prompt chair test, Tripo H3.1 at default settings produced 965,873 faces with fused rails and posts and fused seat-to-leg joints, described as "a chaotic web of triangles". The reviewer also reports auto-rig failure past about 30° of joint rotation (no sample size given) and calls Tripo's textures "excellent" with "solid PBR materials". Treat this as biased, n=1. — [Neural4D blog: Tripo 3.1 review (2026)](https://blog.neural4d.com/comparisons/tripo-3-1-model-review/)
- **Retopology test (top3d.ai, 2026-01-02; the operator uses Tripo and Meshy referral links):** one test object reduced to roughly 1K and 2K faces, then checked with Blender's 3D Print Toolbox.

  | Check (Low / Mid) | Hunyuan 3D | Meshy | Tripo |
  |---|---|---|---|
  | Non-manifold | 0 / 0 | 21 / 6 | 6 / 31 |
  | Zero-area faces | 570 / 964 | 725 / 3,301 | 2,048 / 3,974 |

  Ranking: Hunyuan 1st (no cleanup needed), Tripo 2nd (usable after merge-by-distance), Meshy 3rd (heavy cleanup). Versions were not stated, so this likely predates Tripo P1.0 / H3.1 and Meshy 7. — [top3d.ai retopology showdown](https://www.top3d.ai/learn/retopology-comparison-2025)

**Hunyuan 3D**
- Hunyuan 3D 3.1 (hosted) is described as "the most consistent, with rare broken geometry". It goes up to 1.5M faces with 4K PBR, offers Normal, LowPoly, Geometry and Sketch modes, and takes 3–4 min. (Reseller, no methodology.) — [3D AI Studio (2026-05-22)](https://www.3daistudio.com/blog/rodin-2-5-vs-tripo-vs-hunyuan-3d-comparison)
- Hunyuan 3D 3.0 (Sep 2025) introduced 1536³ geometry, and its weights were never released. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)
- Hunyuan 3D Pro lets you set the face count numerically from 40,000 to 1,500,000. — [Mixar, Tripo vs Hunyuan (2026-08-01)](https://www.mixar.app/blog/tripo-vs-hunyuan) (sells a Blender-based editor)

**Rodin (Hyper3D)**
- Rodin Gen-2.5 launched in May 2026 (Cinevva says 26 May; a repost of the vendor's social post says 19 May). Raw output can exceed 10M polygons, with quad and low-poly outputs. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026); [Scenario help: Rodin Gen-2.5](https://help.scenario.com/articles/3791851266-rodin-hyper3d-gen-2-5-models-the-essentials)
- On Scenario, Gen-2.5 comes in standard and Fast tiers, and Fast is capped at 20K faces. Gen-2 and Gen-1 are now legacy. — [Scenario help: Rodin Gen-2.5](https://help.scenario.com/articles/3791851266-rodin-hyper3d-gen-2-5-models-the-essentials)
- Rodin Gen-2.5 has five quality tiers taking 30 s to 7 min, up to 2M faces, PBR with delighting, a force-symmetry option, and 2–5 view input. — [3D AI Studio (2026-05-22)](https://www.3daistudio.com/blog/rodin-2-5-vs-tripo-vs-hunyuan-3d-comparison)
- Rodin is recommended "when mesh topology matters most", for its clean quad topology. — [Cinevva, Best AI Asset Generators for Games (2026-06-23, updated 2026-09-06)](https://app.cinevva.com/guides/ai-asset-generators-games) (Cinevva hosts Hunyuan3D)
- Rodin Gen-2 (Oct 2025) is a 10B-parameter model with recursive part-based generation. Gen-2 Edit (31 Mar 2026) adds prompt-based region editing of existing meshes. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)

**Meshy**
- Meshy 7 shipped 12 Aug 2026; the timeline notes surface detail is its weakest score. Native 8K base color arrived 22 Jul 2026, multiview with up to three auxiliary views on 7 Aug 2026, and Meshy 6 GA (18 Jan 2026) added a Low Poly Mode for game developers. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)
- Meshy 7.1 (Sep 2026) offers quad or triangle meshes plus "Ultra 4K" mesh resolution, single-photo only for now. — [Summer Engine: Meshy 7.1](https://www.summerengine.com/models/meshy-7-1) (reseller, unconfirmed by Meshy release notes)
- Meshy 6's remeshed output tops out at about 300K faces, with a quad/tri option and A-pose/T-pose control. — [3D AI Studio, Hitem3D vs Meshy vs Tripo (2026-07-07)](https://www.3daistudio.com/blog/hitem3d-vs-meshy-vs-tripo-comparison)
- A G2 user review says Meshy's topology and mesh optimization "still need improvement for game-ready assets". — [G2 Meshy reviews](https://g2.com/products/meshy/reviews) (search snippet only)

**Hitem3D (Sparc3D lineage)**
- Hitem3D generates on a 1536³ grid ("highest in class") with up to 2M faces, but the topology is dense triangles, there is no pose control, and jobs take 7–8 min. — [3D AI Studio (2026-07-07)](https://www.3daistudio.com/blog/hitem3d-vs-meshy-vs-tripo-comparison)

**TRELLIS.2 (open)**
- TRELLIS.2 outputs textured GLB with Base Color, Roughness, Metallic and Opacity maps (4096 textures in the example). It supports open surfaces, non-manifold geometry and internal structures, and opacity is off by default (OPAQUE export). — [GitHub microsoft/TRELLIS.2](https://github.com/microsoft/TRELLIS.2)

**Stability SPAR3D**
- SPAR3D outputs a textured, UV-unwrapped mesh in under a second from a 512×512 input. It has a delighting step that removes baked lighting, and predicts roughness/metallic per object rather than as full PBR maps. — [HF model card: stable-point-aware-3d](https://huggingface.co/stabilityai/stable-point-aware-3d)

**General**
- Every engine still outputs "a dense blockout that still needs cleaning, retopology to a real budget, UV rebuilding, and baking" before it ships. — [Mixar (2026-08-01)](https://www.mixar.app/blog/tripo-vs-hunyuan)

### Inferences
- For a Unity URP indie game, Tripo looks like a strong default for **props, creatures and stylized characters**. The reasons are the cross-source geometry and texture preference results, P1.0's low-poly path, and the price. For **hard-surface hero props that need clean quads** (weapons, vehicles, modular kit pieces), Rodin Gen-2.5 is the better bet, or Hunyuan 3D with explicit face-count control. Tripo H3.1 at defaults can return near-million-face fused meshes.
- Stylized vs realistic: no source tested this split directly. The secondhand evidence is that P1.0 suits "background props and stylized characters, not hero characters" (Neural4D) and that Hunyuan suits "hero assets". Treat that as weak evidence.
- **Environment pieces:** no source evaluated modular or tiling environment geometry. All of these generate one object at a time, so environment kits will still need manual modular authoring or kitbashing.
- Unity URP compatibility (general knowledge, verify): the PBR outputs here are glTF metal/rough. URP Lit expects metallic plus smoothness (inverted roughness, packed in alpha), so either glTFast's import conversion or a channel-pack step will be needed whichever tool is chosen.

### Gaps
- No independent, methodology-documented test compared watertightness, UV quality, texel density or baked-lighting artifacts across all the tools in 2026.
- No 2026 test separated stylized from realistic, or characters from props from environment, across tools.
- I found no hands-on independent test of Tripo P1.0 topology. The levelup.gitconnected test returned HTTP 403.

---

## 2. Feature coverage per tool (retopology, UV, part segmentation, texture editing, rigging/animation, multi-view, API, Blender/Unity plugins, MCP)

### Takeaway
Tripo has the broadest game-pipeline feature set behind a single API. That covers quad and tri retopology, smart low-poly, Smart UV, segmentation and part completion, auto-rig including **non-humanoid/quadruped rigs**, animation retargeting, multiview, an official Unity plugin, and an official (alpha, Blender-only) MCP server. Meshy is the closest rival: a Unity plugin, humanoid-only rigging and an animation library. Hunyuan's hosted suite covers segmentation, UV and texture editing but not rigging. Rodin adds region editing and quads but no rigging.

### Cited Findings
**Tripo**
- The Tripo API task list includes: Retopology (v3.0 Triangle / v3.0 Quad / v2.0 Smart / v1.0 Basic), Smart UV, Segmentation, Part Completion, Smart Segmentation (image/model), Auto Rig, Rig Check (free), Animation Retarget, Image to Multiview, Edit Multiview, Format Convert, and texture tiers up to "8K Ultra". There are H, P and "Splat" generation series. — [Tripo Developers pricing](https://developers.tripo3d.ai/en/pricing)
- **Conflict:** Mixar says Tripo lacks part segmentation and does not list UV unwrap, but Tripo's own API pricing lists both Segmentation and Smart UV. Prefer the primary source. — [Mixar (2026-08-01)](https://www.mixar.app/blog/tripo-vs-hunyuan) vs [Tripo Developers pricing](https://developers.tripo3d.ai/en/pricing)
- Mixar also says Tripo's quad toggle spans 500–300,000 faces. — [Mixar](https://www.mixar.app/blog/tripo-vs-hunyuan)
- Tripo has two riggers. Rigging v2.5 targets non-humanoid skeletons (Quadruped, Hexapod, Octopod, Avian, Serpentine, Aquatic), and v1.0 is still recommended for bipeds and humanoids. — [Scenario help: Tripo 3.1 Suite](https://help.scenario.com/articles/6323782235-tripo-3-1-suite-the-essentials); [Scenario: Tripo Rigging v2.5](https://www.scenario.com/models/tripo-rigging-v25)
- "Since Feb 2026, its rigger also handles non-humanoid creatures." — [Cinevva games guide](https://app.cinevva.com/guides/ai-asset-generators-games)
- Tripo has the broadest rigging of the hosted tools: high- and low-poly rigging for biped and multi-legged creatures. Meshy's Auto Rig covers biped humanoids only. — [3D AI Studio (2026-07-07)](https://www.3daistudio.com/blog/hitem3d-vs-meshy-vs-tripo-comparison)
- The official Tripo Unity plugin is v1.0.1 for Unity 2022+. It is installed into `Packages/` and opened from Tools > Tripo API Unity. — [Tripo Developers: Plugins](https://developers.tripo3d.com/en/docs/plugins)
- Tripo 2.5 (Jan 2025) shipped Blender and Unity plugins. — [Tripo blog: 2.5 and plugins](https://www.tripo3d.ai/blog/tripo-2-5-and-plugins)
- The official Tripo MCP server (VAST-AI-Research) is in **alpha** and supports only the Tripo Blender add-on (`uvx tripo-mcp`). — [GitHub VAST-AI-Research/tripo-mcp](https://github.com/VAST-AI-Research/tripo-mcp)
- The community blender-mcp `generate_3d` tool supports Tripo, Hunyuan3D (via Tencent Cloud, with an International Pro path in ap-singapore with PBR), Hyper3D Rodin, and Meshy (bring your own key; paid plan required). — [GitHub ahujasid/blender-mcp](https://github.com/ahujasid/blender-mcp)
- VAST, Tripo's parent, also released open models: TripoSG (MIT, Mar 2025), a 1.5B shape model, and UniRig (MIT, Apr 2025), for automatic skeleton and skinning. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)
- The H3.1 API allows 3 parallel tasks for H-series generation. — [Tripo Developers: H3.1](https://developers.tripo3d.ai/en/models/v3-1)

**Meshy**
- Meshy rigging "currently only works well with standard humanoid (bipedal) assets". It requires textured meshes with the face toward +Z and at most 300K faces (use Remesh first if larger), outputs FBX/GLB, and includes walk and run animations. A separate Animation API applies more animations. — [Meshy API docs: Rigging & Animation](https://docs.meshy.ai/en/api/rigging-and-animation)
- Meshy 5 (Aug 2025) brought multi-view input and 500+ animations for rigged characters. Meshy 3D Agent (chat-based) entered beta on 4 Jun 2026. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)
- The official Meshy Unity plugin was tested on Unity 6000.0.45f1. — [Meshy docs: Unity plugin](https://docs.meshy.ai/th/webapp/plugins/unity/introduction)
- Meshy puts models straight into Assets with materials mapped. — [Meshy blog (Jun 2026)](https://www.meshy.ai/sv/blog/ai-3d-models-for-unity-official-meshy-plugin-2026) (vendor)
- API access, an API Playground and request logs come on all paid plans at 20 req/s (Enterprise gets 100 req/s). Asset retention is only **3 days** on Pro–Ultra, 14 days on Studio, and forever on Enterprise. — [Meshy pricing](https://www.meshy.ai/pricing)

**Hunyuan 3D (hosted and open)**
- Hunyuan's hosted suite offers part segmentation, standalone UV unwrap, texture editing, and retopology with polygon-type and level settings. It does not address rigging. — [Mixar (2026-08-01)](https://www.mixar.app/blog/tripo-vs-hunyuan)
- The open add-ons are Hunyuan3D-Part (28 Sep 2025; part segmentation and part completion) and Hunyuan3D-Omni (25 Sep 2025; control via point cloud, voxel, skeleton or bounding box). Each has its own community license. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)
- Hunyuan 3D 3.1 accepts named multi-view input (front, back, left, right, top, bottom). — [3D AI Studio (2026-05-22)](https://www.3daistudio.com/blog/rodin-2-5-vs-tripo-vs-hunyuan-3d-comparison)

**Rodin**
- Creator tier adds multi-image input, Smart Low-Poly, HD and custom textures, and normals baked from high-poly. Business adds full API access, high-resolution textures and "High-Poly Quads". Enterprise adds on-prem deployment and custom LoRA fine-tuning. — [Hyper3D pricing](https://hyper3d.ai/pricing)
- Gen-2 Edit (Mar 2026) adds prompt-based region editing. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)
- Rodin is integrated in blender-mcp. — [GitHub ahujasid/blender-mcp](https://github.com/ahujasid/blender-mcp)

**Others**
- Hitem3D accepts image and 4-view input and has an optional PBR toggle, but no pose control. — [3D AI Studio (2026-07-07)](https://www.3daistudio.com/blog/hitem3d-vs-meshy-vs-tripo-comparison)
- Hitem3D lists a "Stylized Texture" tool, multi-color print, and an API platform at platform.hi3d.ai. — [Hitem3D pricing](https://www.hitem3d.ai/pricing)
- TRELLIS.2 ships separate shape-conditioned PBR texturing scripts (`example_texturing.py`, `app_texturing.py`), Docker images, and community ComfyUI nodes. — [GitHub microsoft/TRELLIS.2](https://github.com/microsoft/TRELLIS.2); [ComfyUI Wiki (2025-12-18)](https://comfyui-wiki.com/en/news/2025-12-18-microsoft-trellis2-3d-generation)
- Luma Genie is still listed on the Luma API, at Pro tier with a 400-character text input limit. Outputs are GLB, OBJ or USDZ. No shutdown notice was found. — [LLM Reference: Genie 3D on Luma API (Apr 2026)](https://www.llmreference.com/model/genie-3d/luma-api); [Gappsy (Jul 2026)](https://gappsy.com/tools/luma-ai/)

### Inferences
- For a Unity team that also uses Blender MCP (this repo already runs a Blender MCP whose `generate_3d` reaches Tripo, Hunyuan3D, Rodin and Meshy), Tripo is the only vendor with both an official Unity plugin and an official MCP server. Its MCP is alpha and Blender-only, so the realistic agent flow is Tripo → Blender (cleanup, URP-friendly export) → Unity. Generating straight into Unity through MCP is not the path.
- Tripo's quadruped and creature rigging is a real differentiator over Meshy's humanoid-only rigging for games with animals or monsters. One competitor reports joint collapse past about 30°, so plan on rig QA or using UniRig or Mixamo as a fallback.
- Meshy's 3-day asset retention on paid tiers means any API pipeline must download immediately.

### Gaps
- I found no evidence of official Unity plugins for Rodin, Hunyuan, Hitem3D or TRELLIS.2, and no official MCP servers for Meshy, Rodin or Hunyuan. Search turned up nothing, which is not proof they don't exist.
- I could not confirm whether the Tripo Unity plugin exposes rigging, retopo or segmentation, or only generation and import.
- No source documented Tripo's or Meshy's texture-editing (inpaint or retexture) quality comparatively.

---

## 3. Pricing, cost per asset, commercial licensing, and self-hosting / GPU requirements

### Takeaway
Tripo is among the cheapest per asset: about $0.20–$0.30 for a textured image-to-3D on the Pro sub or API, and roughly $1 fully rigged with an animation. The catch is that its free-tier licensing is contradictory across Tripo's own documents, so a commercial team should simply pay. Its top "Team" tier tripled to $329.70/mo in Aug 2026. Hunyuan3D 2.1 open weights are free but **legally unusable in the EU, UK and South Korea**, need a separate license above 1M MAU, and forbid training other models on the outputs. TRELLIS.2 (MIT) is the cleanest self-host license but needs a 24 GB NVIDIA GPU and Linux.

### Cited Findings
**Tripo**
- Tripo API pricing: 1 credit = $0.01. Text-to-3D costs 10 (untextured) or 20 (textured) credits; image-to-3D 20 or 30. Texture tiers: Fast/Standard 10, HD 20, 8K 30. Add-ons: HD geometry +20, quad +5, smart low-poly +10, generate parts +20. Retopology costs 30–40, Smart UV 20, Segmentation 40, Auto Rig 25, Animation Retarget 10 per animation. Prepaid packs run $50, $100 or $250. — [Tripo Developers pricing](https://developers.tripo3d.ai/en/pricing)
- H3.1 detailed-texture image-to-3D costs 40 credits. — [Tripo Developers: H3.1](https://developers.tripo3d.ai/en/models/v3-1)
- Tripo plans as of the 2026-08-24 verification: Free $0, 200 credits (about 13 models), "public models, non-commercial". Pro $19.90/mo, 3,000 credits (about 200 models), private and commercial, 10 concurrent. Max $89.90/mo, 25,000 credits, 100 concurrent. Team $329.70/mo, 90,000 credits, 200 concurrent. The same page has an FAQ that says the free tier gets 300 credits. — [CostBench: Tripo AI](https://www.costbench.com/software/ai-3d-generation/tripo-ai/) (aggregator; tripo3d.ai/pricing returned HTTP 403 to my fetch)
- The top tier went from about $110 to $329.70/mo between the 2026-07-06 and 2026-08-24 snapshots. CostBench notes this may reflect a restructuring rather than a straight price rise. — [CostBench: Tripo price history](https://costbench.com/software/ai-3d-generation/tripo-ai/price-history/)
- **Free-tier license conflict in Tripo's own documents:**
  - Tripo's commercial-use help page reportedly lists commercial use for free users as "Not granted" and says outputs are retained by Tripo. — [Tripo help: commercial use](https://www.tripo3d.ai/help/privacy-policy/how-to-use-tripo-models-commercially)
  - Tripo's blog says free-tier outputs are CC BY 4.0 and commercially usable with attribution. — [Tripo blog: commercial use](https://www.tripo3d.ai/blog/commercial-use-ai-3d-models)
  - Cinevva says the "free tier is non-commercial". — [Cinevva games guide](https://app.cinevva.com/guides/ai-asset-generators-games)

  Both Tripo pages returned 403 to direct fetch, so these are search-snippet readings.
- On the top3d.ai arena, Tripo v3.1 is listed at $0.30 and 1.5 min per generation. — [Sloyd blog citing top3d.ai (2026-08-04)](https://www.sloyd.ai/blog/ai-3d-model-generator-rankings)

**Meshy**
- Meshy plans:

  | Plan | Price | Credits | Assets | Concurrent |
  |---|---|---|---|---|
  | Free | $0 | 100 | 10 | 1 |
  | Pro | $20/mo | 1,000 | ~100 | 10 |
  | Premium | $40/mo | 3,000 | ~300 | 30 |
  | Ultra | $100/mo | 8,000 | ~800 | 100 |
  | Studio | $70/mo + $10/seat | 5,500 shared | — | 60 |

  Free outputs are CC BY 4.0 ("commercial use with attribution"); paid outputs are "private & customer owned". Annual billing is 20% off. — [Meshy pricing](https://www.meshy.ai/pricing)
- Downloading Meshy 7 models requires Pro or above. — [Summer Engine: Meshy 7.1](https://www.summerengine.com/models/meshy-7-1) (reseller)
- On top3d.ai, Meshy 6 is listed at $0.40 and 3 min per generation. — [Sloyd/top3d.ai](https://www.sloyd.ai/blog/ai-3d-model-generator-rankings)

**Rodin (Hyper3D)**
- Rodin plans: Free is pay-by-result with 10 private assets, and direct credits cost $1.50 each. Creator is $30/mo ($24/mo annual) for about 60 models. Business is $120/mo ($96 annual) for about 416 models and includes the API at 120–240 RPM. Enterprise is custom with on-prem deployment. Paid tiers allow "unlimited export and any use". The page gives no explicit license terms for free-tier output. — [Hyper3D pricing](https://hyper3d.ai/pricing)
- Rodin is "free to generate, paid to download". — [Cinevva games guide](https://app.cinevva.com/guides/ai-asset-generators-games)
- On top3d.ai, Rodin Gen 2.5 is listed at $0.29 and 4.5 min. — [Sloyd/top3d.ai](https://www.sloyd.ai/blog/ai-3d-model-generator-rankings)

**Hunyuan3D, open weights (2.1)**
- The Tencent Hunyuan 3D 2.1 Community License:
  - Territory is "worldwide … excluding the European Union, United Kingdom and South Korea", and use outside it is unlicensed.
  - Products above 1 million MAU must request a license, which Tencent grants at its discretion.
  - "Tencent claims no rights in Outputs."
  - Outputs may not be used "to improve any other AI model".
  - Third-party distribution needs a Notice file.
  - The Acceptable Use Policy requires machine-generated content in public contexts to be identified.

  — [HF: Hunyuan3D-2.1 LICENSE](https://huggingface.co/tencent/Hunyuan3D-2.1/blob/main/LICENSE)
- The same EU/UK/South Korea carve-out exists in the 2.0 license, and Hacker News practitioners flagged it as "strange". — [HF: Hunyuan3D-2 LICENSE](https://huggingface.co/tencent/Hunyuan3D-2/blob/main/LICENSE); [Hacker News thread](https://news.ycombinator.com/item?id=43420870)
- Hunyuan3D 2.1 needs about 10 GB VRAM for shape, 21 GB for texture, and 29 GB for both. It runs on macOS, Windows and Linux. Shape and paint weights were released 2025-06-14. — [GitHub Tencent-Hunyuan/Hunyuan3D-2.1](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1)
- Hunyuan 2.5, 3.0 and 3.1 are hosted only. Hunyuan 3D 3.1 (28 Jan 2026) runs on the global platform with 20 free generations a day. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026); [Cinevva games guide](https://app.cinevva.com/guides/ai-asset-generators-games); [triposr.org Hunyuan versions](https://triposr.org/blog/hunyuan3d-versions)
- Hunyuan 3D v3.1 Rapid costs $0.0225 per run on reseller WaveSpeed. — [WaveSpeed: Hunyuan 3D v3.1 Rapid](https://wavespeed.ai/id/models/wavespeed-ai/hunyuan-3d-v3.1/image-to-3d-rapid)
- 3D AI Studio charges 40 credits for Hunyuan 3D 3.1, with PBR extra. — [3D AI Studio](https://www.3daistudio.com/blog/rodin-2-5-vs-tripo-vs-hunyuan-3d-comparison)

**TRELLIS.2**
- TRELLIS.2 is 4B parameters, MIT for both code and model (dependencies nvdiffrast and nvdiffrec have their own licenses). It needs an NVIDIA GPU with at least 24 GB and is "tested only on Linux". On an H100 it takes about 3 s at 512³, 17 s at 1024³ and 60 s at 1536³. — [GitHub microsoft/TRELLIS.2](https://github.com/microsoft/TRELLIS.2)
- It was released 16 Dec 2025. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)
- A consumer RTX 4090 is reportedly about 2–3× slower than the H100 figures. — [ComfyUI Wiki](https://comfyui-wiki.com/en/news/2025-12-18-microsoft-trellis2-3d-generation) (secondary)

**Hitem3D**
- Hitem3D plans: Free is 100 lifetime credits (about 10 generations), public CC BY 4.0. Pro is 1,000/mo, Max 2,400/mo and Ultra 8,000/mo, all "private & commercial use". Dollar prices were not rendered. — [Hitem3D pricing](https://www.hitem3d.ai/pricing)
- Hitem3D's mid tier is about $19.90/mo, and the free tier requires "Generated with Hitem3D" attribution. — [3D AI Studio (2026-07-07)](https://www.3daistudio.com/blog/hitem3d-vs-meshy-vs-tripo-comparison)
- On top3d.ai, Hitem3D costs $0.73 per generation. — [Sloyd/top3d.ai](https://www.sloyd.ai/blog/ai-3d-model-generator-rankings)
- Sparc3D was published as a paper and demo in May 2025, and its weights were never published; it was commercialized as Hitem3D. — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026); [GameBusiness.jp (2025-06-27)](https://www.gamebusiness.jp/article/2025/06/27/24512.html)

**Others**
- Stability SPAR3D is under the Stability AI Community License: free, including commercially, below $1M annual revenue, with an Enterprise license needed above that. Access is gated. — [HF: stable-point-aware-3d](https://huggingface.co/stabilityai/stable-point-aware-3d)
- CSM plans: Tinkerer (free), Maker $20/mo, Creative Pro $60/mo, Startup $499/mo, Studio $2K/mo, Enterprise custom. — [CostBench: CSM vs Tripo](https://www.costbench.com/compare/csm-vs-tripo-ai/) (aggregator, unverified)
- Kaedim plans: Trial $50, Indie $400/mo for 20 credits (extra $22), Pro $1,200/mo for 60 credits (extra $16), Enterprise custom. Hidden costs include human-in-the-loop refinement. — [CostBench: Kaedim](https://costbench.com/software/ai-3d-generation/kaedim/) (aggregator; CostBench dates it Jun or Aug 2026)
- Other open models:
  - Step1X-3D: Apache 2.0, about 27–29 GB VRAM for both stages.
  - Direct3D-S2: MIT.
  - Pixal3D (Tencent ARC): MIT, May 2026, PBR plus training code, multi-view added Sep 2026.
  - NVIDIA PartPacker: non-commercial license.
  - SAM 3D Objects (Meta, Nov 2025): SAM License, outputs Gaussian splats rather than meshes.

  — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026); [Meta AI: SAM 3D](https://ai.meta.com/blog/sam-3d/)

### Inferences (my arithmetic from the cited prices)
- **Tripo cost per asset:**

  | Workflow | Credits | API cost | On Pro sub |
  |---|---|---|---|
  | Basic textured image-to-3D | 30 | $0.30 | ~$0.20 |
  | HD texture + HD geometry + quad | 40 + 20 + 5 = 65 | $0.65 | — |
  | ...plus auto-rig and one animation | 65 + 25 + 10 = 100 | ~$1.00 | — |

  The Pro sub price assumes $19.90 / 3,000 credits ≈ $0.0066 per credit.
- **Cost per asset elsewhere:**
  - Meshy Pro: about $0.20 (assuming roughly 10 credits per asset, as implied by the page's "up to 100 assets" per 1,000 credits).
  - Rodin: about $0.50 on Creator, $0.29 on Business.
  - Kaedim: about $20 per credit.
- **Self-hosting:** an indie team with an RTX 4090 or 5090 class card (24–32 GB) can run TRELLIS.2, which is MIT, Linux-first and needs WSL2 or a community fork on Windows. They can also run Hunyuan3D 2.1, but its texture stage alone needs 21 GB, and its license only works if the team, and arguably its distribution, is outside the EU, UK and South Korea. That legal question needs counsel.
- Hunyuan's hosted 3.x output terms are not the same document as the 2.1 open-weights license. Do not assume the territory clause applies to hosted use, or that it doesn't.

### Gaps
- I could not read tripo3d.ai/pricing or Tripo's Terms of Service directly (HTTP 403), so Tripo plan prices rest on CostBench, and the free-tier commercial status remains contradictory.
- I found no official license or terms text for Hunyuan 3D 3.x hosted outputs or the international platform.
- Hitem3D dollar prices, Rodin's free-tier output license, and current CSM, Luma Genie and Kaedim pricing came only from aggregators or not at all.
- No official GPU requirement was found for SPAR3D.

---

## 4. Independent benchmarks and head-to-head tests (2025–2026)

### Takeaway
There is no fully neutral, methodology-documented 2026 benchmark. The two largest preference datasets disagree on Tripo:
- **top3d.ai crowd arena** (about 187K votes, Aug 2026): Tripo v3.1 is **#1 overall** and #1 on texture (Oct 2026). Caveat: the operator uses Tripo referral and discount codes.
- **Pixazo's VLM-judged editorial Elo** (Jul 2026): Hunyuan 3D Pro and Rodin first, but it tested the outdated Tripo 2.5.

The most credible signal is the Meshy employee's test, run against his own employer, which still put Tripo first on geometry.

### Cited Findings
- **top3d.ai arena, overall quality** (ELO, snapshot 2026-08-03, 186,939 votes): Tripo v3.1 1042 (88.6% win rate), Hitem3D v2.0 1025, Sloyd v3 1015, N4D-2.5 (Neural4D) 1013, Triverse 3.0 1012, Rodin Gen 2.5 1009, Hitem3D v2.1 1005, Meshy 6 1004. ELO resets each season. — [Sloyd blog (2026-08-04)](https://www.sloyd.ai/blog/ai-3d-model-generator-rankings) (Sloyd ranks itself 3rd in its own article)
- **top3d.ai texture board** (live, read 2026-10-19): Tripo v3.1 1,097 (507 votes), Hi3D v3.0 1,094, Meshy 7 1,085. Geometry, Low Poly, Segmentation and Smart UV boards exist but did not render. — [top3d.ai](https://top3d.ai/)
- An earlier top3d.ai snapshot (Feb 2026, 82,000+ votes) had Hunyuan 3D v3.1 and Hitem3D (old v1.5) in the top three overall. — [3D AI Studio (2026-07-07)](https://www.3daistudio.com/blog/hitem3d-vs-meshy-vs-tripo-comparison)
- The top3d.ai operator's Tripo links carry an invite code and a "TRIPOCREW" discount code. — [top3d.ai retopology article](https://www.top3d.ai/learn/retopology-comparison-2025)
- **Pixazo leaderboard** (Jul 2026): a VLM judge scores multi-view renders on alignment, texture and form, converted to Bradley-Terry Elo. There is no crowd voting and confidence intervals overlap. Ranking: Hunyuan 3D Pro 1508, Rodin 2 1477, Meshy 6 Preview 1335, Rodin 2.5 1288, "Pixal 3D (Pixazo)" 1224, Tripo 3D-2.5 1217, TRELLIS 2 1051. Pixazo ranks a model it labels as its own. Cinevva attributes a model named Pixal3D to Tencent ARC, so either the names collide or Pixazo is attributing incorrectly. — [Pixazo leaderboard](https://www.pixazo.ai/leaderboard/ai-3d-model-generation); [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)
- **Meshy employee's three-way API test** (Apr–May 2026): Tripo leads white-mesh geometry and topology; Meshy leads textured single-view. Full numbers are in section 1. — [HackerNoon (2026-06-03)](https://hackernoon.com/how-i-stress-tested-3-ai-3d-generators-on-the-same-inputs-what-the-numbers-actually-show)
- **Meshy's own blind test:** 1,331 "senior 3D artists from NetEase and Tencent" preferred Meshy 6 over Tripo 3.1 by 63.8%. This is vendor-run and the organizer is not disclosed. — [3D AI Studio (2026-07-07)](https://www.3daistudio.com/blog/hitem3d-vs-meshy-vs-tripo-comparison)
- **3D Arena (Hugging Face, academic):** a pairwise human-preference platform with 123,243 votes from 8,096 users across 19 models at paper time (mid-2025). Gaussian-splat outputs scored about 16.6 ELO above meshes, a presentation bias that matters when comparing mesh vs splat tools. — [arXiv 2506.18787](https://arxiv.org/html/2506.18787v1)
- 3D Arena's top model in Mar 2025 was CSM Cube, which is outdated. — [Dylan Ebert on X (2025-03)](https://x.com/dylan_ebert_/status/1902383506262458461)
- The live 3D Arena Space did not render its leaderboard to my fetch. Papers with Code mirrors it hourly and lists Pixal3D (2026-05-11) among entries. — [HF Space 3d-arena](https://huggingface.co/spaces/dylanebert/3d-arena); [Papers with Code mirror](https://paperswithcode.co/benchmark/3d-arena-iso3d-living)
- **Retopology checks** (top3d.ai, Jan 2026): Hunyuan > Tripo > Meshy; numbers in section 1. — [top3d.ai](https://www.top3d.ai/learn/retopology-comparison-2025)
- Academic: SAM 3D Objects reports F1@0.01 of 0.2344 on SA-3DAO vs 0.1533 for TripoSG and 0.1629 for Hi3DGen. These are paper-reported numbers, not commercial products. — [Segmentit summary of SAM 3D](https://segmentit.com/sam-3d/)
- Meta claims SAM 3D Objects wins at least 5:1 in human preference over leading models (vendor claim). — [Meta AI: SAM 3D](https://ai.meta.com/blog/sam-3d/)
- Academic: the Seed3D 1.0 paper (ByteDance, Oct 2025) compares itself against Step1X-3D and others on ULIP and Uni3D metrics using 1,000 test images. — [arXiv 2510.19944](https://arxiv.org/html/2510.19944v1)

### Inferences
- Across sources with different biases (a Meshy employee, a crowd arena with Tripo affiliate links, a competitor's critique that still praises the textures), Tripo v3.1/H3.1 is clearly top-tier on overall preference, geometry and texture. Its most consistent criticism is topology and mesh hygiene on complex shapes. Hunyuan 3D 3.1 Pro is the most consistent rival on geometry quality, and Rodin on topology.
- Arena "overall" votes reward how a turntable looks, not game-readiness (triangle count, UVs, manifoldness). For game assets, weight the retopology and mesh-hygiene evidence more heavily than raw ELO.
- Newer 2026 entrants worth watching appear in the arena: Neural4D N4D-2.5, Sloyd v3, Triverse 3.0, plus open Pixal3D (MIT). None has displaced Tripo, Hunyuan or Rodin in any source.

### Gaps
- I found no 2026 YouTube head-to-head comparisons through search and none could be fetched.
- The current 3D Arena (HF) standings could not be read.
- The Indie Hackers "tested 9" article returned HTTP 403.
- No benchmark tested Unity import or URP rendering specifically.

---

## 5. What practitioners currently prefer (r/gamedev, r/aigamedev, r/blender, X)

### Takeaway
I could not access Reddit directly; WebFetch blocks reddit.com and site-restricted searches returned no threads. The secondhand consensus in 2026 roundups that cite community discussion:
- **Meshy** as the all-rounder.
- **Tripo** for game-ready output and creature rigging.
- **Rodin** for clean quads.
- **Hunyuan3D 2.1 / TRELLIS.2** for self-hosting.

All of them insist outputs still need cleanup.

### Cited Findings
- "Meshy for rigs, Tripo for game assets, Hitem3D for prints" is the rule of thumb in a reseller's roundup. — [3D AI Studio (2026-07-07)](https://www.3daistudio.com/blog/hitem3d-vs-meshy-vs-tripo-comparison)
- Cinevva's verdicts: Meshy is "the most popular all-rounder"; Tripo is "strong, game-focused output"; Rodin is for when "mesh topology matters most"; Hunyuan3D 2.1 and TRELLIS.2 are for self-hosting, and TRELLIS.2 is "the current open leader". — [Cinevva games guide (2026-06/09)](https://app.cinevva.com/guides/ai-asset-generators-games) (Cinevva hosts Hunyuan3D)
- A 2026 roundup says it cross-referenced r/aigamedev discussion and calls Meshy the most complete single platform; other guides frame Hunyuan as the free option that rivals paid tools on hard-surface. — [Blyth.ai roundup](https://blyth.ai/blog/best-ai-3d-model-generators-2026); [Trify3D ranking](https://trify3d.com/blog/best-ai-3d-model-generators) (search-snippet level only; not fetched)
- On Hacker News, practitioners pushed back on Hunyuan's EU/UK/South Korea exclusion ("Nice model, but strange license"). — [Hacker News](https://news.ycombinator.com/item?id=43420870)
- Practitioner-style caveat: "every important asset still needs inspection in a 3D viewer and … cleanup, retopology, UV work". — [Blyth.ai roundup](https://blyth.ai/blog/best-ai-3d-model-generators-2026) (snippet); echoed by [Mixar](https://www.mixar.app/blog/tripo-vs-hunyuan)

### Inferences
- The community framing matches the benchmark picture: Tripo for speed and volume of game assets, Meshy for humanoid rigs, textures and an all-in-one UI, Rodin for topology, and open models when cost or privacy dominates.

### Gaps
- No direct r/gamedev, r/aigamedev, r/blender or X threads from 2026 could be retrieved or quoted. Treat practitioner sentiment as unverified secondhand. A human pass over r/aigamedev search ("tripo", "hunyuan", "trellis 2", sorted by new) is recommended before finalizing.

---

## 6. Should Tripo be the main generator for a Unity URP indie team, and what should it be paired with?

### Takeaway
Yes, with a paid plan, Tripo is a defensible primary generator. It is top or near-top on crowd preference, white-mesh geometry and texture. It is among the cheapest per asset, has the widest game-pipeline API (retopo, UV, parts, quadruped rigging, retarget), an official Unity plugin and an official, if alpha, MCP. Pair it with **Hunyuan 3D 3.1 Pro or Rodin Gen-2.5** for hero and hard-surface assets where clean, controllable topology matters. Use **TRELLIS.2** (MIT) as the local, license-clean fallback. **Meshy** is an optional add-on for humanoid rig and animation libraries or textured single-image hero shots.

### Cited Findings
- Tripo is cost-competitive: $0.30 per generation on the top3d.ai listing vs $0.40 for Meshy 6, $0.29 for Rodin 2.5 and $0.73 for Hitem3D. — [Sloyd/top3d.ai](https://www.sloyd.ai/blog/ai-3d-model-generator-rankings)
- Tripo leads white-mesh geometry, per the Meshy employee's test. — [HackerNoon](https://hackernoon.com/how-i-stress-tested-3-ai-3d-generators-on-the-same-inputs-what-the-numbers-actually-show)
- Hunyuan has the cleanest retopology and controllable face counts. — [top3d.ai](https://www.top3d.ai/learn/retopology-comparison-2025); [Mixar](https://www.mixar.app/blog/tripo-vs-hunyuan)
- Rodin is the topology and quad pick, with on-prem and LoRA options at Enterprise. — [Cinevva](https://app.cinevva.com/guides/ai-asset-generators-games); [Hyper3D pricing](https://hyper3d.ai/pricing)
- TRELLIS.2 is MIT and self-hostable on 24 GB. — [GitHub TRELLIS.2](https://github.com/microsoft/TRELLIS.2)
- Meshy's rigging is humanoid-only, while Tripo has non-humanoid riggers. — [Meshy docs](https://docs.meshy.ai/en/api/rigging-and-animation); [Scenario help](https://help.scenario.com/articles/6323782235-tripo-3-1-suite-the-essentials)

### Inferences (suggested pairing matrix; synthesis, not a sourced claim)

| Asset need | Primary | Pair / fallback | Why |
|---|---|---|---|
| Background props, clutter, pickups (volume) | **Tripo P1.0** (low-poly) | — | Fastest and cheapest. Vendor claims engine-ready low-poly; verify topology per batch. |
| Mid-detail props, stylized characters, creatures | **Tripo H3.1** + Tripo retopo / quad + Smart UV | Hunyuan 3D 3.1 for retries | Top preference scores. Tripo's quadruped rigging is unique among hosted tools. |
| Hard-surface hero props, modular kit pieces | **Rodin Gen-2.5** (quad / low-poly, symmetry) or **Hunyuan 3D 3.1 Pro** (face count 40K–1.5M) | Tripo as concept / blockout | Tripo H3.1 at defaults can fuse parts and emit about 1M triangles on complex shapes. |
| Humanoid characters needing an animation library | Tripo v1.0 rig or **Meshy** (humanoid rig + animation library) | Mixamo / UniRig (MIT) | Meshy is humanoid-only but mature. Competitor reports Tripo rig joints collapsing past 30°. |
| Local / offline / NDA batch generation | **TRELLIS.2** (MIT) | Hunyuan3D 2.1, only if outside EU/UK/South Korea and under 1M MAU | TRELLIS.2 has the cleanest license. Hunyuan's open license has territory and MAU limits and no training on outputs. |
| 3D-print or ultra-detail sculpt reference | Hitem3D | — | Most raw detail but dense triangles, so not game-ready without retopo. |
| Deprioritize | Luma Genie, CSM, SPAR3D, Kaedim | — | Few or no 2026 benchmark appearances. SPAR3D's license has a $1M revenue threshold. Kaedim costs about $20/credit. |

- **Operational rules for the team:**
  1. Only use paid-tier outputs commercially. Tripo's free-tier commercial status conflicts across its own documents; Meshy and Hitem3D free tiers are CC BY.
  2. Download assets immediately; Meshy keeps them only 3 days.
  3. Budget a Blender cleanup pass. The repo already has Blender MCP with `generate_3d` covering Tripo, Hunyuan, Rodin and Meshy, so one agent pipeline can A/B the same reference image across vendors.
  4. Convert glTF metal/rough to URP metallic-smoothness on import.

### Gaps
- No source evaluated these tools specifically in Unity URP (shader conversion, LODs, lightmap UVs).
- No independent test of Tripo P1.0 was found.
- The pairing matrix above is a synthesis and should be validated with an in-house A/B on 5–10 representative assets from the game's art bible.
