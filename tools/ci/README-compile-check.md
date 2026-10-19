# Unity compile check (headless)

Compiles the game's C# scripts with `dotnet build` and no Unity editor, so an
edit can be verified before it is opened in Unity. Prerequisites: a .NET SDK
(8+) on PATH, and a Unity-generated project (`*.csproj` + `Library\`) either in
the repo root or in `D:\fratrow` (override with `-UnityProjectDir`).

    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ci\unity-compile-check.ps1

Works with Windows PowerShell 5.1 and `pwsh`. Exit code: 0 pass, 1 compile
errors, 2 setup problem (missing csproj/Library/dotnet), 3 dotnet failed with
no parsable diagnostic. Output is an error list (`file:line: error CSxxxx msg`),
a distinct-warning count and a PASS/FAIL line.

- Projects are generated into `Temp\CompileCheck\` (gitignored via `/[Tt]emp/`);
  full logs are in `Temp\CompileCheck\logs\`. Override with `-OutDir`.
- One project per `.asmdef` under `Assets/` (references from the asmdef become
  project references), plus `Assembly-CSharp` and `Assembly-CSharp-Editor`.
  `.cs` files are globbed on every run, so new files and new asmdefs are picked
  up. A new asmdef with no Unity csproj yet is synthesized from a template csproj.
- Useful switches: `-NoAnalyzers`, `-ShowWarnings`, `-Incremental` (keeps
  obj/bin; warnings of up-to-date assemblies are not re-reported), `-RepoRoot`.

Limits: it compiles the editor configuration (`UNITY_EDITOR` defined), so code
that is only wrong in a player build is not caught. Unity's NGO IL post-processing
is not run (RPC methods compile without it). Package assemblies come from
`Library\ScriptAssemblies`, which Unity writes; if the editor is recompiling
while you run, a transient CS0009 means re-run.
