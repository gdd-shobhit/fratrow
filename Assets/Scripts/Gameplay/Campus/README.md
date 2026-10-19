# Campus NPCs and conversations

This README covers how campus NPCs are authored, where they show up on campus, and how players talk to them. The
weekly loop itself is described in [`Docs/GameLoop.md`](../../../../Docs/GameLoop.md) and
[`design/gdd/core-game-loop.md`](../../../../design/gdd/core-game-loop.md).

## What you get

- **Open campus.** NPCs move between campus locations on server-authored schedules, so every player sees the same NPC in the same place.
  - On party night they stand at whichever house they chose to party at.
  - At your own party they are spawned as guests instead.
- **Conversations are decided by the NPC.** Press **E** on an NPC to open the talk panel (`NpcTalkPanel`). Every option goes to the host's social backend, and the NPC answers from their own persona, voice and memory. The panel offers:
  - ask for their number;
  - invite them to Saturday's party;
  - pitch rush (PNMs only);
  - type anything;
  - leave.
- **What the NPC decides sticks.** That covers RSVPs (yes, no, maybe, or "meet me at the door"), trust and regard, grievances, and rush interest.
  - The local LLM makes these decisions when it's enabled.
  - Otherwise the deterministic fallback provider does.
- **Phone contacts.** The phone's Invite templates for named contacts go through the same backend.

There is no scripted dialogue runtime. Yarn Spinner was removed because static lines contradicted what NPCs remember. If you
later need authored beats such as a tutorial or a cutscene, add them as a separate system. Don't use them for NPC replies.

## Authoring NPCs

| Tier | File | What it holds |
|---|---|---|
| Hand-written | `Assets/Resources/Campus/CampusRoster.json` | 32 NPCs, each with id, gender, archetype (`guestClass`), persona, voice, popularity (0–100), status concern, unlock gates, PNM / underage flags, GPA, rival-house leanings and a weekly schedule. |
| Generated | `Assets/Resources/Campus/PersonalityArchetypes.json` | 16 personality types expanded into 32 more named NPCs. The expansion is deterministic, so the same people come back on every load. Templates use `{name}` `{they}` `{them}` `{their}` `{They}` `{Their}`. |

The rules are enforced by `CampusRoster.Validate()` and covered by `Tests/GameLoopHarness`:

- Personas must be 900 characters or fewer and describe behaviour, not invented history.
- `voice.tone` must be one of casual, assertive, bubbly, dry, reserved, playful or laid-back.
- Each schedule entry has `day` 0 (Monday) to 6, start and end hours from 0 to 24, and a location from the list below.
- PNMs (`isPnm`) are boys.

`NamedNpcDatabase` builds `NamedNpcSO`s from the roster. `PhoneContactDatabase` adds a phone contact for every roster NPC.
ScriptableObject assets under `Resources/NamedNPCs` still override the roster when present.

**To add an NPC:**
1. Add an entry to `CampusRoster.json`, or bump an archetype's `count`.
2. Run `dotnet run --project Tests/GameLoopHarness/GameLoopHarness.csproj`. The first test validates the roster.

## Campus locations

NPC schedules, player locations, crimes and police patrols all use these `CampusLocation.locationId` values:

`PartyHouse`, `Shop`, `Quad`, `Gym`, `Library`, `Dorm`, `House_Kappa`, `House_Theta`, `House_Lambda`

Place `CampusLocation` markers with these ids in `Game.unity`. Any id that is missing gets a fallback marker near the origin, and a
warning is logged. Players count as being "at" a location within 14 m (`NetworkedCampaign.locationRadius`).

## Runtime flow

```text
Player presses E on a NamedNpcVisual
        ↓
NpcTalkPanel (local UI) ──RequestAction / RequestTalk──▶ NetworkedCampaign (host)
        ↓                                                    ↓ validates location / contact / phase (GameSession)
        ↓                                                    ↓ submits as the player to the social service
NPC reply + RSVP tag ◀──OnConversation─────────────── grounded decision committed (reply, trust/regard, RSVP)
                                                             ↓
                                    Saturday: accepted NPCs arrive at your party; declined ones never do
```

| Local only | Server authoritative |
|---|---|
| Talk panel, phone browsing | Clock, schedules and NPC locations, contact unlocks, RSVPs, relationships, rush and bids, party spawning |

## Troubleshooting

| Symptom | Fix |
|---|---|
| No talk panel | Check that `Resources/Dialogue/Dialogue.uxml` and a `PanelSettings` asset exist. Look in the Console for `[NpcTalkPanel]`. |
| NPC never appears | They have no schedule slot at the current day and hour, or their location id is not placed. Check `NetworkedCampusNpcDirector`. |
| "isn't interested in giving … their number yet" | The house's popularity or grade is below the NPC's `unlockMinPopularity` / `unlockMinGrade`. |
| Contact missing on the phone | The roster `displayName` must match the contact name. Contacts are created from the roster automatically. |

## Key scripts

| Path | Purpose |
|---|---|
| `Assets/Scripts/UI/Talk/NpcTalkPanel.cs` | In-person conversation UI (social backend) |
| `Assets/Scripts/Networking/NetworkedCampaign.cs` | Host game loop adapter: actions, conversations, replication |
| `Assets/Scripts/Networking/NetworkedSocialConversation.cs` | Social backend host: identity, memory, providers |
| `Assets/Scripts/Networking/NetworkedCampusNpcDirector.cs` | NPC positions and visuals from the authoritative loop |
| `Assets/Scripts/Gameplay/Campus/NamedNpcDatabase.cs` | Roster → `NamedNpcSO` |
| `Assets/Scripts/Game/Core/CampusRoster.cs` | Roster schema, validation, archetype expansion |
