"""Generate, fetch and record Tripo AI meshes for FratRow.

Tripo makes meshes and textures only. Rigging is not taken from it: characters
are bound to the repo's own skeleton (tools/blender/charkit). Raw output goes
to Blender cleanup and from there to Unity. Each asset has one folder,
ArtSource/AI/<AssetName>/:

    ref/             input images; committed and hashed in provenance.json
    raw/             downloads from Tripo; gitignored (big files, and LFS is
                     off on this repo)
    provenance.json  committed; an append-only task log (schema 1) that backs
                     the Steam AI-content disclosure

API calls go through the official SDK, tripo3d 1.x (apiv3). It covers every
route used here, handles auth, uploads (presigned PUT above 50 MB) and
polling, never retries a create call (a retry would bill twice), and needs no
other package. The 0.x releases stop working when Tripo's v2 API shuts down on
2026-10-31. Request bodies are built here and sent with the SDK's public
`create_task`, so --dry-run prints exactly what will be sent and the
provenance file records it. Result files are downloaded with urllib straight
after a task finishes, because their signed URLs expire after about five
minutes. `fetch` gets fresh URLs later.

Studio subscription credits do not pay for API calls; the API is billed
separately (pay-as-you-go, 100 credits = $1). For assets made in Tripo Studio,
download them by hand and record them with `register`, which needs no API key.

    py -3.12 tools/tripo/tripo_gen.py image keg.png --name Prop_Keg --dry-run
    py -3.12 tools/tripo/tripo_gen.py register --name Prop_Keg --file Prop_Keg.glb --ref keg.png
"""
import argparse
import asyncio
import hashlib
import http.client
import json
import os
import re
import shutil
import sys
import urllib.parse
import urllib.request
import zipfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

# --- Pinned versions (checked 2026-10-19 against tripo3d 1.0.0) ---------------
# P2.0 Smart Mesh is the game-budget generator: native quads, parts split at
# generation time, up to 50K tris / 25K quads. H3.1 (v3.1-20260211) is the
# dense HD model (~1M faces by default) and is only worth it as a bake source.
DEFAULT_MODEL_VERSION = "P2-20260801"
LOWPOLY_MODEL_VERSION = "v3.0"            # /v3/mesh/decimate; v3.0 is newest, adds smart_uv
SEGMENT_MODEL_VERSION = "v2.0-20260430"   # the server default is still v1.0
SDK_MAJOR = "1."

# Limits from the Tripo developer docs. Models not listed are sent unchecked.
MODEL_LIMITS = {
    "P2-20260801": {"faces": (48, 50000), "quad_faces": (48, 25000)},
    "P1-20260311": {"faces": (50, 20000),
                    "unsupported": ("quad", "smart_low_poly", "generate_parts", "geometry_quality")},
    "v3.1-20260211": {},
    "v3.0-20250812": {},
    "v2.5-20250123": {"pre_v3": True},
    "v1.4-20240625": {"pre_v3": True},
}
LOWPOLY_LIMITS = {"v2.0": (500, 20000, 10000), "v3.0": (500, 20000, 10000)}  # min, tri max, quad max

# --- API ----------------------------------------------------------------------
API_BASE = "https://openapi.tripo3d.ai"   # the SDK's global endpoint; shown by --dry-run
KEY_ENV = "TRIPO_API_KEY"
ROUTES = {  # task type -> apiv3 route (as mapped by TripoClient.create_task)
    "text_to_model": "/v3/generation/text-to-model",
    "image_to_model": "/v3/generation/image-to-model",
    "multiview_to_model": "/v3/generation/multiview-to-model",
    "convert_model": "/v3/models/convert",
    "highpoly_to_lowpoly": "/v3/mesh/decimate",
    "mesh_segmentation": "/v3/mesh/segment",
}
UPLOAD_ROUTE, PRESIGN_ROUTE = "/v3/files", "/v3/files/presign"
MULTIPART_MAX_BYTES = 50 * 1024 * 1024
UPLOAD_MAX_BYTES = 150 * 1024 * 1024
DEFAULT_TIMEOUT_S = 900
DOWNLOAD_TIMEOUT_S = 300
USER_AGENT = "fratrow-tripo-gen/1"
CONVERT_FORMATS = ("GLTF", "FBX", "OBJ", "USDZ", "STL", "3MF")
REGISTER_FORMATS = (".glb", ".gltf", ".fbx", ".obj", ".zip")

# --- Asset layout and provenance ----------------------------------------------
REPO = Path(__file__).resolve().parents[2]
AI_ROOT = Path("ArtSource", "AI")
NAME_RE = re.compile(r"^[A-Za-z][A-Za-z0-9_-]{0,63}$")
SCHEMA = 1
GENERATOR = "tripo"
STUDIO_TYPE = "studio_manual"
DISCLOSURE = ("3D mesh and texture generated with Tripo AI, then cleaned, fitted "
              "and optimised in Blender.")
UPLOAD_MARK = "<upload:{}>"
SNIFF = ((b"glTF", ".glb"), (b"PK\x03\x04", ".zip"), (b"Kaydara FBX", ".fbx"),
         (b"\x89PNG", ".png"), (b"\xff\xd8\xff", ".jpg"), (b"RIFF", ".webp"))


class ToolError(Exception):
    """A problem the user can fix. Printed without a traceback."""


@dataclass
class Asset:
    name: str
    dir: Path

    @classmethod
    def at(cls, root, name):
        if not NAME_RE.match(name or ""):
            raise ToolError(f"asset name {name!r}: use letters, digits, _ or -, starting with a letter "
                            "(e.g. Prop_Keg)")
        return cls(name, Path(root) / AI_ROOT / name)

    @property
    def ref(self):
        return self.dir / "ref"

    @property
    def raw(self):
        return self.dir / "raw"

    @property
    def provenance(self):
        return self.dir / "provenance.json"

    def rel(self, path):
        return Path(path).relative_to(self.dir).as_posix()


@dataclass
class Ref:
    src: Path
    path: Path      # destination in ref/
    rel: str        # "ref/<file>", as recorded
    sha256: str


@dataclass
class Request:
    task_type: str
    body: dict      # uploads appear as UPLOAD_MARK placeholders
    uploads: list   # Ref objects to upload before submitting

    @property
    def route(self):
        return ROUTES[self.task_type]


# --- Provenance -----------------------------------------------------------------

def utc_now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def new_provenance(name):
    return {"schema": SCHEMA, "asset": name, "generator": GENERATOR, "tasks": [],
            "inputs": [], "prompt": None, "disclosure": DISCLOSURE}


def load_provenance(asset):
    if not asset.provenance.exists():
        return new_provenance(asset.name)
    try:
        data = json.loads(asset.provenance.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ToolError(f"{asset.provenance} is not valid JSON ({exc}). It was left untouched; fix it by hand.")
    if data.get("schema") != SCHEMA or data.get("asset") != asset.name:
        raise ToolError(f"{asset.provenance}: expected schema {SCHEMA} for asset {asset.name!r}, "
                        f"found schema {data.get('schema')!r} for {data.get('asset')!r}")
    for key, value in new_provenance(asset.name).items():
        data.setdefault(key, value)
    return data


def save_provenance(asset, data):
    """Write via a temp file and rename, so a crash never leaves half a file."""
    asset.dir.mkdir(parents=True, exist_ok=True)
    tmp = asset.provenance.with_name(asset.provenance.name + ".tmp")
    tmp.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    os.replace(tmp, asset.provenance)


def merge_task(data, entry):
    """Append `entry`, or fold it into the recorded task with the same id.

    Keys that are present and not None win, except created_utc, which keeps
    the first time seen. Outputs accumulate without repeats.
    """
    for task in data["tasks"]:
        if task["id"] == entry["id"]:
            for key, value in entry.items():
                if key == "outputs":
                    task["outputs"] = task.get("outputs", []) + [o for o in value if o not in task.get("outputs", [])]
                elif key == "created_utc" and task.get(key):
                    continue  # the first sighting wins
                elif value is not None:
                    task[key] = value
            return task
    data["tasks"].append(dict(entry))
    return data["tasks"][-1]


def add_inputs(data, refs):
    for ref in refs:
        item = {"path": ref.rel, "sha256": ref.sha256}
        if item not in data["inputs"]:
            data["inputs"].append(item)


def record(asset, entry, refs=(), prompt=None, adopt=None):
    """Load, merge and save provenance.json in one step. Returns the merged task.

    `adopt` is a server task payload; its type, model and input fill in a
    task that was not created by this tool (only when it is not yet recorded).
    """
    data = load_provenance(asset)
    if adopt is not None and not any(t["id"] == entry["id"] for t in data["tasks"]):
        entry = {**entry_from_server(adopt), **entry}
    add_inputs(data, refs)
    if prompt:
        data["prompt"] = prompt
    task = merge_task(data, entry)
    save_provenance(asset, data)
    return task


def entry_from_server(task):
    created = task.get("create_time")
    if isinstance(created, (int, float)) and created > 0:
        created = datetime.fromtimestamp(created, timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    elif not isinstance(created, str):
        created = task.get("created_at")
    task_input = task.get("input") if isinstance(task.get("input"), dict) else {}
    return {"id": str(task.get("task_id")), "type": task.get("type") or None,
            "model_version": task_input.get("model"), "params": task_input,
            "created_utc": created, "status": None, "outputs": []}


def find_asset(root, task_id):
    """The assets whose provenance.json records `task_id`."""
    hits = []
    for path in sorted((Path(root) / AI_ROOT).glob("*/provenance.json")):
        try:
            tasks = json.loads(path.read_text(encoding="utf-8")).get("tasks", [])
        except (OSError, json.JSONDecodeError):
            continue
        if any(t.get("id") == task_id for t in tasks):
            hits.append(path.parent.name)
    return hits


def resolve_asset(root, task_id, name, required=True):
    if name:
        return Asset.at(root, name)
    hits = find_asset(root, task_id)
    if len(hits) == 1:
        return Asset.at(root, hits[0])
    if len(hits) > 1:
        raise ToolError(f"task {task_id} is recorded under several assets ({', '.join(hits)}); pass --name")
    if required:
        raise ToolError(f"task {task_id} is not in any {AI_ROOT.as_posix()}/*/provenance.json; pass --name")
    return None


# --- Input files ------------------------------------------------------------------

def file_sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def stage_refs(asset, sources, write=True):
    """Copy input images into ref/ and hash them.

    A file keeps its name; if a different file already holds that name it gets
    _2, _3, ... An identical copy is reused. With write=False nothing is copied.
    """
    claimed, refs = {}, []
    for src in sources:
        src = Path(src)
        if not src.is_file():
            raise ToolError(f"input not found: {src}")
        if src.stat().st_size > UPLOAD_MAX_BYTES:
            raise ToolError(f"{src} is over Tripo's 150 MB upload limit")
        sha = file_sha256(src)
        n = 1
        while True:
            name = f"{src.stem}{'' if n == 1 else f'_{n}'}{src.suffix.lower()}"
            dest = asset.ref / name
            held = claimed.get(name) or (file_sha256(dest) if dest.exists() else None)
            if held in (None, sha):
                break
            n += 1
        claimed[name] = sha
        if write and not dest.exists():
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)
        refs.append(Ref(src, dest, f"ref/{name}", sha))
    return refs


# --- Request building -------------------------------------------------------------

def parse_extra(pairs):
    """KEY=VALUE pairs for fields this CLI has no flag for; VALUE is JSON when it parses."""
    extra = {}
    for pair in pairs or ():
        key, sep, value = pair.partition("=")
        if not sep or not key:
            raise ToolError(f"--extra {pair!r}: expected KEY=VALUE")
        try:
            extra[key] = json.loads(value)
        except json.JSONDecodeError:
            extra[key] = value
    return extra


def merge_extra(body, extra):
    for key, value in extra.items():
        if key in body:
            raise ToolError(f"--extra {key} is already set by a flag")
        body[key] = value
    return body


def _set(body, **fields):
    """Add the fields that were given. A False flag is the server default, so it is left out."""
    for key, value in fields.items():
        if value is not None and value is not False:
            body[key] = value
    return body


def generation_body(args, head):
    body = {"model": args.model_version, **head,
            "texture": args.texture, "pbr": args.pbr and args.texture}
    _set(body, face_limit=args.face_limit, texture_quality=args.texture_quality,
         geometry_quality=args.geometry_quality, quad=args.quad,
         smart_low_poly=args.smart_low_poly, generate_parts=args.parts,
         auto_size=args.auto_size, model_seed=args.seed, texture_seed=args.texture_seed)
    if hasattr(args, "texture_alignment"):
        _set(body, texture_alignment=args.texture_alignment, orientation=args.orientation)
    return merge_extra(body, parse_extra(args.extra))


def build_image(args, refs):
    head = {"input": UPLOAD_MARK.format(refs[0].rel)}
    body = generation_body(args, head)
    _set(body, enable_image_autofix=args.autofix)
    return Request("image_to_model", body, refs)


def build_multiview(args, refs):
    """`refs` follow the API's view order: front, left, back, right (None = missing)."""
    head = {"inputs": [UPLOAD_MARK.format(r.rel) if r else None for r in refs]}
    return Request("multiview_to_model", generation_body(args, head), [r for r in refs if r])


def build_text(args):
    head = {"prompt": args.prompt}
    _set(head, negative_prompt=args.negative_prompt)
    return Request("text_to_model", generation_body(args, head), [])


def build_convert(args):
    body = {"input": args.task_id, "format": args.format}
    _set(body, quad=args.quad, force_symmetry=args.force_symmetry,
         face_limit=args.face_limit, texture_size=args.texture_size, texture_format=args.texture_format,
         scale_factor=args.scale_factor, pivot_to_center_bottom=args.pivot_to_center_bottom,
         pack_uv=args.pack_uv, fbx_preset=args.fbx_preset, export_orientation=args.export_orientation)
    if not args.bake:
        body["bake"] = False
    return Request("convert_model", merge_extra(body, parse_extra(args.extra)), [])


def build_lowpoly(args):
    body = {"input": args.task_id, "model": args.model_version}
    _set(body, face_limit=args.face_limit, quad=args.quad, smart_uv=args.smart_uv)
    if not args.bake:
        body["bake"] = False
    return Request("highpoly_to_lowpoly", merge_extra(body, parse_extra(args.extra)), [])


def build_segment(args):
    body = {"input": args.task_id, "model": args.model_version}
    _set(body, segmentation_granularity=args.granularity,
         split_by_connectivity=args.split_by_connectivity)
    return Request("mesh_segmentation", merge_extra(body, parse_extra(args.extra)), [])


def check_request(req, skip=False):
    """Check a request against the documented limits. Returns warnings; raises on errors."""
    body, errors, warnings = req.body, [], []
    model = body.get("model")
    if req.task_type in ("image_to_model", "multiview_to_model", "text_to_model"):
        limits = MODEL_LIMITS.get(model)
        if limits is None:
            warnings.append(f"model {model!r} is not in the pinned list; sent unchecked")
            limits = {}
        for key in limits.get("unsupported", ()):
            if body.get(key):
                errors.append(f"{model} does not support {key}")
        span = limits.get("quad_faces") if body.get("quad") and "quad_faces" in limits else limits.get("faces")
        if span and body.get("face_limit") is not None and not span[0] <= body["face_limit"] <= span[1]:
            errors.append(f"face_limit {body['face_limit']} is outside {span[0]}-{span[1]} for {model}"
                          f"{' with quad' if body.get('quad') else ''}")
        if limits.get("pre_v3") and (body.get("texture_quality") in ("detailed", "extreme")
                                     or body.get("geometry_quality") == "detailed"):
            errors.append(f"detailed/extreme quality needs a v3.0+ model, not {model}")
    elif req.task_type == "highpoly_to_lowpoly":
        span = LOWPOLY_LIMITS.get(model)
        limit = body.get("face_limit")
        if span and limit is not None:
            top = span[2] if body.get("quad") else span[1]
            if not span[0] <= limit <= top:
                errors.append(f"face_limit {limit} is outside {span[0]}-{top} for lowpoly {model}"
                              f"{' with quad' if body.get('quad') else ''}")
        if body.get("smart_uv") and model != "v3.0":
            errors.append("smart_uv needs lowpoly model v3.0")
    if errors and not skip:
        raise ToolError("; ".join(errors) + " (--skip-checks sends it anyway)")
    return warnings + errors


def replace_marks(value, lookup):
    """Swap upload placeholders for lookup[rel] throughout a request body."""
    if isinstance(value, dict):
        return {k: replace_marks(v, lookup) for k, v in value.items()}
    if isinstance(value, list):
        return [replace_marks(v, lookup) for v in value]
    if isinstance(value, str) and value.startswith("<upload:") and value.endswith(">"):
        return lookup[value[len("<upload:"):-1]]
    return value


def recorded_params(body):
    """The body as provenance keeps it: uploads named by their ref/ path."""
    return replace_marks(body, _Identity())


class _Identity(dict):
    def __missing__(self, key):
        return key


def describe_request(req, asset, root):
    lines = ["DRY RUN: nothing is sent and nothing is written."]
    for ref in req.uploads:
        size = ref.src.stat().st_size
        route = PRESIGN_ROUTE + " + presigned PUT" if size > MULTIPART_MAX_BYTES else UPLOAD_ROUTE
        lines.append(f"POST {API_BASE}{route}  upload {ref.rel} ({size} bytes, sha256 {ref.sha256[:12]}) -> file_token")
    lines += [f"POST {API_BASE}{req.route}",
              f"Authorization: Bearer ${KEY_ENV}",
              "Content-Type: application/json",
              json.dumps(req.body, indent=2)]
    if req.uploads:
        lines.append("(each <upload:...> is replaced by that file's file_token)")
    for ref in req.uploads:
        if ref.src.resolve() != ref.path.resolve():
            lines.append(f"copy {ref.src} -> {display(ref.path, root)}")
    lines.append(f"then poll GET {API_BASE}/v3/tasks/<task_id>, download every output to "
                 f"{display(asset.raw, root)}/ and append the task to {display(asset.provenance, root)}")
    return "\n".join(lines)


def display(path, root):
    try:
        return Path(path).resolve().relative_to(Path(root).resolve()).as_posix()
    except ValueError:
        return str(path)


# --- Network layer (replaced by fakes in the tests) -----------------------------

def api_key_from_env():
    """The API key from TRIPO_API_KEY. It is passed to the SDK and never printed or saved."""
    key = os.environ.get(KEY_ENV, "").strip()
    if not key:
        raise ToolError(f"{KEY_ENV} is not set. API credits are bought separately from Studio at "
                        f"platform.tripo3d.ai; then run `setx {KEY_ENV} tsk_...` and open a new terminal. "
                        "Assets made in Tripo Studio need no key: use `register`.")
    if not key.startswith("tsk_"):
        raise ToolError(f"{KEY_ENV} does not look like a Tripo API key (those start with tsk_)")
    return key


class SdkBackend:
    """The live API through the official tripo3d SDK, imported only when needed."""

    def __init__(self, api_key):
        # On import the SDK looks up the machine's country through public IP
        # services (ip-api.com and others) in a background thread. The client
        # never reads the result, so skip that lookup.
        os.environ.setdefault("TRIPO_DISABLE_GEO_DETECTION", "1")
        try:
            import tripo3d
        except ImportError:
            raise ToolError("the tripo3d SDK is not installed: "
                            "py -3.12 -m pip install -r tools/tripo/requirements.txt") from None
        if not tripo3d.__version__.startswith(SDK_MAJOR):
            raise ToolError(f"tripo3d {tripo3d.__version__} is installed; this tool needs 1.x "
                            "(0.x stops working when Tripo's v2 API shuts down on 2026-10-31)")
        self._sdk = tripo3d
        self._client = tripo3d.TripoClient(api_key=api_key)

    def _run(self, coro):
        try:
            return asyncio.run(coro)
        except self._sdk.TripoError as exc:
            raise ToolError(f"Tripo API: {exc}") from None

    def upload(self, path):
        return self._run(self._client.upload_file(str(path)))["file_token"]

    def submit(self, task_type, body):
        return self._run(self._client.create_task({"type": task_type, **body}))

    def get_task(self, task_id):
        return self._run(self._client.get_task(task_id)).raw

    def wait(self, task_id, timeout):
        task = self._run(self._client.wait_for_task(task_id, timeout=timeout, verbose=True))
        print()
        return task.raw

    def balance(self):
        bal = self._run(self._client.get_balance())
        return {"balance": bal.balance, "frozen": bal.frozen}


def http_download(url, dest):
    """Stream a signed result URL to `dest`. The API key is not sent."""
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=DOWNLOAD_TIMEOUT_S) as resp, open(dest, "wb") as fh:
        shutil.copyfileobj(resp, fh, 1 << 20)


@dataclass
class Context:
    root: Path
    backend: object = None      # callable returning a backend; built lazily so --dry-run needs no key
    download: object = http_download
    now: object = utc_now

    def api(self):
        if self.backend is None:
            return SdkBackend(api_key_from_env())
        return self.backend()


# --- Downloads ------------------------------------------------------------------

def output_urls(output):
    """(field, url) for every URL in a task's output, once per URL, one level deep."""
    found, seen = [], set()

    def visit(mapping, prefix=""):
        for key, value in mapping.items():
            if isinstance(value, dict):
                visit(value, prefix)
            elif isinstance(value, str) and value.startswith(("http://", "https://")) and value not in seen:
                seen.add(value)
                field = prefix + (key[:-4] if key.endswith("_url") else key)
                found.append((field, value))

    visit(output if isinstance(output, dict) else {})
    return found


def url_ext(url):
    return os.path.splitext(urllib.parse.urlparse(url).path)[1].lower()


def sniff_ext(path):
    with open(path, "rb") as fh:
        head = fh.read(16)
    return next((ext for magic, ext in SNIFF if head.startswith(magic)), ".bin")


def extract_zip(zip_path, dest, write=True):
    """Unpack into `dest`, refusing entries that would land outside it."""
    try:
        with zipfile.ZipFile(zip_path) as zf:
            members = [i for i in zf.infolist() if not i.is_dir()]
            root = dest.resolve()
            for info in members:
                if not (root / info.filename).resolve().is_relative_to(root):
                    raise ToolError(f"{Path(zip_path).name}: unsafe path {info.filename!r}; nothing extracted")
            if write:
                zf.extractall(dest)
    except zipfile.BadZipFile as exc:
        raise ToolError(f"{zip_path}: not a valid zip ({exc})") from None
    return [dest / info.filename for info in members]


def download_outputs(task, raw_dir, download):
    """Download every output of a finished task into raw/ right away.

    Files are named <task_id>_<field><ext>. Each is written to .part and
    renamed, so a failed download never looks like a finished one. Zips are
    unpacked next to themselves. Returns (files, failed fields).
    """
    raw_dir.mkdir(parents=True, exist_ok=True)
    files, failed = [], []
    for field, url in output_urls(task.get("output")):
        stem = f"{task['task_id']}_{field}"
        part = raw_dir / f"{stem}.part"
        try:
            download(url, part)
        except (OSError, http.client.HTTPException) as exc:
            part.unlink(missing_ok=True)
            failed.append(f"{field} ({exc})")
            continue
        final = raw_dir / f"{stem}{url_ext(url) or sniff_ext(part)}"
        os.replace(part, final)
        files.append(final)
        if final.suffix == ".zip":
            files += extract_zip(final, raw_dir / stem)
    return files, failed


# --- Commands ---------------------------------------------------------------------

def submit_and_collect(args, ctx, asset, req, refs=(), prompt=None):
    for warning in check_request(req, args.skip_checks):
        print(f"warning: {warning}")
    if args.dry_run:
        print(describe_request(req, asset, ctx.root))
        return 0
    api = ctx.api()
    tokens = {ref.rel: api.upload(ref.path) for ref in req.uploads}
    task_id = api.submit(req.task_type, replace_marks(req.body, tokens))
    # Recorded before waiting: if anything below fails, the paid task is still on file.
    record(asset, {"id": task_id, "type": req.task_type, "model_version": req.body.get("model"),
                   "params": recorded_params(req.body), "created_utc": ctx.now(),
                   "status": "queued", "outputs": []}, refs=refs, prompt=prompt)
    print(f"submitted {req.task_type} task {task_id} for {asset.name}")
    if args.no_wait:
        print(f"collect it later with: tripo_gen.py fetch {task_id}")
        return 0
    try:
        task = api.wait(task_id, args.timeout)
    except (ToolError, KeyboardInterrupt) as exc:
        raise ToolError(f"{str(exc) or 'interrupted'}\ntask {task_id} is recorded in "
                        f"{display(asset.provenance, ctx.root)}; run `tripo_gen.py fetch {task_id}` "
                        "once it has finished") from None
    return collect(asset, task, ctx)


def collect(asset, task, ctx, adopt=False):
    """Download a task's outputs (if it succeeded) and merge the result into provenance."""
    status = str(task.get("status") or "unknown").lower()
    files, failed = download_outputs(task, asset.raw, ctx.download) if status == "success" else ([], [])
    entry = {"id": str(task.get("task_id")), "status": status, "outputs": [asset.rel(f) for f in files]}
    if task.get("credits_consumed") is not None:
        entry["credits"] = task["credits_consumed"]
    record(asset, entry, adopt=task if adopt else None)
    print(f"task {entry['id']}: {status}" + (f", {entry['credits']} credits" if "credits" in entry else ""))
    for path in files:
        print(f"  {display(path, ctx.root)}")
    if status != "success":
        print(f"  {task.get('error_msg') or task.get('error_message') or 'no output to download'}")
        return 1
    if failed:
        print("could not download: " + "; ".join(failed))
        print(f"signed URLs expire after about 5 minutes; `tripo_gen.py fetch {entry['id']}` gets fresh ones")
        return 1
    return 0


def cmd_image(args, ctx):
    asset = Asset.at(ctx.root, args.name)
    refs = stage_refs(asset, [args.image], write=not args.dry_run)
    return submit_and_collect(args, ctx, asset, build_image(args, refs), refs)


def cmd_multiview(args, ctx):
    asset = Asset.at(ctx.root, args.name)
    views = [args.front, args.left, args.back, args.right]
    staged = iter(stage_refs(asset, [v for v in views if v], write=not args.dry_run))
    refs = [next(staged) if v else None for v in views]
    return submit_and_collect(args, ctx, asset, build_multiview(args, refs), [r for r in refs if r])


def cmd_text(args, ctx):
    if not args.prompt.strip():
        raise ToolError("the prompt is empty")
    asset = Asset.at(ctx.root, args.name)
    return submit_and_collect(args, ctx, asset, build_text(args), prompt=args.prompt)


def cmd_post(builder):
    def run(args, ctx):
        asset = resolve_asset(ctx.root, args.task_id, args.name)
        return submit_and_collect(args, ctx, asset, builder(args))
    return run


def cmd_status(args, ctx):
    asset = resolve_asset(ctx.root, args.task_id, args.name, required=False)
    if args.dry_run:
        print(f"DRY RUN: GET {API_BASE}/v3/tasks/{args.task_id}")
        return 0
    task = ctx.api().get_task(args.task_id)
    status = task.get("status")
    print(f"task {args.task_id}: {status}, {task.get('progress', 0)}%, type {task.get('type')}"
          + (f", {task['credits_consumed']} credits" if task.get("credits_consumed") is not None else ""))
    if task.get("error_msg"):
        print(f"  {task['error_msg']}")
    if asset:
        entry = {"id": args.task_id, "status": status}
        if task.get("credits_consumed") is not None:
            entry["credits"] = task["credits_consumed"]
        record(asset, entry, adopt=task)
    return 0


def cmd_fetch(args, ctx):
    asset = resolve_asset(ctx.root, args.task_id, args.name)
    if args.dry_run:
        print(f"DRY RUN: GET {API_BASE}/v3/tasks/{args.task_id} (fresh signed URLs), "
              f"then download every output to {display(asset.raw, ctx.root)}/")
        return 0
    return collect(asset, ctx.api().get_task(args.task_id), ctx, adopt=True)


def cmd_balance(args, ctx):
    if args.dry_run:
        print(f"DRY RUN: GET {API_BASE}/v3/account/balance")
        return 0
    bal = ctx.api().balance()
    print(f"API balance: {bal['balance']} credits available, {bal['frozen']} reserved by running tasks "
          "(API platform credits; Studio subscription credits are separate)")
    return 0


def cmd_register(args, ctx):
    """Record a model downloaded by hand from Tripo Studio. Needs no API key."""
    asset = Asset.at(ctx.root, args.name)
    src = Path(args.file)
    if not src.is_file():
        raise ToolError(f"file not found: {src}")
    if src.suffix.lower() not in REGISTER_FORMATS:
        raise ToolError(f"{src.name}: expected one of {', '.join(REGISTER_FORMATS)}")
    write = not args.dry_run
    sha = file_sha256(src)
    task_id = f"studio-{sha[:12]}"  # from the content, so registering the same file twice is a no-op
    refs = stage_refs(asset, args.ref or [], write=write)
    stem = f"{task_id}_{src.stem}"
    if src.suffix.lower() == ".zip":
        files = extract_zip(src, asset.raw / stem, write=write)
    else:
        files = [asset.raw / f"{stem}{src.suffix.lower()}"]
        if write:
            asset.raw.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, files[0])
    params = {k: v for k, v in (("source_file", src.name), ("source_sha256", sha), ("studio_url", args.studio_url),
                                ("prompt", args.prompt), ("refs", [r.rel for r in refs] or None)) if v}
    entry = {"id": task_id, "type": STUDIO_TYPE, "model_version": args.model_version, "params": params,
             "created_utc": ctx.now(), "status": "success", "outputs": [asset.rel(f) for f in files]}
    if args.dry_run:
        print("DRY RUN: nothing is written. Would record in "
              f"{display(asset.provenance, ctx.root)}:\n{json.dumps(entry, indent=2)}")
        return 0
    record(asset, entry, refs=refs, prompt=args.prompt)
    print(f"registered {src.name} as {task_id} for {asset.name}")
    for path in files:
        print(f"  {display(path, ctx.root)}")
    return 0


# --- CLI --------------------------------------------------------------------------

def build_parser():
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--dry-run", action="store_true", help="print the request; no network, no files written")
    common.add_argument("--skip-checks", action="store_true", help="send even if a documented limit is exceeded")

    task = argparse.ArgumentParser(add_help=False)
    task.add_argument("--no-wait", action="store_true", help="submit and exit; collect later with fetch")
    task.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT_S, help="seconds to wait (default %(default)s)")
    task.add_argument("--extra", action="append", metavar="KEY=VALUE",
                      help="extra request field (VALUE parsed as JSON if possible); repeatable")

    gen = argparse.ArgumentParser(add_help=False, parents=[common, task])
    gen.add_argument("--name", required=True, help="asset name, e.g. Prop_Keg")
    gen.add_argument("--model-version", default=DEFAULT_MODEL_VERSION, help="default %(default)s")
    gen.add_argument("--no-texture", dest="texture", action="store_false", help="geometry only (implies --no-pbr)")
    gen.add_argument("--no-pbr", dest="pbr", action="store_false", help="base colour only, no PBR maps")
    gen.add_argument("--texture-quality", choices=("standard", "detailed", "extreme"),
                     help="detailed ~4K, extreme 8K (costs more); server default standard")
    gen.add_argument("--geometry-quality", choices=("standard", "detailed"))
    gen.add_argument("--face-limit", type=int)
    gen.add_argument("--quad", action="store_true", help="quad topology")
    gen.add_argument("--smart-low-poly", action="store_true")
    gen.add_argument("--parts", action="store_true", help="generate_parts: split into parts")
    gen.add_argument("--auto-size", action="store_true", help="real-world scale")
    gen.add_argument("--seed", type=int, help="model_seed")
    gen.add_argument("--texture-seed", type=int)

    img = argparse.ArgumentParser(add_help=False, parents=[gen])
    img.add_argument("--texture-alignment", help="original_image (server default) or geometry")
    img.add_argument("--orientation", help="default (server default) or align_image")

    ap = argparse.ArgumentParser(prog="tripo_gen.py", description=__doc__.split("\n\n")[0])
    sub = ap.add_subparsers(dest="command", required=True)

    p = sub.add_parser("image", parents=[img], help="image to 3D")
    p.add_argument("image")
    p.add_argument("--autofix", action="store_true", help="enable_image_autofix")
    p.set_defaults(func=cmd_image)

    p = sub.add_parser("multiview", parents=[img], help="front (+ left/back/right) views to 3D")
    p.add_argument("--front", required=True)
    for view in ("left", "back", "right"):
        p.add_argument(f"--{view}")
    p.set_defaults(func=cmd_multiview)

    p = sub.add_parser("text", parents=[gen], help="text prompt to 3D")
    p.add_argument("prompt")
    p.add_argument("--negative-prompt")
    p.set_defaults(func=cmd_text)

    post = argparse.ArgumentParser(add_help=False, parents=[common, task])
    post.add_argument("task_id")
    post.add_argument("--name", help="asset name; found from provenance.json when omitted")

    p = sub.add_parser("convert", parents=[post], help="format / quad / face-limit conversion of a task")
    p.add_argument("--format", required=True, type=str.upper, choices=CONVERT_FORMATS)
    p.add_argument("--quad", action="store_true")
    p.add_argument("--force-symmetry", action="store_true")
    p.add_argument("--face-limit", type=int)
    p.add_argument("--texture-size", type=int, help="server default 4096")
    p.add_argument("--texture-format", help="server default JPEG")
    p.add_argument("--scale-factor", type=float)
    p.add_argument("--pivot-to-center-bottom", action="store_true")
    p.add_argument("--pack-uv", action="store_true")
    p.add_argument("--no-bake", dest="bake", action="store_false")
    p.add_argument("--fbx-preset", help="server default blender")
    p.add_argument("--export-orientation", help="server default +x")
    p.set_defaults(func=cmd_post(build_convert))

    p = sub.add_parser("lowpoly", parents=[post], help="smart low poly / retopology of a task")
    p.add_argument("--model-version", default=LOWPOLY_MODEL_VERSION, help="v1.0, v2.0 or v3.0 (default %(default)s)")
    p.add_argument("--face-limit", type=int, help="tri 500-20000, quad 500-10000; adaptive if omitted")
    p.add_argument("--quad", action="store_true")
    p.add_argument("--smart-uv", action="store_true", help="UV unwrap in the same task (v3.0)")
    p.add_argument("--no-bake", dest="bake", action="store_false")
    p.set_defaults(func=cmd_post(build_lowpoly))

    p = sub.add_parser("segment", parents=[post], help="part segmentation of a task")
    p.add_argument("--model-version", default=SEGMENT_MODEL_VERSION, help="default %(default)s")
    p.add_argument("--granularity", choices=("simple", "balanced", "detailed"))
    p.add_argument("--split-by-connectivity", action="store_true")
    p.set_defaults(func=cmd_post(build_segment))

    for name, func, text in (("status", cmd_status, "show a task's status"),
                             ("fetch", cmd_fetch, "download a task's outputs again (fresh URLs)")):
        p = sub.add_parser(name, parents=[common], help=text)
        p.add_argument("task_id")
        p.add_argument("--name", help="asset name; found from provenance.json when omitted")
        p.set_defaults(func=func)

    p = sub.add_parser("balance", parents=[common], help="API credit balance")
    p.set_defaults(func=cmd_balance)

    p = sub.add_parser("register", parents=[common], help="record a model downloaded from Tripo Studio (no API key)")
    p.add_argument("--name", required=True)
    p.add_argument("--file", required=True, help="the downloaded .glb/.fbx/.obj/.gltf or .zip")
    p.add_argument("--ref", action="append", help="reference image used in Studio; repeatable")
    p.add_argument("--prompt")
    p.add_argument("--model-version", help="as shown in Studio, e.g. P2.0 or H3.1")
    p.add_argument("--studio-url", help="link to the model in Studio")
    p.set_defaults(func=cmd_register)
    return ap


def main(argv=None, ctx=None):
    args = build_parser().parse_args(argv)
    ctx = ctx or Context(REPO)
    try:
        return args.func(args, ctx)
    except ToolError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
