# ADR-0002: Gossip writes to NPC memory are batched, core-identified and deferrable

## Status

Proposed. This awaits the user's confirmation. Only the user, or technical-director on the user's confirmation, may mark it Accepted.

## Date

2026-10-19

## Last Verified

2026-10-19

## Decision Makers

Lead engineer (Claude session). The user must accept.

## Summary

Campus gossip makes NPCs remember what they heard, which means hundreds of new social-ledger records per semester. The social
journal rewrites its entire state on every transaction, and the game bridge deep-copied that state on every read. Gossip therefore
writes all of a tick's records in **one** transaction, gives every record a **deterministic id from the game core**, so the
write can run on a background queue and a replay is idempotent, and reads the store through a **zero-copy** committed snapshot.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6000.5.6f1 (the core is engine-free) |
| **Domain** | Core / Persistence |
| **Layer** | Foundation |
| **Knowledge Risk** | LOW. Plain C# 9, `System.Threading.Tasks` (already used by `FratRow.Social`). |
| **References Consulted** | `JournalSocialStore`, `SocialWorld`, `SocialWorldBridge`, `GameSession` |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | The host builds `SocialWorldBridge(world, deferGossipWrites: true)` and calls `WaitForGossipWrites()` before saving/quitting. Not yet wired in `NetworkedCampaign` (no Unity hook in this change). |

## ADR Dependencies

ADR-0001 (Unity-free game core). This extends `ISocialBridge`, which ADR-0001 introduced.

## Context

- `JournalSocialStore.Transact` copies the state, applies the change, and appends the *whole* state to the journal with an fsync.
  `Read()` returns a deep JSON copy. Both scale with campaign size. ADR-0001 already lists the uncompacted journal as a v1 limitation.
- A first gossip build wrote each story's origin record through `Observe` (one transaction each) and read regard through a
  deep-copied snapshot. Measured on a 6-week, 3-bot semester (`GameLoopHarness -- --profile-gossip --weeks 6 --seed 61`):
  - 1338 social transactions vs 891 with gossip off;
  - a 1.9 MB state vs 1.2 MB;
  - 99.9 s wall time vs 38.8 s;
  - **169 ms** of host frame per gossip round late in the semester.
- Who-said-what chains need each listener's record to link to the teller's record, so record ids must be known before the next
  round, but the game core must not wait on disk.

## Decision

1. **Batch.** The core queues gossip observations (story origins, overheard lines, denials) and receipts (retellings). It flushes them
   at the start of every round and at the end of every `Tick`, `Apply` and `OnConversationCommitted`, through one
   `ISocialBridge.RecordGossip(observations, shares)` → `SocialWorld.RecordGossip` transaction. Pending records never outlive a tick
   or action, so a game save never misses them.
2. **Core-assigned ids.** Each record gets a 32-hex id from `GameRng(seed, "gossip-ledger:" + campaignId, ++ledgerSeq)`. The core sets
   `knowledge.ledger` immediately. `RecordGossip` skips an id that already exists, so replaying the same batch after a crash or reload
   records nothing new.
3. **Deferrable.** `RecordGossip` returns nothing. `SocialWorldBridge(world, deferGossipWrites: true)` runs writes on an ordered
   continuation chain. A failed write rethrows on the next call or on `WaitForGossipWrites()`, so failures are never silent. The
   default stays synchronous, so tests and the harness are deterministic.
4. **Scope.** By default only stories that involve the players become NPC memory records. Rival-only stories still change opinions
   game-side. `gossip.ledgerAllStories` writes everything.
5. **Zero-copy reads.** `ISocialStore.ReadShared()` returns the committed state object. Commits publish a new object and never
   mutate a published one, so readers can hold it across threads. The game bridge's per-tick snapshot uses it.

## Consequences

**Positive** (same profile, after the change)
- 1034–1100 transactions (+16–23 % over gossip off) and a 1.6 MB state.
- Gossip writes take 4–6 s per semester; social reads went from 3.9 s to **0.0 s**, which also speeds up games with gossip off.
- One round with its flush costs **10.7 ms of host frame deferred**, vs 163 ms synchronous. That includes one-time cache warm-up;
  game rules alone are about 1 ms (`Gossip_RoundCost` test).
- Replays are idempotent, and deferred and synchronous writes give identical game state and NPC memories
  (`Gossip_DeferredLedgerWrites` test).

**Negative / risks**
- The journal still grows with every transaction: 746 MB per semester with gossip off, 1.1 GB with it on. Compaction remains the top
  infrastructure follow-up (ADR-0001's v1 limitation, now quantified).
- In deferred mode a crash can lose the last few gossip records. NPCs then simply don't remember that retelling. The game state
  still says they know it. That is acceptable for flavour memory, and records are recreated if the same ticks replay.
- `ReadShared()` callers must not mutate. Only the game bridge uses it, and it is read-only by construction.
- A replay after reload that diverges (different player input) reuses ids with different content. The first version wins in NPC
  memory.

## GDD Requirements Addressed

`design/gdd/campus-gossip.md` §3.9 (NPC memory) and acceptance criteria 6, 13, 18, 19 and 20.

## Alternatives Considered

- **One `Observe` per story plus one transaction per round.** This was the first build. It was 3–4× the transaction count and put the
  write on the host frame.
- **Let the social store assign ids and hand them back.** This forces the core to wait for the write, so it can't be deferred.
- **Keep gossip out of NPC memory entirely (game-side only).** Rejected: conversations would never mention what an NPC heard, and who
  said what would not reach the LLM.
- **Fix the journal now (incremental log or compaction).** The real fix, but it changes the store's recovery guarantees ("keep the
  complete journal in v1"). That is the author's decision and needs its own ADR.
