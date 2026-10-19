# tools/unity — driving the Unity editor from scripts and Claude

Small, dependency-free Python 3 helpers for checking Unity work without
clicking through the editor. The project is **Unity 6000.5.6f1 (URP)**; the
Unity project root is the repository root (the folder holding `Assets/` and
`ProjectSettings/`).

| Script | Use when | Does |
|---|---|---|
| `mcpcall.py` | the editor **is open** on this project | MCP client for the MCP for Unity server: list tools, read resources, call tools, run C# snippets, refresh + compile |
| `wait_not_playing.py` | the editor is open but may be in Play mode | blocks until edit mode and `ready_for_tools` |
| `wait_port.py` | you just launched the editor (or Blender) | blocks until its MCP port accepts connections |
| `unity_batch.py` | the editor **is closed** | runs Unity in batch mode: `-executeMethod` or EditMode/PlayMode tests, prints compile errors and an NUnit summary |

Run them from anywhere with the literal path, e.g.
`python D:\fratrow\tools\unity\mcpcall.py list`. They work under `python -I`.

## Which route: MCP or batch?

1. **Find out which checkout the editor has open.** A git worktree is a separate
   Unity project with its own `Library/`. If the editor is open on
   `D:\fratrow` and you are editing `D:\fratrow\.claude\worktrees\<name>`, MCP
   checks see the *other* tree's files. Ask MCP:
   write `return Application.dataPath;` to a scratch `.cs` file outside
   `Assets/` and run `mcpcall.py code <file>`.
2. **Is `<project>\Temp\UnityLockfile` present?** Then that project is probably
   open: use MCP (`unity_batch.py` refuses while an editor holds the lock; a
   stale file left by a crash does not block it). Absent: use `unity_batch.py`.
3. **A worktree with no `Library/`** imports the whole project on its first batch
   run. That can take many minutes; let it finish once rather than killing it.

## mcpcall.py

Talks JSON-RPC over streamable HTTP to `http://127.0.0.1:8080/mcp` (override
with `UNITY_MCP_URL`). The session id is cached in
`%TEMP%\unity_mcp_session_8080.txt` (override with `UNITY_MCP_SESSION_FILE`)
and renewed automatically when the server restarts.

```text
python tools/unity/mcpcall.py list                          # what the server offers
python tools/unity/mcpcall.py schema read_console           # one tool's arguments
python tools/unity/mcpcall.py read mcpforunity://editor/state
python tools/unity/mcpcall.py compile                       # refresh, wait for a new compile, print errors
python tools/unity/mcpcall.py call read_console "{\"types\": [\"error\"], \"count\": 20}"
python tools/unity/mcpcall.py call read_console @args.json  # JSON from a file: no shell quoting
python tools/unity/mcpcall.py code C:\Users\me\AppData\Local\Temp\probe.cs
```

`code` sends the file to the server's `execute_code` tool. The file is a method
body that returns a value; `UnityEngine`/`UnityEditor` names resolve
unqualified, anything else needs its namespace:

```csharp
var item = AssetDatabase.LoadAssetAtPath<FratRow.Characters.CosmeticItem>(
    "Assets/Art/Characters/Female/Cosmetics/SKM_F_Top_CamiCrop.asset");
if (item == null) return "MISSING";
int hidden = 0;
foreach (bool h in item.HiddenMask()) if (h) hidden++;
return $"{item.id} slot={item.slot} kind={item.kind} hides={hidden} topology={item.bodyTopologyHash}";
```

Menu items run the same way: `EditorApplication.ExecuteMenuItem("Tools/FratRow/Characters/Run Dressed Check (M3)"); return "ok";`.

`compile` exits 1 on timeout (400 s). The console errors it prints are the most
recent ones, which can predate this compile: read them, do not just count them.

## wait_not_playing.py / wait_port.py

```text
python tools/unity/wait_not_playing.py                  # default: up to 25 min
python tools/unity/wait_not_playing.py --timeout 300
python tools/unity/wait_port.py 8080 180                # after launching the editor
```

Both exit 0 when ready and 1 on timeout. `wait_not_playing` prints the last
state it saw, so "server unreachable" and "still in Play mode" are not
confused. For long waits, start them with the Bash tool's `run_in_background`.

## unity_batch.py

Runs `Unity.exe` in batch mode for a project. The editor is located from
`ProjectSettings/ProjectVersion.txt` under
`C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe`; it refuses to
run while another editor holds `Temp/UnityLockfile` (a stale lockfile left by a
crash does not block it).

```text
python tools/unity/unity_batch.py --project D:\fratrow --import-only     # open, import, compile, quit
python tools/unity/unity_batch.py --project D:\fratrow --tests EditMode
python tools/unity/unity_batch.py --project D:\fratrow --execute FratRow.Art.Characters.Editor.CharacterTrioCheck.RunBatch
```

`--execute` takes a public static method with no parameters
(`Namespace.Class.Method`); it should end with `EditorApplication.Exit(status)`
(otherwise `-quit` ends the run). The log goes to `<project>\Logs\batch-*.log`
(`--log` to override) and the script prints its error lines plus the last
tagged messages (`[CosmeticSidecar] ...`, `[TrioCheck] ...`) — which is how
`--import-only` shows what an import did. `--editor` / `UNITY_EDITOR` override
the editor path; `--timeout` defaults to 3600 s.

Exit code: the editor's own, `3` when compile errors were logged, `4` when
tests failed, `5` when no tests ran, `124` on timeout. **A test run that
reports `total=0` is not a pass** — the test assembly did not compile or was
not found; the script exits `5` and says so on the `--- tests:` line.

Character entry points (Assets/Art/Characters/Editor):

| Method / menu item | Does |
|---|---|
| `FratRow.Art.Characters.Editor.CharacterTrioCheck.RunBatch` | Re-imports every sidecar, rebuilds every recipe asset and `Female/Prefabs/CH_*.prefab`, writes walk filmstrips, `unity_trio_<view>.png` and `unity_<id>_face_<view>.png` to `production/qa/evidence/m4-trio/`; exits 1 on any failure |
| `FratRow.Art.Characters.Editor.CharacterHumanoidCheck.RunDressed` | M3 dressed walk filmstrips (crop top + hoops, Base and Curvy) to `.../m3-cosmetic-pipeline/` |
| Tools → FratRow → Characters → Rebuild Recipes and Prefabs / Run Trio Check (M4) / Run Dressed Check (M3) / Run Humanoid Check (M2) | the same from the open editor (`EditorApplication.ExecuteMenuItem` through `mcpcall.py code`) |

## Gotchas

- **Play mode.** The editor is often left in Play mode. Run
  `wait_not_playing.py` before reimports, `compile`, or any MCP check.
- **`Object.GetInstanceID()` is a compile error in Unity 6.5.** Use object
  references or the objects themselves as dictionary keys.
- **Preview scenes** (`EditorSceneManager.NewPreviewScene`): a manually rendered
  camera does not re-skin `SkinnedMeshRenderer`s — bake each frame
  (`SkinnedMeshRenderer.BakeMesh`) into a static proxy; a manually evaluated
  `PlayableGraph` does not move bones — sample with `AnimationMode`; the open
  scene's global fog leaks into preview renders — wrap the render in
  `Unsupported.SetRenderSettingsUseFogNoDirty(false)` and restore it.
  `Assets/Art/Characters/Editor/CharacterHumanoidCheck.cs` does all three.
- **Scratch `.cs` files** for `mcpcall.py code` go outside `Assets/` (system
  temp is fine), or Unity compiles them into the project.
- **No BOMs.** Windows PowerShell 5.1 `Set-Content -Encoding utf8` writes a
  UTF-8 BOM; Python's `json.load` rejects a BOM'd recipe. Write source and JSON
  with the Write/Edit tools.
- **Worktree-isolated Claude sessions:** Bash commands that compute paths at
  runtime (variables, `$(...)`, pipes into git/python/sed) can be refused by the
  harness. Use literal paths, plain separate commands, and `git -C <literal path>`.
  `cd` in Bash changes the session's working directory for later commands.
- **Git LFS is disabled** for this repository (push answers 403). Commit FBX,
  PNG and `.blend` files as normal git files; never add `filter=lfs` rules.
