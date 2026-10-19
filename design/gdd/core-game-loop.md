# Core Game Loop — Semester, Week, Party Night

> **Status**: Draft v1 (2026-10-18) — backend implemented in `Assets/Scripts/Game/Core` (`FratRow.Game`).
> **Tuning**: every number below is a key in `Assets/Resources/Campus/GameLoopConfig.json` unless marked *fixed*.
> **Related**: `Docs/SocialBrains.md` (conversational NPC backend), `design/gdd/campus-roster.md` (NPC roster),
> `docs/architecture/adr-0001-unity-free-game-core.md` (why the rules live outside MonoBehaviours).

## 1. Overview

FratRow is a co-op multiplayer college-life game. A lobby of friends are brothers in one underdog
fraternity on an open campus. Over a **semester** of **weeks**, they compete against three AI rival
houses to become the top fraternity. Every week has the same rhythm. A compressed **weekday** prep phase
covers Monday to Friday: talk to and invite NPCs, rush pledges, shop for supplies, study, work, and
sabotage rivals. It is followed by **Saturday party night**, when every house throws a party at once and
the players run theirs live. **Sunday debrief** then scores the parties, hands out Greek Cup points, settles
cash, heat and the Dean, and runs Bid Day.

Named NPCs are conversational. Each has a persona, voice, memory and relationships held by the host's
social backend. What players say to them decides who comes to which party and who pledges which house.
Trouble is the double-edged sword: wild parties score well, but heat brings roaming police, raids, fines
and Dean probation.

## 2. Player Fantasy

- *"We're the underdogs."* You start as the least popular house on campus (popularity 5 vs rivals
  25–60) and climb the weekly rankings together.
- *"Every week is a heist."* The weekday is a planning montage with a clock running: who do we invite,
  who goes to the shop, who pranks Kappa's lawn while the cops are across campus.
- *"Saturday night is chaos we barely control."* Kegs run dry, fights break out, an underage freshman
  is drinking, the neighbours call the cops. Someone has to card people at the door. Someone has to
  talk to the officer.
- *"These people remember us."* Ashley remembers that Tyler insulted her. Nina saw you trash the
  Lambda house. Chloe only comes if your house is worth being seen at. Talking matters.
- *"Our roles matter."* The President smooth-talks the Dean, the Social Chair throws the party, the
  Rush Chair lands pledges, the Treasurer stretches the budget, and the Risk Manager keeps everyone out of jail.

## 3. Detailed Rules

### 3.1 Time structure

| Unit | Contents |
|---|---|
| Semester | `semesterWeeks` weeks (default 6), then **SemesterEnd** results. The host may start the next semester. Cup points reset, and popularity regresses toward each house's base. |
| Week | **Weekday** (Mon–Fri) → **Party** (Sat night) → **Debrief** (Sun) |

**Weekday (compressed).** Five in-game days, each running `weekdayStartHour`→`weekdayEndHour` (08:00→20:00)
in `weekdaySecondsPerDay` real seconds (default 120 s, so the whole week takes 10 min). `dayOfWeek` runs
0..4, so existing NPC schedules (`ScheduleEntry.dayOfWeek/startHour/endHour`) keep working. Each day
boundary runs the **daily tick** (§3.9). If every connected player is ready, the host may start the weekend
early. The skipped days still run their daily ticks, so the rules stay the same.

**Party (Saturday).** `dayOfWeek = 5`. The clock runs `partyStartHour`→`partyEndHour` (21:00→03:00) over
`partySeconds` real seconds (default 480 s). The simulation ticks every `partyTickSeconds` (default 5 s).
All four houses party at the same time. The players' party is simulated guest by guest and spawned in the
world. Rival parties are simulated abstractly (§3.7).

**Debrief (Sunday).** `dayOfWeek = 6`. Settlement (§3.8) runs once on entering. The phase lasts
`debriefSeconds` (default 60 s), or less if the host continues. After the last week's debrief comes
**SemesterEnd**.

### 3.2 Houses

There are 4 houses: the players' house plus 3 rivals, defined in config (`houses[]`). Each house has a
**personality** (`prestige`, `chaos`, `aggression`, `caution`, 0–1) that drives its AI. Per-house state:

| Field | Range | Meaning |
|---|---|---|
| `popularity` | 0–100 | Public campus buzz. Same scale as NPC popularity, and fed to the NPC social context. |
| `cash` | ≥ 0 | Treasury. |
| `heat` | 0–100 | Police and administrative attention. |
| `dean` | 0–100 | Standing with the Dean of Students. |
| `members` | int | Players plus NPC brothers (recruits). |
| `gpa` | 0–4 | House GPA (players' study plus members' base GPA). |
| `cupPoints` | int | Greek Cup points this semester. |
| `rating`/`grade` | 0–100 / A–F | Derived (§4.1). |
| `probation` / `suspended` | bool | Dean sanctions for next week (§3.8). |
| `trashed` | bool | House was pranked and not cleaned (party vibe penalty). |

Default rivals: **Kappa Sigma Psi "The Yacht Club"** (prestige, rich, cautious), **Theta Delta Chi
"Animal House"** (chaos, aggressive), and **Lambda Nu Tau "The Brain Trust"** (cautious, Dean's
favourite). Each house has a home `locationId` on campus: `PartyHouse` for players, plus
`House_Kappa`, `House_Theta` and `House_Lambda`.

### 3.3 Players and roles

Every connected player is a brother in the players' house, identified by a durable host-issued `playerId`
(the social backend credential, §6). Each player holds one **role**, and each role except Brother is unique.

| Role | Perks (config `roles[]`) |
|---|---|
| President | Only role that can `meet_dean`. +25 % `talk_to_police`. Can start the weekend early. |
| Social Chair | Only role that can `set_theme`. DJ hype ×1.5. Filler arrivals +10 %. |
| Rush Chair | `rush_talk` interest ×1.5. Can `extend_bid` (the President can too). |
| Treasurer | Shop prices −15 %. `work_shift` pay ×1.5. Can `set_cover` (the President can too). |
| Risk Manager | `card_check` effectiveness ×2. Clears incidents faster. +25 % `talk_to_police`. House heat −2/day. |
| Brother | Default role. No perk. |

The initial role comes from the lobby class: `President→President`, `SocialChair→SocialChair`,
`Chad→RushChair`, `RichGuy→Treasurer`. A duplicate becomes Brother. `claim_role` takes a free role during
a weekday.

Per-player state: `role`, `location` (resolved by the host from position, §6), `detainedUntil` (busted
players cannot act), `record` (times busted), `rep` (personal campus rep), `studyThisWeek`, and daily
action counters.

### 3.4 Player actions

The host validates every action: phase, location, role, cost, cooldown or daily cap, and whether the player
is detained. Each action produces an **ActionResult** and usually a **news item**. Location IDs come from
`CampusLocation.locationId`.

**Weekday actions**

| Action | Where | Effect |
|---|---|---|
| `talk` (free text) | in person with the NPC, or text if their contact is unlocked | Conversational turn through the social backend. The NPC decides the RSVP and relationship effects. |
| `invite` | same as `talk` | A canned invitation sent as a conversation turn. |
| `rush_talk` | in person with a PNM | Canned rush pitch turn. Rush interest +`rushTalkInterest` (×role). |
| `extend_bid` | anywhere | Queue a bid to a PNM for Bid Day. |
| `buy` item×qty | `Shop` | Treasury −price, stash +qty. |
| `work_shift` | `Shop` | Treasury +`workShiftPay`, capped at 1/day. |
| `study` | `Library` | GPA progress, capped at 2/day. |
| `workout` | `Gym` | Personal rep +1, capped at 1/day. |
| `volunteer` | `Quad` | Dean +3, heat −5, capped at 1/day. |
| `meet_dean` | `Quad` | President only, 1/week. Dean +6, and a pending probation may be lifted. |
| `set_theme` | `PartyHouse` | Social Chair or President. Sets this week's theme. |
| `set_cover` | anywhere | President or Treasurer. Cover charge 0–20. |
| `clean_house` | `PartyHouse` | Clears `trashed`, heat −3. |
| `prank` rival | rival house | Crime. Needs `prank_kit` (§3.6). |
| `spread_rumor` NPC, rival | in person with the NPC | Crime-lite. Lowers the NPC's regard for that rival. |
| `steal_mascot` rival | rival house, evenings (17–20) | Crime. Popularity +4 if not caught. |
| `claim_role` | anywhere | Take a free role. |
| ready | anywhere | Existing ready-up. When all are ready, the host may start the weekend. |

**Party-night actions** (at `PartyHouse` unless stated)

| Action | Effect |
|---|---|
| `serve_drinks` | Speeds drinking. Mood up. |
| `card_check` (toggle) | Underage guests can't get alcohol. Their mood drops slightly. |
| `dj` | Needs speakers. Hype up. |
| `turn_down_music` | Noise and hype down. Police attention falls faster. |
| `break_up_fight` / `help_guest` / `fix_damage` | Resolves the matching incident. |
| `kick_out` | Removes the drunkest troublemaker. |
| `beer_run` | Buys a keg mid-party at a 25 % markup. The player is away for 3 ticks. |
| `talk_to_police` | During a police warning or raid. Chance to downgrade a raid to a warning. |
| `call_cops_on` rival | From anywhere. Crime-lite. Rival attention +25, with a trace risk. |
| `stink_bomb` rival | At the rival house. Crime. Needs `stink_bomb`. |
| `steal_keg` rival | At the rival house. Crime. Stash +1 keg. |
| `guard_house` | At `PartyHouse`. Blocks the next rival sabotage against your party (weekday evening or party). |

**Talking is always available.** `talk`/`invite` route through `NetworkedSocialConversation.SubmitLocalAsync`.
The social backend journals the speech, asks the provider for social acts, validates them, and commits
reply, relationship effects and RSVP atomically. With no model loaded, a deterministic heuristic provider
emits valid grounded acts (§6.2).

### 3.5 Shop and stash

The shop sells from `shop[]`. Purchases go into the house **stash** (shared, persisted) and are consumed by
party mechanics. Default catalogue:

| Item | Price | Party effect |
|---|---|---|
| `keg` | 60 | 40 alcoholic servings (strength 0.10) |
| `liquor` | 90 | 25 servings (strength 0.25) |
| `soda_snacks` | 20 | 30 non-alcoholic servings. Mood up. Reduces passing out. |
| `speakers` | 80 | Enables `dj`. Base hype +15. Noise +10. (Persistent: not consumed.) |
| `decorations` | 40 | Vibe +10 for one party. Doubled when matching the theme. |
| `security` | 70 | One party: automatic card check at 60 %. Trouble −20 %. |
| `prank_kit` | 30 | Consumed by `prank`. |
| `stink_bomb` | 25 | Consumed by `stink_bomb`. |
| `cleanup_crew` | 50 | Debrief: heat −8 and clears `trashed`. |

The existing cosmetic shop (`ShopOwner`/inventory) is separate, and personal items are unchanged.

### 3.6 Crimes, witnesses, police, getting busted

**Crimes**: `prank`, `steal_mascot`, `stink_bomb`, `steal_keg` (full), and `spread_rumor`, `call_cops_on`
(lite). Resolving a crime takes three steps:

1. **Witnesses.** Named NPCs scheduled at that location and hour witness it. The host records
   `SocialWorld.Observe(kind, playerId, rivalHouse, text, location, witnesses…)`, so those NPCs remember
   *who* did it. It enters their conversation memory and can become a grievance.
2. **Detection.** The chance is `p = crimeBase + patrolHere + witnesses×witnessFactor − riskMgrBonus`
   (§4.4). On detection the player is **busted**: fine `bustFine` from the treasury, heat +`bustHeat`,
   dean −`bustDean`, player `record`+1, and the player is detained for `detainSeconds` and cannot act.
3. **Effect.** The effect applies only if the crime was *not* detected. If detected, the act is foiled.

**Police patrols.** The number of units is `1 + floor(maxHeat / patrolHeatStep)` (max `maxPatrols`), +1 on
party night. Each unit travels between campus locations (`patrolTravelSeconds` per hop). It chooses the
next stop weighted by the heat of the house at that location (`patrolHeatBias`), plus uniform randomness.
The host replicates unit positions, and clients render them as police NPCs. At heat ≥ `copsParkedHeat`
during a weekday, a unit loiters at `PartyHouse` ("cop car outside").

**Party attention and raids.** During the party, the house's **attention** rises from heat, noise, open
incidents and underage drinking (§4.5). At `attention ≥ warningThreshold`, a unit is dispatched. When it
arrives, if attention is still ≥ `raidThreshold`, a **raid** happens. Otherwise it is a **warning**: heat
+5, and it leaves. A `talk_to_police` success downgrades a raid to a warning, once per party.

**Raid**: the party ends immediately. Fines are `underageFine×underageDrinkers + noiseFine + incidentFine×openIncidents`.
Heat +`raidHeat`. Dean −`raidDean`. Party score ×`raidScoreFactor`. Popularity −`raidPopPenalty`, or
+`infamyBonus` instead if fun ≥ `infamyFun`. One player who served an underage guest that night is
cited (busted).

### 3.7 Rival AI

**Week start.** Each rival picks a plan:

- Party budget: `cash × (0.15 + 0.25×prestige)`.
- A theme.
- Sabotage intent: `aggression × (1.3 if the players rank above them or within 10 popularity, else 1) × (1 − caution × heat/100)`.

**Daily.** With probability `sabotageIntent × rivalDailySabotage`, the rival sabotages its target. The
target is usually the players' house, otherwise the next house up. The actions are:

- `prank`: target `trashed`, popularity −2.
- `rumor`: up to 2 random NPCs witness a rival badmouthing the house, and their rush interest in it −5.
- `poach`: a rival's PNM interest +8.

`guard_house` blocks the next one. The players get a news item when it happens, attributed only if a
witness or police saw it.

**Party night.** Each rival may `call_cops` on the players (attention +20) or throw a `stink_bomb` (mood −20,
some guests leave), subject to `guard_house`.

**Abstract rival party score**: popularity, budget, members, chaos, theme, sabotage modifiers and noise
(§4.6). Raid probability comes from heat, chaos and caution.

### 3.8 Debrief settlement (once per week, in order)

1. **Score** each party (§4.6). **Rank** the houses. Award Greek Cup points `cupPoints[rank]` (10/6/3/1).
   On probation the points are ×0.5. A suspended house scores 0.
2. **Popularity**: `rankPop[rank]` + party quality + notable guests + raid/infamy (§4.2). Clamped 0–100.
3. **Cash**: cover charge, plus dues (`dues × members`), plus a sponsorship for 1st place, minus fines.
4. **Heat**: −`weeklyHeatCooldown`, and −8 if `cleanup_crew` was bought.
5. **Dean**: ±GPA adjustment (§4.3). A Dean below `probationDean` puts the house on **probation** next week
   (party capacity ×0.5, points ×0.5). Below `suspensionDean` it is **suspended** (no party, 0 points).
   Probation and suspension last one week and are re-evaluated every debrief.
6. **Bid Day** (§3.10).
7. **Grades**: rating and grade are recomputed for every house (§4.1).
8. **News**: weekly headlines are published. "Campus news" events go to every NPC's memory (public knowledge).
9. **Social sync**: the social party ends (unresolved promises are marked missed, the social `day` advances), a
   new planned party is created for next week, and rival standings are pushed to the NPC context.

### 3.9 Daily tick (each weekday boundary)

Heat −`dailyHeatDecay` (Risk Manager on the roster: −2 more). Patrol count is recomputed. Rival daily
actions run. Daily action counters reset. A **morning briefing** news line summarises the day: confirmed
and undecided RSVPs, rivals' public party themes, where police are watching, a trashed-house reminder, and
"party tomorrow night" on Friday. Named NPCs follow their schedules (existing
`NetworkedCampusNpcDirector`, driven by the core clock).

### 3.10 Recruitment (Rush and Bid Day)

**PNMs** are roster NPCs with `isPnm: true`, plus `walkOnPnms` anonymous walk-ons per week. Each named PNM
has **rush interest** 0–100 per house:

- **Players' house** (§4.7): driven by the NPC's `fraternityRegard` toward the players' house. That value is
  owned by the social backend, so *conversation* moves it. Added on top: `rush_talk`, attending a fun party,
  popularity rank, and witnessed crimes.
- **Rivals**: base from rival popularity × status concern, plus rival effort and noise.

On Bid Day, each PNM considers the houses that bid. The players' house bids are queued with `extend_bid`.
Bid Day opens from week `bidsOpenWeek` + 1 (week 2 by default), so rush spans the semester; bids extended
earlier stay queued. Each rival bids on at most `rivalBidsPerWeek` (2) PNMs with interest ≥ `rivalBidInterest` (50).
The PNM joins the bidder with the highest interest if that interest is ≥ `acceptBidInterest` (60). Otherwise they
stay a PNM. Rush bonuses decay 20 % per week. A new member:

- adds +1 to `members` and to dues;
- stops being a party guest;
- gives +`capacityPerMember` (1) party capacity, counting at most `capacityMemberCap` (20) members;
- contributes their `gpa` to the house;
- updates the existing contact roster `Recruited` flag.

Walk-ons join houses using the formula in §4.7.

### 3.11 Party simulation (players' house; tick every `partyTickSeconds`)

1. **Arrivals**:
   - **Committed named NPCs.** The social backend's `accepted` commitments for this week's party arrive in
     the first `committedArrivalWindow` of the party. Conditional promises resolve at the entrance using the
     existing rule.
   - **Uncommitted named NPCs.** At party start, each named non-member NPC without a commitment chooses a
     destination: the players' party, a rival party, or home. The choice uses house appeal and their regard
     (§4.8). `declined` NPCs never choose the players' party. `deferred` NPCs get a bonus toward it if their
     regard ≥ 0.
   - **Fillers.** Anonymous archetype guests (`GuestClass` stats from `GuestSO`) arrive at
     `fillerRate(appeal) × (1 − fill)` per tick until capacity. Each guest is underage with probability
     `underageShare`.
2. **Drinking**: each guest drinks with probability `drinkChance` while servings remain, preferring alcohol
   unless underage and carded. Drunkenness rises by the serving's `strength`.
3. **Mood** per guest: music/hype, decor and theme, drinks, crowding (best at 60–90 % capacity), trashed
   house, open incidents, stink bomb. Guests with mood < `leaveMood` may leave.
4. **Hype and noise**: hype decays and is boosted by `dj` and speakers. Noise is a function of hype, guest
   count and speakers.
5. **Trouble**: drunk guests × archetype trouble → `trouble` (0–100). Security reduces it.
6. **Incidents** are rolled from state: `fight`, `passed_out`, `noise_complaint`, `property_damage`,
   `keg_dry` and `celebrity_dropin`. Unresolved incidents escalate after `incidentEscalateTicks`:
   - a fight becomes an injury: heat +10;
   - a passed-out underage guest becomes a hospital trip: heat +25, dean −10;
   - property damage costs `damageCost`.
7. **Police**: update attention, dispatch, then warning or raid (§3.6).
8. **End**: at the party end hour, or on a raid. Leftover servings stay in the stash.

The Unity adapter spawns and despawns guests from the core's arrival and departure events, using the
existing `NetworkedPartyManager.SpawnGuest`/`SpawnNamedGuest`. It mirrors party metrics into the existing
`NetworkedParty` NetworkVariables.

### 3.12 SemesterEnd

The final table is sorted by Greek Cup points, with popularity as the tiebreaker. The winner is
"Fraternity of the Year". News is published to all NPCs. The host can `start_semester`: cup points
reset, heat resets to 0, rival popularity regresses 30 % toward base, and members, cash and
relationships persist.

## 4. Formulas

All randomness uses the host's seeded RNG (`seed`, the stream key and the draw index). Results are
reproducible in tests and survive reloads (§5).

**4.1 Rating and grade**

`rating = round(0.45·pop + 0.15·clamp(cash/2000·100) + 0.20·clamp(members/40·100) + 0.20·dean)`

Grade thresholds are unchanged from `FraternityGradeCalculator`: A ≥ 85, B ≥ 70, C ≥ 55, D ≥ 40, E ≥ 25, else F.

**4.2 Weekly popularity delta**

`Δpop = rankPop[rank] + (fun − 50)/10 + Σ(notable guest pop)/notableDivisor + raidTerm`

- A positive Δ is scaled by `max(0.1, 1 − pop/popularitySoftCap)` (soft cap 150) so leaders slow down.
- Rivals then regress `weeklyRegress` (10 %) toward their starting popularity each week.

- `rankPop` defaults to [+8, +4, 0, −3].
- `raidTerm = −raidPopPenalty`, or `+infamyBonus` if fun ≥ `infamyFun`.
- Plus campus buzz from successful sabotage, minus being pranked.

**4.3 GPA and Dean**

- `playerGpa` = 2.0 + `studyGpaPerSession` × min(studyThisWeek, 6). Defaults: 0.25, cap 4.0.
- `houseGpa` = the mean over players and NPC members. Each NPC brother has a base `gpa` from the roster.
- Weekly dean change: −`weeklyDrift` (2), then +5 if houseGpa ≥ 3.0, −5 if < 2.5.
- Rivals recover `deanRecoveryBase + caution × deanRecoveryCaution` (1 + 4·caution) per week; `volunteer` gives +1.5.

**4.4 Crime detection**

`p = crimeBase[crime] + (patrol at location ? patrolHere : patrol arriving ? patrolNear : 0) + witnesses × witnessFactor − (riskManagerOnline ? 0.05 : 0)`

The result is clamped to [0.02, 0.95]. Defaults: `patrolHere` 0.55, `patrolNear` 0.25, `witnessFactor` 0.06.

**4.5 Party attention (per tick)**

`target = 0.35·heat + 0.35·noise + 8·openIncidents + underageAttention·underageDrinkersPresent + calledCops`

`noise = 0.5·hype + noisePerGuest·guests + speakerNoise − (25 if the music was turned down in the last 3 ticks)`;
defaults `underageAttention` 3, `noisePerGuest` 1.2. A unit is dispatched at attention ≥ 40 and raids at ≥ 55.

`attention += (target − attention)·0.25`, then `−6` if `turn_down_music` was used this tick.

**4.6 Party score**

`score = guests·(fun/100) + Σ(top 5 notable guest pop)/notableScoreDivisor + hype/10 − 2·escalated − 0.25·handled`

The score is ×`raidScoreFactor` (0.4) on a raid; probation halves cup points, not the score. `keg_dry` is not a
scored incident (it has its own mood penalty).

Guest mood moves 20 %/tick toward `25 + 0.25·hype + vibe + crowdTerm + 20·min(drunk, 0.8) − 4·openIncidents − stinkBomb`.
`dj` adds `djHype × djMult` at most every `djCooldownTicks` (4); hype decays by `hypeDecay + 4 %` per tick.

Rival parties are abstract but use the same score shape:

- `guests = min(capacity, namedGuests + capacity · clamp(0.2 + pop/200 + budget/3000 + 0.05·theme ± 0.05))`
- `fun = 45 + budget/30 + 15·chaos ± 8 − moodPenalty`, `hype = 20 + 20·prestige + 20·chaos`
- incidents ~ Poisson(4·chaos), of which escalated ~ Poisson(2·chaos·(1 − caution))
- raid chance = 0.05 + heat/250 + 0.08·chaos + attention/150 − 0.05·caution (max 0.8); a raided rival scores ×0.4.

**4.7 Rush interest (players' house)**

`interest = clamp(playerInterestBase + regard/2 + rushBonus + partyBonus + rankBonus − crimePenalty, 0, 100)`, `playerInterestBase` = 25.

Rival interest = `pop · concernWeight · 0.4 + rivalRegard + rushBonus + 2·(houses − rank) + 5`.

- `regard` = the social backend `fraternityRegard` (−100..100).
- `rushBonus` = Σ rush_talk × `rushTalkInterest` × role multiplier.
- `partyBonus` = +10 if the PNM attended and fun ≥ 60, −5 if they attended and fun < 40.
- `rankBonus` = (4 − rank) × 3.
- `crimePenalty` = 6 per witnessed crime by the house.

Walk-ons per week = `round(walkOnPnms × share)`, where `share = (5 − rank) / 10` for the players' house.

**4.8 Destination choice (uncommitted named NPC)**

`appeal(h) = popularityWeight·pop(h) + regardWeight·regard(h) + themeMatch + rng(0, appealNoise)`

`popularityWeight` is 0.4, 0.8 or 1.2 for Low, Moderate or High status concern.

The NPC goes to argmax(appeal) if that appeal is ≥ `stayHomeAppeal`, otherwise they stay home.

**4.9 Filler arrivals**

`appeal = 0.5·pop + hype/4 + decor + 5·confirmedNamed − 10·trashed`

`expected/tick = baseFillerRate · (0.4 + appeal/60) · (1 − guests/capacity) · (socialChair ? 1.1 : 1)`

Arrivals are drawn as `floor(expected) + Bernoulli(frac)`. Each filler's archetype is picked by `GuestSO.baseArrivalChance`.

## 5. Edge Cases

- **Player disconnects mid-week.** Their role is held for their `playerId`. On reconnect (social
  credential) they get it back. Their actions in flight are cancelled, and received speech still commits
  (existing social guarantee).
- **Host reload mid-party.** The core state saves every phase change and every `autosaveSeconds`. On
  reload, the party resumes at the saved tick. Already-arrived guests are re-spawned from the guest list.
  Social commitments are reconciled by the existing resume path.
- **No players at the house during the party.** The simulation still runs. Unattended incidents escalate,
  which is the penalty.
- **Stash empty.** The `keg_dry` incident fires once, and mood falls until a `beer_run`.
- **Suspended house.** Party night still runs for the rivals. The players' party is skipped (no spawning)
  and the HUD shows suspended. Campus actions remain available.
- **Everyone ready during the weekday.** The skipped days' daily ticks run instantly.
- **Busted player.** They are detained for `detainSeconds`. All actions are rejected with `detained`. Talking
  is still allowed (phone).
- **Treasury can't cover a fine.** Cash floors at 0. The remainder becomes a Dean penalty (1 dean per $20).
- **Rival suspended.** Its party score is 0. It still sabotages, at half intent.
- **An NPC's commitment is to this week's party but the NPC was recruited by a rival on Bid Day.** Not
  possible: Bid Day runs after the party. Next week, members are excluded from guest pools.
- **Crime with a detained or absent target** (e.g. `steal_keg` from a rival with no stash). It is still a
  crime (witnesses, detection), with no loot.
- **LLM unavailable or slow.** The heuristic provider is used. The social queue (16) returns `busy`, and the
  action layer surfaces it.
- **Two players claim the same role.** First valid request wins.

## 6. Dependencies

| System | Direction | Contract |
|---|---|---|
| `FratRow.Social` (`SocialWorld`, `SocialConversationService`) | Core → Social | NPC registration, standings (incl. rivals), party lifecycle, commitments, regard, `Observe`/news. |
| `NetworkedSocialConversation` | Adapter | Binds clients to durable `playerId`s and exposes `TryGetPlayerId(clientId)`. Always on. The LLM provider is optional. |
| `NetworkedCampaign` (new, on GameManager) | Adapter | Owns `GameSession` on the host. Ticks it, validates RPC actions, and replicates HUD state, houses, players, patrols, incidents, stash and news. |
| `NetworkedCampusCalendar` | Adapter | Mirrors the core phase, day, week and clock anchor (extended `GamePhase.SemesterEnd`). |
| `NetworkedWeekendReady` | Adapter | The existing ready mask. Start Weekend calls the core. |
| `NetworkedPartyManager` / `NetworkedParty` | Adapter | Spawns and despawns guests from core events. Mirrors metrics. Rewards come from the core. |
| `NetworkedGameManager` | Adapter | `myFraternity`, popularity, cash and trouble mirror the core players' house. |
| `NetworkedContactRoster` | Adapter | Recruited flags from Bid Day. Unlocks unchanged. |
| `NetworkedCampusNpcDirector` | Adapter | Schedules from the roster. Hides NPCs away at parties. |
| `CampusLocation` registry | Adapter | Resolves a player's position to a `locationId`. Adds rival house locations. |
| Roster data | Data | `Assets/Resources/Campus/CampusRoster.json`. Loaded by `NamedNpcDatabase`, `PhoneContactDatabase` and the core. |
| Config data | Data | `Assets/Resources/Campus/GameLoopConfig.json`. Loaded by the core (Unity reads it via `TextAsset`; the harness reads the file). |

### 6.1 Identity

The core uses `playerId` strings. In Unity these are the social backend's host-issued IDs. The headless
harness registers players directly.

### 6.2 Heuristic social provider

`HeuristicSocialModelProvider` implements `ISocialModelProvider` without a model. It classifies the speech
(invitation, rush pitch, insult, apology, compliment, question). It weighs persona tone, status concern,
house versus NPC popularity, trust, regard and grievances, with seeded noise. It then emits grounded acts
through `GroundedSocialTurn.Compose`, so its proposals pass exactly the same validation as the LLM's. It
is the default whenever no model is configured.

## 7. Tuning Knobs

All are in `GameLoopConfig.json`:

- `semesterWeeks`, `weekdaySecondsPerDay`, `partySeconds`, `debriefSeconds`, `partyTickSeconds`, `autosaveSeconds`
- `houses[]` (start stats and personality), `roles[]`, `shop[]`, `themes[]`, `crimes[]`
- Party: `baseCapacity`, `baseFillerRate`, `underageShare`, `drinkChance`, `leaveMood`, `incident*`, `committedArrivalWindow`
- Police: `patrolHeatStep`, `maxPatrols`, `patrolTravelSeconds`, `copsParkedHeat`, `warningThreshold`, `raidThreshold`, fines, `raid*`
- Economy: `dues`, `workShiftPay`, `sponsorship`, `bustFine`
- Heat and Dean: `dailyHeatDecay`, `weeklyHeatCooldown`, `probationDean`, `suspensionDean`, `bustHeat`, `bustDean`
- Rush: `rushTalkInterest`, `rivalBidInterest`, `acceptBidInterest`, `walkOnPnms`
- Scoring: `cupPoints[]`, `rankPop[]`, `notableDivisor`, `raidScoreFactor`, `infamyFun`, `infamyBonus`

## 8. Acceptance Criteria

1. A headless 2-player, 6-week semester runs to SemesterEnd with the deterministic provider. Every week
   produces a debrief with 4 ranked scores and cup points that sum to 20 per week. The same seed gives
   byte-identical results.
2. An NPC that accepts an invitation through conversation arrives at the players' party in the
   committed-arrival window. A declined NPC never arrives.
3. A crime at a location where a scheduled NPC is present creates a social event witnessed by that NPC.
   The NPC's next conversation context includes it.
4. Heat ≥ `raidThreshold` attention with no mitigation raids the party: early end, fines and heat applied.
   `turn_down_music` and `card_check` measurably lower raid frequency over 50 seeded parties.
5. Bid Day converts a PNM with players' interest ≥ `acceptBidInterest` who received a bid into a member.
   Members never spawn as guests.
6. Probation halves capacity and points the next week. Suspension skips the party.
7. Roles enforce exclusivity and perks (Treasurer discount, Rush Chair multiplier, President-only `meet_dean`).
8. State save → load → continue produces the same final table as an uninterrupted run (same seed).
9. All 65 existing social integrity checks still pass. New social changes (rival standings, many NPCs,
   heuristic provider) have their own checks.
10. The Unity adapter assemblies compile against the editor's references (`tools/ci/unity-compile-check.ps1`).
