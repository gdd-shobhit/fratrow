# Networking & Lobby Setup Guide

Steps to get the lobby on screen and make multiplayer work.

---

## 1. Unity Services (one-time)

1. **Open the Services window** (path depends on your Unity version):
   - **Window → General → Services**, or
   - **Edit → Project Settings**, then select **Services** in the left sidebar.
   Sign in with your Unity account and create or link a cloud project.
2. **Enable services** in the dashboard:
   - **Authentication** (required for multiplayer).
   - **Multiplayer** (or Relay / session services your project uses).
3. **Project Settings → Services** – confirm the project is linked.

---

## 2. Scenes in Build Settings

1. **File → Build Settings**.
2. **Add Open Scenes** (or drag in):
   - `MainMenu`
   - `Lobby` (create in step 3)
   - `Game`
3. **Order:** MainMenu first (as default scene).

---

## 3. Create the Lobby Scene

1. **File → New Scene** (or duplicate an existing one).
2. **File → Save As** → `Assets/Scenes/Lobby.unity`.
3. **Add Lobby UI:**
   - Create empty GameObject: **Create Empty** → name it `LobbyUI`.
   - Add component **UI Document** (UI Toolkit).
   - In UI Document:
     - **Source Asset** = `Assets/UI Toolkit/LobbyVisualTree.uxml`.
     - **Panel Settings** = your existing one (e.g. from MainMenu, or create from **UI Toolkit → Panel Settings**).
   - Add component **Lobby UI** (script `LobbyUI.cs`).
4. **Optional:** Add a Camera and EventSystem if the scene has none.
5. **Save the scene.**

---

## 4. Main Menu Scene – Networking Objects

Your main menu should own the networking and lobby logic so it survives the switch to the Lobby scene.

1. **Open `MainMenu.unity`.**
2. **Add Netcode NetworkManager:**
   - **GameObject → Create Empty** → name `NetworkManager`.
   - Add component **Network Manager** (from **Netcode for GameObjects**).
   - In the inspector:
     - **Network Transport** = add **Unity Transport** (or your transport).
     - **Player Prefab** = assign in step 6 (can be done later).
   - Netcode usually sets this object to **DontDestroyOnLoad** so it stays when you load Lobby.
   - **Do not** add any **Network Object** (or put one on a child) — Netcode forbids NetworkManager and NetworkObject in the same hierarchy.
3. **Add lobby logic as a separate root object (not under NetworkManager):**
   - Netcode does **not** allow a **Network Object** on the same GameObject as **Network Manager** or on any of its children. So lobby must be its own root object.
   - **GameObject → Create Empty** at the **root** of the Hierarchy (same level as `NetworkManager`) → name `LobbySystems`.
   - Add component **Network Object** (Netcode). Uncheck **Spawn With Observers** if you want it only as a scene object.
   - Add component **Lobby Manager** (your script) — it calls `DontDestroyOnLoad`, so this object persists when you load the Lobby scene.
   - Add component **Lobby RPCs** (your script).
4. **Bootstrap and session:**
   - Create empty GameObject at root → name `Services`.
   - Add **Network Bootstrap** (`NetworkBootstrap.cs`).
   - Add **Session Manager** (`SessionManager.cs`).
   - Optional: add **Network Connection Handler** (`NetworkConnectionHandler.cs`).

So you have roughly (all at **root** level; do not put LobbySystems under NetworkManager):

- `NetworkManager` (Network Manager + Unity Transport only; no NetworkObject on it or its children)
- `LobbySystems` (Network Object + Lobby Manager + Lobby RPCs)
- `Services` (Network Bootstrap, Session Manager, …)

---

## 5. Lobby Scene – Only UI

- **Lobby** scene should **not** create a new NetworkManager or LobbyManager.
- It only needs the **Lobby UI** (UIDocument + `LobbyVisualTree.uxml` + `LobbyUI.cs`) so the lobby is shown when you load this scene after Start/Join.

When you click **Start** on the main menu:

1. Services init and session is created.
2. Host starts (NetworkManager).
3. Scene changes to **Lobby**.
4. NetworkManager (and LobbySystems) persist; Lobby scene shows the lobby UI, which talks to the existing LobbyManager.

---

## 6. Player Prefab (for when you go to Game)

**If you see "PlayerPrefab has no NetworkObject assigned":** the prefab used as Player Prefab must have a **Network Object** component on its root.

1. In the **Project** window, open **Assets/Prefabs/Player.prefab** (double‑click to open in Prefab mode, or drag it into the Hierarchy to edit).
2. With the **root** of the prefab selected (the top object, e.g. "Player"):
   - **Add Component** → search for **Network Object** (Netcode for GameObjects) → add it.
   - **Add Component** → add **Networked User Character** (your script).
   - If the prefab doesn’t have **User Character** yet, add that too (your gameplay script).
3. **Save** the prefab (Ctrl+S in Prefab mode, or **Overrides → Apply All** if you edited an instance).
4. In the **Main Menu** scene, select **NetworkManager** and in the Inspector set **Player Prefab** to this **Player** prefab.

When the game starts and the game scene loads, the server can spawn this player prefab for each client.

---

## 7. Game Scene Name

- Your code uses the scene name **"Game"** for the play scene (e.g. in `LobbyManager`).
- In **Build Settings**, the scene file must be named/listed so that `SceneManager.LoadScene("Game", ...)` loads it (usually the scene name is the filename without `.unity`). If your scene is `Game.unity`, you’re good.

---

## 8. Flow Summary

| Step | Who          | Action |
|------|---------------|--------|
| 1    | Player        | Opens game → Main Menu scene. |
| 2    | Player        | Clicks **Start** → init services, create session, StartHost(), load **Lobby** scene. |
| 3    | Lobby scene   | Shows Lobby UI; LobbyManager (on DontDestroyOnLoad hierarchy) already exists. |
| 4    | Host          | Sees session code, player list, **Start Game** when all ready. |
| 5    | Host          | Clicks **Start Game** → load **Game** scene; server spawns player prefabs. |

**Join flow:**  
Main menu **Join** should eventually open a way to enter a session code, then call `JoinSessionAsync(code)`, `StartClient()`, and load **Lobby**. The Join button currently has a TODO for the code input.

---

## 9. Quick Checklist

- [ ] Unity Services linked; Authentication + Multiplayer (or Relay) enabled.
- [ ] Build Settings: MainMenu, Lobby, Game.
- [ ] Lobby scene created and saved; has UIDocument + `LobbyVisualTree.uxml` + `LobbyUI.cs`.
- [ ] Main Menu scene has NetworkManager (with transport), LobbySystems (Network Object + Lobby Manager + Lobby RPCs), and Services (Network Bootstrap, Session Manager).
- [ ] Player prefab has Network Object + Networked User Character; assigned in Network Manager as Player Prefab.
- [ ] Game scene exists and is named **"Game"** in Build Settings.

After this, running from Main Menu and pressing **Start** should take you to the Lobby scene and show the lobby UI.
