# tripo — Tripo AI asset generation and provenance

`tripo_gen.py` creates meshes with Tripo AI (tripo3d.ai), downloads the results,
and records where every AI asset came from. Tripo supplies **mesh and textures
only**: characters are rigged on the repo's own skeleton
(`tools/blender/charkit`), never Tripo's. Assets go raw → Blender cleanup
(`tools/blender/aikit`) → Unity.

## There are two ways in, billed separately

| Path | When | Credits | Command |
|---|---|---|---|
| **Studio** (studio.tripo3d.ai) | Most assets. Generate in the browser, download by hand | The team's Studio subscription | `register` (no API key) |
| **API** (platform.tripo3d.ai) | Scripted or batch generation | **API credits bought separately**: pay-as-you-go, 100 credits = $1, they never expire, failed tasks are not charged | `image`, `multiview`, `text`, `convert`, `lowpoly`, `segment`, `status`, `fetch`, `balance` |

A Studio subscription **does not** pay for API calls. Tripo's developer FAQ says:
"Tripo API and Tripo Studio operate under separate billing systems and require
separate purchases". `balance` shows API credits only.

## Setup

```
py -3.12 -m pip install -r tools/tripo/requirements.txt
setx TRIPO_API_KEY tsk_your_key_here
```

`setx` takes effect in **new** terminals only. The key is read from the
environment, passed to the SDK, and never printed or written to disk. Do not put
it in a file in the repo. `register` and `--dry-run` need neither the key nor the
SDK.

## Where files go

```
ArtSource/AI/<AssetName>/
  ref/              input images, copied and hashed            committed
  raw/              everything downloaded from Tripo           gitignored (big; LFS is off)
  provenance.json   append-only task log, schema 1             committed
  <AssetName>_clean.glb   lean result from Blender, added later    committed
```

Names are letters, digits, `_` and `-`, starting with a letter (for example
`Prop_Keg`, `Char_Bro_Hoodie`). Raw files are named
`<task_id>_<field><ext>` (`..._model.glb`, `..._rendered_image.webp`). A
downloaded `.zip` is unpacked into `raw/<task_id>_<field>/`.

## Commands

Every command accepts `--dry-run`, which prints the exact request (method, URL,
JSON body; the key appears only as `$TRIPO_API_KEY`) and touches neither the
network nor the disk.

**Studio download (no key):**

```
py -3.12 tools/tripo/tripo_gen.py register --name Prop_Keg --file "%USERPROFILE%\Downloads\keg.glb" ^
    --ref keg_concept.png --prompt "dented aluminium beer keg" --model-version P2.0 ^
    --studio-url https://studio.tripo3d.ai/...
```

This copies the file into `raw/`, or unpacks a `.zip` there. Refs are copied
and hashed into `ref/`. The command then appends a `"type": "studio_manual"`
task. Its id is `studio-<first 12 hex of the file's sha256>`, so registering the
same file again changes nothing.

**API generation.** Each command submits, waits, downloads and records:

```
py -3.12 tools/tripo/tripo_gen.py image keg.png --name Prop_Keg --face-limit 8000 --quad
py -3.12 tools/tripo/tripo_gen.py multiview --front f.png --left l.png --back b.png --right r.png --name Char_Bro
py -3.12 tools/tripo/tripo_gen.py text "red plastic party cup" --name Prop_Cup --texture-quality detailed
```

Options:

| Option | Effect |
|---|---|
| `--model-version` | Default `P2-20260801`; see the next section |
| `--no-texture` | Geometry only; implies `--no-pbr` |
| `--no-pbr` | No PBR maps |
| `--texture-quality standard\|detailed\|extreme` | detailed is ~4K; extreme is 8K and costs more |
| `--geometry-quality standard\|detailed` | |
| `--face-limit N` | |
| `--quad` | Quad topology |
| `--smart-low-poly` | |
| `--parts` | `generate_parts` |
| `--auto-size` | |
| `--seed`, `--texture-seed` | |
| `--texture-alignment`, `--orientation` | `image` and `multiview` only |
| `--autofix` | `image` only |
| `--negative-prompt` | `text` only |
| `--extra KEY=VALUE` | Any field without a flag of its own, e.g. `--extra texture_version=v3.5-20260815` |
| `--no-wait` | Submit and exit |
| `--timeout S` | Default 900 |
| `--skip-checks` | Send the request even when it breaks a documented limit (the checks are listed below) |

There is no style option: Tripo removed API stylization in October 2026. Style
comes from the reference image or prompt.

**Post-processing an existing task.** The asset is found from
`provenance.json`, or you pass `--name`:

```
py -3.12 tools/tripo/tripo_gen.py convert <task_id> --format FBX --pivot-to-center-bottom --face-limit 10000
py -3.12 tools/tripo/tripo_gen.py lowpoly <task_id> --quad --face-limit 6000 --smart-uv
py -3.12 tools/tripo/tripo_gen.py segment <task_id> --granularity balanced
```

- `convert` (`/v3/models/convert`) changes the format (GLTF, FBX, OBJ, USDZ,
  STL, 3MF) and can also set quad output, face limit, texture size and format,
  scale, pivot, `pack_uv`, `--no-bake`, `fbx_preset` and `export_orientation`.
  The server defaults are FBX preset "blender" and orientation "+x", so set
  orientation explicitly and check the axis in Blender.
- `lowpoly` (`/v3/mesh/decimate`) is retopology, pinned to `v3.0`.
- `segment` (`/v3/mesh/segment`) is pinned to `v2.0-20260430`; the server's
  own default is still v1.0.

**Task housekeeping:**

```
py -3.12 tools/tripo/tripo_gen.py status <task_id>     # print status; update provenance if the task is recorded
py -3.12 tools/tripo/tripo_gen.py fetch <task_id>      # fresh URLs, download again into raw/
py -3.12 tools/tripo/tripo_gen.py balance
```

`fetch --name X` also adopts a task that was not created by this tool, for
example one submitted with `--no-wait` from another machine.

## Pinned versions (`tripo_gen.py`, top of file)

| Constant | Value | Why |
|---|---|---|
| `DEFAULT_MODEL_VERSION` | `P2-20260801` | Smart Mesh P2.0 is the game-budget model: native quads, parts split at generation, tri 48–50K / quad 48–25K faces. For an HD bake source, use `v3.1-20260211` (H3.1, ~1M faces by default). |
| `LOWPOLY_MODEL_VERSION` | `v3.0` | Newest retopology. Supports `smart_uv`. |
| `SEGMENT_MODEL_VERSION` | `v2.0-20260430` | Segmentation v2. The granularity option needs it. |

Other ids the API accepts: `P1-20260311`, `v3.0-20250812`, `v2.5-20250123`,
`v1.4-20240625`. Always pin a full id. Never rely on the server default, which
for the SDK's generation calls is `v2.5-20250123`.

The local checks, from Tripo's developer docs, block a request before any
credits are spent. P1 accepts no `quad`, `smart_low_poly`, `generate_parts` or
`geometry_quality`. Face limits are P1 50–20K, P2 48–50K (48–25K with quad),
and lowpoly 500–20K (quad up to 10K). detailed/extreme quality needs a v3.0+
model, and `smart_uv` needs lowpoly v3.0. Use `--skip-checks` if Tripo has
changed a limit.

## Credit costs (API, from the developer portal pricing cards, read 2026-10-19)

Source: developers.tripo3d.ai, via `research_notes/Tripo in game asset pipelines/tripo_product_capabilities.md` §7. Verify against `balance` before and after a run.

| Operation | API credits |
|---|---|
| H series (H3.1 etc.): text → 3D | 10–20 |
| H series: image or multiview → 3D | 20–30 |
| P series: text → 3D | 30–40 |
| P series: image or multiview → 3D | 40–50, **but** the same docs say "P2 base is 100 credits", texture standard +10 |
| Texture | 10–30 |
| Format convert | 5–10 |
| Retopology (lowpoly) | 10–30; v3.0: 35 tri / 40 quad |
| Smart UV | 20 |
| Segmentation & completion | 40–50 |
| Rig check / auto rig / retarget | free / 25 / 10 per animation (not used: we rig in Blender) |
| Worked example: `v3.1-20260211`, detailed texture and geometry | ~50 |

Studio, for comparison (Tripo blog, 2026-10-10): P2 mesh 100, Mesh Edit 50,
Smart UV 20, Texture 20.

## Warnings

- **Tripo's v2 API shuts down on 2026-10-31.** SDK 0.x releases stop working
  then. `requirements.txt` pins `tripo3d>=1.0.0,<2.0.0`, which uses apiv3 and
  promises no breaking changes before 2.0, and the tool refuses to run on any
  other major version. Tutorials, plugins and the official Blender add-on that
  bundle 0.x will break; the Blender add-on vendors a 2025 SDK.
- **Output URLs expire after about 5 minutes.** The tool downloads as soon as a
  task succeeds. If a download fails or you used `--no-wait`, `fetch` gets
  fresh URLs.
- **The task is recorded before the wait.** A crash, timeout or Ctrl+C never
  loses track of a paid task: `fetch` it later.
- **Commercial use: paid accounts only.** Tripo's own documents contradict each
  other on free-tier rights. The pricing page says "Non-Commercial Use". The
  Terms (§5.2.1, July 2025) say Tripo "retains all rights" to Free users'
  inputs and outputs, and no current free-tier licence grant was found. Treat free-tier output
  as unusable for the game, and generate anything that ships on a paid Studio
  plan or with purchased API credits. Our own conservative rule, not Tripo's:
  don't ship assets made with free API trial credits either. Reference images
  still need their own rights cleared.
- **Steam disclosure.** AI meshes and textures that ship are "Pre-Generated" AI
  content on Steam's content survey. `provenance.json` holds the per-asset
  record and the `disclosure` sentence.

## provenance.json (schema 1)

```json
{
  "schema": 1,
  "asset": "Prop_Keg",
  "generator": "tripo",
  "tasks": [
    {"id": "<task id or studio-…>", "type": "image_to_model | multiview_to_model | text_to_model | convert_model | highpoly_to_lowpoly | mesh_segmentation | studio_manual",
     "model_version": "P2-20260801", "params": {"...": "request body, uploads named by ref/ path"},
     "created_utc": "2026-10-19T12:00:00Z", "status": "success", "outputs": ["raw/..."], "credits": 50}
  ],
  "inputs": [{"path": "ref/keg.png", "sha256": "..."}],
  "prompt": "latest prompt, or null",
  "disclosure": "3D mesh and texture generated with Tripo AI, then cleaned, fitted and optimised in Blender."
}
```

Each task appears once. A later command with the same id merges into it:
status and credits update, outputs accumulate, and `created_utc` keeps the
first value. `credits` is present only when Tripo reports it. The file is
written to a temp file and renamed into place, and a corrupt file is never
overwritten.

## Tests

```
py -3.12 -m unittest discover tools/tripo/tests
```

The tests need no network and no key: the API and the downloader are faked.
One test (`SdkAdapterTests`) runs only when `tripo3d` is installed. It drives
the real SDK with its HTTP layer faked, to check the routes each task type maps
to.

## Not verified (no API key on the dev machine at build time)

- Nothing has been sent to the live API yet. The SDK wiring is tested only
  against SDK 1.0.0's own code.
- P2 pricing: the pricing cards say 40–50 credits but the docs also say 100.
  The first run will show which.
- P2 as a text-to-3D model, and P2 with `pbr` or `texture_quality`, are listed
  as accepted ids and options but have not been tried.
- `texture_alignment` and `orientation` values, `--extra texture_version` /
  `delight` at generation time, and the default unit scale of FBX exports.
- The exact output fields of convert, lowpoly and segment tasks, for example
  whether OBJ/FBX arrive as a zip. The tool downloads every URL in the task
  output and unpacks zips, so it should cope either way.
