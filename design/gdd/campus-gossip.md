# Campus Gossip — The Rumor Mill

> **Status**: Draft v1 (2026-10-19). Backend implemented in `Assets/Scripts/Game/Core` (`GameSession.Gossip.cs`,
> `GameSession.GossipActions.cs`, `GossipTypes.cs`). No Unity UI yet; the new actions already route through the generic action RPC.
> **Tuning**: every number below is a key under `gossip`, `storyKinds[]` or `smearClaims[]` in `Assets/Resources/Campus/GameLoopConfig.json`.
> **Related**: `design/gdd/core-game-loop.md` (§3.4 actions, §3.6 crimes, §3.7 rivals, §3.8 settlement, §4.2 popularity),
> `design/gdd/campus-roster.md` (gossip trait, schedules), `Docs/SocialBrains.md` (NPC memory), ADR-0002 (ledger writes).
> **Research basis**: Allport & Postman's basic law of rumor (importance × ambiguity; leveling, sharpening); Dunbar and Robbins & Karan
> (most talk is about absent others, a minority of it negative); Grosser et al. 2010 (negative gossip travels over friendship ties);
> Maki–Thompson rumor model (spreaders stop when they keep meeting people who already know); Estévez et al. 2022 (it takes more than one
> source to damage a relationship); Altay et al. 2020 (source attribution decays to "someone said"); Feinberg & Willer 2012 (gossip as
> punishment of norm violators); Wegner et al. 1981 (denials spread innuendo). Campus texture from Greek-life gossip culture: Sunday
> brunch debriefs, group chats, anonymous campus apps, house tier talk.

## 1. Overview

Things that matter on campus become **stories**, and stories move from person to person. A story is born only from a real event the
simulation produced — a raid, a fight, a freshman in the hospital, a celebrity at a party, a brother getting busted, a prank someone
saw, an insult that actually hurt someone, a promise broken at the door, a big pledge, a Dean sanction, a house shooting up or crashing
down the Greek Cup table — or from a deliberate **smear**. Trivial events never become stories: each one must clear a significance gate
that scales with how much reputation or status actually changed. Stories spread **one hop per round** between NPCs who are really
together (same place at the same hour, the same clique, house brothers, close friends, people who partied at the same house), lose
names and gain exaggeration as they travel, burn out when tellers keep meeting people who already know, and fade from memory over
three weeks. An NPC's **opinion** of a house or a player is what they *believe* they heard. Opinions decide where NPCs party, how
interested pledges are, whether someone gives a brother their number, how cold the conversational NPC is, and — through **word of
mouth** — part of every house's weekly popularity. Players can ask what people are saying, deny it (carefully), trace a rumor back to
its source, and expose a rival's lies; rivals can do the same to the players.

## 2. Player Fantasy

- *"By Monday everyone knew."* The raid on Saturday is the Sunday-brunch story, the Monday-in-class story, and a footnote by Thursday.
- *"Who told you that?"* Rumors have a trail. Follow it from the girl at the library to the Kappa pledge who started it, then blow it up.
- *"Tyler, what did you SAY to her?"* A brother who insults the wrong person becomes the story — first by name and quote, then as
  "one of the ABR guys" — and the next three people he invites have heard about him.
- *"Don't deny it to people who haven't heard."* Damage control is a skill: deny to believers, not to the uninformed, and never to
  people who were there.
- *"Chloe was at our party."* Good news travels too. Landing a tastemaker at your party is a story that brings more people next week.

## 3. Detailed Rules

### 3.1 Data

**Story** (`GossipStory`, persisted): `id`, `kind`, subjects (`houseId`, `otherHouseId`, `playerId`, `npcId`), `quote` (who-said-what only:
the speaker's own line, single line, ≤ `quoteChars`, data only), `detail` (crime phrase, smear claim), `origin`
(`witness` | `npc:<id>` | `player:<id>` | `house:<id>`), `fabricated` (host-only truth flag, never shown), `severity` 0–100, `heat`
0–100, `createdDay`, `exposed`, `retired`, `tracedBy[]`, `socialEventId`.

**Knowledge** (`GossipKnowledge`, one per NPC per story): `heardFrom` (teller NPC id, `witness`, `player:<id>`, `house:<id>`),
`tellers[]` (distinct, ≤ 4), `detail` (0 full: names + quote; 1 secondhand: names, no quote; 2 vague: "one of the ABR guys"),
`spice` (0 accurate, 1 exaggerated, 2 wild), `belief` 0–1, `stifles`, `spreader`, `learnedDay`, `ledger` (the NPC's memory record).

**Gossip trait** (roster `gossip` 0–1, archetype `gossipMin/Max`): how much a person talks about others. Unauthored NPCs derive it:
MeanGirl 0.7, None 0.45, Jock 0.4, Nerd 0.25; +0.1 High status concern, −0.05 Low; + popularity/200; clamped 0.05–0.95.
The **hubs** are the `hubCount` (3) most gossipy NPCs with gossip ≥ `hubGossip` (0.75): Megan, Kayla (a generated campus influencer)
and Maya with the shipped roster.

### 3.2 Story kinds (`storyKinds[]`)

Each kind has a `topic` (party, conduct, status, rush, said, smear), a `valence` (±1 for its house), a base `severity`, opinion weights
(`houseWeight`, `playerWeight`, signed `otherHouseWeight`) and three texts by spice. Defaults:

| Kind | Seeded by | Valence | Initial knowers |
|---|---|---|---|
| `party_legendary` / `party_flop` | players' party fun ≥ `legendaryFun` (75) & ≥ half full / fun < `flopFun` (35) or < ¼ full; rival rank 1 / last with < 40 % of the winner's score | + / − | named guests of that party |
| `party_raided` / `raid_legend` | a raid (legend if fun ≥ `infamyFun`) | − / + | guests |
| `party_fight`, `party_hospital` | escalated fight (someone hurt), underage hospital trip | − | guests |
| `celebrity_sighting` | the most popular notable guest (pop ≥ `notablePopularity`) | + | guests |
| `busted` | any bust (crime or raid citation) | − | NPCs at the scene, else the hubs (police blotter) |
| `heist` / `dirty_move` | a witnessed, undetected prank or mascot theft / stink bomb or keg theft | + / − (victim house −) | witnesses |
| `snitched`, `smear_backfire` | a traced `call_cops_on` / `spread_rumor` | − | hubs + witnesses + the listener |
| `house_trashed` | a rival prank with witnesses (culprit named when attributed) | − | witnesses |
| `who_said` | a conversation turn with real impact (§4.1) | − (player 1.0, house 0.3) | the person spoken to + bystanders |
| `who_charmed` | an upset: a notable NPC (pop ≥ 20) more popular than the house newly accepting | + | the NPC + bystanders |
| `stood_up` | a missed "meet me at the door" promise | − | the NPC |
| `smear` | `spread_rumor`, rival rumors | − | the listener(s) |
| `smear_exposed` | exposure of a fabricated smear | − for the liar, + for the victim | hubs + exposer's members + people present |
| `dean_probation` / `dean_suspended` | a *new* sanction | − | the house's members + hubs |
| `on_the_rise` / `falling_off` | cup-table move ≥ `rankSwingPlaces` (2) or taking/losing #1; or popularity swing ≥ `popularitySwing` (12) | + / − | hubs + members + that house's party guests |
| `pledged` / `pledge_snub` | a notable pledge (pop ≥ `notablePledge` 15) / a PNM picking a rival over the players' bid | + (snubbed house −) | the pledge + members + hubs |

### 3.3 Seeding (the significance gate)

`Seed(kind, severity, knowers, …)` creates a story only if: gossip is enabled; `severity ≥ seedThreshold` (35); at least one real NPC
knows it. A story with the same kind and subjects that is still going around is **reinforced** — severity and heat raised, age reset,
new knowers added — instead of duplicated ("Kappa throws good parties" is one reputation, refreshed every weekend). Caps: at most
`maxNewStoriesPerWeek` (14) per gossip week (Sunday→Saturday) and `maxPerKindPerWeek` (3) of any one kind, unless
`severity ≥ capBypassSeverity` (80); at most `maxActiveStories` (16) active — a new story evicts the coldest one only if it is hotter. Initial knowers know it
firsthand (`heardFrom = witness`, belief 1, detail 0) unless the seeding site says otherwise (smears: `heardFrom` is the liar).

### 3.4 Spreading (rounds)

- **When.** Weekdays at `roundHours` (10:00 and 15:00, live on the clock); **Sunday brunch**: `brunchRounds` (2) right after settlement;
  rounds the clock skipped (early weekend) run before the day ends, so starting the weekend early never skips gossip.
- **Who meets whom.** Contacts and tie strength (§4.2) come from the roster: schedule co-location at that hour (brunch: who partied
  at the same house), clique (`guestClass`), house membership, and weekly shared hours (familiarity).
- **Who tells.** Every NPC who holds a story with `spreader` and `belief ≥ spreadMinBelief` (0.25) may tell up to 1 contact
  (+1 if gossip ≥ 0.6, +1 more if ≥ 0.85), strongest ties first. For each contact they pick their **juiciest** story (§4.2) and tell it
  with probability `p` (§4.2). **Nasty stories need a close tie** (`negativeTieMin` 0.55); good news travels over any tie.
- **One hop per round.** All tells are chosen first and applied after; listeners receive the version their teller had at the start of
  the round. Nobody hears a story from someone who learned it in the same round.
- **Hearing it new.** detail = teller's + 1 with `levelingChance` (0.45), capped at 2 (names fade to "one of the X guys"; a victim's
  name fades, a celebrity's never does); spice = teller's + 1 with `sharpenChance` × teller gossip (cap `maxSpice` 2); belief per §4.3.
- **Hearing it again.** The teller gains a **stifle**; after `stifleAfter` (2) the teller stops spreading it. If the teller is a new
  source, the listener's belief rises (§4.3) and a better-detailed version replaces theirs (converging reports keep names alive).

### 3.5 Lifecycle and the weekly rhythm

Each day end: `heat ×= heatRetentionBase + severity/100 × heatRetentionPerSeverity`; a story retires when heat < `retireHeat` (8) or
nobody is spreading it. A retired story still affects opinions (fading over `memoryWeeks` = 3 weeks) and is then forgotten with every
version of it. Rhythm: **Saturday** produces the material; **Sunday** settlement seeds it and brunch spreads it; **Monday–Tuesday** it
crosses cliques in class; by **Wednesday** small stories are dead; scandals (severity ≥ 70) last 1–2 weeks. Players see a **rumor-mill
digest** on Sunday, a **"Word on campus"** line in each morning briefing, and an **"Everyone's talking"** alert when a story reaches
`viralShare` (25 %) of campus.

### 3.6 Smears and exposure

- **`spread_rumor`** (crime-lite, existing): the player picks a claim (`item` = a `smearClaims[]` id, else random) and tells one NPC in
  person. The listener knows exactly who told them. The story is **fabricated** unless the claim happens to be true (`truth` flag:
  probation/suspended/trashed/raided). If police trace it, it becomes a `smear_backfire` story about the player instead.
- **Rival rumors** (rival AI daily action): the same, told by a named rival member if the rival has recruits ("Mason from Kappa"), else
  "a Kappa brother".
- **Tracing.** `trace_rumor` asks one NPC who told them. Witnesses say they saw it; a rival's own member won't say; reaching a rival
  member or "a Kappa brother" marks the story **traced** by the players.
- **Exposing.** `expose_rumor` on a traced, fabricated story: every knower's belief × `exposeBeliefFactor` (0.2) and stops spreading; a
  `smear_exposed` story about the liar spreads instead (public news). A true claim can't be exposed ("It checks out").
- **Rivals trace the players.** Each day, a fabricated player smear about a rival that a member of that rival heard (or that reached
  `rivalTraceReach` = 8 people) is exposed with probability `rivalTraceBase + caution × rivalTraceCaution` (0.15 + 0.35·caution).

### 3.7 Player actions

All are conversations: allowed in person or by text with an unlocked contact, allowed while detained, rejected when gossip is disabled.

| Action | Target / item | Effect | Limit |
|---|---|---|---|
| `ask_gossip` | NPC | The NPC's top 3 stories, **in their version**, with ids (`[#12] …`). Members never dish dirt on their own house. | 1 per NPC per day |
| `deny_rumor` | NPC, story id | Only bad stories about the players' house or a brother. Knows it secondhand → belief × (1 − power) (§4.5), and a true story's exaggeration resets to accurate. Witness → fails. **Hadn't heard → now they have** (belief `denyBackfireBelief` 0.3, secondhand). | 1 per NPC per day |
| `trace_rumor` | NPC, story id | Who told them; may mark the story traced. | `tracePerDay` (4) per player |
| `expose_rumor` | story id | §3.6. | once per story |

### 3.8 What opinions change

- **Party choice** (core §4.8): appeal += `destinationWeight` (0.35) × opinion(house).
- **Rush interest** (core §4.7): += `rushWeight` (0.3) × opinion(house).
- **Weekly popularity** (core §4.2): += word of mouth (§4.4), applied at every settlement while stories are remembered.
- **Phone numbers**: an NPC whose opinion of a player is ≤ `numberRefuseOpinion` (−12) refuses `ask_number`: "I've heard about you."
- **Conversation**: each believed story about the players reaches the NPC's social memory as a rumor receipt (§3.9). The LLM reads it as
  reported, not witnessed; the deterministic provider's decision score moves 0.06 per receipt naming the speaker and 0.03 per receipt
  about the house (± 0.18 cap).

### 3.9 NPC memory (social ledger)

Stories involving the players (their house, a brother, or the players as the second house) become social records: one origin record
per seeding, one receipt per retelling. A full-detail (detail 0) retelling **links** to the teller's own record, so the listener's
memory carries the alleged actor and original words (who said what); vaguer retellings are unlinked paraphrases that do not name
anyone the teller didn't. Rival-only stories still move opinions but write no records unless `ledgerAllStories` is on (each record grows
the social journal; see ADR-0002). All gossip writes of a tick/action/round go out in **one** social transaction with core-assigned
ids, so the bridge can write them in the background.

## 4. Formulas

All randomness uses the campaign seed (`GameRng`), per round `Rng("gossip-<day|brunch>", round)` keyed by semester/week/day.

**4.1 Who-said-what severity.** For a committed turn with applied deltas `trust`, `regard`, grievance `g`, and RSVP change:
`bad = 45·g + 15·[trust ≤ −5] + 10·[regard < 0] + 25·[accepted/conditional → declined]`; `severity = bad × (0.8 + npcPop/100)`.
Example: an insult to Ashley (pop 10) that creates a grievance with trust −5 and regard −2 → (45+15+10) × 0.9 = **63** → seeded.
A "come on, don't be lame" (trust −2) → 0 → never a story. Charm is news only as an **upset**: `upset = npcPop − housePop > 0` and
`npcPop ≥ notablePopularity`; `severity = 25 + 1.5·upset + 10·[trust > 0]`. Chloe (35) saying yes to the week-1 underdog (pop 5) →
25 + 45 + 10 = **80**; to a house at 30 → **42.5**; to a house at 35+ → not news. (A first build without the upset rule let a crew farm
word of mouth by inviting every notable NPC; the semester simulation caught it.)

**4.2 Ties, juice and telling.** Familiarity `F` = weekly scheduled hours two NPCs share at the same place (from the roster).
`tie = max(brother 1.0, together∧clique 0.9, same party 0.65, clique 0.55, friends[F ≥ 12] 0.6, together → 0.45 + min(0.35, 0.03·F))`.
Example: two regulars sharing 6 h/week at the Library, together now → 0.45 + 0.18 = **0.63** (can trade dirt); strangers who happen to
meet → 0.45 (good news only). `juice = heat × (1 + 0.15·spice) × (1.25 if negative) × relevance`, relevance ×0.2 if the teller is a
member of the house and it's bad (×1.5 if good), ×1.3 if the listener is a member of either house, ×1.15 for a MeanGirl listener on
party/status/said stories, ×1.15 for a High-concern listener on status/rush. `p = clamp(tellBase × (0.4 + g_teller) × juice/100 × tie, 0, 0.95)`.
Example: Megan (g 0.95) telling a clique-mate (tie 0.9, MeanGirl) a raid story at heat 70: juice = 70 × 1.25 × 1.15 = 100.6 →
p = 0.75 × 1.35 × 1.006 × 0.9 = **0.92**.

**4.3 Belief.** New: `b = 0.35 + 0.35·tie + 0.3·(b_teller − 0.5) + 0.2·(g_listener − 0.5)`; a member of the story's house: ×0.3 if bad,
+0.15 if good; `+ valence × regard(house)/100 × 0.2` (people who like a house discount bad news about it); ×0.3 if exposed;
clamp 0.05–0.95. Repeat from a new teller: `b += 0.3 × tie × (1 − b)` (cap 0.97). **Believes** = `b ≥ 0.6`, or `b ≥ 0.4` from 2+ tellers.
Example: the clique-mate above (g 0.7) → 0.35 + 0.315 + 0.15 + 0.04 = **0.855**; if they like the house at +40 → 0.775.

**4.4 Opinion and word of mouth.** For each believed story: `w = severity/100 × spiceMult[spice] (1, 1.25, 1.5) × belief × fade × opinionScale (30)`,
`fade = 1 − age_days / 21`. Opinion(house) += valence × houseWeight × w (members excluded); opinion(other house) += otherHouseWeight × w;
opinion(player) += valence × playerWeight × w only while the NPC's version names them (detail ≤ 1). Positive contributions are
× `positiveOpinionFactor` (0.6): bad news weighs more than good news of the same size (negativity bias). Each opinion is clamped to ±50.
`wordOfMouth(h) = mean opinion of h over every named non-member`; settlement adds `clamp(0.12 × wordOfMouth, −4, +4)` to Δpop
(before the soft cap). Example: a believed sev-70 raid at spice 1, belief 0.8, 2 days old → w = 0.7 × 1.25 × 0.8 × 0.905 × 30 = **19.0**;
if campus averages −8 about the house, next Sunday's popularity gets **−0.96**.

**4.5 Denial.** `credibility = clamp(0.35 + regard(players)/200 + 0.1·[has talked to this player], 0.1, 1)`;
`power = 0.5 × credibility × (1.3 if fabricated else 0.35)`; `belief ×= 1 − power`. Example: a fabricated smear, neutral NPC →
power 0.23 → belief 0.8 → **0.62**; a true story → 0.061 → 0.75.

**4.6 Heat.** `retention = 0.55 + 0.35 × severity/100`; days to retire from `heat = severity`: ln(8/sev)/ln(retention) →
sev 40: **4.3 days**; sev 70: **9.4**; sev 85: **14.3**.

## 5. Edge Cases

- **Event with no NPC present** (a party nobody named came to, an unwitnessed prank): no story. The official news still happens.
- **Insult by text**: only the recipient knows; in person, everyone at that spot overheard (their own record names the speaker).
- **Same story twice in a week** (two insults to the same NPC, two rival smears): one story, reinforced.
- **A pledge joins a house that has bad stories about it**: opinions are recomputed; members ignore stories about their own house and
  bury its dirt (×0.2 juice) while bragging about its wins.
- **A smear claim that is true** (the players really are on probation): not fabricated — it cannot be exposed and rivals never "trace" it.
- **Denying to someone who hadn't heard**: they hear it now, secondhand, at belief 0.3 (innuendo). Denying to a witness: refused,
  belief unchanged. Denying good news or a rival's story: rejected.
- **Exposing before tracing / twice / a true story**: rejected with `invalid_target` / `already_done` / "It checks out".
- **Weekend started early**: the skipped days' rounds run first; the gossip result is identical to playing the week out (tested).
- **Save/load mid-day**: all gossip state is in `GameState`; continuing gives the same result (tested). Pending ledger writes are flushed
  at the end of every tick and action, so a save never misses them.
- **Crash with deferred writes in flight / replay after reload**: record ids are deterministic per campaign; recording an id that already
  exists is a no-op, so NPC memories are not duplicated.
- **Suspended players' house at party time**: no party stories and no stood-up stories (the party didn't happen); the suspension itself
  is a story. **Suspended at this Sunday's settlement** (e.g. by the raid itself): the party did happen — its stories seed, and the
  social party is recorded as ended, not cancelled. (Fixes a pre-existing settlement bug that cancelled RSVPs of a party that took place.)
- **Player renamed**: story text renders the current name; NPC memories keep the words as they were told.
- **A quote with line breaks, quote marks or instructions**: stored as one capped line (`"` → `'`), shown only at full detail, and
  reaches NPC prompts as encoded data.
- **Config without some story kinds / crimes from an older JSON**: missing kinds and crime mappings fall back to code defaults by id.
- **Gossip disabled**: no stories, no opinions, word of mouth 0, gossip actions return `unknown_action`.
- **Roster NPC removed after a save**: their knowledge is ignored and pruned with its story.

## 6. Dependencies

| System | Direction | Contract |
|---|---|---|
| Core game loop (`design/gdd/core-game-loop.md`) | both | Gossip reads party, crime, police, rival, Dean, rush and settlement events; feeds destination appeal (§4.8), rush interest (§4.7), weekly popularity (§4.2) and the news feed. Rounds run on the core clock. |
| Campus roster (`design/gdd/campus-roster.md`) | Gossip → Roster | Schedules (co-location, familiarity), `guestClass` cliques, popularity, status concern, `gossip` trait. |
| Social backend (`Docs/SocialBrains.md`) | Gossip → Social | `RecordGossip` (one transaction, core-assigned ids, idempotent), `TurnImpact` on committed turns, `ReadShared` snapshots; rumor receipts rendered in NPC memory; the heuristic provider's hearsay term. |
| `NetworkedCampaign` (Unity) | Adapter (future) | Already routes `ask_gossip`/`deny_rumor`/`trace_rumor`/`expose_rumor` through the generic action RPC and broadcasts gossip news. Should construct `SocialWorldBridge(world, deferGossipWrites: true)` and call `WaitForGossipWrites()` before saving/quitting. A rumor panel can read `Stories()` / `WhatNpcHeard()` (no truth flag). |

## 7. Tuning Knobs (`gossip` in GameLoopConfig.json)

| Knob | Default | Safe range | Affects |
|---|---|---|---|
| `seedThreshold` | 35 | 25–50 | How much has to happen before campus talks. Lower = chatter. |
| `maxActiveStories` / `maxNewStoriesPerWeek` / `maxPerKindPerWeek` / `capBypassSeverity` | 16 / 14 / 3 / 80 | 8–24 / 8–20 / 2–5 / 70–90 | Volume and variety; scandals always get through. |
| `roundHours` / `brunchRounds` | [10, 15] / 2 | 1–3 rounds/day / 1–3 | Spread speed. |
| `tellBase`, `negativityBias` | 0.75, 1.25 | 0.5–0.9, 1–1.5 | How talkative campus is; bad news bias. |
| `negativeTieMin`, `familiarityPerHour`, `friendHours` | 0.55, 0.03, 12 | 0.5–0.7, 0.02–0.05, 8–16 | Who trades dirt. Above 0.65 nasty stories get stuck in cliques. |
| `stifleAfter` | 2 | 1–4 | How fast stories burn out. |
| `levelingChance`, `sharpenChance`, `maxSpice` | 0.45, 0.4, 2 | 0.3–0.6, 0.2–0.6, 1–2 | Name loss and exaggeration per hop. |
| `beliefThreshold`, `multiSourceBelief`, `beliefBase`, `beliefTie`, `beliefRepeat` | 0.6, 0.4, 0.35, 0.35, 0.3 | ±0.1 | When a story changes minds. |
| `heatRetentionBase`, `heatRetentionPerSeverity`, `retireHeat`, `memoryWeeks` | 0.55, 0.35, 8, 3 | sum ≤ 0.95, 5–12, 2–4 | Story life (§4.6) and how long opinions last. |
| `opinionScale`, `opinionCap`, `positiveOpinionFactor` | 30, 50, 0.6 | 20–40, 30–60, 0.4–1 | Per-NPC opinion strength; how much less good news counts. |
| `destinationWeight`, `rushWeight` | 0.35, 0.3 | 0.2–0.5 | Gossip's pull on party choice and pledging. |
| `wordOfMouthScale`, `wordOfMouthCap` | 0.12, 4 | 0.08–0.2, 2–6 | Gossip's share of weekly popularity (rankPop is +8…−3). |
| `saidGrievance`, `saidTrustDrop`, `saidRegardDrop`, `saidWithdrew`, `charmBase` | 45, 15, 10, 25, 25 | ±10 | Which conversations become stories. |
| `denyPower`, `denyTrueFactor`, `denyBackfireBelief`, `exposeBeliefFactor` | 0.5, 0.35, 0.3, 0.2 | 0.3–0.7, 0.2–0.5, 0.2–0.4, 0.1–0.4 | Damage control strength. |
| `rivalTraceBase`, `rivalTraceCaution`, `rivalTraceReach` | 0.15, 0.35, 8 | 0.05–0.3, 0.2–0.5, 5–15 | Risk of smearing a rival. |
| `numberRefuseOpinion`, `viralShare`, `quoteChars` | −12, 0.25, 140 | −20…−8, 0.2–0.4, 80–200 | Social consequences, alerts, quote length. |
| `ledgerAllStories` | false | — | Rival-only stories in NPC memory (costs journal size). |

Story texts, severities and weights live in `storyKinds[]`; smear claims in `smearClaims[]`; per-crime story mapping in
`crimes[].gossipKind`/`gossipPhrase`; per-NPC talkativeness in the roster (`gossip`) and archetypes (`gossipMin`/`gossipMax`).

## 8. Acceptance Criteria

Each is an automated test in `Tests/GameLoopHarness/GossipTests.cs` (`dotnet run --project Tests/GameLoopHarness -- --gossip`).

1. Events below `seedThreshold`, events with no real witness, small slights, ordinary RSVPs and low-profile accepts never seed; an insult
   with a grievance seeds `who_said` with the speaker's quote; landing a notable guest seeds `who_charmed`.
2. An in-person insult is known by exactly the victim and the NPCs at that spot; the victim knows it through the speech they heard and
   each bystander through their own record naming the speaker.
3. From 3 seed knowers, reach after round 1 is ≤ 12; every new knower heard it from someone who knew it the round before; reach never
   shrinks within the week and never reaches the whole campus.
4. A negative story is never told over a tie below `negativeTieMin`; a positive one is.
5. A severity-40 story is retired within a week and forgotten (story and every version) after `memoryWeeks`; the active cap holds.
6. After spreading, every retelling of a who-said story has a social receipt; it links to the teller's record only at full detail; a
   linked chain still resolves to the speaker; a vague receipt contains neither the speaker's name nor the quote; spice ≤ `maxSpice`.
7. One teller at belief 0.5 changes no opinion; two tellers do; higher belief has a stronger effect; members ignore their own house.
8. A believed raid lowers the NPC's opinion and rush interest, lowers that house's (only) word of mouth, sends fewer named guests to it
   on Saturday, and its settlement word-of-mouth term is negative and within the cap.
9. A player smear about a rival is fabricated, attributed to the player, traced and exposed by the rival, collapses the believers,
   becomes `smear_exposed` about the player, notifies the players, and turns campus word of mouth about the players' house negative.
10. A rival smear can be traced hop by hop to "a Kappa brother"; exposing before tracing fails; exposing debunks it and turns it on
    Kappa; exposing twice fails; a true claim can't be exposed.
11. Denial lowers a hearsay believer's belief; a second denial the same day is capped; a witness can't be talked out of it; denying to
    someone who hadn't heard makes them hear it; only bad stories about the players can be denied.
12. `ask_gossip` lists the NPC's version with story ids, is capped per NPC per day, and rejects unknown NPCs.
13. A secondhand receipt naming the speaker appears in that NPC's next conversation context as a rumor, and lowers the deterministic
    provider's score for that speaker.
14. The same seed gives identical gossip; save → load mid-week → continue gives identical gossip.
15. Starting the weekend early gives the same gossip as playing the week out.
16. Active, weekly and per-kind caps hold under a flood; a story still going around is reinforced, not duplicated; a scandal still seeds.
17. A quote is one capped line without raw quote marks; secondhand versions drop the words.
18. A 4-week bot semester seeds ≥ 6 stories, keeps the caps, keeps word of mouth within ±4, and every knower of a story about the
    players has a social record in the store.
19. A gossip round costs < 25 ms of game rules with 64 NPCs and 16 stories, and gossip makes at most one social write per round.
20. Deferred ledger writes produce exactly the same game state and NPC memories as synchronous ones; replaying a batch records nothing.
21. With gossip disabled: no stories, no word of mouth, actions refused.
22. A raided party whose settlement suspends the house is recorded as ended (not cancelled) in the social backend, advances the
    social day, and its raid still becomes a story.
