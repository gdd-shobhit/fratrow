# ADR-0002: Drunk speech is a procedural, host-side rewrite (no second language model)

## Status

Proposed. This awaits the user's confirmation. Only the user, or technical-director on the user's confirmation, may mark it Accepted.

## Date

2026-10-18

## Last Verified

2026-10-18

## Decision Makers

Lead engineer (Claude session). The user must accept.

## Summary

The drunk mechanic (`design/gdd/drunk-mechanic.md`) rewrites what a drunk brother says, more as he gets drunker. The
rewrite is a deterministic, data-tuned procedure in the Unity-free core (`FratRow.Game.DrunkSpeech`). It runs on the
host when the turn is accepted, and its result is sent back to the speaker. No second model is loaded, and no extra
model pass is added. The existing NPC model, when enabled, only receives a host-observed "speaker is drunk" fact.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6000.5.6f1 + Netcode for GameObjects 2.13.1 |
| **Domain** | Core / Networking / Social |
| **Layer** | Feature |
| **Knowledge Risk** | LOW. The core is plain C#. The adapter reuses the project's existing `[Rpc]`, `RpcTarget.Single` and `NetworkList` patterns. |
| **References Consulted** | `Native/SocialBrain/social_brain.cpp`, `NativeSocialModelProvider`, `LlamaServerModelProvider`, `SocialConversationService`, `Docs/SocialBrains.md` latency table |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Host and one client in the editor: drink, slur, NPC remark, HUD on both machines. The NGO ILPP for the new RPC is not exercised by the headless compile gate. |

## ADR Dependencies

ADR-0001 (rules live in the Unity-free core; NetworkBehaviours are thin host adapters).

## Context

The user asked for drunk speech rewriting. They suggested "we can pass the sentence through our AI model already there,"
or "maybe another small model." They also asked how it works over the network. These facts from the codebase decide it:

1. **There is one resident model.** The native runtime allows exactly one loaded model (`fr_social_create`, single
   resident runtime). A second "small model" cannot sit beside the NPC brain in-process. It would need a model swap
   (seconds) or a second `llama-server` process (more RAM on top of the ~2.2 GB host peak).
2. **Inference is serialized for the whole lobby.** `SocialConversationService` runs every NPC turn for every player
   through one `SemaphoreSlim(1,1)`. On CPU a turn takes about 13–27 s (`Docs/SocialBrains.md`). A rewrite pass would
   go in the same queue, so one drunk player's message would delay every other player's NPC replies. That is the
   multiplayer cost.
3. **The model is off by default.** It needs `--social-brains` or an EditorPref, a Debug or Editor build, and Windows.
   Its licensing is not cleared for shipping. Shipped players get the heuristic provider, so the feature must work with
   no model at all.
4. **The NPC reply is not model prose.** The model picks social acts and the host composes canned lines
   (`GroundedSocialTurn`). The project already prefers host-controlled wording over free generation for validity.
5. **"We would have to recalibrate it."** Calibration needs explicit, repeatable knobs. A procedure exposes one rate per
   effect, a curve and word lists in JSON. Tuning a model means prompt and temperature changes. Its output varies run
   to run, and it tends to "fix" or sanitize the joke, or change the meaning.

## Decision

- **Procedural rewrite.** `DrunkSpeech.Rewrite(text, intensity, tuning, GameRng)` applies, at rates scaled by intensity:
  slurring, neighbouring-key typos, dropped, doubled and swapped letters, stretched endings, shouting, repeated words,
  interjections, trail-offs and drunk tangents.
  - Intensity comes from the brother's drunk level: `threshold` → 0.1 … `max` → 1, with a curve.
  - Button lines (invite and rush) use a keyword-safe "cosmetic" subset, so their intent survives.
- **The host does it, once.** It runs inside `GameSession.Conversation` when the host accepts the turn. The text the
  social service journals, and the NPC hears, is the rewritten line. The randomness is the campaign's `GameRng` stream.
  Clients never re-run it, so there is no cross-client determinism to keep, and a modded client cannot skip it.
- **Networking.**
  - The drunk level replicates in the existing `BrotherNetState` row (`Drunk` 0..1 and `DrunkTier`). It is reliable,
    polled at 4 Hz and re-sent on a ≥ 1% change.
  - The spoken line goes back to the speaker only, in a reliable targeted `SpeechClientRpc` (≤ 800 chars, once per
    turn). It arrives before the NPC reply.
  - The house hears about it through the existing news broadcast.
- **NPC awareness.** `ConversationRequest.speakerCondition` is host-set; the legacy client submit path clears it.
  - The model provider gets a `SPEAKER_CONDITION` line in every prompt projection.
  - The grounded composer adds a short aside.
  - The heuristic provider applies a persona-dependent bias.
- **The seam is kept.** `GameSession.SpokenText` is the single place the line is transformed. A later model pass (see
  Alternatives) would wrap it in the adapter, not replace it.

## Consequences

- **Positive:**
  - Zero added latency or memory.
  - It works in shipped builds without the model, and identically for every provider.
  - It is deterministic and unit-tested; edit damage grows monotonically with drunkenness.
  - It is tunable from JSON, with a preview tool: `GameLoopHarness --drunk-speech`.
  - Bandwidth is about one extra small RPC per talk turn, plus 5 bytes per brother row.
- **Negative:**
  - The rewrite is character- and word-level. It cannot make semantic drunk changes (oversharing about the actual
    topic, saying the opposite). The tangents are canned.
  - With the heuristic provider, a heavily garbled typed invite can lose its keywords and be read as small talk. This
    is intended (the button path is safe), but a player may read it as a bug.
- **Neutral:** older saves and journals load as sober (field defaults).

## GDD Requirements Addressed

`design/gdd/drunk-mechanic.md` §3.3 (rewrite), §3.4 (NPC reaction), §3.6 (multiplayer). No TR-IDs are registered yet:
run `/architecture-review` to assign them in `tr-registry.yaml`.

## Alternatives Considered

1. **Rewrite through the existing NPC model (an extra free-text pass).** Rejected for now:
   - It adds 13–27 s per turn on CPU in the shared queue, which blocks every player.
   - It is unavailable in shipped builds.
   - It is non-deterministic and hard to calibrate.
2. **A second small model (for example a 0.5–1B GGUF, or Sentis/`com.unity.ai.inference` with a tiny seq2seq).**
   Rejected for now:
   - The native runtime allows one resident model, and `llama-server` doubles the process memory.
   - A Sentis text model needs an ONNX export plus a tokenizer port.
   - It has the same licensing question as the NPC model.
   - It would run on whichever peer hosts, so a weak host lags the whole lobby.
3. **Client-side rewrite (each client mangles its own text).** Rejected:
   - The client is not authoritative, so a modded client could skip it.
   - The NPC journal would trust client text that claims to be drunk speech.
4. **A future hybrid (recommended if semantic drunk talk becomes a goal).** At *wasted* only, and only when the model is
   enabled and the inference queue is idle:
   - Ask the model for a one-line "drunk paraphrase" with a hard 2 s budget.
   - Fall back to the procedural output on timeout.
   - Still do it on the host, and still send the result through `SpeechClientRpc`.

   This keeps every property above and adds flavour opportunistically.
