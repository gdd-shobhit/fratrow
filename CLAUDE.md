# Claude Code Game Studios -- Game Studio Agent Architecture

Indie game development managed through 49 coordinated Claude Code subagents.
Each agent owns a specific domain, enforcing separation of concerns and quality.

## Technology Stack

- **Engine**: [CHOOSE: Godot 4 / Unity / Unreal Engine 5]
- **Language**: [CHOOSE: GDScript / C# / C++ / Blueprint]
- **Version Control**: Git with trunk-based development
- **Build System**: [SPECIFY after choosing engine]
- **Asset Pipeline**: [SPECIFY after choosing engine]

> **Note**: Engine-specialist agents exist for Godot, Unity, and Unreal with
> dedicated sub-specialists. Use the set matching your engine.

## Project Structure

@.claude/docs/directory-structure.md

## Engine Version Reference

<!-- ENGINE-REFERENCE-IMPORT: the line below is engine-specific. /setup-engine
     rewrites it to @docs/engine-reference/<engine>/VERSION.md for the chosen
     engine, so a Unity or Unreal project stops loading the Godot reference every
     session. It defaults to Godot (the template's example engine); skills that
     need the pinned version read docs/engine-reference/<engine>/VERSION.md on
     demand regardless of this import. -->
@docs/engine-reference/godot/VERSION.md


## Technical Preferences

`project.yaml` at the repo root is the primary config store — engine, specialists,
naming, platform, performance, modes. Skills resolve it via `resolve_config`
(see `.claude/docs/config-resolution.md`).

`.claude/docs/technical-preferences.md` is the **legacy fallback**, read on demand
when a key is absent from `project.yaml`. It is no longer imported here: before
`/setup-engine` runs it is almost entirely `[TO BE CONFIGURED]` placeholders, and
after it runs `project.yaml` holds the real values.

## Coordination Rules

@.claude/docs/coordination-rules.md

## Collaboration Protocol

**User-driven collaboration, not autonomous execution.**
Every task follows: **Question -> Options -> Decision -> Draft -> Approval**

- Agents MUST show drafts or summaries before requesting approval
- Multi-file changes require explicit approval for the full changeset
- A skipped or dismissed question is not an answer: act on nothing, re-ask in
  plain text and wait
- Started outside this file's folder? Warn first: hooks, deny rules and the
  ask-first mode load only when Claude Code starts here

See `docs/COLLABORATIVE-DESIGN-PRINCIPLE.md` for full protocol and examples.

> **First session?** If the project has no engine configured and no game concept,
> run `/start` to begin the guided onboarding flow.

## Coding Standards

@.claude/docs/coding-standards.md

## Unity Editor Automation — `unity-mcp` CLI, never the MCP tools

Drive the Unity Editor with the **`unity-mcp` command-line tool** (Bash or
PowerShell), every time. Do not call `mcp__UnityMCP__*` tools — they are denied
in `.claude/settings.json` — and this rule overrides the `unity-mcp-skill`
skill. The CLI talks to the same MCP for Unity server and reaches every tool the
MCP exposes, without the MCP round-trip.

- **Installed globally** as a uv tool (`~/.local/bin/unity-mcp`, on the user
  PATH; a shell opened before the install may need that full path). It is pinned
  to `mcpforunityserver==10.3.0` to match `com.coplaydev.unity-mcp#v10.3.0` in
  `Packages/manifest.json` — when that tag changes, run
  `uv tool install --force "mcpforunityserver==<new version>"`.
- **Check first**: `unity-mcp status`. It needs the Editor open on this project
  and the server listening on `127.0.0.1:8080`. Editor open but no server → start
  it in the background with `mcp-for-unity --transport http --http-url http://127.0.0.1:8080`
  (also installed globally) and wait for the Editor bridge to connect
  (`unity-mcp instances`).
- **Discover, don't guess**: `unity-mcp --help`, `unity-mcp <group> --help`
  (`gameobject`, `component`, `scene`, `prefab`, `asset`, `material`, `script`,
  `code execute` for C#, `editor` for play mode and console, `build`, `profiler`,
  `ui`, …). Add `-f json` for machine-readable output.
- **Any MCP tool by its own name**: `unity-mcp raw <tool_name> '<json params>'`,
  e.g. `unity-mcp raw read_console '{"count": 20}'` — same names and parameters
  as the MCP schema. Many steps at once: `unity-mcp batch inline '<json>'`.
- **Editor closed, or headless work** (tests, builds): Unity's own command line —
  `"C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe" -batchmode -projectPath <root> …`
  with `<version>` from `ProjectSettings/ProjectVersion.txt`. It cannot open the
  project while the Editor has it open. Compile-only check:
  `tools/ci/unity-compile-check.ps1`.

## Context Management

Read `.claude/docs/context-management.md` on demand — it is a reference, not
session context. Two of its conventions are load-bearing and cited by name
elsewhere in the repo, so they are restated here rather than lost:

- **`production/session-state/active.md` is the session checkpoint.** The file is
  the memory, not the conversation. Read it first after any compaction, crash, or
  `/clear`.
- **Helpers in `.claude/scripts/` emit observations, never verdicts.** A script
  that scores or judges will eventually contradict a mode or override it cannot
  see. (Cited by `artifact-check.sh` and `adr-dep-graph.sh`.)
