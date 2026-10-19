# Backend game loop: runbook

This document covers the semester → week → party-night loop, the rivals, roles, economy, crimes, police, rush and the
conversational NPCs. The design is in [`design/gdd/core-game-loop.md`](../design/gdd/core-game-loop.md), and the architecture decision is
[ADR-0001](architecture/adr-0001-unity-free-game-core.md). Everything below is host-authoritative and works headlessly.
The HUD and the dev panel are bindings, not logic.

## How a week plays

| Phase | Real time (default) | What happens |
|---|---|---|
| **Weekday**, Mon–Fri, 08:00→20:00 | 2 min per day | NPCs follow their schedules around campus. Brothers talk to people, invite them, rush pledges, shop for supplies, work shifts, study, volunteer, meet the Dean, set the theme and cover, and prank or rumor rival houses. Every night the rivals may sabotage you, unless someone guards the house. When everyone is ready, the President can start the weekend early. |
| **Party night**, Sat 21:00→03:00 | 8 min, ticking every 5 s | All four houses party. Guests arrive at yours: people who RSVP'd yes, walk-ins who chose your house, and filler archetypes. You run the night: serve drinks, card at the door, DJ, break up fights, help people who pass out, do beer runs, turn the music down. Noise, heat, incidents and underage drinking build police attention. At 40 a unit is dispatched; if attention is still 55 or more when it arrives, it raids. A smooth talk downgrades a raid to a warning, once per party. |
| **Debrief**, Sunday | 1 min (the host can skip) | Parties are scored and ranked, and Greek Cup points awarded (10/6/3/1). Popularity, cash (dues, cover, sponsorship), heat cooldown and the Dean review are applied, with probation or suspension next week. Bid Day runs from week 2. Campus news goes into every NPC's memory. |
| **SemesterEnd**, after 6 weeks | until the President continues | Fraternity of the Year is crowned. Starting the next semester resets cup points; members, cash and relationships carry over. |

The four houses are your underdog **Alpha Beta Rho** (popularity 5) against Kappa Sigma Psi "The Yacht Club"
(60, rich and prestigious), Theta Delta Chi "Animal House" (45, chaotic) and Lambda Nu Tau "The Brain Trust" (25, the Dean's favourite).

Roles come from the lobby class:

| Lobby class | Role | What it does |
|---|---|---|
| President | President | Meets the Dean, starts the weekend, bids, sets cover. |
| SocialChair | Social Chair | Sets the theme; DJ hype ×1.5. |
| Chad | Rush Chair | Rush pitches ×1.5; bids. |
| RichGuy | Treasurer | 15 % shop discount; shift pay ×1.5; sets cover. |
| — | Risk Manager | Claimable. Cards ×2, police talk bonus, heat cooldown. |

## Conversational NPCs

There are 64 people on campus. 32 are hand-written in `Resources/Campus/CampusRoster.json`. The other 32 are generated from 16
personality types in `PersonalityArchetypes.json`, expanded deterministically so the same people return on every load.
Every one of them has their own persona, voice, schedule, popularity, status concern and memory in the host's
social backend.

What players say decides RSVPs (accept, decline, defer, or "meet me at the door") and changes relationships. Grievances
come from insults and from crimes the NPC witnessed. Rush interest follows the NPC's regard for your house, and
NPCs know the public standings of all four houses.

**Model.** By default the host uses the deterministic `HeuristicSocialModelProvider`, which works in every build.
Enable the local LLM with the existing editor menu **FratRow → Social Brains → Enable development experiment
(CUDA fallback)**, or with `--social-brains --social-llama-server <exe>` on development players. Both providers go through the same
grounded validation and commit path. Phone **Invite** templates for named contacts now become real invitations,
and the old contact-roster recruit request now extends a Bid Day bid. Pressing **E** on an NPC opens the talk
panel (`NpcTalkPanel`). It offers asking for a number, inviting them, a rush pitch (PNMs only), free text, and leaving, and every
reply comes from the social backend. Yarn Spinner has been removed: no scripted lines can contradict what an NPC remembers.

## Campus gossip

Campus talks. A raid, a fight, a freshman in the hospital, a celebrity at your party, a brother getting busted, a prank someone saw,
an insult that actually hurt someone, a broken "meet me at the door" promise, a big pledge, a Dean sanction, or a house shooting up
or crashing down the cup table each becomes a **story**, but only if it clears a significance gate. Small talk and small slights never
do. Stories spread twice a day (10:00, 15:00) and twice at Sunday brunch, one hop at a time. They travel between people who are actually together:
the same spot on their schedule, the same clique, house brothers, close friends, people who partied at the same house. Nasty stories need
a close tie. Names fade ("one of the ABR guys"), details get exaggerated, and tellers stop once everyone they meet already knows. NPCs
act on what they **believe**. It changes where they party, how interested pledges are, whether they'll give you their number, and
how a conversation goes, since gossip about you is in their memory. Every Sunday, campus word of mouth adds up to ±4 to each house's popularity.

You can `ask_gossip` (hear someone's version, with story ids), `deny_rumor` (works on believers, backfires on people who hadn't
heard, never works on witnesses), `trace_rumor` (who told them?) and `expose_rumor` (blow up a rival's traced lie). `spread_rumor` now
plants a real story the listener can pin on you, and rivals trace and expose those. You get a rumor-mill digest every Sunday, a
"Word on campus" line every morning and an alert when a story about anyone goes viral. Full rules: [`design/gdd/campus-gossip.md`](../design/gdd/campus-gossip.md).

## Scene checklist (one-time)

1. **GameManager.prefab** needs `NetworkedCampaign` alongside the existing campus and social components.
   **FratRow → Social Brains → Bake and validate network components** adds it and removes the duplicate
   `NetworkedPhoneInvites` from PartyManager.
2. **Campus locations.** `Game.unity` has all nine `CampusLocation` markers under `CampusLocations`, on the Terrain:
   `PartyHouse` at the spawn and party door, `Shop` beside the shopkeeper, `House_Theta` at the blue house, `Quad`, `Library`
   and `Gym` east of the party house, `Dorm` and `House_Kappa` to the north, and `House_Lambda` to the south-west. Move a
   marker to move everyone who goes there. Any id that is missing gets a fallback object near the origin, and a warning
   is logged. Players count as "at" a location within 14 m (`NetworkedCampaign.locationRadius`).
3. **Party entrance.** PartyManager persists from the menu, so it resolves the Game scene's `GuestSpawnLocation`
   object at runtime (falling back to the `PartyHouse` marker). Guests spawn there. "Meet me at the door" means standing
   within 4 m of it.
4. The **HUD** (`GameplayUIManager` + `HUDVisualTree.uxml`) binds to `NetworkedCampaign` automatically.

## Starting a session (menu → lobby → game)

1. In the **Main menu**, **Start** initialises Unity Gaming Services, creates a Relay session (which starts the NGO host), and
   loads the **Lobby**. Friends use **Join** with the session code.
2. In the **Lobby**, everyone pick a class (it becomes your frat role) and press **Ready**; Ready toggles. When everyone is
   ready, the host presses **Start Game**, and NGO loads **Game** for everybody.
3. The campaign, NPC visuals and campus locations start only once **Game** is active. GameManager persists from the menu, but
   the week clock doesn't run in the lobby.

If **Start** logs `Failed to initialize services`, the log now lists the inner causes. The usual one is an editor project that
isn't linked to Unity Cloud. Fix it in **Edit → Project Settings → Services** by linking the existing **fratrow** project
(org `shobhitdhamania`). In the editor and development builds, a failed Start falls back to a **local host** over direct
transport, and the lobby code shows **LOCAL**. Others on the same machine (for example a ParrelSync clone) or LAN join by
typing `LOCAL` or the host's IP into the Join field.

## Driving and testing it

- **In play mode (development builds):** press **F1** for the campaign dev panel. It offers every action for the
  current phase, plus NPC Talk, Invite, Rush, Ask number and Bid.
- **Two local clients:** `Tests/SocialHarness/run-two-clients.ps1` and the inbox driver now accept campaign commands.
  `{"requestId":"1","action":"game","text":"buy","item":"keg","amount":2}` runs as that client. `game_dump` (host only)
  writes the whole campaign state.
- **Headless tests:** `dotnet run --project Tests/GameLoopHarness/GameLoopHarness.csproj` runs 23 rule and integration
  tests. They cover RSVP arrivals, conditional entrance promises, witnesses entering memory, raids and their
  mitigation over 30 seeds, insults creating grievances and withdrawn RSVPs, Bid Day, probation and suspension, roles, busts and detention, police parking, save/load
  determinism, the fallback provider on all 64 NPCs, rival standings in NPC context, rival sabotage, and semester rollover. It
  also runs the 27 campus-gossip tests (gossip GDD §8). Add `-- --gossip` to run only those.
- **Gossip cost:** `... -- --profile-gossip --weeks 6 --seed 61` plays the same bot semester with gossip off and on. It reports wall
  time, social transactions, state and journal size, time spent in gossip writes and reads, and one round's host-frame cost,
  synchronous vs deferred.
- **Whole semesters:** `dotnet run --project Tests/GameLoopHarness/GameLoopHarness.csproj -- --simulate --weeks 6 --players 4 --seed 7 [--reckless]`
  writes `Library/GameLoopHarness/report.md`. Add `--model <gguf> --llama-server <exe>` to use the real local model.
- **One party, tick by tick:** `... -- --debug-party --heat 60 --pop 45`.
- **Social integrity suite (existing):** `dotnet run --project Tests/SocialHarness/SocialHarness.csproj` runs the 65 checks.
- **Compile against the open editor's references:** `tools/ci/unity-compile-check.ps1`. From a git worktree, pass
  `-RepoRoot <worktree> -UnityProjectDir D:\fratrow` to borrow the main checkout's `Library` (fixed: the flag used to be ignored).

## Tuning

All numbers live in `Assets/Resources/Campus/GameLoopConfig.json`. Missing keys fall back to the code defaults in
`GameConfig.cs`. To regenerate the file from the defaults, run `... -- --export-config`. Check every change with `--simulate`
on a few seeds. With the shipped values, a well-played crew climbs from 4th to 2nd during semester 1, Kappa remains the
semester boss, and reckless crews get raided, then put on probation, then suspended.

## Saves

Saves live in `Application.persistentDataPath/SocialCampaigns/fratrow-main/`:

- `social.journal` and `social.snapshot`: NPC memory and relationships.
- `game/game.json` and `game.json.bak`: the campaign. The file is checksummed and written atomically, and the host autosaves every 30 s and on every phase or day change.

Reloading mid-party respawns the guests. To start fresh, use **FratRow → Campaign → Reset local campaign save**.

## Known limits / not yet verified

- The loop has not been played in Unity yet. The NGO adapters, replication, prefab bake and HUD compile against the
  editor's references, but host plus two clients must still play one full week in the editor. NGO RPC code generation
  (ILPP) is not exercised by the headless compile check.
- Rival parties are abstract (scored, not walkable). Rival houses are sabotage targets and places where NPCs
  can witness you.
- The fallback provider's replies come from the same small grounded phrase banks as the model's. They are functional, not
  literary. Human review of NPC judgment is still outstanding (see `Docs/SocialBrains.md`).
- The social journal is never compacted, so long campaigns grow it (an existing v1 limitation). Every transaction appends the
  *whole* state. Measured in a 6-week bot semester (`--profile-gossip`): 746 MB with gossip off and 1.1 GB with gossip on (state 1.2 MB
  vs 1.6 MB). Compaction (truncate the journal after a verified snapshot) is the top infrastructure follow-up. Gossip already
  batches its writes (ADR-0002).
- Gossip actions have no Unity UI yet (not even F1 panel buttons). They already work through `NetworkedCampaign.RequestAction`
  (any action id goes to the core) and the inbox driver's `game` command. The
  host should build the bridge with `new SocialWorldBridge(world, deferGossipWrites: true)` and call `WaitForGossipWrites()` before
  saving or quitting. Otherwise each gossip round's journal write (tens of ms late in a semester) lands on the host frame.
