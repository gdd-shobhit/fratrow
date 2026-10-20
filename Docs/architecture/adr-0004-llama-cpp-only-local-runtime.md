# ADR-0004: llama.cpp is the only local NPC inference runtime (CUDA, Vulkan, CPU builds)

## Status

Accepted. Decided by the user on 2026-10-19 ("drop RunAnywhere and switch to llama.cpp").

## Date

2026-10-19

## Last Verified

2026-10-19

## Decision Makers

The user (decision); lead engineer (Claude session) for the implementation.

## Summary

The social backend had two local runtimes: the RunAnywhere C++ SDK (CPU only, through a native shim) and llama.cpp's
`llama-server` (CUDA). All dialogue evaluation, including the persona-reply work, ran on llama.cpp. RunAnywhere is removed.
The host now launches one of three builds of the **same** llama.cpp release (`b11718`): CUDA for NVIDIA GPUs, Vulkan for any
other GPU, CPU as the last resort, chosen automatically from the builds installed on the machine.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6000.5.6f1 (the runtime and its selection live in the engine-free `FratRow.Social` core) |
| **Domain** | Core / AI dialogue |
| **Layer** | Foundation |
| **Knowledge Risk** | LOW. No Unity APIs beyond `SystemInfo.graphicsDeviceVendor`; llama.cpp is driven over its HTTP API as before. |
| **References Consulted** | `LlamaServerModelProvider`, `NetworkedSocialConversation`, `SocialBrainSetup`, llama.cpp `b11718` release assets |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Persona replies on each build (done, see Consequences); play in the Unity editor on a non-NVIDIA machine (not done). |

## ADR Dependencies

ADR-0001 (Unity-free game core). ADR-0002 (one resident runtime and one inference queue for the lobby) still holds; this
decides *which* runtime that is.

## Context

- RunAnywhere's Windows kit is CPU only. Measured in October: 12–27 s per model call for Qwen3.5 2B, past the 15 s reply
  deadline for persona replies, which make two calls. It needs a C++ shim built with Visual Studio 2026 and has
  eligibility and attribution licence terms that were never cleared for shipping.
- llama.cpp (MIT) was already the measured runtime: about 2 s per turn on the RTX 4080 through CUDA. But CUDA only runs on
  NVIDIA GPUs, and the host fell back to RunAnywhere whenever no CUDA server path was configured.
- llama.cpp publishes Vulkan and CPU builds of the same release with the identical `llama-server` API, so one provider class
  can serve every machine.

## Decision

1. **Remove RunAnywhere**: `NativeSocialModelProvider`, the `FratRowSocialNative` native shim (`Native/SocialBrain`), its
   plugin import settings and bootstrap steps. The GBNF proposal grammar it hosted moves to `SocialProposalGrammar`.
2. **One provider, three builds.** `LlamaServerModelProvider` is the only model provider. `LlamaRuntimes` (engine-free)
   picks the build: CUDA when the GPU is NVIDIA *and* the CUDA driver (`nvcuda.dll`) is present, otherwise Vulkan,
   otherwise CPU, considering only builds installed under `Library/SocialNative/llama-{cuda,vulkan,cpu}` (editor) or
   `<player>/SocialNative` (development player). An explicit `--social-llama-server`, or the editor's
   *FratRow → Social Brains → llama.cpp build* menu, overrides the choice. CPU gets 0 offloaded layers and a 90 s reply deadline.
3. **One pinned release.** `tools/social/bootstrap.ps1 -Runtime auto|cuda|vulkan|cpu|all` installs builds from llama.cpp
   `b11718` only, each verified against the release's published SHA256 digest.
4. **Shipping is not decided here.** The model still runs only in the editor and development builds. Whether and how
   players get it (bundled runtime, cloud, improved fallback, or a hybrid) is a separate decision.

## Consequences

- **Measured on the RTX 4080 machine, Qwen3.5 2B, persona replies** (`SocialHarness --smoke --persona`): CUDA 0.93 s warm,
  Vulkan 1.4 s warm, CPU 4.9 s warm. First request 8–40 s, mostly the model checksum and load.
- AMD and Intel GPUs now get GPU inference through Vulkan instead of a CPU-only fallback. Not yet verified on such hardware.
- No C++ toolchain is needed to set up the dialogue runtime; the bootstrap only downloads and verifies archives.
- Disk: the CUDA build plus its runtime is about 630 MB, Vulkan 30 MB, CPU 18 MB (compressed). A shipped game would most
  likely carry Vulkan and CPU, adding CUDA only if it measurably beats Vulkan.
- Licences left before shipping: llama.cpp (MIT, include the notice), the Qwen model card's licence, and NVIDIA's
  redistribution terms for the CUDA runtime DLLs if the CUDA build ships.
- `SocialHarness --native` remains as an alias of `--smoke`; `bootstrap.ps1 -CudaFallback` remains as an alias of `-Runtime cuda`.

## GDD Requirements Addressed

`design/gdd/core-game-loop.md` §3.4 (NPC conversations decided by the host social backend): the backend's local model
runtime. No requirement IDs are registered in `Docs/architecture/tr-registry.yaml` for it yet.
