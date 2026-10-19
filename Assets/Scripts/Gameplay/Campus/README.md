# Campus NPCs & Dialogue

How to add a named campus NPC with Yarn dialogue, and what the engine does at runtime.

## What you get

- **Weekday campus loop** in `Game` scene: roam, meet NPCs, unlock contacts on the phone
- **Yarn Spinner** dialogue (UI Toolkit presenter)
- **Server-authored schedules**: everyone sees the same NPC at the same location
- **Shared frat unlocks / affinity / recruit** over the network
- **Weekend**: Ready on phone → host Start Weekend → party (named invites can show up)

---

## Engine setup (do this once)

### 1. Packages / compile

Yarn Spinner lives at `Packages/dev.yarnspinner.unity` (patched for Unity 6.5 `EntityId` APIs). Tests are under `Tests~` so they are ignored.

Open the project in Unity and wait for scripts to compile. If Burst complains about `YarnSpinnerTests`, reimport or clear `Library/Bee`.

### 2. Network bootstrap (automatic)

When `NetworkedGameManager` wakes up (before spawn), it adds:

| Component | Role |
|-----------|------|
| `NetworkedCampusCalendar` | Weekday / Party / Debrief + in-game clock |
| `NetworkedContactRoster` | Shared unlocks, affinity, recruits |
| `NetworkedWeekendReady` | Phone ready-up / host start |
| `NetworkedPhoneInvites` | Pending party invites |
| `CampusSystemsBootstrap` | Dialogue host + default locations |
| `NetworkedCampusNpcDirector` | Schedules + local NPC visuals |

You do **not** need to place these by hand if GameManager is in the networked Game session.

### 3. Dialogue UI assets

Must exist under Resources:

```
Assets/Resources/Dialogue/Dialogue.uxml
Assets/Resources/Dialogue/Dialogue.uss
Assets/Resources/Dialogue/CampusNPCs.txt   ← Yarn source (TextAsset)
```

`CampusDialogueService` loads every `TextAsset` in `Resources/Dialogue` whose text contains `title:`, compiles them at runtime, and runs nodes by name.

### 4. Campus locations

Default locations are created at runtime if none exist:

| `locationId` | Meaning |
|--------------|---------|
| `PartyHouse` | House / party |
| `Shop` | Shop |
| `Quad` | Quad |
| `Gym` | Gym |
| `Library` | Library |
| `Dorm` | Dorm |

**Recommended:** place empty GameObjects in `Game.unity` with `CampusLocation` + a parent `CampusLocationRegistry`, and set matching `locationId`s to real transforms (shop, gym, etc.). Runtime fallbacks sit at placeholder positions near origin.

### 5. Play mode checklist

1. Host (or host + clients) into **Game**
2. Session starts in **Weekday** (not party)
3. Capsule / prefab NPCs appear at scheduled locations
4. Interact (raycast / E) → dialogue UI
5. Phone **T** → contacts / invites / **Ready for Weekend**
6. Host **Start Weekend** when everyone is ready → party

---

## Add a new NPC (authoring checklist)

### Step A — Phone contact

Contacts are seeded in `PhoneContactDatabase` (or as `ContactSO` assets under `Resources/Contacts`).

The NPC’s `contactDisplayName` / `displayName` must match the contact `displayName` exactly (e.g. `"Nova"`).

If you only use runtime seed, add a line in `SeedDefaultContacts()`:

```csharp
AddContact(MakeContact("Nova", GuestClass.Nerd, ContactGender.Girl, unlocked: false));
```

### Step B — Named NPC data

**Option 1 (quick):** extend `NamedNpcDatabase.SeedDefaults()`.

**Option 2 (preferred):** create an asset:

`Create → Campus → Named NPC`  
Save under `Assets/Resources/NamedNPCs/` so it loads automatically.

Fill:

| Field | Notes |
|-------|--------|
| `npcId` | Stable id, e.g. `Nova` (used in Yarn commands) |
| `displayName` | Shown in world / logs |
| `contactDisplayName` | Must match phone contact name |
| `yarnNodeName` | Yarn `title:` to run (defaults to `npcId`) |
| `guestClass` | Archetype DNA for party fillers / stats |
| `rarity` | Common / Rare / Legendary |
| `unlockMinPopularity` | Frat popularity gate |
| `unlockMinGrade` | Best grade allowed as minimum (A best … F worst) |
| `unlockChance` | 0–1 roll after gates pass |
| `isRecruitable` | Allows `<<recruit>>` |
| `recruitMinAffinity` | Affinity needed to recruit |
| `schedule` | List of day/time → `locationId` |

**Schedule entry**

| Field | Notes |
|-------|--------|
| `dayOfWeek` | 0 = Mon … 6 = Sun |
| `startHour` / `endHour` | In-game hours (0–24). Overnight: start > end is OK |
| `locationId` | Must match a `CampusLocation.locationId` |

Example: Nova at Library Mon–Fri mornings:

```
dayOfWeek 0–4, startHour 9, endHour 13, locationId = Library
```

### Step C — Yarn dialogue

Edit `Assets/Resources/Dialogue/CampusNPCs.txt` (or add another `.txt` in that folder).

Minimal pattern:

```yarn
title: Nova
---
<<if $can_unlock == false>>
Nova: Not interested. Build your house first.
<<jump Nova_Done>>
<<endif>>

Nova: Oh, you're from the frat?
-> Can I get your number?
    Nova: Fine. Don't spam me.
    <<unlock_contact Nova>>
-> Just saying hi.
    Nova: Cool.
===

title: Nova_Done
---
===
```

**Rules**

- Node `title:` must match `yarnNodeName` / `npcId`
- Character lines: `Name: text`
- Choices: `-> option text` then indented replies
- Engine sets these before start:
  - `$can_unlock` (bool) — popularity / grade gates
  - `$npc_id` (string)
  - `$affinity` (number)
- Commands (registered in `CampusDialogueService`):
  - `<<unlock_contact Nova>>` → server validates rarity roll + gates, shared unlock
  - `<<recruit Nova>>` → server requires unlocked + recruitable + affinity

Declare vars once at top of the file (already present):

```yarn
title: Declarations
---
<<declare $can_unlock = false as bool>>
<<declare $npc_id = "" as string>>
<<declare $affinity = 0.0 as number>>
===
```

### Step D — Visual (optional)

`NetworkedCampusNpcDirector` defaults to a capsule visual. Assign a `visualPrefab` on the director (on GameManager at runtime) to use a custom mesh. Network components on that prefab are stripped; visuals are **local** and driven by replicated state.

---

## Runtime flow

```text
Player interacts NamedNpcVisual
        ↓
CampusDialogueService.StartNamedNpcDialogue(npc, canUnlock)
        ↓
Yarn node runs (local UI)
        ↓
<<unlock_contact>> / <<recruit>>  →  ServerRpc on NetworkedContactRoster
        ↓
PhoneController.ApplySharedRoster  →  contacts appear on all players' phones
        ↓
Invite from Messages  →  NetworkedPhoneInvites
        ↓
Weekend start  →  SpawnGuests prefers named invite + affinity RSVP
```

**Networking split**

| Local only | Server authoritative |
|------------|----------------------|
| Dialogue UI, phone browsing | Phase, clock, NPC positions/schedules |
| Yarn playback | Unlock, recruit, affinity, invites, party spawn |

---

## Weekend / party hooks

- Phone Home: **Ready for Weekend** / host **Start Weekend**
- `Interact_Start` world button does **not** start party during Weekday
- On party end: invites cleared, day advances, `CampusPersistence` writes unlocks/affinity/day to `persistentDataPath/campus_save.json`

Outreach bias uses `OutreachSettings` (Resources `Campus/OutreachSettings` or runtime defaults): popularity × grade × avg affinity × invites.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| No dialogue UI | Ensure `Resources/Dialogue/Dialogue.uxml` + PanelSettings resolve; check Console for `[CampusDialogue]` |
| Yarn compile errors | Check `title:` / `===` / declare block; look for `[RuntimeYarn]` logs |
| NPC never appears | Schedule day/hour mismatch, or missing `locationId` on registry |
| Unlock always rejects | Raise frat popularity / grade, or lower NPC `unlockMin*` / raise `unlockChance` |
| Contact missing on phone | `contactDisplayName` ≠ contact `displayName` |
| Burst / YarnSpinnerTests | Keep tests in `Tests~`; reimport package if needed |
| Party starts from world button | Expected only outside Weekday; use phone Ready/Start |

---

## Key scripts

| Path | Purpose |
|------|---------|
| `Assets/Scripts/UI/Dialogue/CampusDialogueService.cs` | Yarn runner + commands |
| `Assets/Scripts/UI/Dialogue/UITKDialoguePresenter.cs` | Dialogue UI |
| `Assets/Scripts/Gameplay/Campus/NamedNpcSO.cs` | NPC definition |
| `Assets/Scripts/Gameplay/Campus/NamedNpcDatabase.cs` | Load / seed NPCs |
| `Assets/Scripts/Networking/NetworkedCampusNpcDirector.cs` | Schedules + visuals |
| `Assets/Scripts/Networking/NetworkedContactRoster.cs` | Unlock / affinity / recruit |
| `Assets/Scripts/Networking/NetworkedWeekendReady.cs` | Ready / start weekend |
| `Assets/Scripts/UI/Phone/PhoneView.cs` | Phone UI incl. weekend buttons |
