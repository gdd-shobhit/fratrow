# Backend NPC social experiment

> **Update, 18 October 2026 (evening): persona replies and NPC playtests.** NPCs gave near-identical answers
> because no model wrote the words: without a model the keyword heuristic picks canned phrases (judged 2.3/5, 75% of
> replies duplicated verbatim across NPCs), and with a model the grounded composer still assembled template lines.
> See [`Reports/NpcDialogueDiagnosis-2026-10-18.md`](Reports/NpcDialogueDiagnosis-2026-10-18.md).
>
> - **Persona replies (default reply mode when a model is enabled):** `PersonaSocialTurn` lets the model speak in
>   character first (character card, plain-English situation, real chat history), then a constrained pass labels
>   the reply's RSVP/trust/regard/grievance; explicit yes/no/maybe wording overrides the label, and host validation is
>   unchanged. Switch with `--social-reply-mode persona|grounded` or *FratRow → Social Brains → Reply mode*. Grounded
>   replies remain available and are still required by the service in grounded mode.
> - **Playtests:** `Tests/NpcPlaytest` (skill `/npc-playtest`) runs the backend headlessly while a Claude Haiku 4.5
>   tester (≤5 messages) or a fixed script talks to NPCs; a Haiku judge scores each conversation against the hidden persona.
> - The heuristic's speech cues now match whole words ("what are you into" is no longer an invitation).
>
> The notes below are kept as written.

> **Update, 18 October 2026: campaign integration.** The social backend is now part of the core game loop
> (see [`Docs/GameLoop.md`](GameLoop.md) and [`design/gdd/core-game-loop.md`](../design/gdd/core-game-loop.md)), and is no
> longer an Ashley-only experiment.
>
> - **Every campus NPC** has their own persona, voice, standing and memory. That is 32 hand-written in
>   `Resources/Campus/CampusRoster.json` plus 32 generated from `PersonalityArchetypes.json`.
> - **The social world is always on.** The local model stays opt-in for development builds. Otherwise the deterministic
>   `HeuristicSocialModelProvider` emits grounded acts through the same composer, validation and commit path.
> - **Additive context:**
>   - Public standings of the three rival houses and the players' rank are included.
>     A change bumps the standing revision, so stale drafts get repaired.
>   - NPC affiliation is included (guest, a players' brother after Bid Day, or a rival member).
>   - Weekly campus news is recorded as one memory per NPC.
>   - Crimes the NPC witnessed are recorded as world events, with the acting player as the actor.
> - **The campaign drives the party lifecycle**, with one planned social party per week, and calls `ObserveEntrance`/`MarkArrived`.
> - **Commitments decide attendance.** An NPC who accepted arrives early in the night. An NPC who declined never comes.
>   Uncommitted NPCs pick a house by public appeal and their regard.
> - **Testing:** the 65 integrity checks still pass. The new behaviour is covered by `Tests/GameLoopHarness`.
>   A one-week simulation through Qwen 2B on the CUDA llama-server committed all 56 turns, at about 1.7 s per turn.
> - **Unchanged limits:** social-judgment quality and Unity two-client play.
>
> The historical notes below are kept as written.

This development feature gives Ashley one host-owned history across players and parties. It is disabled by default. The default provider now emits grounded social acts in one model call; host code composes and validates English dialogue before committing it. The historical free-form experiments below remain documented for comparison. The model judges social meaning and attendance; the host validates evidence, bounds effects, saves consequences, and executes the party commitment. A successful JSON completion is not evidence that the NPC's judgment is good.

The existing phone renderer is not connected to free-text conversations. `NetworkedSocialConversation.SubmitLocalAsync` is the reusable input API for both the command driver and the future phone. `OnResult`, `RequestOwnHistory` and `OnHistory` provide committed results and the requesting player's own recent history. Other players' transcripts and private NPC memories are never broadcast.

## Current grounded dialogue contract

The previous three-call planner/writer/model-auditor path produced invented schedules and attendance contradictions that its own audit approved. The default native and direct providers now make **one constrained model call**. The model selects the social decision, attitude, reason, topic, wording variant, evidence, personal trust/house regard changes and grievance references. Popularity and personality remain context for its judgment; no affinity threshold or scripted rule decides acceptance. Rude and unfair attitudes remain allowed.

Host code realizes those acts through seven tone banks and a bounded set of topics. Attendance wording comes from the same decision that executes the canonical RSVP. Accept agrees to attend; decline refuses; defer remains undecided; counteroffer always makes attendance depend on the **inviter** meeting the NPC at the entrance during the **party's first minute**. It cannot become a different deadline, immediate travel, the NPC proving attendance, or a new chore. No-party and already-fulfilled contexts exclude new attendance decisions. Full parties exclude new reservations. Existing stale-state, capacity, exactly-once and entrance-condition checks remain in force.

This deliberately narrows free-form dialogue. The model cannot write arbitrary factual prose. The renderer cannot invent curfews, earlier parties, friends, drinks, repairs, timetables, approval requirements or travel. Its only dynamic identity text is a quoted, bounded name from a visible saved grievance. Witnessed versus reported provenance is preserved; another player's words are explicitly separated from the current speaker. When a different housemate follows an unresolved grievance, this attribution clause is included even if the model chooses a different topic. Questions and opinions about the house are allowed. Other NPCs' private memories remain unavailable.

The interpretation saved alongside each new turn is a host-written description of the selected attitude/reason/decision plus evidence references, rather than another free-form factual account. Old model interpretations stay in the historical ledger but are not promoted into the new decision facts. A malicious or defective adapter cannot append unsupported text or change an RSVP phrase: the host recomposes both interpretation and reply and requires exact equality before committing. The Unity service requires this grounded metadata. No second model audit is involved. `rawReply` is now host-composed text; the evaluator labels `replySource=host-grounded-composer` and leaves `rawCheck` empty.

The vocabulary is intentionally smaller than open-ended chat, and currently supports English profiles only. It preserves persona-based choices and distinct tone banks, but does not certify that every invitation judgment is socially convincing. Human behavior grading is still required. These replies are partly authored text realization; they must not be described as unrestricted model-generated dialogue.

Each runtime turn mixes the provider base seed with its immutable **host-issued speech receipt**. Retries and reloads keep the draw; a client request ID or display-name change cannot choose it. The evaluator instead uses a reproducible NPC/step/repeat seed matched within low/high popularity pairs. `--fixed-seed` retains the former per-repeat diagnostic behavior. This avoids accidentally reusing one sampling draw across all characters without predetermining outcomes.

The old path is available only for explicit standalone diagnostics with `--legacy-freeform` (`--compact` selects its three-pass variant). The game service still requires grounded metadata, so that diagnostic provider cannot publish free-form proposals there. The old `--social-compact-inference` flag is retained for launch compatibility and does not disable grounded replies. SDK, weights, context limits, GPU fallback and networking remain unchanged.

The 65-test integrity suite includes all 420 combinations of seven tones, six attitudes, two phrasings and five attendance decisions, plus forged wording, unsupported facts, missing grounding, original-speaker memory clauses, reported gossip, identity familiarity, no-party decisions, receipt-bound sampling and reload/retry safety. Passing these checks establishes the dialogue/state contract, not a 95% attribution or 85% human-rated behavior result. Unity import, actual two-client gameplay and frame impact are still unverified due to the editor license limitation.

The final 18 October run is in [GroundedRepliesReport-2026-10-18.html](Reports/GroundedRepliesReport-2026-10-18.html), with exact model acts, host-composed replies, saved consequences and all transcripts. All 329 scenario turns plus 50 paired-speaker turns committed. An independent export verified the exact journaled reply/proposal, one decision event per receipt, visible evidence, permitted effect targets, grievance provenance and RSVP state for all 329 scenario turns.

| Grounded Qwen 2B / direct CUDA | Turns | Warm median / p95 | Paired first / second p95 |
| --- | --- | --- | --- |
| 11 personalities × low/high popularity | 44 + 10 paired | 1.87 / 2.39 s | 2.65 / 5.28 s |
| Ashley, low/high × three trials | 24 | 1.98 / 2.59 s | Not measured |
| 20 regression fixtures × three personas × three seeds | 261 + 40 paired | 2.01 / 2.50 s | 2.31 / 4.68 s |

Every scenario turn used one generation with no repair or model audit. These are measured on the shared RTX 4080, not a guaranteed two-second response: 155 of 326 warm scenario turns exceeded two seconds. One warm regression turn took 6.66 seconds, of which 2.09 seconds was provider time and 4.57 seconds was remaining host/scheduling time; the cause was not isolated. First requests including checksum/model loading took 3.97–5.57 seconds; filesystem caches were not flushed. Helper RAM peak was about 2.111 GB, private commit about 2.73 GB, before unquantified GPU allocations. The approximately 2 GB combined memory target is still not met/certified.

The same grounded native interface passed its RunAnywhere CPU smoke in 14.72 seconds cold: 1.49 seconds checksum, 2.01 seconds loading and 11.19 seconds generation, with 569 input / 66 output tokens. This confirms compatibility, not warm CPU p95 or two-second performance. Use the explicit CUDA fallback configuration for the fast development path.

Social judgment remains a separate limitation: the model often repeats guest-list questions and deferrals, sometimes declines after light teasing, and frequently leaves relationship values unchanged after cruelty. Rudeness is allowed, so those reactions are not automatically integrity failures. The narrower vocabulary and underused relationship effects still need human review and further behavior work. Older free-form transcripts below are historical failures; none has been relabeled as a current quality pass.

## Setup

Use PowerShell 7, .NET 9 or newer, CMake, and Visual Studio 2026 C++ tools. The older installed MSVC 14.38 cannot link the RunAnywhere kit. Models and generated native binaries are ignored by Git.

```powershell
pwsh -File Native/SocialBrain/bootstrap.ps1 -Models -CudaFallback
dotnet run --project Tests/SocialHarness/SocialHarness.csproj
```

The bootstrap verifies archive and model SHA256 values before use. It builds the C++ shim and installs the generated DLL under `Assets/Plugins/x86_64`. The importer enables it only for Windows x64 and the Windows editor. The independent core is `FratRow.Social`, with no Unity engine references.

Pinned components:

| Component | Pin |
| --- | --- |
| RunAnywhere | `cpp-desktop-v0.20.39`, Windows x64 desktop kit |
| Direct llama.cpp fallback | `b11718`, Windows CUDA 12.4 package and matching runtime |
| Qwen | `bartowski/Qwen_Qwen3.5-2B-GGUF`, revision `7d26695454df6de5fbcce2e58681e62dae06ce43`, Q4_K_M |
| MiniCPM | `openbmb/MiniCPM5-1B-GGUF`, revision `3d55fac80935ae6456986ad2384b5cbcc4d6c948`, Q4_K_M |

Model hashes live in `SocialModelCatalog`; archive hashes live in the bootstrap. The GGUF metadata supplies the pinned chat template. Generation uses separate system and user messages, non-thinking mode, 4,096 context tokens, 384 maximum output tokens, temperature 0.4, top-p 0.9, and seeded sampling. Inference contexts are cleared between NPCs. Grammar restricts host-owned identity/evidence IDs, unique relationship scopes, executable conditions, and field lengths. Semantic validation remains necessary.

Both grounded and legacy compact formats use campaign-stable player aliases: Player Two stays `player_2` when the speaker changes. The provider restores typed IDs before host validation. Unknown aliases fail closed. The grounded wire grammar emits minified JSON with descriptive field names; replies are bounded to 300 characters. Only legacy free-form JSON permits ordinary whitespace and arbitrary interpretations.

The historical free-form compact generation experiment used three sequential calls on the **same resident model**: structured social planning, a short in-character reply, and a grounding audit. No extra model is loaded. Voice/audit receive a facts-only context that excludes earlier NPC interpretations. The host still validates every proposed effect and commitment. One additional generation attempt is allowed after rejection; repeated failure keeps the received speech pending and commits no proposed effects. The model-based audit misses some errors and rejects some valid replies; it is an experimental filter, not a factual guarantee. The original one-pass diagnostic requires `--legacy-freeform` without `--compact`.

The host saves grievance actor, fraternity, knowledge type and immediate reporter from immutable ledger receipts, independently of model prose. Explicit gossip chains retain the alleged original actor while remaining reported knowledge; intermediate reporters do not become the offender. NPC interpretations cannot be used as external grievance causes. These are additive save fields; existing histories are preserved. Existing custom NPC personas are not overwritten.

An earlier, more aggressive single-letter-key experiment produced contradictory reply/decision pairs (a refusal in prose with `accept` in JSON). It was rejected. Neither the baseline nor the compact candidate has passed the social-quality gate; keep this experiment disabled outside development.

RunAnywhere's [pinned license](https://raw.githubusercontent.com/RunanywhereAI/runanywhere-sdks/cpp-desktop-v0.20.39/LICENSE) has eligibility and attribution requirements. This is a local experiment, not a declaration that commercial redistribution is cleared. Review SDK and model terms before shipping a player containing them; include the required notices. No SDK sources or model weights are committed.

## Per-NPC identity and voice

Every NPC now has a saved `NpcVoiceProfile`, separate from their social persona and memories. The profile supplies generation, role, gender, response language, tone, slang level and up to three optional example lines. The grounded decision context includes generation, role, gender and tone; English phrasing comes from the tone bank. Slang settings and examples remain saved for the historical free-form writer but do not expand the grounded vocabulary. Age and gender identify the character; their own persona and tone shape their choices.

Available tones:

| Tone | Direction |
| --- | --- |
| casual | Friendly and straightforward |
| assertive | Direct, self-respecting; clear pushback |
| bubbly | Upbeat and expressive, with boundaries |
| dry | Understated and dryly witty |
| reserved | Quiet, cautious and brief |
| playful | Light teasing; takes cruelty seriously |
| laid-back | Relaxed and easygoing |

Slang levels are `none`, `light` or `frequent`; language is a host-authored string, initially English. Profiles ask for short, natural dialogue and contractions, discourage customer-service/therapy wording, and explicitly say not to thank someone for insulting them. There is no requirement to use the same slang or personality for all students. Language configuration is prompt support, not a multilingual quality certification.

Ashley starts as a Gen Z college girl with an assertive, lightly colloquial English voice. Her current catalog wording example is “I'm not here to be your punching bag.” Earlier experiments also used “Hey, that's not nice.” These guide voice; they are not historical events or mandatory answers. A refusal such as “Okay, I'm not coming then” is permitted only with an actual declined commitment. When party context exists, the model can reconsider attending after mistreatment; it is not required to reject every insult. Without party context, it can push back without inventing an attendance change.

Configure another NPC through the host domain:

```csharp
world.EnsureNpc("Brad", "Cautious, values reliability and keeps his promises.",
    new NpcVoiceProfile { gender = "boy", tone = "reserved", slang = "none" });
world.SetNpcVoice("Brad",
    new NpcVoiceProfile { gender = "boy", tone = "dry", language = "English" });
```

These methods have no client RPC. A voice update is journaled and increments the NPC revision, so an in-flight draft using an old profile is repaired before committing. Editing a voice does not reset trust, grievances, commitments or history. Older saves can omit the additive voice field: they use a general Gen Z college-student voice until authored; bootstrapping Ashley fills her missing voice without replacing her existing persona. Repeated `EnsureNpc` calls preserve an already-authored voice. No additional model is loaded for another voice.

The voice regressions live in `Tests/SocialHarness/voice-episodes.json`. The evaluator now records the selected voice and accepts `--npc-id`, `--voice-gender`, `--voice-tone`, `--voice-language` and `--voice-slang`. Persona variants default to assertive, reserved and playful voices. Those new prompt versions must be measured separately from historical timing/quality runs. Voice guidance does not guarantee factual accuracy or correct social judgment.

## Eleven authored girl personalities and the game setting

`SocialCharacterCatalog` contains Ashley plus ten new Gen Z college-girl profiles. Each has a separate persona, voice, initial public popularity and status concern. These are backend character definitions, not ten new spawned or fully network-enabled Unity characters.

| Character | Voice | Status concern | Popularity | Main social preference |
| --- | --- | --- | --- | --- |
| Ashley | assertive | moderate | 10 | A good party, banter and being treated as an individual |
| Maya | bubbly | moderate | 32 | Bringing people together and a lively atmosphere |
| Chloe | dry | high | 35 | Prestige, social circles and a convincing invitation |
| Zoe | playful | low | 22 | Music, jokes and the actual vibe |
| Nina | reserved | low | 12 | Considerate invitations and a manageable atmosphere |
| Sofia | bubbly | moderate | 25 | Inclusion, kindness and trustworthy connections |
| Tessa | assertive | moderate | 28 | Direct plans, energy and independence |
| Brooke | laid-back | low | 18 | Good company over a prestigious house name |
| Ivy | dry | high | 30 | Credible networking opportunities and the real guest list |
| Leah | casual | moderate | 20 | Clear plans and concise answers to missing details |
| Jade | assertive | low | 26 | Authenticity, independence and a good comeback |

Use `SocialCharacterCatalog.Register(world, "Maya")` to register an authored backend profile. Registration fills missing fields and preserves an existing persona, voice, public standing and relationship history. Defaults are fresh objects so editing one character does not change another campaign. New Ashley defaults use the same catalog persona/voice as the comparison fixtures; existing campaign authoring is preserved.

The historical free-form prompt included a 78-word setting paragraph identifying FratRow as a fictional college social and party-management game about reputation, friendships, rivalry and throwing parties. It asked the model to portray the student and distinguish light banter from genuine cruelty, without adding world facts. The grounded decision prompt uses a shorter game-setting sentence and explicit guidance to react to the current speech, distinguish cruelty from teasing/repair, and keep housemates separate. The evaluator's `--omit-game-setting` switch removes the relevant framing in either mode; it is not a client request field. The earlier framing comparison below does not measure this newer prompt.

A neutral message may receive a rude, defensive, dismissive or snarky response. That alone is allowed personality variation, not a failed test. Popularity and personality inform judgments; there is no required acceptance outcome or fixed line. Review actor attribution, unsupported concrete world claims and reply/commitment disagreement separately. The console uses a fixed seed for comparisons; these tests do not establish response probabilities or calibrate model confidence.

The exact comparisons are in [GirlsPopularityComparison-2026-10-18.html](Reports/GirlsPopularityComparison-2026-10-18.html), with all profiles also available as [JSON](Reports/GirlPersonalities-2026-10-18.json). The profile matrix has 22 fresh conversations / 44 turns: each girl receives the same invitation and follow-up with fraternity popularity 5 versus 60. Each NPC's own popularity, persona, voice and starting relationships remain matched across the two conditions. Later histories diverge naturally. The run used one Qwen3.5 2B runtime across all profiles, one seed, and no simultaneous-speaker trial. It had one pending turn and warm p95 4.28 seconds.

Ashley was also tested at both popularity levels with invitation, teasing, insult and Player Two's apology, with the setting paragraph off and on: eight turns per mode. The omitted-framing run had three pending turns, warm median 1.89 s and p95 4.69 s. The included-framing run had zero pending turns, warm median 3.60 s and p95 9.12 s. Different replies, repairs, evolving histories and a shared GPU confound a causal latency comparison. Adding game context did not establish better grounding or a stronger popularity effect. Repeated defensive wording may be influenced by the supplied voice example; this run did not separately ablate that example. The report retains concrete actor/commitment/factual issues, without grading rude attitude as an error. No social-quality or two-second performance pass is claimed.

Fixtures are `girls-popularity-episodes.json` and `ashley-game-setting-episodes.json`. `profileId` selects a catalog character; the evaluator logs the exact selected voice, persona, context mode, result, saved consequences and last captured model draft/audit. Token counts aggregate calls in the last provider attempt; timing includes retries. The initial cold turn of each runtime is excluded from its warm statistics.

## Public reputation and social standing

Conversation context now includes host-owned public fraternity popularity, grade/rating, the NPC's public popularity, their interest in status, and whether the current player has previously spoken directly to them. These are separate from NPC-to-player trust and NPC-to-fraternity regard. A famous house can still be disliked; a famous student can be friendly and approachable. A supplied player display name is not proof of a previous introduction, and overhearing somebody speak to another NPC is not a direct introduction.

The Unity adapter mirrors `NetworkedGameManager.TotalPopularity` and the existing fraternity grade/rating. It reads each registered named NPC's `NamedNpcSO.basePopularity` and `socialStatusConcern`. Values stay in the game's existing score units, with no invented 0–100 popularity normalization. Missing values in older campaigns remain unknown. The persisted mirror is refreshed from the current host game state; it does not overwrite the game manager's totals. Updates subscribe to game popularity/fraternity events, refresh before accepting a request, and reconcile on the existing half-second host poll. Identical values do not journal another update. Fraternity changes carry a global standing revision; NPC popularity changes advance that NPC's revision. A proposal generated from stale standings must be repaired before commit.

`NamedNpcSO` now exposes social persona, voice and status concern for authoring. Concern is `Low`, `Moderate` or `High`, independently of popularity and gender. Ashley reads an authored persona/voice on first registration, with defaults otherwise; an existing authored campaign profile is preserved. Other NPCs can use the same domain profile APIs, while the actual Unity conversation slice still enables only Ashley. No additional inference runtime is loaded for another stored profile.

A status-conscious popular NPC can ask who an unfamiliar inviter is, question a low-profile house's appeal, or require convincing. This is model judgment, not a popularity threshold that predetermines attendance. Low popularity must not turn into invented evidence of bad parties or cruelty. Existing relationships and a sincere invitation can matter more than prestige. Voice controls how the character expresses that judgment.

For a supplied live party, the context also lists up to six of the most popular **publicly confirmed other guests**, plus the total confirmed count. Accepted commitments are public in this slice. Conditional/unconfirmed guests are excluded, and an RSVP is explicitly distinguished from arrival. This exposes public party appeal, not other NPCs' private conversations. A player's claim that a celebrity is coming remains an unverified claim. Guest/commitment changes already advance the party revision and invalidate stale invitation proposals.

The existing party system computes named guests' popularity from `basePopularity` plus the existing affinity modifier and awards party rewards through its normal completion path. The social layer adds no instant popularity reward for sending or accepting an invitation. A high-status guest must actually attend to contribute through that existing system; an accepted social invitation still bypasses the legacy random RSVP roll.

Host setup examples:

```csharp
world.SetFraternityStanding(5, "Pine House", "C", 50);
world.SetNpcStanding("Ashley", 35, StatusConcern.High);
// Public fame and private opinion remain independent:
// these setters do not change Ashley's trust, house regard or attendance.
```

`Tests/SocialHarness/status-episodes.json` covers low/high houses, high/low status concern, a remembered introduction, public versus claimed celebrity attendance, and an insult at a popular house. Setup fields are `fraternityPopularity`, `npcPopularity`, `statusConcern`, `personaOverride` and optional `confirmedGuests`. The latter seeds a host fixture public RSVP, not a tested model conversation with that guest.

The voice/status development results are in [SocialVoiceAndStatusReport-2026-10-18.html](Reports/SocialVoiceAndStatusReport-2026-10-18.html): 33 exact turns in 19 separate conversations. The voice run had five pending Ashley turns; Brad's invitation followed by insult produced an accepted then declined commitment. The status run committed all nine turns but still invented house membership and produced reply/decision contradictions missed by the audit. Its warm p95 was 4.76 seconds, above both the two-second preference and original four-second single-request target. These exposed integration cases do not pass the behavior gate. No pair-concurrency or human-grade claim is made for these runs.

## Run the game backend without phone UI

Open Unity and use **FratRow → Social Brains → Bake and validate network components**. GameManager already contains the new network behaviour. All four guest archetypes have their guest/network components baked, including the Jock prefab variant; runtime spawning does not add network behaviours.

For the measured GPU path, use **FratRow → Social Brains → Enable development experiment (CUDA fallback)**, then enter the existing lobby/game flow. It requires the downloaded fallback executable and saves the choice in editor preferences; an explicit `--social-llama-server` overrides that preference. The original **Enable development experiment** menu selects RunAnywhere CPU. Both select the downloaded Qwen model. Disable the scenario with the adjacent menu item. Native CPU generation can exceed the default 15-second operational deadline; use `--social-timeout 90` to inspect that configuration. Increasing the deadline does not change the four-second performance target. No inference is enabled outside this opt-in development scenario.

Build a development player using the editor or this method after activating a valid Unity license:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Unity.exe' `
  -batchmode -quit -projectPath D:/fratrow `
  -executeMethod SocialBrainSetup.BuildDevelopmentPlayer `
  -logFile D:/fratrow/Logs/social-build.log

pwsh -File Tests/SocialHarness/run-two-clients.ps1 `
  -Player D:/fratrow/BuildTest/Social/FratRow.exe
```

The two-client script uses the actual lobby, game, players and party through local NGO transport. It bypasses UGS/Relay setup for repeatability. It starts two hidden development player processes with distinct credential profiles, submits an insult and invitation, starts the real party, waits for the entrance window, and writes host state plus replies. Acceptance/refusal is left to the real model. Review attribution and dialogue manually. This script does not prove Relay connectivity, a fulfilled entrance meeting, or restart recovery in Unity; domain tests separately cover promise fulfillment and durable recovery.

For manual testing, launch development players with:

```text
--social-brains
--social-campaign social-development
--social-profile p1
--social-model D:/fratrow/Library/SocialModels/Qwen_Qwen3.5-2B-Q4_K_M.gguf
--social-inbox D:/fratrow/Library/SocialCommands/p1-in.json
--social-outbox D:/fratrow/Library/SocialCommands/p1-out.json
```

Use a distinct `--social-profile p2` and command paths for the second local process. To use GPU inference, additionally pass:

```text
--social-llama-server D:/fratrow/Library/SocialNative/llama-cuda/llama-server.exe
```

Without that flag a development player uses RunAnywhere; editor hosts use the chosen menu preference. The published RunAnywhere Windows kit contains CPU inference only; requesting GPU layers through that shim returns an explicit error. The direct fallback owns one loopback helper, one inference slot, a random API key, and a Windows job that kills the helper if the host crashes. It disables the web UI and external model downloads.

Write a complete JSON object atomically to the inbox. Example:

```json
{"requestId":"manual-001","action":"talk","npcId":"Ashley","channel":"text","text":"Would you consider coming to our party?"}
```

Other development actions are `start_party`, `end_party`, `dump`, and `metrics`; only the host can execute world changes. `metrics` reports recent frame p95, Unity allocation, and host process memory. A `cancel` command's `text` contains the queued request ID, or call `CancelQueuedLocal` directly. Retry an interrupted conversation with the same request ID and identical content. Use a new request ID for new speech. The development text contact is unlocked explicitly, without an affinity bonus. Release builds ignore feature activation flags.

## State and authority

- The host binds connections to durable, host-issued player IDs. Reconnect credentials use random 256-bit secrets; only their hashes are in the campaign save. The client stores its credential by campaign/profile. UGS anonymous IDs, NGO client IDs, and display names do not key relationships.
- Campaign data is under `Application.persistentDataPath/SocialCampaigns/<campaign>`. Credentials are under `SocialCredentials`. New campaign names isolate grudges. Keep credentials to retain identity; losing them creates a new person.
- The ledger distinguishes speech, world observations, inference, and rumor. Each event has witnesses/provenance. `SocialWorld.Observe` and `TellRumor` are host-only domain inputs, with no client RPC equivalent. NPCs never retrieve another NPC's private history without an explicit knowledge-transfer event.
- Personal trust and fraternity regard are separate. The legacy shared affinity imports once into fraternity regard; personal trust starts at zero. Roster affinity becomes a read projection for Ashley. Legacy templates and arbitrary affinity/invite RPCs cannot change her social state.
- Turn execution is serialized per NPC and globally limited to one inference. The queue holds at most 16 requests, one outstanding request per player. One hundred stored NPCs do not create one hundred models, prompts, or retained native contexts.
- Received speech is journaled before generation. The resulting reply, relationship changes, commitment and completed request are saved atomically before delivery. A retry cannot create another insult or regenerate accepted history. Queued/unreceived speech can be canceled; received speech survives disconnects and unfinished decisions resume after reload.
- The store flushes each journal commit and writes atomic checksummed snapshots every 32 transactions. A torn final journal record is truncated; a complete corrupt record fails closed. v1 retains the full state journal for recovery, so disk growth needs a compaction/migration design before long campaigns.
- Effects are clamped to [-100,100], with absolute influence limits of 10 per player/scope/episode and 20 per NPC/scope/game day. These limit farming without deciding sincerity. Party changes remain possible after the influence budget is exhausted.
- Accepted/conditional commitments reserve party capacity. Accepted Ashley bypasses the legacy RSVP roll. A conditional commitment requires its actual inviter within four metres of the entrance during the first 60 seconds; fulfillment/failure is recorded once. Spawning keeps NPC ID and party ID on the replicated guest. Reloading an active party reconciles its named guests from commitments.

## Verification and evaluation

The console integrity suite uses a deterministic test provider to exercise ordering, persistence and validation. It does not claim model intelligence. It covers two-player attribution/state isolation, gossip, rejected/unheard speech, malformed/malicious proposals, stale state, queue cancellation, pending-turn recovery, duplicate delivery, invitation capacity, entrance deadlines, repeated influence, journal corruption, snapshots, and 100-profile retrieval.

The current suite passes 65 checks, including saved grievance attribution across a different speaker, name changes and reload; forwarded gossip; rejected-audit transaction boundaries; isolated/persistent NPC voices; invalid profile rejection; and in-flight voice changes; separate public standing and private regard; reputation changes during inference; bounded confirmed guest context; and direct versus overheard introductions. Retrieval budgets important receipts first, then presents selected events chronologically; a long-message flood regression protects unresolved older incidents. Alias tests cover forged references, another player's effects, and persistent prose when the current speaker changes.

Run both real candidates against the frozen 20 scenario fixtures, three personas, three sampling seeds:

```powershell
dotnet run --project Tests/SocialHarness/SocialHarness.csproj -- --evaluate `
  --model D:/fratrow/Library/SocialModels/Qwen_Qwen3.5-2B-Q4_K_M.gguf `
  --llama-server D:/fratrow/Library/SocialNative/llama-cuda/llama-server.exe `
  --output Library/SocialEvaluation/Qwen
```

Repeat with `MiniCPM5-1B-Q4_K_M.gguf` and a distinct output directory. Omit `--llama-server` to evaluate RunAnywhere CPU; first copy the generated DLL next to the harness executable under `Library/SocialHarness/bin/net9.0`. `--deadline 90` permits inspecting slow CPU output. `--limit`, `--personas`, and `--repeats` permit smaller integration runs; those do not satisfy the complete matrix.

The report retains exact replies, consequences, raw last model output, token counts, cold/warm timing, and host/helper memory. End-to-end seconds include reception journaling, all generation attempts, validation, and durable commit. The compact evaluator also records generation attempt count, total provider time (including cold loading), and remaining host time. Token counts aggregate planning/voice/audit within the last provider attempt, not across retries. Model-pass and attempt counts include retries. Each individual generation remains bounded to 4,096 context tokens. A run with zero paired samples cannot certify the paired latency target. Null human-grading fields are intentional. Use each fixture's rubric to grade attribution, social acceptability, and dialogue/commitment agreement. Targets are 95% critical attribution, 85% acceptable episodes, zero critical reply/state contradictions, and zero invalid canonical mutations. These fixtures were exposed during integration and are regression cases; create a fresh independently reviewed held-out set before claiming generalization.

The evaluator additionally runs 20 pairs of simultaneous speakers, including queue time (`--pairs` changes that count). They share Ashley and one runtime; this is not a 100-conversation concurrency claim. Two simultaneous speakers must also be measured through the game. The target is p95 ≤4 seconds for one speaker and ≤8 seconds for the second. A console latency result does not certify Unity frame impact.

RAM working set, private commit, model file size and dedicated GPU allocations are distinct. WDDM returned unavailable for per-process GPU memory on this machine; combined added RAM+VRAM is not certified. Do not add shared pages twice or label a process-RAM reading a 2 GB inference result. A separate graphics workload can affect timings.

Initial measured smoke observations:

| Configuration | Cold request | Process memory | Result |
| --- | --- | --- | --- |
| RunAnywhere / Qwen 2B / CPU | 26.87 s | ~2.22 GB host peak working set | Valid structure; misses latency target |
| RunAnywhere / MiniCPM 1B / CPU | 12.96 s | ~1.20 GB host peak working set | Valid constrained structure; copied placeholder prose, social-quality failure |
| Direct llama.cpp / Qwen 2B / RTX 4080 | ~6.0–6.2 s | ~2.10 GB helper peak working set, GPU unquantified | Valid structure; cold time is not warm p95 |

The latest compact RunAnywhere/Qwen CPU smoke also returned valid canonical structure: 20.13 seconds cold, 514 prompt tokens, 125 output tokens, and 2.22 GB host peak working set. It does not establish warm CPU p95 and still exceeds the latency target.

Historical one-pass GPU backend measurements are saved in [SocialBrains.Validation.json](SocialBrains.Validation.json). Each candidate ran the 20 regression scenarios across three personas and three seeds (180 episodes / 261 turns), followed by 20 paired-speaker trials (40 turns):

| Run | Warm p95 | Paired first / second p95 | Uncommitted scenario / paired turns |
| --- | --- | --- | --- |
| Earlier verbose Qwen baseline | 4.11 s | 5.43 / 10.35 s | 5 / 21 |
| Compact Qwen, chronological memories | 2.15 s | 3.25 / 5.08 s | 0 / 0 |
| Compact MiniCPM, chronological memories | 0.45 s | 0.64 / 1.29 s | 0 / 0 |

Qwen median prompt/output sizes fell from 749/178 tokens to 536/102. Remaining host processing p95 was 7.9 ms; generation dominates. Qwen still had one second-speaker outlier at 16.88 seconds. These are observed runs on a shared RTX 4080 with another graphics workload present, not an isolated hardware benchmark. Qwen helper peak working set was 2.107 GB; MiniCPM's was 1.061 GB, before unquantified GPU allocations. The approximately 2 GB combined inference target is not certified.

Neither model passes the behavior gate. Spot checks include invented repairs/times, speaker confusion, and commitments whose prose does not express the selected condition. MiniCPM's faster output is frequently generic or inappropriate. The validation JSON retains concrete counterexamples; human accuracy/acceptability scores remain unset. Successful commits demonstrate transaction validity, not acceptable social judgment. Keep compact mode opt-in and the entire social feature disabled outside development.

The historical three-pass grounding experiment was evaluated separately; do not attribute the historical two-second result to those profiles:

| Historical compact profile | Scope | Warm p95 | Uncommitted turns |
| --- | --- | --- | --- |
| Qwen 2B, RTX 4080 | Two attribution episodes × three personas × three seeds; 36 turns | 1.64 s | 0 |
| Optional Qwen 4B, RTX 4080 | 20 episodes × three personas × one seed; 87 turns | 4.65 s | 6 |

The 2B attribution run's warm median was 1.32 s. It did not include paired speakers, and a native CPU smoke overlapped its early turns. The 4B run's warm median was 2.18 s; five paired trials measured 2.53 / 5.03 s first/second p95. These are integration regressions, not a new held-out quality pass. Sampled Player Two replies no longer directly blame him for Player One's insult, but free-text beliefs still invert who defended whom, and some replies invent approval requirements or plans. The audit misses some of those errors. Six 4B turns remained pending after failed validation, including audit false positives. Successful JSON commits must not be described as a behavior pass.

The optional 4B diagnostic requires `bootstrap.ps1 -Compare4B` and an explicit `--social-model` / evaluator `--model` path. It is **not the default**. Its weights alone are 3.01 GB, with a measured helper RAM peak of 4.27 GB before unquantified GPU allocations. It misses both the memory target and the single-request p95 target. The 2B helper RAM peak in the focused attribution run was 2.29 GB; combined RAM plus VRAM remains uncertified.

RunAnywhere timing was separated into checksum, loading and generation. In the diagnostic two-pass profile, RunAnywhere took **14.64 s cold and 12.48 s warm** on CPU; checksum plus loading accounted for only 2.15 s of the cold request. Direct llama.cpp on CPU took **4.99 s warm** with the same 2B weights and six threads. The engine builds, actual batch sizes and sampler defaults differ, so this does not isolate wrapper overhead. It establishes a real gap in the pinned CPU artifact; the earlier RTX 4080 timings belong to the direct CUDA fallback. The RunAnywhere kit used here has no CUDA backend.

The updated `cpu-bridge-2` configured-generation export also passed a three-call API smoke in **17.85 s cold** (1.03 s checksum, 1.19 s loading, 15.61 s generation). Its reply reversed who was inviting whom and the audit missed it. That smoke confirms native interoperability, not social quality; it overlapped the focused GPU evaluation and is not an isolated performance benchmark. Raw measurements and counterexamples are retained in the validation JSON. No runtime/model configuration is currently certified for the requested social quality.

Unity 6000.5.6f1 batch execution currently exits with code 198: no valid `com.unity.editor.headless` license. C# compiler checks using the installed Unity/package references pass, but editor import, NGO postprocessing, the player build, two-client party flow, and graphics frame measurements remain unverified until a licensed editor can run. One existing Unity 6.5 compile blocker was repaired by replacing obsolete `GetInstanceID()` deduplication with a `HashSet<GameObject>`.

Phone UI, voice, cloud campaigns, host migration, autonomous gossip scheduling, shipping licenses, and quality approval are subsequent work.
