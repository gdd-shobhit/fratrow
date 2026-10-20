"""Offline tests for tripo_gen.py: no network and no API key.

    py -3.12 -m unittest discover tools/tripo/tests

The API is replaced by FakeApi and downloads by a function that writes canned
bytes. The SdkBackend test runs only when the tripo3d SDK is installed. It
checks the adapter against the real SDK, with the SDK's HTTP layer faked.
"""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import tripo_gen as tg  # noqa: E402

TASK_ID = "11111111-2222-3333-4444-555555555555"
NOW = "2026-10-19T12:00:00Z"
PNG = b"\x89PNG\r\n\x1a\n" + b"front-view"
GLB = b"glTF" + b"\x02\x00\x00\x00" + b"mesh"
SECRET = "tsk_do_not_print_me"


def zip_bytes(entries):
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as zf:
        for name, data in entries.items():
            zf.writestr(name, data)
    return buf.getvalue()


class FakeApi:
    """Stands in for SdkBackend. Records calls and returns canned tasks."""

    def __init__(self, task=None, wait_error=None):
        self.calls = []
        self.task = task
        self.wait_error = wait_error

    def upload(self, path):
        self.calls.append(("upload", Path(path).name))
        return f"file_{Path(path).stem}"

    def submit(self, task_type, body):
        self.calls.append(("submit", task_type, body))
        return TASK_ID

    def wait(self, task_id, timeout):
        self.calls.append(("wait", task_id))
        if self.wait_error:
            raise self.wait_error
        return self.task

    def get_task(self, task_id):
        self.calls.append(("get_task", task_id))
        return self.task

    def balance(self):
        return {"balance": 1200.0, "frozen": 50.0}


def success_task(output, credits=50):
    return {"task_id": TASK_ID, "type": "image_to_model", "status": "success", "progress": 100,
            "output": output, "credits_consumed": credits, "input": {"model": "P2-20260801"}}


class Base(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name) / "repo"
        self.root.mkdir()
        self.inbox = Path(tmp.name) / "inbox"
        self.inbox.mkdir()
        self.served = {}        # url -> bytes, or an exception to raise
        self.downloads = []
        self.api = FakeApi()

    def download(self, url, dest):
        self.downloads.append(url)
        data = self.served[url]
        if isinstance(data, Exception):
            raise data
        Path(dest).write_bytes(data)

    def ctx(self, api=None):
        def no_api():
            raise AssertionError("the API must not be used here")
        api = api if api is not None else self.api
        return tg.Context(self.root, backend=(lambda: api) if api else no_api,
                          download=self.download, now=lambda: NOW)

    def make(self, name, data):
        path = self.inbox / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def run_cli(self, argv, api=None):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = tg.main(argv, self.ctx(api))
        return code, out.getvalue(), err.getvalue()

    def asset(self, name="Prop_Keg"):
        return tg.Asset.at(self.root, name)

    def provenance(self, name="Prop_Keg"):
        return json.loads(self.asset(name).provenance.read_text(encoding="utf-8"))


class ProvenanceTests(Base):
    def test_new_file_has_schema_skeleton(self):
        asset = self.asset()
        tg.record(asset, {"id": "t1", "type": "image_to_model", "status": "queued", "outputs": []})
        data = self.provenance()
        self.assertEqual(data["schema"], 1)
        self.assertEqual(data["asset"], "Prop_Keg")
        self.assertEqual(data["generator"], "tripo")
        self.assertEqual(data["disclosure"], tg.DISCLOSURE)
        self.assertIsNone(data["prompt"])
        self.assertEqual([t["id"] for t in data["tasks"]], ["t1"])
        self.assertFalse(asset.provenance.with_name("provenance.json.tmp").exists())

    def test_same_id_merges_and_new_id_appends(self):
        asset = self.asset()
        tg.record(asset, {"id": "t1", "type": "image_to_model", "model_version": "P2-20260801",
                          "params": {"face_limit": 8000}, "created_utc": "2026-10-19T10:00:00Z",
                          "status": "queued", "outputs": []})
        tg.record(asset, {"id": "t1", "status": "success", "outputs": ["raw/a.glb"], "credits": 50,
                          "model_version": None, "created_utc": "2026-10-19T11:00:00Z"})
        tg.record(asset, {"id": "t1", "outputs": ["raw/a.glb", "raw/b.webp"]})
        tg.record(asset, {"id": "t2", "type": "convert_model", "status": "queued", "outputs": []})
        tasks = self.provenance()["tasks"]
        self.assertEqual([t["id"] for t in tasks], ["t1", "t2"])
        t1 = tasks[0]
        self.assertEqual(t1["status"], "success")
        self.assertEqual(t1["model_version"], "P2-20260801")       # None did not overwrite
        self.assertEqual(t1["params"], {"face_limit": 8000})
        self.assertEqual(t1["created_utc"], "2026-10-19T10:00:00Z")  # first sighting kept
        self.assertEqual(t1["outputs"], ["raw/a.glb", "raw/b.webp"])
        self.assertEqual(t1["credits"], 50)

    def test_corrupt_file_is_left_untouched(self):
        asset = self.asset()
        asset.dir.mkdir(parents=True)
        asset.provenance.write_text("{not json", encoding="utf-8")
        with self.assertRaises(tg.ToolError):
            tg.record(asset, {"id": "t1", "status": "queued", "outputs": []})
        self.assertEqual(asset.provenance.read_text(encoding="utf-8"), "{not json")

    def test_wrong_asset_name_is_rejected(self):
        tg.record(self.asset("Prop_Cup"), {"id": "t1", "outputs": []})
        moved = self.asset("Prop_Keg")
        moved.dir.mkdir(parents=True)
        moved.provenance.write_bytes(self.asset("Prop_Cup").provenance.read_bytes())
        with self.assertRaises(tg.ToolError):
            tg.load_provenance(moved)

    def test_bad_asset_names(self):
        for name in ("../escape", "has space", "1Prop", ""):
            with self.assertRaises(tg.ToolError, msg=name):
                tg.Asset.at(self.root, name)


class RefTests(Base):
    def test_copy_and_hash(self):
        src = self.make("keg.PNG", PNG)
        (ref,) = tg.stage_refs(self.asset(), [src])
        self.assertEqual(ref.rel, "ref/keg.png")
        self.assertEqual(ref.sha256, hashlib.sha256(PNG).hexdigest())
        self.assertEqual(ref.path.read_bytes(), PNG)

    def test_identical_file_is_reused_different_file_gets_suffix(self):
        asset = self.asset()
        first = tg.stage_refs(asset, [self.make("keg.png", PNG)])[0]
        again = tg.stage_refs(asset, [self.make("copy/keg.png", PNG)])[0]
        other = tg.stage_refs(asset, [self.make("other/keg.png", b"different")])[0]
        self.assertEqual(again.rel, first.rel)
        self.assertEqual(other.rel, "ref/keg_2.png")
        self.assertEqual(sorted(p.name for p in asset.ref.iterdir()), ["keg.png", "keg_2.png"])

    def test_inputs_recorded_once(self):
        asset = self.asset()
        refs = tg.stage_refs(asset, [self.make("keg.png", PNG)])
        tg.record(asset, {"id": "t1", "outputs": []}, refs=refs)
        tg.record(asset, {"id": "t2", "outputs": []}, refs=refs)
        self.assertEqual(self.provenance()["inputs"],
                         [{"path": "ref/keg.png", "sha256": hashlib.sha256(PNG).hexdigest()}])

    def test_no_write_mode_copies_nothing(self):
        tg.stage_refs(self.asset(), [self.make("keg.png", PNG)], write=False)
        self.assertFalse(self.asset().dir.exists())

    def test_missing_input(self):
        with self.assertRaises(tg.ToolError):
            tg.stage_refs(self.asset(), [self.inbox / "nope.png"])


class DryRunTests(Base):
    def dry(self, argv):
        with mock.patch.dict(os.environ, {tg.KEY_ENV: SECRET}):
            code, out, err = self.run_cli(argv + ["--dry-run"], api=False)
        self.assertEqual(code, 0, err)
        self.assertNotIn(SECRET, out + err)
        self.assertFalse((self.root / "ArtSource").exists(), "dry run wrote files")
        return out

    def body(self, out):
        start = out.index("\n{") + 1
        return json.loads(out[start:out.index("\n}", start) + 2])

    def test_image_request(self):
        out = self.dry(["image", str(self.make("keg.png", PNG)), "--name", "Prop_Keg",
                        "--face-limit", "8000", "--quad", "--texture-quality", "detailed"])
        self.assertIn(f"POST {tg.API_BASE}/v3/files  upload ref/keg.png", out)
        self.assertIn(f"POST {tg.API_BASE}/v3/generation/image-to-model", out)
        self.assertIn("Authorization: Bearer $TRIPO_API_KEY", out)
        self.assertEqual(self.body(out), {
            "model": tg.DEFAULT_MODEL_VERSION, "input": "<upload:ref/keg.png>",
            "texture": True, "pbr": True, "face_limit": 8000, "texture_quality": "detailed", "quad": True})

    def test_multiview_keeps_view_order(self):
        out = self.dry(["multiview", "--front", str(self.make("f.png", PNG)),
                        "--back", str(self.make("b.png", b"back")), "--name", "Char_Bro"])
        self.assertEqual(self.body(out)["inputs"], ["<upload:ref/f.png>", None, "<upload:ref/b.png>", None])

    def test_text_request_and_no_texture_implies_no_pbr(self):
        out = self.dry(["text", "a red plastic party cup", "--name", "Prop_Cup",
                        "--negative-prompt", "blurry", "--no-texture", "--model-version", "v3.1-20260211"])
        self.assertIn("/v3/generation/text-to-model", out)
        self.assertEqual(self.body(out), {"model": "v3.1-20260211", "prompt": "a red plastic party cup",
                                          "negative_prompt": "blurry", "texture": False, "pbr": False})

    def test_extra_fields(self):
        out = self.dry(["text", "cup", "--name", "Prop_Cup", "--extra", "texture_version=v3.5-20260815",
                        "--extra", "auto_size=true"])
        body = self.body(out)
        self.assertEqual(body["texture_version"], "v3.5-20260815")
        self.assertIs(body["auto_size"], True)
        code, _, err = self.run_cli(["text", "cup", "--name", "Prop_Cup", "--dry-run",
                                     "--extra", "model=x"], api=False)
        self.assertEqual(code, 1)
        self.assertIn("already set", err)

    def test_documented_limits(self):
        img = str(self.make("keg.png", PNG))
        for args in (["--model-version", "P1-20260311", "--quad"],
                     ["--face-limit", "30000", "--quad"],
                     ["--model-version", "v2.5-20250123", "--texture-quality", "extreme"]):
            code, _, err = self.run_cli(["image", img, "--name", "Prop_Keg", "--dry-run"] + args, api=False)
            self.assertEqual(code, 1, args)
            self.assertIn("--skip-checks", err)
        out = self.dry(["image", img, "--name", "Prop_Keg", "--model-version", "P1-20260311",
                        "--quad", "--skip-checks"])
        self.assertIn("warning: P1-20260311 does not support quad", out)

    def test_post_process_requests(self):
        out = self.dry(["convert", TASK_ID, "--name", "Prop_Keg", "--format", "fbx", "--face-limit", "5000",
                        "--pivot-to-center-bottom", "--no-bake"])
        self.assertIn("/v3/models/convert", out)
        self.assertEqual(self.body(out), {"input": TASK_ID, "format": "FBX", "face_limit": 5000,
                                          "pivot_to_center_bottom": True, "bake": False})
        out = self.dry(["segment", TASK_ID, "--name", "Prop_Keg", "--granularity", "simple"])
        self.assertEqual(self.body(out), {"input": TASK_ID, "model": tg.SEGMENT_MODEL_VERSION,
                                          "segmentation_granularity": "simple"})
        out = self.dry(["lowpoly", TASK_ID, "--name", "Prop_Keg", "--quad", "--face-limit", "8000", "--smart-uv"])
        self.assertIn("/v3/mesh/decimate", out)
        self.assertEqual(self.body(out), {"input": TASK_ID, "model": tg.LOWPOLY_MODEL_VERSION,
                                          "face_limit": 8000, "quad": True, "smart_uv": True})

    def test_missing_key_message_never_echoes_a_bad_key(self):
        with mock.patch.dict(os.environ, {tg.KEY_ENV: "not-a-key-123"}):
            with self.assertRaises(tg.ToolError) as caught:
                tg.api_key_from_env()
        self.assertNotIn("not-a-key-123", str(caught.exception))
        with mock.patch.dict(os.environ, {}, clear=True):
            with self.assertRaisesRegex(tg.ToolError, "register"):
                tg.api_key_from_env()


class DownloadTests(Base):
    MODEL = "https://cdn.example/out/model.glb?sig=abc"
    PREVIEW = "https://cdn.example/out/preview.webp?sig=abc"

    def test_image_generation_end_to_end(self):
        self.served = {self.MODEL: GLB, self.PREVIEW: b"RIFF....WEBP"}
        self.api.task = success_task({"model_url": self.MODEL, "pbr_model_url": self.MODEL,
                                      "rendered_image_url": self.PREVIEW, "topology": "quad"})
        code, out, err = self.run_cli(["image", str(self.make("keg.png", PNG)), "--name", "Prop_Keg"])
        self.assertEqual(code, 0, err)
        self.assertEqual(self.api.calls[0], ("upload", "keg.png"))
        self.assertEqual(self.api.calls[1][2]["input"], "file_keg")       # the token went on the wire
        self.assertEqual(self.downloads, [self.MODEL, self.PREVIEW])       # duplicate URL fetched once
        raw = self.asset().raw
        self.assertEqual((raw / f"{TASK_ID}_model.glb").read_bytes(), GLB)
        self.assertTrue((raw / f"{TASK_ID}_rendered_image.webp").exists())
        self.assertEqual(list(raw.glob("*.part")), [])
        data = self.provenance()
        (task,) = data["tasks"]
        self.assertEqual(task["id"], TASK_ID)
        self.assertEqual(task["type"], "image_to_model")
        self.assertEqual(task["model_version"], tg.DEFAULT_MODEL_VERSION)
        self.assertEqual(task["params"]["input"], "ref/keg.png")           # provenance names the ref
        self.assertEqual(task["created_utc"], NOW)
        self.assertEqual(task["status"], "success")
        self.assertEqual(task["credits"], 50)
        self.assertEqual(task["outputs"], [f"raw/{TASK_ID}_model.glb", f"raw/{TASK_ID}_rendered_image.webp"])
        self.assertEqual(data["inputs"][0]["path"], "ref/keg.png")

    def test_extensionless_url_is_sniffed_and_zip_unpacked(self):
        bare = "https://cdn.example/out/abc?sig=1"
        packed = "https://cdn.example/out/model.zip"
        self.served = {bare: GLB, packed: zip_bytes({"model.fbx": b"Kaydara FBX Binary", "tex/base.png": PNG})}
        files, failed = tg.download_outputs(success_task({"model_url": bare, "base_model_url": packed}),
                                            self.asset().raw, self.download)
        self.assertEqual(failed, [])
        names = [self.asset().rel(f) for f in files]
        self.assertEqual(names, [f"raw/{TASK_ID}_model.glb", f"raw/{TASK_ID}_base_model.zip",
                                 f"raw/{TASK_ID}_base_model/model.fbx", f"raw/{TASK_ID}_base_model/tex/base.png"])
        self.assertTrue(all(f.is_file() for f in files))

    def test_failed_download_is_reported_not_recorded(self):
        self.served = {self.MODEL: GLB, self.PREVIEW: OSError("403 expired")}
        self.api.task = success_task({"model_url": self.MODEL, "rendered_image_url": self.PREVIEW})
        code, out, _ = self.run_cli(["text", "keg", "--name", "Prop_Keg"])
        self.assertEqual(code, 1)
        self.assertIn("fetch", out)
        self.assertEqual(self.provenance()["tasks"][0]["outputs"], [f"raw/{TASK_ID}_model.glb"])
        self.assertEqual(list(self.asset().raw.glob("*.part")), [])

    def test_task_is_recorded_before_waiting(self):
        self.api.wait_error = tg.ToolError("Tripo API: timed out")
        code, _, err = self.run_cli(["text", "keg", "--name", "Prop_Keg"])
        self.assertEqual(code, 1)
        self.assertIn(f"fetch {TASK_ID}", err)
        task = self.provenance()["tasks"][0]
        self.assertEqual((task["status"], task["params"]["prompt"]), ("queued", "keg"))
        self.assertEqual(self.provenance()["prompt"], "keg")

    def test_failed_task(self):
        self.api.task = {"task_id": TASK_ID, "status": "failed", "error_msg": "bad image", "output": {}}
        code, out, _ = self.run_cli(["text", "keg", "--name", "Prop_Keg"])
        self.assertEqual(code, 1)
        self.assertIn("bad image", out)
        self.assertEqual(self.provenance()["tasks"][0]["status"], "failed")

    def test_fetch_finds_the_asset_and_adopts_unknown_tasks(self):
        tg.record(self.asset(), {"id": TASK_ID, "type": "image_to_model", "status": "queued", "outputs": []})
        self.served = {self.MODEL: GLB}
        self.api.task = success_task({"model_url": self.MODEL})
        code, _, err = self.run_cli(["fetch", TASK_ID])
        self.assertEqual(code, 0, err)
        self.assertEqual(self.provenance()["tasks"][0]["outputs"], [f"raw/{TASK_ID}_model.glb"])

        code, _, err = self.run_cli(["fetch", TASK_ID, "--name", "Prop_Other"])
        self.assertEqual(code, 0, err)
        adopted = self.provenance("Prop_Other")["tasks"][0]
        self.assertEqual((adopted["type"], adopted["model_version"]), ("image_to_model", "P2-20260801"))

        code, _, err = self.run_cli(["fetch", "99999999-0000-0000-0000-000000000000"])
        self.assertEqual(code, 1)
        self.assertIn("--name", err)

    def test_convert_goes_to_the_source_tasks_asset(self):
        tg.record(self.asset(), {"id": "source-task", "type": "image_to_model", "status": "success", "outputs": []})
        fbx = "https://cdn.example/out/m.fbx"
        self.served = {fbx: b"Kaydara FBX Binary"}
        self.api.task = success_task({"model_url": fbx}, credits=None)
        code, _, err = self.run_cli(["convert", "source-task", "--format", "FBX"])
        self.assertEqual(code, 0, err)
        tasks = self.provenance()["tasks"]
        self.assertEqual([t["type"] for t in tasks], ["image_to_model", "convert_model"])
        self.assertNotIn("credits", tasks[1])
        self.assertIsNone(tasks[1]["model_version"])

    def test_zip_slip_is_refused(self):
        evil = self.make("evil.zip", zip_bytes({"../../outside.txt": b"x"}))
        with self.assertRaises(tg.ToolError):
            tg.extract_zip(evil, self.asset().raw / "x")
        self.assertFalse((self.root / "outside.txt").exists())


class RegisterTests(Base):
    def test_register_glb_without_api_key(self):
        glb = self.make("Prop_Keg.glb", GLB)
        ref = self.make("keg.png", PNG)
        with mock.patch.dict(os.environ, {}, clear=True):
            code, out, err = self.run_cli(
                ["register", "--name", "Prop_Keg", "--file", str(glb), "--ref", str(ref),
                 "--prompt", "a beer keg", "--model-version", "P2.0",
                 "--studio-url", "https://studio.tripo3d.ai/x"], api=False)
        self.assertEqual(code, 0, err)
        task_id = "studio-" + hashlib.sha256(GLB).hexdigest()[:12]
        data = self.provenance()
        (task,) = data["tasks"]
        self.assertEqual(task["id"], task_id)
        self.assertEqual(task["type"], "studio_manual")
        self.assertEqual(task["model_version"], "P2.0")
        self.assertEqual(task["status"], "success")
        self.assertEqual(task["outputs"], [f"raw/{task_id}_Prop_Keg.glb"])
        self.assertEqual(task["params"]["studio_url"], "https://studio.tripo3d.ai/x")
        self.assertEqual(task["params"]["refs"], ["ref/keg.png"])
        self.assertEqual(data["prompt"], "a beer keg")
        self.assertEqual(data["inputs"], [{"path": "ref/keg.png", "sha256": hashlib.sha256(PNG).hexdigest()}])
        self.assertEqual((self.asset().raw / f"{task_id}_Prop_Keg.glb").read_bytes(), GLB)

    def test_register_zip_unpacks_into_raw_and_is_idempotent(self):
        pack = self.make("Prop_Keg_fbx.zip", zip_bytes({"Prop_Keg.fbx": b"Kaydara FBX Binary",
                                                        "textures/base.png": PNG}))
        for _ in range(2):
            code, _, err = self.run_cli(["register", "--name", "Prop_Keg", "--file", str(pack)], api=False)
            self.assertEqual(code, 0, err)
        (task,) = self.provenance()["tasks"]
        folder = f"raw/{task['id']}_Prop_Keg_fbx"
        self.assertEqual(task["outputs"], [f"{folder}/Prop_Keg.fbx", f"{folder}/textures/base.png"])
        self.assertTrue((self.asset().dir / folder / "textures" / "base.png").is_file())

    def test_register_dry_run_and_bad_input(self):
        glb = self.make("Prop_Keg.glb", GLB)
        code, out, _ = self.run_cli(["register", "--name", "Prop_Keg", "--file", str(glb), "--dry-run"], api=False)
        self.assertEqual(code, 0)
        self.assertIn("studio_manual", out)
        self.assertFalse((self.root / "ArtSource").exists())
        code, _, err = self.run_cli(["register", "--name", "Prop_Keg", "--file", str(self.make("a.blend", b"x"))],
                                    api=False)
        self.assertEqual(code, 1)
        self.assertIn(".glb", err)


@unittest.skipUnless(importlib.util.find_spec("tripo3d"), "tripo3d SDK not installed")
class SdkAdapterTests(unittest.TestCase):
    """SdkBackend against the real SDK, with its HTTP layer (a private attribute) faked."""

    def test_submit_routes_and_task_payload(self):
        sent = []

        class FakeHttp:
            async def request(self, method, path, *, params=None, json_data=None):
                sent.append((method, path, json_data))
                if method == "POST":
                    return {"task_id": TASK_ID}
                return {"task_id": TASK_ID, "type": "image_to_model", "status": "success",
                        "output": {"model_url": "https://cdn.example/m.glb"}, "credits_consumed": 40}

            async def close(self):
                pass

        backend = tg.SdkBackend("tsk_test_key")
        backend._client._http = FakeHttp()
        body = {"model": tg.DEFAULT_MODEL_VERSION, "input": "file_x", "texture": True, "pbr": True}
        for task_type, route in tg.ROUTES.items():
            self.assertEqual(backend.submit(task_type, body), TASK_ID)
            self.assertEqual(sent[-1], ("POST", route, body))
        task = backend.get_task(TASK_ID)
        self.assertEqual(sent[-1][:2], ("GET", f"/v3/tasks/{TASK_ID}"))
        self.assertEqual(task["credits_consumed"], 40)
        self.assertEqual(tg.output_urls(task["output"]), [("model", "https://cdn.example/m.glb")])


if __name__ == "__main__":
    unittest.main()
