# Fraternity Grade Loop — Scene Setup

What you need in Unity so grade / rating / HUD / party settle actually run. Most of this is already in the project; this is the checklist to verify and finish wiring.

---

## Already automatic (no scene work)

These happen in code once `NetworkedGameManager` is spawned:

- Frat init on server: name `"Frat Test Name"`, grade **C**, rating **50**
- Campus helpers attached on GameManager Awake (`NetworkedContactRoster`, calendar, phone invites, etc.)
- Party end: cash + pop + survive-chaos pop boost + trouble ledger → `RecalculateFraternityGradeServer()`
- Recruit: bumps `memberCount` → grade recalc
- Guest spawn: arrival chance biased by current frat grade

---

## 1. Managers (MainMenu → DDOL)

**Scene:** `Assets/Scenes/MainMenu.unity` (or wherever you host/join)

Ensure these prefabs are in the session and get spawned / `DontDestroyOnLoad`:

| Object | Prefab | Must have |
|--------|--------|-----------|
| GameManager | `Assets/Prefabs/GameManager.prefab` | `NetworkObject` + `NetworkedGameManager` with **Player Classes** and **Guest Classes** lists filled |
| PartyManager | `Assets/Prefabs/PartyManager.prefab` | `NetworkObject` + `NetworkedPartyManager` + `NetworkedPhoneInvites` |

`NetworkedGameManager` inspector:

- Assign every `PlayerClassSO` / `GuestSO` you use (already set on the prefab — don’t clear them).

Optional: change default frat name in code field `fratName` on `NetworkedGameManager` (currently private `"Frat Test Name"`), or set `myFraternity.fratName` from lobby later.

---

## 2. Game scene — required objects

**Scene:** `Assets/Scenes/Game.unity` (prefer this over legacy `Game1`)

### HUD

1. GameObject **HUD** with:
   - `UIDocument`
     - **Panel Settings:** `Assets/Scripts/UI/UI Toolkit/PanelSettings.asset`
     - **Source Asset:** `Assets/Scripts/UI/UI Toolkit/HUDVisualTree.uxml`
   - Script: `GameplayUIManager` (file `GameplayUI.cs` — class name is `GameplayUIManager`)

2. Confirm UXML has the frat panel elements (already in tree):
   - `FratName`, `FratGrade`, `FratRating` under `FratIdentity`

If the HUD still points at an old UXML or missing `GameplayUIManager`, reassign Source Asset and component.

### Party runtime

| Object | Notes |
|--------|--------|
| **NetworkedParty** | Prefab `Assets/Prefabs/NetworkedParty.prefab` — must exist in the game session (scene or spawned). It registers itself with `NetworkedPartyManager`. |
| **GuestSpawnLocation** | Empty Transform where guests appear. |
| **PartyManager.guestSpawnLocation** | Drag that Transform into the PartyManager instance (prefab field is often **None** — fix in scene or override). |
| **Interact_Start** | World interactable that calls `NetworkedPartyManager.Instance.StartPartyServer()` so a party can start/end and settle grade. |

Without a started → ended party, grade stays at the initial C/50 (unless you recruit).

### Guests

- Each `GuestSO` in GameManager’s list needs a valid **networked guest prefab**.
- Spawn uses that prefab at `guestSpawnLocation`.

---

## 3. Build / flow checklist

1. Main menu → Host or Join (UGS + NGO as usual).
2. Lobby → Start → load **Game** scene.
3. Confirm in Hierarchy (runtime): `GameManager` and `PartyManager` still alive (DDOL).
4. Interact to **start party**; let it run until it **ends** (timer / `EndPartyServerRPC`).
5. Console should log something like:
   - `[NetworkedParty] Survived chaos boost...` (if trouble &gt; 0)
   - `[Fraternity] Grade X rating N ...`
6. HUD frat panel should update **name / grade badge / Rating N**.

---

## 4. Quick verification matrix

| Want to see | Do this |
|-------------|---------|
| Grade panel visible | HUD Source = `HUDVisualTree.uxml` + `GameplayUIManager` |
| Rating moves after party | Start + finish a party (not just open HUD) |
| Survive-chaos boost | Party ends with `_troubleMaker` &gt; 0 |
| Arrival bias from grade | Higher grade → slightly higher filler/invite rolls (check spawn logs) |
| Member count helps grade | Recruit a campus contact, then recalc (auto on recruit) |

---

## 5. Common misses

1. **`guestSpawnLocation` unset** on PartyManager → guests fail to spawn / party metrics stay empty.
2. Using **Game1** with old `PartyManager` (non-networked) → grade settle never runs.
3. HUD script missing / wrong UXML → no `FratName` / `FratGrade` / `FratRating` queries.
4. GameManager guest/player SO lists empty → spawn dictionary empty.
5. No **NetworkedParty** in session → `StartPartyServer` has nothing to start.

---

## 6. Optional polish (not required for the loop)

- Create `Resources/Campus/OutreachSettings.asset` to tune outreach weights (code falls back to defaults).
- Style-only: tweak `.frat-identity-*` in `HUD.uss`.
- Expose frat name in lobby UI later; until then it stays `"Frat Test Name"`.

---

## Minimal “does it work?” path

1. Fix PartyManager **Guest Spawn Location** in Game scene.  
2. Confirm HUD → `HUDVisualTree.uxml` + `GameplayUIManager`.  
3. Play → start party → wait for end → watch HUD grade/rating + Console `[Fraternity]` log.
