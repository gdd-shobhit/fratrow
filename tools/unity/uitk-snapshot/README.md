# uitk-snapshot

Render a UI Toolkit screen (UXML + its USS, fonts, images and theme) to a PNG without opening
the game project in the editor. Use it for UI evidence (`production/qa/evidence/`) when the
editor is busy, the Unity MCP bridge is down, or you are working in a git worktree that has
no `Library/`.

It copies the UXML and everything it references (with their `.meta` files, so GUID references
resolve) into a small cached **harness** Unity project. It then runs batch-mode Unity there
with the GPU (not `-nographics`): it enters play mode, renders the screen into a RenderTexture
through a PanelSettings that mirrors the project's own, and writes the PNG.

## Usage

From the repo root (or a worktree root):

```sh
python tools/unity/uitk-snapshot/snapshot.py \
    --uxml "Assets/Scripts/UI/UI Toolkit/LobbyVisualTree.uxml" \
    --out production/qa/evidence/lobby.png \
    --fixture path/to/lobby.fixture.json \
    --background path/to/backdrop.png
```

Several shots in one Unity run (faster than one run per shot):

```sh
python tools/unity/uitk-snapshot/snapshot.py --batch shots.json
```

```json
{
  "defaults": {"uxml": "Assets/Scripts/UI/UI Toolkit/LobbyVisualTree.uxml", "background": "#3a3f4b"},
  "shots": [
    {"label": "host", "fixture": "host.json", "out": "lobby-host.png"},
    {"label": "client", "fixture": "client.json", "out": "lobby-client.png"}
  ]
}
```

Relative paths in a batch file resolve against the batch file's folder first, then the cwd.

| Option | Default | Meaning |
|---|---|---|
| `--uxml` | required | UXML under a Unity project's `Assets/` |
| `--out` | required | PNG to write |
| `--width` / `--height` | 1920 / 1080 | Render size |
| `--background` | `#3a3f4b` | `#rrggbb`, `rgb(...)`, `transparent`, or a PNG/JPG path |
| `--background-fit` | `cover` | `cover`, `contain` or `stretch` for an image background |
| `--fixture` | none | JSON that fills in code-driven UI (see below) |
| `--batch` | none | Several shots in one run |
| `--panel-settings` | nearest to the UXML | PanelSettings to mirror (`none` = ScaleWithScreenSize 1920x1080) |
| `--theme` | the PanelSettings theme | `.tss` theme |
| `--harness` | `%LOCALAPPDATA%\FratRow\uitk-snapshot-harness\<unity version>` | Harness project folder |
| `--unity` | Hub install matching `ProjectVersion.txt` | Unity executable |
| `--frames` | 8 | Frames to wait before capturing |
| `--timeout` | 1800 s cold, 600 s warm | Unity is killed after this |
| `--dry-run` | | List the files that would be copied, then stop |
| `--strict` | | Exit 1 on warnings (missing assets, unknown fixture names) |
| `--verbose` | | Print copied files, the Unity command line and progress |

Exit codes: `0` every shot rendered, `1` a shot failed (or `--strict` with warnings), `2` bad
arguments or setup, `3` Unity crashed, timed out or never ran the job.

## Fixtures

UI built in C# at runtime (player lists, values bound from network state) is empty in a plain
render. A fixture recreates it:

```json
{
  "ops": [
    {"name": "SessionCode", "text": "K7QX2P"},
    {"name": "FratNameField", "value": "Alpha Beta Gamma"},
    {"name": "FratNameLabel", "display": "flex", "text": "Alpha Beta Gamma"},
    {"name": "StartGameButton", "enabled": false},
    {"name": "PopularityFill", "style": {"width": "70%"}},
    {"name": "ReadyButton", "addClass": ["lobby-button--ready-on"]},
    {"name": "PlayerList", "clear": true, "append": [
      {"type": "VisualElement", "class": ["lobby-player-row"], "children": [
        {"type": "Label", "class": ["lobby-player-name"], "text": "Jordan"},
        {"type": "Label", "class": ["hud-chip", "hud-chip--good"], "text": "READY"}
      ]}
    ]}
  ]
}
```

Each op targets `root.Q(name)` (or `withClass`; `"all": true` targets every match) and applies:

| Key | Effect |
|---|---|
| `text` | Label/Button text (or the value of a text field) |
| `value` | `SetValueWithoutNotify` on TextField, DropdownField, Toggle, Slider... |
| `placeholder`, `tooltip`, `label`, `choices` | The matching property |
| `enabled` | `SetEnabled` |
| `display` | `flex` / `none` |
| `visible` | Visibility |
| `style` | Inline styles: sizes (`"62%"`, `"40px"`), margins, padding, colours, flex, text align... |
| `class` / `addClass`, `removeClass`, `toggleClass` | USS classes |
| `clear`, `remove` | Empty the element, or remove it |
| `append` / `children` | New child elements: `{"type": "Label", "class": [...], "text": "...", "children": [...]}` |
| `props` | Any other public property, via reflection |

Unknown element names and keys are reported as warnings.

## Known limits

- **Project C# is not compiled into the harness.** Custom `VisualElement` subclasses in the
  UXML are missing (a warning names them). Rebuild their look with `append`.
- **Popups are not rendered.** DropdownField menus, tooltips and other overlays only appear
  on interaction.
- **Assets from `Packages/` are not copied.** A warning names them.
- **Fonts fall back to the theme's default** unless the UXML or USS references the font asset.
- The **first run imports a fresh harness project** and takes a few minutes. Later runs only
  re-copy the files that changed.
- **One run at a time per harness.** A lock file guards it. Pass `--harness` to run in parallel.
- The background is a flat colour or image, not the 3D scene. To check layout against world
  content, pass a backdrop image that marks where that content sits on screen.
