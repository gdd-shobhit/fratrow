#!/usr/bin/env python3
"""uitk-snapshot: render a Unity UI Toolkit screen (UXML + USS + fonts + images) to a PNG
headlessly, without opening the game project in the editor.

It copies the UXML and everything it references (with .meta files, so GUID references
resolve) into a small cached "harness" Unity project, then runs Unity in batch mode (with
the GPU, not -nographics) to render the screen into a RenderTexture via PanelSettings.

Typical use, from the repo root:

    python tools/unity/uitk-snapshot/snapshot.py \
        --uxml "Assets/Scripts/UI/UI Toolkit/HUDVisualTree.uxml" \
        --out production/qa/evidence/hud.png \
        --fixture tools/unity/uitk-snapshot/examples/hud-party.fixture.json

See README.md next to this file for the fixture format, batch files and known limits.

Exit codes: 0 every shot rendered, 1 a shot failed (or --strict and warnings), 2 bad
arguments / setup problem, 3 Unity crashed, timed out or never ran the job.
"""
from __future__ import annotations

import argparse
import html
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import time
import urllib.parse
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
RUNNER_SOURCE = TOOL_DIR / "harness" / "UitkSnapshotRunner.cs"
HARNESS_OWNED = "Assets/_UitkSnapshotHarness"  # tool-owned folder inside the harness; never synced
EXECUTE_METHOD = "FratRowTools.UitkSnapshot.UitkSnapshotRunner.Run"
DEFAULT_BACKGROUND = "#3a3f4b"
DEFAULT_THEME_FALLBACK = "Assets/Scripts/UI/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss"
STATE_FILE = ".uitk-snapshot-state.json"
LOCK_FILE = ".uitk-snapshot.lock"
KEEP_LOGS = 10

# Only built-in modules: nothing to download, nothing to compile, fast first import.
HARNESS_MANIFEST = {
    "dependencies": {
        "com.unity.modules.imageconversion": "1.0.0",
        "com.unity.modules.imgui": "1.0.0",
        "com.unity.modules.jsonserialize": "1.0.0",
        "com.unity.modules.ui": "1.0.0",
        "com.unity.modules.uielements": "1.0.0",
    }
}
HARNESS_DEFAULT_THEME = '@import url("unity-theme://default");\n'

UI_TEXT_EXT = {".uxml", ".uss", ".tss"}
YAML_EXT = {".asset", ".mat", ".prefab", ".fontsettings", ".spriteatlas", ".spriteatlasv2", ".guiskin"}
NEVER_COPY_EXT = {".cs", ".dll", ".asmdef", ".asmref", ".so", ".jslib", ".rsp"}
BUILTIN_GUIDS = {
    "0000000000000000e000000000000000",
    "0000000000000000f000000000000000",
    "0000000000000000d000000000000000",
    "00000000000000000000000000000000",
}
KNOWN_UXML_NAMESPACES = {"", "UnityEngine.UIElements", "UnityEditor.UIElements"}

PROJECT_URL_RE = re.compile(r"project://database/([^?\"')#\s]+)(\?[^\"')#\s]*)?")
URL_FUNC_RE = re.compile(r"url\(\s*(['\"]?)(.*?)\1\s*\)", re.S)
RESOURCE_FUNC_RE = re.compile(r"resource\(\s*(['\"]?)(.*?)\1\s*\)", re.S)
IMPORT_STRING_RE = re.compile(r"@import\s+(['\"])(.*?)\1")
SRC_ATTR_RE = re.compile(r"\bsrc\s*=\s*\"([^\"]*)\"")
YAML_GUID_RE = re.compile(r"(?i)guid:\s*([0-9a-f]{32})")
GUID_PARAM_RE = re.compile(r"guid=([0-9a-fA-F]{32})")
CSS_COMMENT_RE = re.compile(r"/\*.*?\*/", re.S)
XML_COMMENT_RE = re.compile(r"<!--.*?-->", re.S)
HEX_COLOR_RE = re.compile(r"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")

FIXTURE_PROP_KEYS = {
    "text", "value", "label", "choices", "placeholder", "tooltip", "enabled", "display", "visible",
    "style", "class", "addClass", "removeClass", "toggleClass", "clear", "append", "children", "remove", "props",
}
FIXTURE_OP_KEYS = FIXTURE_PROP_KEYS | {"name", "withClass", "all"}
FIXTURE_SPEC_KEYS = FIXTURE_PROP_KEYS | {"type", "name"}
FIXTURE_STYLE_KEYS = {
    "width", "height", "minwidth", "maxwidth", "minheight", "maxheight", "left", "top", "right", "bottom",
    "margin", "marginleft", "margintop", "marginright", "marginbottom", "padding", "paddingleft",
    "paddingtop", "paddingright", "paddingbottom", "flexbasis", "fontsize", "letterspacing",
    "borderradius", "flexgrow", "flexshrink", "opacity", "borderwidth", "color", "backgroundcolor",
    "unitybackgroundimagetintcolor", "bordercolor", "display", "visibility", "position", "overflow",
    "flexdirection", "flexwrap", "justifycontent", "alignitems", "alignself", "aligncontent",
    "whitespace", "textoverflow", "unitytextalign", "textalign", "unityfontstyle",
    "unityfontstyleandweight", "fontstyle",
}

# Log lines worth surfacing (case-insensitive substring match).
LOG_FATAL_MARKERS = (
    "another unity instance is running",
    "it looks like another unity instance",
    "no valid unity editor license",
    "license is not valid",
    "scripts have compiler errors",
    "aborting batchmode due to failure",
    "executemethod class",
    "couldn't set project path",
    "could not find method",
    "failed to activate/update license",
)
LOG_UI_ASSET_RE = re.compile(r"\.(uxml|uss|tss)\b", re.I)
LOG_PROBLEM_RE = re.compile(
    r"(error|warning|unknown|invalid|expected|missing|not found|could not|couldn't|failed|unsupported|unexpected)",
    re.I,
)
LOG_NOISE_PREFIXES = ("start importing", "-> (artifact", "refreshing native plugins", "asset pipeline",
                      "[uitk-snapshot]", "commandline", "command line", "  ")


class SetupError(Exception):
    """Bad arguments or environment; maps to exit code 2."""


class Reporter:
    def __init__(self, verbose: bool):
        self.verbose = verbose
        self.warnings: list[str] = []

    def info(self, msg: str) -> None:
        print(f"uitk-snapshot: {msg}", flush=True)

    def detail(self, msg: str) -> None:
        if self.verbose:
            print(f"uitk-snapshot:   {msg}", flush=True)

    def warn(self, msg: str) -> None:
        if msg not in self.warnings:
            self.warnings.append(msg)
            print(f"uitk-snapshot: WARN  {msg}", flush=True)

    def error(self, msg: str) -> None:
        print(f"uitk-snapshot: ERROR {msg}", file=sys.stderr, flush=True)


# --------------------------------------------------------------------------- project discovery
def find_project_root(start: Path) -> Path | None:
    p = start if start.is_dir() else start.parent
    for d in [p, *p.parents]:
        if (d / "Assets").is_dir() and (d / "ProjectSettings").is_dir():
            return d
    return None


def asset_rel(path: Path, root: Path) -> str:
    return path.resolve().relative_to(root.resolve()).as_posix()


def read_unity_version(root: Path) -> str:
    pv = root / "ProjectSettings" / "ProjectVersion.txt"
    if not pv.is_file():
        raise SetupError(f"{pv} not found; is {root} a Unity project?")
    m = re.search(r"^m_EditorVersion:\s*(\S+)", pv.read_text(encoding="utf-8", errors="replace"), re.M)
    if not m:
        raise SetupError(f"could not read m_EditorVersion from {pv}")
    return m.group(1)


def read_color_space(root: Path) -> str:
    ps = root / "ProjectSettings" / "ProjectSettings.asset"
    try:
        m = re.search(r"^\s*m_ActiveColorSpace:\s*(\d)", ps.read_text(encoding="utf-8", errors="replace"), re.M)
    except OSError:
        m = None
    return "Linear" if (m is None or m.group(1) == "1") else "Gamma"


def unity_hub_editor_dirs() -> list[Path]:
    dirs: list[Path] = []
    if os.name == "nt":
        for env in ("ProgramFiles", "ProgramW6432"):
            base = os.environ.get(env)
            if base:
                dirs.append(Path(base) / "Unity" / "Hub" / "Editor")
        dirs.append(Path(r"C:\Program Files\Unity\Hub\Editor"))
        secondary = Path(os.environ.get("APPDATA", "")) / "UnityHub" / "secondaryInstallPath.json"
        if secondary.is_file():
            try:
                value = json.loads(secondary.read_text(encoding="utf-8"))
                if isinstance(value, str) and value.strip():
                    dirs.append(Path(value.strip()))
            except (OSError, ValueError):
                pass
    elif sys.platform == "darwin":
        dirs.append(Path("/Applications/Unity/Hub/Editor"))
    else:
        dirs.append(Path.home() / "Unity" / "Hub" / "Editor")
    unique: list[Path] = []
    for d in dirs:
        if d not in unique:
            unique.append(d)
    return unique


def unity_exe_in(version_dir: Path) -> Path:
    if os.name == "nt":
        return version_dir / "Editor" / "Unity.exe"
    if sys.platform == "darwin":
        return version_dir / "Unity.app" / "Contents" / "MacOS" / "Unity"
    return version_dir / "Editor" / "Unity"


def find_unity(version: str, override: str | None) -> Path:
    if override:
        exe = Path(override)
        if exe.is_dir():
            exe = unity_exe_in(exe)
        if not exe.is_file():
            raise SetupError(f"--unity {override}: Unity executable not found")
        return exe
    installed: list[str] = []
    for d in unity_hub_editor_dirs():
        exe = unity_exe_in(d / version)
        if exe.is_file():
            return exe
        if d.is_dir():
            installed += [p.name for p in d.iterdir() if p.is_dir()]
    raise SetupError(
        f"Unity {version} (from ProjectSettings/ProjectVersion.txt) is not installed under "
        f"{', '.join(str(d) for d in unity_hub_editor_dirs())}. Installed: {', '.join(sorted(set(installed))) or 'none'}. "
        f"Install it via Unity Hub or pass --unity <path to Unity.exe>.")


# --------------------------------------------------------------------------- GUID index
class GuidIndex:
    """guid -> asset path for every .meta under Assets/ (built lazily, ~1s for thousands of files)."""

    def __init__(self, root: Path):
        self.root = root
        self._map: dict[str, Path] | None = None

    def lookup(self, guid: str) -> Path | None:
        if self._map is None:
            self._build()
        assert self._map is not None
        return self._map.get(guid.lower())

    def _build(self) -> None:
        mapping: dict[str, Path] = {}
        for dirpath, _dirnames, filenames in os.walk(self.root / "Assets"):
            for fn in filenames:
                if not fn.endswith(".meta"):
                    continue
                fp = os.path.join(dirpath, fn)
                try:
                    with open(fp, "r", encoding="utf-8", errors="replace") as fh:
                        for _ in range(4):
                            line = fh.readline()
                            if line.startswith("guid:"):
                                mapping[line[5:].strip().lower()] = Path(fp[:-5])
                                break
                except OSError:
                    continue
        self._map = mapping


def meta_guid(asset: Path) -> str | None:
    meta = Path(str(asset) + ".meta")
    try:
        with open(meta, "r", encoding="utf-8", errors="replace") as fh:
            for _ in range(4):
                line = fh.readline()
                if line.startswith("guid:"):
                    return line[5:].strip().lower()
    except OSError:
        return None
    return None


# --------------------------------------------------------------------------- dependency walk
class Collector:
    """Finds every project file a set of UI assets needs, following UXML/USS/TSS URLs and YAML GUIDs."""

    def __init__(self, root: Path, guids: GuidIndex, rep: Reporter):
        self.root = root.resolve()
        self.assets_dir = (self.root / "Assets").resolve()
        self.guids = guids
        self.rep = rep
        self.files: dict[Path, str] = {}  # absolute file -> who referenced it first
        self.custom_elements: dict[str, str] = {}

    def collect(self, start: Path, reason: str) -> None:
        stack: list[tuple[Path, str]] = [(start.resolve(), reason)]
        while stack:
            f, why = stack.pop()
            if f in self.files:
                continue
            if f.suffix.lower() in NEVER_COPY_EXT:
                self.rep.detail(f"skipping script/binary reference {self._rel(f)} (from {why})")
                continue
            self.files[f] = why
            for dep in self._dependencies(f):
                if dep not in self.files:
                    stack.append((dep, self._rel(f)))

    def _rel(self, p: Path) -> str:
        try:
            return asset_rel(p, self.root)
        except ValueError:
            return str(p)

    def _dependencies(self, f: Path) -> list[Path]:
        ext = f.suffix.lower()
        if ext in UI_TEXT_EXT:
            return self._ui_text_dependencies(f, ext)
        if ext in YAML_EXT:
            return self._yaml_dependencies(f)
        return []

    def _ui_text_dependencies(self, f: Path, ext: str) -> list[Path]:
        try:
            raw = f.read_text(encoding="utf-8-sig", errors="replace")
        except OSError as e:
            self.rep.warn(f"cannot read {self._rel(f)}: {e}")
            return []
        if ext == ".uxml":
            self._scan_uxml_elements(f, raw)
            text = html.unescape(XML_COMMENT_RE.sub("", raw))
        else:
            text = CSS_COMMENT_RE.sub("", raw)

        refs: list[tuple[str, str | None, str]] = []
        for m in PROJECT_URL_RE.finditer(text):
            guid_match = GUID_PARAM_RE.search(m.group(2) or "")
            refs.append((urllib.parse.unquote(m.group(1)), guid_match.group(1).lower() if guid_match else None, "project"))
        for m in URL_FUNC_RE.finditer(text):
            refs.append((m.group(2).strip(), None, "relative"))
        for m in IMPORT_STRING_RE.finditer(text):
            refs.append((m.group(2).strip(), None, "relative"))
        if ext == ".uxml":
            for m in SRC_ATTR_RE.finditer(text):
                refs.append((m.group(1).strip(), None, "relative"))
        for m in RESOURCE_FUNC_RE.finditer(text):
            refs.append((m.group(2).strip(), None, "resource"))

        out: list[Path] = []
        for ref, guid, kind in refs:
            if not ref:
                continue
            if kind == "relative" and "://" in ref:
                continue  # project:// handled above; unity-theme://, http:// etc. are built-in or external
            resolved = self._resolve(ref, guid, kind, f)
            if resolved is not None:
                out.append(resolved)
        return out

    def _scan_uxml_elements(self, f: Path, raw: str) -> None:
        try:
            tree = ET.fromstring(raw.encode("utf-8"))
        except ET.ParseError as e:
            self.rep.warn(f"{self._rel(f)}: XML parse error ({e}); Unity will likely reject this UXML")
            return
        for el in tree.iter():
            tag = el.tag if isinstance(el.tag, str) else ""
            if tag.startswith("{"):
                ns, local = tag[1:].split("}", 1)
            else:
                ns, local = "", tag
            if ns in KNOWN_UXML_NAMESPACES and not (ns == "" and "." in local and not local.startswith("UnityEngine.UIElements.")):
                continue
            full = f"{ns}.{local}" if ns else local
            self.custom_elements.setdefault(full, self._rel(f))

    def _yaml_dependencies(self, f: Path) -> list[Path]:
        try:
            with open(f, "rb") as fh:
                head = fh.read(5)
                if head != b"%YAML":
                    return []  # binary-serialized asset: cannot follow references
                data = head + fh.read()
        except OSError:
            return []
        out: list[Path] = []
        for guid in sorted(set(g.lower() for g in YAML_GUID_RE.findall(data.decode("utf-8", errors="replace")))):
            if guid in BUILTIN_GUIDS:
                continue
            p = self.guids.lookup(guid)
            if p is None:
                self.rep.detail(f"{self._rel(f)} references GUID {guid} that is not under Assets/ (package asset?) - skipped")
                continue
            out.append(p.resolve())
        return out

    def _resolve(self, ref: str, guid: str | None, kind: str, from_file: Path) -> Path | None:
        where = self._rel(from_file)
        if kind == "resource":
            return self._resolve_resource(ref, where)

        if kind == "project":
            rel = ref.lstrip("/")
            if rel.startswith("Packages/"):
                self.rep.warn(f"{where}: package asset {rel} is not copied into the harness (it will be missing)")
                return None
            candidate = self.root / rel
            if candidate.is_file() and (guid is None or meta_guid(candidate) in (guid, None)):
                return self._inside_assets(candidate, ref, where)
            if guid:
                by_guid = self.guids.lookup(guid)
                if by_guid is not None and by_guid.is_file():
                    if by_guid.resolve() != candidate.resolve():
                        self.rep.detail(f"{where}: {rel} resolved by GUID to {self._rel(by_guid)}")
                    return self._inside_assets(by_guid, ref, where)
            if candidate.is_file():
                return self._inside_assets(candidate, ref, where)
            self.rep.warn(f"{where}: referenced asset {rel} not found (it will be missing from the render)")
            return None

        # Relative / absolute USS & UXML paths.
        clean = urllib.parse.unquote(ref.split("?", 1)[0].split("#", 1)[0])
        if clean.startswith("project:"):
            clean = clean[len("project:"):].lstrip("/")
            candidate = self.root / clean
        elif clean.startswith("/"):
            candidate = self.root / clean.lstrip("/")
        else:
            candidate = from_file.parent / clean
            if not candidate.exists() and clean.startswith("Assets/"):
                candidate = self.root / clean
        if candidate.is_file():
            return self._inside_assets(candidate, ref, where)
        self.rep.warn(f"{where}: referenced file '{ref}' not found (it will be missing from the render)")
        return None

    def _resolve_resource(self, name: str, where: str) -> Path | None:
        # resource("x") loads from any Resources/ (or Editor Default Resources/) folder, extension optional.
        name = urllib.parse.unquote(name)
        stem = Path(name)
        for dirpath, dirnames, filenames in os.walk(self.assets_dir):
            base = os.path.basename(dirpath)
            if base not in ("Resources", "Editor Default Resources"):
                continue
            target_dir = Path(dirpath) / stem.parent
            if not target_dir.is_dir():
                continue
            for fn in os.listdir(target_dir):
                if fn.endswith(".meta"):
                    continue
                if fn == stem.name or Path(fn).stem == stem.name:
                    return (target_dir / fn).resolve()
        self.rep.warn(f"{where}: resource(\"{name}\") not found in any Resources folder")
        return None

    def _inside_assets(self, p: Path, ref: str, where: str) -> Path | None:
        rp = p.resolve()
        try:
            rp.relative_to(self.assets_dir)
        except ValueError:
            self.rep.warn(f"{where}: '{ref}' points outside Assets/ ({rp}); not copied")
            return None
        return rp


# --------------------------------------------------------------------------- PanelSettings / theme
PANEL_SETTINGS_MARKERS = ("UnityEngine.UIElements.PanelSettings", "fileID: 19101, guid: 0000000000000000e000000000000000")


def is_panel_settings(p: Path) -> bool:
    try:
        if p.stat().st_size > 256 * 1024:
            return False
        with open(p, "r", encoding="utf-8", errors="replace") as fh:
            head = fh.read(4096)
    except OSError:
        return False
    return any(m in head for m in PANEL_SETTINGS_MARKERS)


def find_panel_settings(root: Path, uxml: Path) -> Path | None:
    """Nearest PanelSettings asset: the UXML's folder and its parents first, then anywhere in Assets/."""
    assets = (root / "Assets").resolve()
    d = uxml.resolve().parent
    while True:
        candidates = [p for p in sorted(d.glob("*.asset")) if is_panel_settings(p)]
        if candidates:
            named = [p for p in candidates if p.name == "PanelSettings.asset"]
            return (named or candidates)[0]
        if d == assets or d.parent == d:
            break
        d = d.parent
    found: list[Path] = []
    for dirpath, _dirs, filenames in os.walk(assets):
        for fn in filenames:
            if fn.endswith(".asset") and is_panel_settings(Path(dirpath) / fn):
                found.append(Path(dirpath) / fn)
    if not found:
        return None
    found.sort(key=lambda p: (p.name != "PanelSettings.asset", str(p)))
    return found[0]


def panel_settings_theme(ps: Path, guids: GuidIndex) -> Path | None:
    try:
        text = ps.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return None
    m = re.search(r"themeUss:\s*\{[^}]*guid:\s*([0-9a-f]{32})", text)
    if not m:
        return None
    p = guids.lookup(m.group(1))
    return p if p is not None and p.is_file() else None


def find_any_theme(root: Path) -> Path | None:
    preferred = root / DEFAULT_THEME_FALLBACK
    if preferred.is_file():
        return preferred
    for dirpath, _dirs, filenames in os.walk(root / "Assets"):
        for fn in sorted(filenames):
            if fn.endswith(".tss"):
                return Path(dirpath) / fn
    return None


# --------------------------------------------------------------------------- fixtures
def validate_fixture(fx: object, where: str, rep: Reporter) -> None:
    """Structural errors raise SetupError; unknown keys become warnings (Unity re-checks too)."""
    if not isinstance(fx, dict):
        raise SetupError(f"{where}: fixture must be a JSON object with an \"ops\" array")
    for key in fx:
        if key != "ops" and not is_comment_key(key):
            rep.warn(f"{where}: unknown top-level key '{key}' (expected \"ops\")")
    ops = fx.get("ops", [])
    if not isinstance(ops, list):
        raise SetupError(f"{where}: \"ops\" must be an array")
    for i, op in enumerate(ops):
        w = f"{where} ops[{i}]"
        if not isinstance(op, dict):
            raise SetupError(f"{w}: each op must be an object")
        if "name" not in op and "withClass" not in op:
            raise SetupError(f"{w}: needs \"name\" (or \"withClass\")")
        _validate_props(op, FIXTURE_OP_KEYS, w, rep)


def _validate_props(d: dict, allowed: set[str], where: str, rep: Reporter) -> None:
    for key in d:
        if key not in allowed and not is_comment_key(key):
            rep.warn(f"{where}: unknown key '{key}' ignored")
    style = d.get("style")
    if style is not None:
        if not isinstance(style, dict):
            raise SetupError(f"{where}: \"style\" must be an object")
        for key in style:
            if key.replace("-", "").replace("_", "").lower() not in FIXTURE_STYLE_KEYS:
                rep.warn(f"{where}: style property '{key}' is not supported by fixtures")
    if "props" in d and not isinstance(d["props"], dict):
        raise SetupError(f"{where}: \"props\" must be an object")
    for key in ("append", "children"):
        if key not in d:
            continue
        items = d[key]
        if not isinstance(items, list):
            raise SetupError(f"{where}: \"{key}\" must be an array")
        for j, item in enumerate(items):
            if isinstance(item, str):
                continue
            if not isinstance(item, dict):
                raise SetupError(f"{where}.{key}[{j}]: must be an object or a string")
            _validate_props(item, FIXTURE_SPEC_KEYS, f"{where}.{key}[{j}]", rep)


def is_comment_key(key: str) -> bool:
    return key.startswith("_") or key in ("comment", "$schema")


def load_json_file(path: Path, what: str) -> object:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except OSError as e:
        raise SetupError(f"cannot read {what} {path}: {e}") from e
    except ValueError as e:
        raise SetupError(f"{what} {path} is not valid JSON: {e}") from e


# --------------------------------------------------------------------------- shot specs
SHOT_KEYS = {"uxml", "out", "width", "height", "background", "backgroundFit", "fixture", "theme",
             "panelSettings", "frames", "label"}
SHOT_KEY_ALIASES = {"background_fit": "backgroundFit", "panel_settings": "panelSettings", "bg": "background"}


def normalise_shot_keys(d: dict, where: str, rep: Reporter) -> dict:
    out = {}
    for k, v in d.items():
        k2 = SHOT_KEY_ALIASES.get(k, k)
        if k2 not in SHOT_KEYS and not is_comment_key(k2):
            rep.warn(f"{where}: unknown key '{k}' ignored")
            continue
        out[k2] = v
    return out


def resolve_existing(raw: str, bases: list[Path]) -> Path | None:
    p = Path(raw)
    if p.is_absolute():
        return p.resolve() if p.exists() else None
    for b in bases:
        c = b / p
        if c.exists():
            return c.resolve()
    return None


def build_shots(args: argparse.Namespace, rep: Reporter) -> tuple[list[dict], Path]:
    cwd = Path.cwd()
    cli = {
        "uxml": args.uxml, "out": args.out, "width": args.width, "height": args.height,
        "background": args.background, "backgroundFit": args.background_fit, "fixture": args.fixture,
        "theme": args.theme, "panelSettings": args.panel_settings, "frames": args.frames,
    }
    cli = {k: v for k, v in cli.items() if v is not None}
    raw_shots: list[tuple[dict, Path]] = []  # (spec, base dir for its relative paths)
    if args.batch:
        batch_path = Path(args.batch).resolve()
        data = load_json_file(batch_path, "batch file")
        if isinstance(data, list):
            defaults, shots = {}, data
        elif isinstance(data, dict):
            defaults = normalise_shot_keys(data.get("defaults", {}) or {}, "batch defaults", rep)
            shots = data.get("shots")
            for k in data:
                if k not in ("defaults", "shots") and not is_comment_key(k):
                    rep.warn(f"batch file: unknown top-level key '{k}' ignored")
        else:
            raise SetupError("batch file must be a JSON object {\"defaults\": {...}, \"shots\": [...]} or an array")
        if not isinstance(shots, list) or not shots:
            raise SetupError("batch file has no \"shots\" array")
        for i, s in enumerate(shots):
            if not isinstance(s, dict):
                raise SetupError(f"batch shots[{i}] must be an object")
            merged = {**cli, **defaults, **normalise_shot_keys(s, f"batch shots[{i}]", rep)}
            raw_shots.append((merged, batch_path.parent))
    else:
        if not args.uxml or not args.out:
            raise SetupError("--uxml and --out are required (or use --batch <file>)")
        raw_shots.append((cli, cwd))

    project_root: Path | None = Path(args.project_root).resolve() if args.project_root else None
    shots: list[dict] = []
    for i, (spec, base) in enumerate(raw_shots):
        where = f"shot {i + 1}" if len(raw_shots) > 1 else "shot"
        if "uxml" not in spec or "out" not in spec:
            raise SetupError(f"{where}: needs \"uxml\" and \"out\"")
        bases = [base, cwd] + ([project_root] if project_root else [])
        uxml = resolve_existing(str(spec["uxml"]), bases)
        if uxml is None or uxml.suffix.lower() != ".uxml":
            raise SetupError(f"{where}: UXML file not found: {spec['uxml']}")
        root = project_root or find_project_root(uxml)
        if root is None:
            raise SetupError(f"{where}: {uxml} is not inside a Unity project (no Assets/ + ProjectSettings/ above it); pass --project-root")
        if project_root is None:
            project_root = root
        elif root.resolve() != project_root.resolve():
            raise SetupError(f"{where}: all shots must come from the same Unity project ({project_root} vs {root})")
        try:
            uxml_rel = asset_rel(uxml, project_root)
        except ValueError as e:
            raise SetupError(f"{where}: {uxml} is not under the project root {project_root}") from e
        if not uxml_rel.startswith("Assets/"):
            raise SetupError(f"{where}: {uxml_rel} must live under Assets/ (package UXML is not supported)")

        out = Path(str(spec["out"]))
        if not out.is_absolute():
            out = (base / out)
        out = out.resolve()
        if out.suffix.lower() != ".png":
            raise SetupError(f"{where}: --out must be a .png path ({out})")

        width = int(spec.get("width", 1920))
        height = int(spec.get("height", 1080))
        if not (16 <= width <= 8192 and 16 <= height <= 8192):
            raise SetupError(f"{where}: width/height must be between 16 and 8192")
        frames = int(spec.get("frames", 8))

        background = parse_background(str(spec.get("background", DEFAULT_BACKGROUND)), bases, where)
        fit = str(spec.get("backgroundFit", "cover")).lower()
        if fit not in ("cover", "contain", "stretch"):
            raise SetupError(f"{where}: background fit must be cover, contain or stretch")
        background["fit"] = fit

        fixture = None
        fixture_label = None
        if spec.get("fixture") is not None:
            fx = spec["fixture"]
            if isinstance(fx, dict):
                fixture, fixture_label = fx, "inline fixture"
            else:
                fx_path = resolve_existing(str(fx), bases)
                if fx_path is None:
                    raise SetupError(f"{where}: fixture file not found: {fx}")
                fixture, fixture_label = load_json_file(fx_path, "fixture"), str(fx_path)
            validate_fixture(fixture, f"{where} fixture", rep)

        theme = None
        if spec.get("theme"):
            theme = resolve_existing(str(spec["theme"]), bases)
            if theme is None or theme.suffix.lower() != ".tss":
                raise SetupError(f"{where}: theme .tss not found: {spec['theme']}")
        panel_settings: Path | str | None = None
        ps_value = spec.get("panelSettings")
        if ps_value and str(ps_value).lower() == "none":
            panel_settings = "none"
        elif ps_value:
            panel_settings = resolve_existing(str(ps_value), bases)
            if panel_settings is None:
                raise SetupError(f"{where}: PanelSettings asset not found: {ps_value}")

        shots.append({
            "label": spec.get("label") or out.name, "uxml": uxml, "uxml_rel": uxml_rel, "out": out,
            "width": width, "height": height, "frames": frames, "background": background,
            "fixture": fixture, "fixture_label": fixture_label, "theme": theme, "panel_settings": panel_settings,
        })
    assert project_root is not None
    return shots, project_root


def parse_background(value: str, bases: list[Path], where: str) -> dict:
    v = value.strip()
    if v.lower() in ("transparent", "none"):
        return {"transparent": True}
    if HEX_COLOR_RE.match(v) or v.lower().startswith("rgb"):
        return {"color": v}
    p = resolve_existing(v, bases)
    if p is not None and p.is_file():
        if p.suffix.lower() not in (".png", ".jpg", ".jpeg"):
            raise SetupError(f"{where}: background image must be .png or .jpg ({p})")
        return {"image": str(p), "color": "#000000"}
    raise SetupError(f"{where}: --background '{value}' is neither #rrggbb, rgb(...), 'transparent' nor an existing image file")


# --------------------------------------------------------------------------- harness
def default_harness(unity_version: str) -> Path:
    base = os.environ.get("LOCALAPPDATA") or str(Path.home() / ".cache")
    return Path(base) / "FratRow" / "uitk-snapshot-harness" / unity_version


def write_if_changed(path: Path, data: bytes) -> bool:
    try:
        if path.read_bytes() == data:
            return False
    except OSError:
        pass
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return True


def pid_alive(pid: int) -> bool:
    if pid <= 0:
        return False
    if os.name == "nt":
        import ctypes
        kernel32 = ctypes.windll.kernel32
        handle = kernel32.OpenProcess(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
        if not handle:
            return False
        code = ctypes.c_ulong()
        ok = kernel32.GetExitCodeProcess(handle, ctypes.byref(code))
        kernel32.CloseHandle(handle)
        return bool(ok) and code.value == 259  # STILL_ACTIVE
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


class HarnessLock:
    def __init__(self, harness: Path):
        self.path = harness / LOCK_FILE

    def __enter__(self) -> "HarnessLock":
        self.path.parent.mkdir(parents=True, exist_ok=True)
        if self.path.exists():
            try:
                pid = int(self.path.read_text().strip() or "0")
            except (OSError, ValueError):
                pid = 0
            if pid and pid != os.getpid() and pid_alive(pid):
                raise SetupError(
                    f"another uitk-snapshot run (pid {pid}) is using the harness {self.path.parent}. "
                    f"Wait for it, pass --harness <other dir>, or delete {self.path} if that process is not a snapshot run.")
        self.path.write_text(str(os.getpid()))
        return self

    def __exit__(self, *exc) -> None:
        try:
            self.path.unlink()
        except OSError:
            pass


def kill_tree(pid: int) -> None:
    if os.name == "nt":
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True)
    else:
        try:
            os.kill(pid, 9)
        except OSError:
            pass


def release_stale_unity(harness: Path, rep: Reporter) -> None:
    """A previous run that was killed can leave Unity holding the harness open; only those processes are killed."""
    lockfile = harness / "Temp" / "UnityLockfile"
    if not lockfile.exists():
        return
    try:
        lockfile.unlink()
        return
    except OSError:
        pass
    if os.name != "nt":
        raise SetupError(f"a Unity process still has the harness open ({lockfile}); close it and retry")
    ps = ("Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | "
          "Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress")
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True, timeout=60).stdout
        procs = json.loads(out) if out.strip() else []
    except (OSError, ValueError, subprocess.TimeoutExpired):
        procs = []
    if isinstance(procs, dict):
        procs = [procs]
    needle = str(harness.resolve()).lower().replace("\\", "/").rstrip("/")
    killed = 0
    for p in procs:
        cmdline = (p.get("CommandLine") or "").lower().replace("\\", "/")
        if needle in cmdline:
            rep.warn(f"killing leftover harness Unity process {p.get('ProcessId')} from an interrupted run")
            kill_tree(int(p["ProcessId"]))
            killed += 1
    if killed:
        time.sleep(2)
    try:
        lockfile.unlink()
    except OSError:
        if not killed:
            raise SetupError(f"{lockfile} is locked by a process that is not a harness Unity; close it and retry")


def prepare_harness(harness: Path, root: Path, files: list[Path], rep: Reporter) -> dict:
    root = root.resolve()
    harness.mkdir(parents=True, exist_ok=True)
    changed_infra = 0
    changed_infra += write_if_changed(harness / "Packages" / "manifest.json",
                                      (json.dumps(HARNESS_MANIFEST, indent=2) + "\n").encode("utf-8"))
    changed_infra += write_if_changed(harness / HARNESS_OWNED / "Editor" / "UitkSnapshotRunner.cs",
                                      RUNNER_SOURCE.read_bytes())
    changed_infra += write_if_changed(harness / HARNESS_OWNED / "DefaultRuntimeTheme.tss",
                                      HARNESS_DEFAULT_THEME.encode("utf-8"))

    state_path = harness / STATE_FILE
    try:
        state = json.loads(state_path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        state = {}
    previous: dict[str, list] = state.get("files", {})

    wanted: dict[str, Path] = {}
    for f in files:
        rel = asset_rel(f, root)
        wanted[rel] = f
        meta = Path(str(f) + ".meta")
        if meta.is_file():
            wanted[rel + ".meta"] = meta
        else:
            rep.warn(f"{rel} has no .meta file; GUID references to it will not resolve in the harness")

    copied = unchanged = removed = 0
    new_state: dict[str, list] = {}
    for rel, src in sorted(wanted.items()):
        if rel.startswith(HARNESS_OWNED + "/") or not rel.startswith("Assets/"):
            continue
        st = src.stat()
        signature = [st.st_size, st.st_mtime_ns]
        dest = harness / rel
        # Compare against what we copied last time (not against dest): Unity may rewrite .meta files.
        if dest.is_file() and previous.get(rel) == signature:
            unchanged += 1
        else:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)
            copied += 1
        new_state[rel] = signature

    for rel in previous:
        if rel in new_state:
            continue
        dest = harness / rel
        try:
            dest.unlink()
            removed += 1
        except FileNotFoundError:
            pass
        except OSError as e:
            rep.warn(f"could not remove stale harness file {dest}: {e}")
    prune_empty_dirs(harness / "Assets", keep=harness / HARNESS_OWNED)

    state_path.write_text(json.dumps({"source": str(root), "files": new_state}, indent=1), encoding="utf-8")
    return {"copied": copied, "unchanged": unchanged, "removed": removed, "infra_changed": changed_infra}


def prune_empty_dirs(assets: Path, keep: Path) -> None:
    keep = keep.resolve()
    for dirpath, _dirnames, _filenames in sorted(os.walk(assets, topdown=False), key=lambda t: -len(t[0])):
        d = Path(dirpath)
        if d.resolve() == assets.resolve() or d.resolve() == keep or keep in d.resolve().parents:
            continue
        try:
            if not any(d.iterdir()):
                d.rmdir()
                meta = Path(str(d) + ".meta")
                if meta.is_file():
                    meta.unlink()
        except OSError:
            pass


# --------------------------------------------------------------------------- Unity run
def run_unity(unity: Path, harness: Path, job_path: Path, log_path: Path, timeout: float, rep: Reporter) -> tuple[int | None, float]:
    cmd = [str(unity), "-batchmode", "-projectPath", str(harness), "-executeMethod", EXECUTE_METHOD,
           "-uitkSnapshotJob", str(job_path), "-logFile", str(log_path), "-silent-crashes"]
    rep.detail("running: " + " ".join(f'"{c}"' if " " in c else c for c in cmd))
    start = time.monotonic()
    creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0
    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=creationflags)
    tail = LogTail(log_path, rep)
    next_heartbeat = start + 30
    try:
        while True:
            try:
                code = proc.wait(timeout=0.5)
                break
            except subprocess.TimeoutExpired:
                pass
            now = time.monotonic()
            tail.poll()
            if now - start > timeout:
                rep.error(f"Unity did not finish within {timeout:.0f}s; killing it (see {log_path})")
                kill_tree(proc.pid)
                try:
                    proc.wait(timeout=30)
                except subprocess.TimeoutExpired:
                    pass
                return None, time.monotonic() - start
            if now >= next_heartbeat:
                rep.info(f"... Unity still running ({now - start:.0f}s)")
                next_heartbeat = now + 30
    except KeyboardInterrupt:
        kill_tree(proc.pid)
        raise
    tail.poll()
    return code, time.monotonic() - start


class LogTail:
    """Follows the Unity log and echoes the runner's '[uitk-snapshot]' lines (with --verbose)."""

    def __init__(self, path: Path, rep: Reporter):
        self.path, self.rep, self.offset, self.partial = path, rep, 0, b""

    def poll(self) -> None:
        try:
            with open(self.path, "rb") as fh:
                fh.seek(self.offset)
                chunk = fh.read()
        except OSError:
            return
        if not chunk:
            return
        self.offset += len(chunk)
        data = self.partial + chunk
        lines = data.split(b"\n")
        self.partial = lines.pop()
        for raw in lines:
            line = raw.decode("utf-8", errors="replace").strip()
            if line.startswith("[uitk-snapshot] "):
                self.rep.detail("unity: " + line[len("[uitk-snapshot] "):])


def scrape_log(text: str) -> list[str]:
    found: list[str] = []
    seen: set[str] = set()
    for line in text.splitlines():
        s = line.strip()
        if not s:
            continue
        low = s.lower()
        hit = False
        if any(m in low for m in LOG_FATAL_MARKERS):
            hit = True
        elif "error cs" in low:
            hit = True
        elif LOG_UI_ASSET_RE.search(s) and LOG_PROBLEM_RE.search(s) and not low.startswith(LOG_NOISE_PREFIXES):
            hit = True
        elif re.match(r"^[\w.]+(Exception|Error)\b[:\s]", s) and not low.startswith(("[uitk-snapshot]",)):
            hit = True
        if hit and s not in seen:
            seen.add(s)
            found.append(s)
    return found[:60]


def png_size(path: Path) -> tuple[int, int] | None:
    try:
        with open(path, "rb") as fh:
            head = fh.read(24)
        if head[:8] != b"\x89PNG\r\n\x1a\n":
            return None
        return struct.unpack(">II", head[16:24])
    except OSError:
        return None


def prune_logs(log_dir: Path) -> None:
    logs = sorted(log_dir.glob("run-*.log"), key=lambda p: p.stat().st_mtime, reverse=True)
    for old in logs[KEEP_LOGS:]:
        try:
            old.unlink()
        except OSError:
            pass


# --------------------------------------------------------------------------- main
def parse_args(argv: list[str]) -> argparse.Namespace:
    ap = argparse.ArgumentParser(
        prog="snapshot.py",
        description="Render a Unity UI Toolkit UXML screen to a PNG headlessly (batch-mode Unity harness).",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="See tools/unity/uitk-snapshot/README.md for the fixture format and batch files.")
    ap.add_argument("--uxml", help="UXML file inside a Unity project's Assets/ (absolute or relative to cwd)")
    ap.add_argument("--out", help="PNG to write (parent folders are created)")
    ap.add_argument("--width", type=int, help="render width in pixels (default 1920)")
    ap.add_argument("--height", type=int, help="render height in pixels (default 1080)")
    ap.add_argument("--background", help=f"#rrggbb[aa], rgb(...), 'transparent', or a PNG/JPG path (default {DEFAULT_BACKGROUND})")
    ap.add_argument("--background-fit", choices=("cover", "contain", "stretch"), help="how a background image is scaled (default cover)")
    ap.add_argument("--fixture", help="fixture JSON that fills in code-driven UI (text, values, rows, visibility)")
    ap.add_argument("--batch", help="JSON file with several shots rendered in one Unity run (see README)")
    ap.add_argument("--project-root", help="Unity project containing the UXML (default: walk up to the folder with Assets/)")
    ap.add_argument("--theme", help="theme .tss (default: the one the project's PanelSettings uses)")
    ap.add_argument("--panel-settings", help="PanelSettings .asset to mirror (default: nearest one to the UXML; 'none' = 1920x1080 ScaleWithScreenSize)")
    ap.add_argument("--harness", help="harness project folder (default %%LOCALAPPDATA%%\\FratRow\\uitk-snapshot-harness\\<unity version>)")
    ap.add_argument("--unity", help="Unity executable (default: the Hub install matching ProjectVersion.txt)")
    ap.add_argument("--frames", type=int, help="frames to wait before capturing (default 8)")
    ap.add_argument("--timeout", type=float, help="seconds before Unity is killed (default 1800 on a cold harness, 600 warm)")
    ap.add_argument("--strict", action="store_true", help="exit 1 when there are warnings (missing assets, unknown fixture names...)")
    ap.add_argument("--dry-run", action="store_true", help="resolve and list the files that would be copied; do not run Unity")
    ap.add_argument("--verbose", "-v", action="store_true", help="print copied files, Unity command line and progress")
    return ap.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    rep = Reporter(args.verbose)
    t0 = time.monotonic()
    try:
        return _main(args, rep, t0)
    except SetupError as e:
        rep.error(str(e))
        return 2


def _main(args: argparse.Namespace, rep: Reporter, t0: float) -> int:
    if not RUNNER_SOURCE.is_file():
        raise SetupError(f"harness runner missing: {RUNNER_SOURCE}")
    shots, root = build_shots(args, rep)
    unity_version = read_unity_version(root)
    color_space = read_color_space(root)
    guids = GuidIndex(root)
    collector = Collector(root, guids, rep)

    job_shots = []
    for shot in shots:
        collector.collect(shot["uxml"], "--uxml")
        # PanelSettings + theme: explicit > nearest PanelSettings (and its theme) > fallback theme.
        ps = shot["panel_settings"]
        if ps is None:
            ps = find_panel_settings(root, shot["uxml"])
            if ps is not None:
                rep.detail(f"using PanelSettings {asset_rel(ps, root)}")
        elif ps == "none":
            ps = None
        theme = shot["theme"]
        if theme is None and ps is not None:
            theme = panel_settings_theme(ps, guids)
            if theme is None:
                rep.warn(f"{asset_rel(ps, root)} has no theme under Assets/; using a fallback theme")
        if theme is None:
            theme = find_any_theme(root)
        if ps is not None:
            collector.collect(ps, "PanelSettings")
        if theme is not None:
            collector.collect(theme, "theme")
            theme_rel = asset_rel(theme, root)
        else:
            rep.warn("no .tss theme found in the project; using Unity's default runtime theme")
            theme_rel = HARNESS_OWNED + "/DefaultRuntimeTheme.tss"
        job_shots.append({
            "label": shot["label"], "uxml": shot["uxml_rel"], "out": str(shot["out"]),
            "width": shot["width"], "height": shot["height"], "frames": shot["frames"],
            "theme": theme_rel, "panelSettings": asset_rel(ps, root) if ps is not None else None,
            "background": shot["background"], "fixture": shot["fixture"],
        })

    for element, source in sorted(collector.custom_elements.items()):
        rep.warn(f"{source}: custom element <{element}> is project C# and cannot be created in the harness; "
                 f"it will be missing (recreate its look with fixture 'append' ops)")

    files = sorted(collector.files)
    if args.verbose or args.dry_run:
        rep.info(f"{len(files)} asset(s) needed:")
        for f in files:
            print(f"    {asset_rel(f, root)}")
    if args.dry_run:
        return 1 if (args.strict and rep.warnings) else 0

    unity = find_unity(unity_version, args.unity)
    harness = Path(args.harness).resolve() if args.harness else default_harness(unity_version)
    game_assets = (root / "Assets").resolve()
    if harness == root.resolve() or game_assets == harness or game_assets in harness.parents:
        raise SetupError(f"--harness {harness} is the game project (or inside its Assets/); choose another folder")

    with HarnessLock(harness):
        release_stale_unity(harness, rep)
        cold = not (harness / "Library").is_dir()
        sync = prepare_harness(harness, root, files, rep)
        rep.info(f"Unity {unity_version}, {'COLD harness (first run imports everything; can take several minutes)' if cold else 'warm harness'}: {harness}")
        rep.info(f"assets: {sync['copied']} copied, {sync['unchanged']} unchanged, {sync['removed']} removed")

        job_dir = harness / "SnapshotJob"
        job_dir.mkdir(parents=True, exist_ok=True)
        job_path = job_dir / "job.json"
        result_path = job_dir / "result.json"
        if result_path.exists():
            result_path.unlink()
        for shot in shots:
            if shot["out"].exists():
                shot["out"].unlink()
            shot["out"].parent.mkdir(parents=True, exist_ok=True)

        reimport = sorted(asset_rel(f, root) for f in files if f.suffix.lower() in UI_TEXT_EXT)
        job = {
            "version": 1,
            "colorSpace": color_space,
            "timeoutSeconds": 90 + 30 * len(job_shots),
            "reimport": reimport,
            "shots": job_shots,
        }
        job_path.write_text(json.dumps(job, indent=1), encoding="utf-8")

        log_dir = harness / "SnapshotLogs"
        log_dir.mkdir(parents=True, exist_ok=True)
        log_path = log_dir / f"run-{datetime.now().strftime('%Y%m%d-%H%M%S')}.log"
        timeout = args.timeout or (1800.0 if cold else 600.0)
        rep.info(f"rendering {len(job_shots)} shot(s) in batch-mode Unity...")
        code, unity_seconds = run_unity(unity, harness, job_path, log_path, timeout, rep)
        prune_logs(log_dir)

    return report(shots, result_path, log_path, code, unity_seconds, t0, args.strict, rep)


def report(shots: list[dict], result_path: Path, log_path: Path, code: int | None, unity_seconds: float,
           t0: float, strict: bool, rep: Reporter) -> int:
    try:
        log_text = log_path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        log_text = ""
    try:
        result = json.loads(result_path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        result = None

    for line in scrape_log(log_text):
        rep.warn("unity log: " + line)

    failed = False
    if result is None:
        rep.error(f"Unity exited with code {code} without running the snapshot job. Log: {log_path}")
        return 3
    for err in result.get("errors", []):
        rep.error(err)
        failed = True
    for line in result.get("console", []):
        rep.warn("unity: " + line)

    shot_results = {r.get("index"): r for r in result.get("shots", []) if isinstance(r, dict)}
    for i, shot in enumerate(shots):
        r = shot_results.get(i)
        label = f"[{i + 1}/{len(shots)}] " if len(shots) > 1 else ""
        if r is None:
            rep.error(f"{label}{shot['out']}: not rendered")
            failed = True
            continue
        for w in r.get("warnings", []):
            rep.warn(f"{label}{w}")
        for line in r.get("console", []):
            rep.warn(f"{label}unity: {line}")
        size = png_size(shot["out"])
        if r.get("ok") and size:
            rep.info(f"OK    {label}{shot['out']} ({size[0]}x{size[1]}, {r.get('elements', '?')} elements)")
        else:
            failed = True
            for err in r.get("errors", []) or ["no PNG written"]:
                rep.error(f"{label}{shot['out']}: {err}")

    total = time.monotonic() - t0
    rep.info(f"done in {total:.1f}s (Unity {unity_seconds:.1f}s). Log: {log_path}")
    if failed or code is None or (code not in (0, 1)):
        return 3 if code is None or code >= 2 else 1
    if strict and rep.warnings:
        rep.error(f"--strict: {len(rep.warnings)} warning(s)")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
