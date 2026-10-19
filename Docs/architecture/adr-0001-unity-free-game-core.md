# ADR-0001: Game loop rules live in a Unity-free core (`FratRow.Game`)

## Status

Proposed. This awaits the user's confirmation. Only the user, or technical-director on the user's confirmation, may mark it Accepted.

## Date

2026-10-18

## Last Verified

2026-10-18

## Decision Makers

Lead engineer (Claude session). The user must accept.

## Summary

The weekly campaign rules were spread across several NetworkBehaviours and could not be tested without a licensed Unity editor. Those rules are the calendar, party guests, rewards, contacts, invites and grade. They now live in a deterministic, engine-free C# assembly (`FratRow.Game`), and the existing NetworkBehaviours become thin host adapters around it.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6000.5.6f1 + Netcode for GameObjects 2.x |
| **Domain** | Core / Networking |
| **Layer** | Foundation |
| **Knowledge Risk** | MEDIUM |
| **References Consulted** | Existing project code (`NetworkedSocialConversation`, `NetworkedPartyManager`), `FratRow.Social` asmdef pattern |
| **Post-Cutoff APIs Used** | None in the core. The adapter uses the NGO `[Rpc]`/`RpcTarget`/`NetworkList` APIs already used in this project. |
| **Verification Required** | Host and two clients play one full week in the editor, covering replication, guest spawning and the HUD. ILPP (NGO RPC codegen) is not exercised by the headless compile gate. |

## ADR Dependencies

None. This follows the existing `FratRow.Social` pattern (`noEngineReferences` asmdef).

## Context

- Unity batch mode cannot run here (no headless license), and the editor is shared. The previous loop could only be checked by playing it, and even then not deterministically. It used `UnityEngine.Random` everywhere.
- The loop has grown to cover rivals, roles, a shop and stash, crimes, police, rush and the Dean. It interacts with the conversational NPC backend at many points, including RSVPs, witnesses, gossip, standings and affiliation.
- The coding standards require unit-testable public methods (dependency injection over singletons) and data-driven values.

## Decision

1. `Assets/Scripts/Game/Core` contains the `FratRow.Game` asmdef with `noEngineReferences`, referencing `FratRow.Social`. It holds:
   - `GameSession`: the phase machine and the party, police, rivals, settlement, rush and actions partials.
   - `GameState`, `GameLoopConfig`, `CampusRoster`, `GameRng` and `GameSaveStore`.
   - `ISocialBridge` and `SocialWorldBridge`.
2. All randomness comes from `GameRng`. It hashes the stream key with FNV-1a, never `string.GetHashCode`. The same seed and the same action sequence produce the same campaign.
3. The host adapter is `NetworkedCampaign`, baked on GameManager. It:
   - ticks the session and maps clients to durable social player IDs;
   - resolves positions to `CampusLocation` IDs;
   - turns core events into Unity effects (guest spawns, mirrors, news);
   - replicates a versioned HUD snapshot, houses, brothers, patrols, stash and incidents at 4 Hz, dirty-checked;
   - validates client action RPCs, with a rate limit and size and range checks.
4. Existing NetworkBehaviours are kept and extended, not replaced. These are the calendar, party manager and party, game manager, contact roster, phone invites, weekend ready and NPC director. Each has a campaign mode that defers to the core. Their legacy behaviour remains when no campaign is running.
5. The social world is always on. The local language model is an optional provider. Without it, `HeuristicSocialModelProvider` produces grounded acts that pass the same validation.

## Consequences

**Positive**
- The full loop is testable headlessly. `Tests/GameLoopHarness` has 23 tests, and `--simulate` plays whole semesters with bot crews, with either the fallback provider or the real model.
- Balance is tuned by data (`GameLoopConfig.json`) and checked by simulation.
- Save/load is a single checksummed JSON (`game.json`) next to the social journal. A mid-party reload respawns guests.

**Negative / risks**
- There are two representations of party guests: core `GuestState` and Unity NPCGuest bodies. They are kept in sync through events, and a missed event leaves an orphan body until the party ends.
- Every NGO adapter path, including replication, RPCs and prefab bake, is only compile-verified until someone plays it in the editor.
- `SocialWorld.Store.Read()` deep-copies the campaign. The bridge caches one read per tick, but long campaigns grow the journal. The existing v1 journal-compaction limitation still applies.

## GDD Requirements Addressed

`design/gdd/core-game-loop.md`, all sections. Acceptance criteria 1–9 are covered by `Tests/GameLoopHarness`. Criterion 10 is covered by the compile gate.

## Alternatives Considered

- **Extend the MonoBehaviours in place.** Rejected: untestable without the editor, and nondeterministic.
- **Use Unity Entities/DOTS for the simulation.** Rejected: the guest count is tens, not thousands, so it is unnecessary, and it would not be engine-free.
