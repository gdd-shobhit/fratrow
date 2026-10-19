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
2. **Core-assigned ids.** Each record gets a 32-hex id: the first 16 bytes of SHA-256 over campaign, seed, `++ledgerSeq` and the
   record's content. The core sets `knowledge.ledger` immediately. `RecordGossip` skips an id that already exists, so replaying the
   same batch after a crash or reload records nothing new, and a replay that diverged gets new ids instead of colliding.
3. **Deferrable, never fatal.** `RecordGossip` returns nothing. `SocialWorldBridge(world, deferGossipWrites: true)` runs writes on an
   ordered continuation chain. A failed write is recorded (`LastGossipWriteError`), never thrown on the host thread, and does not stop
   later writes; `WaitForGossipWrites()` throws once if any failed. The core's flush swaps its queues out first and catches failures
   (`GossipLedgerFailures`, `LastGossipLedgerError`), so a broken journal costs NPC memories, never the game loop. The default stays
   synchronous, so tests and the harness are deterministic.
4. **Scope.** By default only stories that involve the players become NPC memory records. Rival-only stories still change opinions
   game-side. `gossip.ledgerAllStories` writes everything.
5. **Zero-copy reads.** `ISocialStore.ReadShared()` returns the committed state object. Commits publish a new object and never
   mutate a published one, so readers can hold it across threads. The game bridge's per-tick snapshot uses it.
6. **Memory follows belief.** Every gossip record carries its story `topicId`, a subject and a valence (0 when the NPC doesn't believe
   it). A second teller who tips an NPC into believing, a successful denial and an exposure each write a new record on the topic; the
   deterministic provider uses the latest record per topic and ignores records older than 3 social weeks.
   The bookkeeping fields stay out of model prompts (`MemoryEventView`): an earlier build serialized them into every memory line,
   costing ~40 characters per memory of context budget, which the social suite's budget test caught.
7. **No revision bumps.** Gossip records do not bump NPC revisions. Hearsay arriving while a turn is generating does not invalidate it
   (with a slow model and dozens of listeners per round it would otherwise interrupt live conversations).

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
- In deferred mode a crash can lose the last few gossip records. NPCs then simply don't remember that retelling, while the game state
  still says they know it. That is acceptable for flavour memory; an identical replay recreates them.
- Deferred does not mean the host never waits: the host's own synchronous social writes (`Observe`, `EndParty`, `SetStandings`,
  party-tick `ObserveEntrance`/`MarkArrived`) still take the store lock and wait if a background gossip write holds it. Gossip's own
  round no longer blocks the frame.
- In deferred mode a conversation can start before the latest receipts land, so its context (and the deterministic provider's
  hearsay) can differ by timing. Keep synchronous mode wherever reproducibility matters (tests, the harness, replays).
- `ReadShared()` callers must not mutate. Only the game bridge uses it, and it is read-only by construction.
- After a reload, NPCs keep memories from the discarded timeline (as with all social memory); new records never overwrite them.

## GDD Requirements Addressed

`design/gdd/campus-gossip.md` §3.9 (NPC memory) and acceptance criteria 6, 13, 18, 19, 20, 25 and 26.

## Alternatives Considered

- **One `Observe` per story plus one transaction per round.** This was the first build. It was 3–4× the transaction count and put the
  write on the host frame.
- **Let the social store assign ids and hand them back.** This forces the core to wait for the write, so it can't be deferred.
- **Keep gossip out of NPC memory entirely (game-side only).** Rejected: conversations would never mention what an NPC heard, and who
  said what would not reach the LLM.
- **Fix the journal now (incremental log or compaction).** The real fix, but it changes the store's recovery guarantees ("keep the
  complete journal in v1"). That is the author's decision and needs its own ADR.
