---
name: uitk-snapshot
description: "Render a UI Toolkit screen (UXML + USS) to a PNG headlessly, for UI screenshot evidence when the Unity editor or MCP bridge is unavailable or you are in a worktree without Library/."
argument-hint: "<path to .uxml> [--fixture file.json] [--out file.png]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash, Write
---

# UI Toolkit Snapshot

Renders a UXML screen to a PNG through a cached batch-mode Unity harness project. The tool is
`tools/unity/uitk-snapshot/snapshot.py`. Its README (same folder) documents every option and
the fixture format.

## When to use it

- A UI story needs a retained screenshot in `production/qa/evidence/` (coding standards:
  UI rows are BLOCKING), and you cannot take one in the editor. Typical reasons: the editor is
  open on another branch, the Unity MCP bridge is down, or you are in a git worktree without
  `Library/`.
- You changed UXML/USS and want to see the result before handing it over.

It shows the screen's layout and styling only. It does not run game code, so it is not a
playtest. Say that in the evidence note.

## Steps

1. **Dry run.** Run `python tools/unity/uitk-snapshot/snapshot.py --uxml <uxml> --out <png> --dry-run`.
   Check that the listed assets are the ones the screen needs. Fix any `WARN` about missing
   assets before rendering.
2. **Write a fixture** for anything the screen builds in C# (lists, bound values, host/client
   variants). Write one fixture per state worth showing. Use the real USS class names, so the
   render matches what the code builds.
3. **Background.** If the screen sits over a 3D scene, pass `--background` with an image that
   marks where world content sits on screen. Without one, the capture cannot show whether
   panels cover the scene.
4. **Render.** Use `--batch` for several states in one Unity run. The first run on a machine
   imports the harness and takes a few minutes. Run it in the background.
5. **Read every PNG** before claiming anything about it. Exit code 0 only means a PNG was
   written. Check the image for overlap, clipped text, missing fonts or images, and unreadable
   contrast.
6. **Keep the evidence.** Copy the final PNGs to `production/qa/evidence/<screen>-<state>-<date>.png`.

## Reporting

State what was rendered, with which fixtures, and the tool's exit code. If the tool could not
run (exit 2 or 3: no matching Unity install, licence, timeout), report the screenshot as
**NOT CAPTURED**, with the log path the tool printed. Never describe a screen you did not see.
