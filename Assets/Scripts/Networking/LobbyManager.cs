using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages lobby state, player data, and synchronization across the network.
/// Handles player joining/leaving, ready states, and lobby-to-game transition.
/// </summary>
public class LobbyManager : NetworkBehaviour
{
    private static LobbyManager _instance;
    public static LobbyManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<LobbyManager>();
            }
            return _instance;
        }
    }

    // Lobby state
    private Dictionary<ulong, LobbyPlayerData> _players = new Dictionary<ulong, LobbyPlayerData>();
    private LobbyRPCs _lobbyRPCs;

    // Events
    /// <summary>Fired when LobbySystems spawns on the client (Main Menu flow). Subscribe to load Lobby scene.</summary>
    public static event System.Action OnClientLobbySystemsSpawned;

    public event System.Action<LobbyPlayerData> OnPlayerAdded;
    public event System.Action<ulong> OnPlayerRemoved;
    public event System.Action<ulong, PlayerClass> OnPlayerClassChanged;
    public event System.Action<ulong, bool> OnPlayerReadyChanged;
    public event System.Action<ulong, string> OnPlayerNameChanged;
    public event System.Action<ulong, int> OnPlayerMeshChanged;
    public event System.Action<ulong, int> OnPlayerMaterialChanged;

    // Other properties
    [SerializeField] private List<Mesh> playerMeshVisualList;
    [SerializeField] private List<Material> playerSkinMaterialList;

    public IReadOnlyList<Mesh> MeshList => playerMeshVisualList ?? new List<Mesh>();
    public IReadOnlyList<Material> MaterialList => playerSkinMaterialList ?? new List<Material>();

    // Scene names
    private const string GAME_SCENE_NAME = "Game"; // Update with actual scene name

    public IReadOnlyDictionary<ulong, LobbyPlayerData> Players => _players;
    public int PlayerCount => _players.Count;
    public int ReadyPlayerCount => _players.Values.Count(p => p.IsReady);

    private void Awake()
    {
        var nm = NetworkManager.Singleton;
        var nmState = nm != null ? $"IsServer={nm.IsServer}, IsClient={nm.IsClient}" : "NetworkManager=null";
        Debug.Log($"[LobbyManager] Awake on {gameObject.name} (scene: {gameObject.scene.name}) {nmState}");

        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        // Resolve LobbyRPCs in Awake so clients can send RPCs before OnNetworkSpawn runs
        _lobbyRPCs = GetComponent<LobbyRPCs>();
        if (_lobbyRPCs == null)
        {
            _lobbyRPCs = gameObject.AddComponent<LobbyRPCs>();
        }
        Debug.Log($"[LobbyManager] Awake done, _lobbyRPCs={(_lobbyRPCs != null ? "ok" : "null")}");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        var no = NetworkObject;
        var noSpawned = no != null && no.IsSpawned;
        Debug.Log($"[LobbyManager] OnNetworkSpawn IsServer={IsServer} IsClient={IsClient} NetworkObject.IsSpawned={noSpawned} LocalClientId={NetworkManager.Singleton?.LocalClientId}");

        // Ensure LobbyRPCs is set (in case Awake ran before LobbyRPCs was added)
        if (_lobbyRPCs == null)
        {
            _lobbyRPCs = GetComponent<LobbyRPCs>();
            if (_lobbyRPCs == null)
            {
                _lobbyRPCs = gameObject.AddComponent<LobbyRPCs>();
            }
        }
        if (IsServer)
        {
            RegisterServerPlayer();
        }
        else if (IsClient)
        {
            Debug.Log("[LobbyManager] Client: starting ClientRequestLobbyStateDelayed");
            OnClientLobbySystemsSpawned?.Invoke();
            // Client: request full lobby state from server (avoids timing/race issues)
            StartCoroutine(ClientRequestLobbyStateDelayed());
        }
    }

    private System.Collections.IEnumerator ClientRequestLobbyStateDelayed()
    {
        // Wait a few frames so we're fully spawned before requesting state
        yield return null;
        yield return null;

        var nm = NetworkManager.Singleton;
        Debug.Log($"[LobbyManager] ClientRequestLobbyStateDelayed: _lobbyRPCs={(_lobbyRPCs != null ? "ok" : "null")} nm={nm != null} LocalClientId={nm?.LocalClientId} NetworkObject.IsSpawned={NetworkObject?.IsSpawned}");

        if (_lobbyRPCs == null)
        {
            Debug.LogWarning("[LobbyManager] ClientRequestLobbyStateDelayed: _lobbyRPCs is null, cannot request state");
            yield break;
        }
        if (nm == null)
        {
            Debug.LogWarning("[LobbyManager] ClientRequestLobbyStateDelayed: NetworkManager is null");
            yield break;
        }

        Debug.Log($"[LobbyManager] Client {nm.LocalClientId} requesting lobby state from server");
        _lobbyRPCs.RequestLobbyStateRPC(nm.LocalClientId);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _players.Clear();
    }

    /// <summary>
    /// Register the server/host player in the lobby.
    /// </summary>
    private void RegisterServerPlayer()
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null) return;

        var localClientId = networkManager.LocalClientId;
        var playerName = UnityEngine.PlayerPrefs.GetString("PlayerName", "Host");

        var playerData = new LobbyPlayerData(playerName, PlayerClass.President, localClientId, true);
        _players[localClientId] = playerData;

        Debug.Log($"[LobbyManager] Registered server player: {playerData}");
        OnPlayerAdded?.Invoke(playerData);
    }

    /// <summary>
    /// Called when a client connects. Server-side only.
    /// </summary>
    public void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        var networkManager = NetworkManager.Singleton;
        if (networkManager != null && clientId == networkManager.LocalClientId)
        {
            // Host is already registered in RegisterServerPlayer(); don't overwrite with isHost=false
            Debug.Log("[LobbyManager] Host already registered, skipping OnClientConnected for self.");
            return;
        }

        Debug.Log($"[LobbyManager] Client connected: {clientId}");

        // Create player data for new client (name will be updated when they send SetPlayerNameRPC)
        var playerName = $"Player {clientId}";
        var playerData = new LobbyPlayerData(playerName, PlayerClass.President, clientId, false);
        _players[clientId] = playerData;

        // Notify all clients
        if (_lobbyRPCs != null)
        {
            _lobbyRPCs.PlayerJoinedLobbyRPC(clientId, playerData.PlayerName, playerData.SelectedClass, false, playerData.SelectedMeshIndex, playerData.SelectedMaterialIndex);
        }

        OnPlayerAdded?.Invoke(playerData);

        // Send current lobby state to new client
        SendLobbyStateToClient(clientId);
    }

    /// <summary>
    /// Called when a client disconnects. Server-side only.
    /// </summary>
    public void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"[LobbyManager] Client disconnected: {clientId}");

        if (_players.ContainsKey(clientId))
        {
            _players.Remove(clientId);
            
            // Notify all clients
            if (_lobbyRPCs != null)
            {
                _lobbyRPCs.PlayerLeftLobbyRPC(clientId);
            }

            OnPlayerRemoved?.Invoke(clientId);
        }
    }

    /// <summary>
    /// Set a player's display name. Server-side only.
    /// </summary>
    public void SetPlayerName(ulong clientId, string playerName)
    {
        if (!IsServer) return;
        if (string.IsNullOrWhiteSpace(playerName)) return;

        if (_players.ContainsKey(clientId))
        {
            LobbyPlayerData data = _players[clientId];
            data.PlayerName = playerName.Trim();
            if (data.PlayerName.Length > 24)
                data.PlayerName = data.PlayerName.Substring(0, 24);
            _players[clientId] = data;
            Debug.Log($"[LobbyManager] Player {clientId} name set to: {data.PlayerName}");
        }
    }

    /// <summary>
    /// Client-side callback when player name changes (called from LobbyRPCs).
    /// </summary>
    public void HandlePlayerNameChanged(ulong clientId, string playerName)
    {
        if (_players.ContainsKey(clientId))
        {
            LobbyPlayerData data = _players[clientId];
            data.PlayerName = playerName ?? $"Player {clientId}";
            _players[clientId] = data;
        }
        OnPlayerNameChanged?.Invoke(clientId, playerName);
    }

    /// <summary>
    /// Set a player's class selection. Server-side only.
    /// </summary>
    public void SetPlayerClass(ulong clientId, PlayerClass playerClass)
    {
        if (!IsServer) return;

        if (_players.ContainsKey(clientId))
        {
            LobbyPlayerData data = _players[clientId];
            data.SelectedClass = playerClass;
            _players[clientId] = data;
            Debug.Log($"[LobbyManager] Player {clientId} class set to: {playerClass}");
        }
    }

    /// <summary>
    /// Set a player's ready status. Server-side only.
    /// </summary>
    public void SetPlayerReady(ulong clientId, bool isReady)
    {
        if (!IsServer) return;

        if (_players.ContainsKey(clientId))
        {
            LobbyPlayerData data = _players[clientId];
            data.IsReady = isReady;
            _players[clientId] = data;
            Debug.Log($"[LobbyManager] Player {clientId} ready status: {isReady}");
        }
    }

    /// <summary>
    /// Check if all players are ready and game can start.
    /// </summary>
    public bool CanStartGame()
    {
        if (!IsServer) return false;
        if (_players.Count < 1) return false; // At least 1 player needed
        if (_players.Count > 4) return false; // Max 4 players

        // All players must be ready
        return _players.Values.All(p => p.IsReady);
    }

    /// <summary>
    /// Start the game. Loads game scene for all clients.
    /// </summary>
    public void StartGame()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[LobbyManager] Only server can start the game!");
            return;
        }

        if (!CanStartGame())
        {
            Debug.LogWarning("[LobbyManager] Cannot start game - not all players ready!");
            return;
        }

        Debug.Log("[LobbyManager] Starting game... Loading game scene for all clients.");

        // Load game scene for all clients
        NetworkManager.Singleton.SceneManager.LoadScene(GAME_SCENE_NAME, LoadSceneMode.Single);
    }

    /// <summary>
    /// Client-side callback when player class changes (called from LobbyRPCs).
    /// </summary>
    public void HandlePlayerClassChanged(ulong clientId, PlayerClass playerClass)
    {
        if (_players.ContainsKey(clientId))
        {
            LobbyPlayerData data = _players[clientId];
            data.SelectedClass = playerClass;
            _players[clientId] = data;
        }
        OnPlayerClassChanged?.Invoke(clientId, playerClass);
    }

    /// <summary>
    /// Client-side callback when player ready status changes (called from LobbyRPCs).
    /// </summary>
    public void HandlePlayerReadyChanged(ulong clientId, bool isReady)
    {
        if (_players.ContainsKey(clientId))
        {
            LobbyPlayerData data = _players[clientId];
            data.IsReady = isReady;
            _players[clientId] = data;
        }
        OnPlayerReadyChanged?.Invoke(clientId, isReady);
    }

    /// <summary>
    /// Set a player's mesh selection. Server-side only. Returns the clamped index that was set.
    /// </summary>
    public int SetPlayerMeshIndex(ulong clientId, int meshIndex)
    {
        if (!IsServer) return 0;

        var count = MeshList?.Count ?? 0;
        if (count == 0) return 0;
        meshIndex = ((meshIndex % count) + count) % count; // Wrap to valid range

        if (_players.ContainsKey(clientId))
        {
            LobbyPlayerData data = _players[clientId];
            data.SelectedMeshIndex = meshIndex;
            _players[clientId] = data;
            Debug.Log($"[LobbyManager] Player {clientId} mesh set to index {meshIndex}");
            return meshIndex;
        }
        return 0;
    }

    /// <summary>
    /// Set a player's material selection. Server-side only. Returns the clamped index that was set.
    /// </summary>
    public int SetPlayerMaterialIndex(ulong clientId, int materialIndex)
    {
        if (!IsServer) return 0;

        var count = MaterialList?.Count ?? 0;
        if (count == 0) return 0;
        materialIndex = ((materialIndex % count) + count) % count; // Wrap to valid range

        if (_players.ContainsKey(clientId))
        {
            LobbyPlayerData data = _players[clientId];
            data.SelectedMaterialIndex = materialIndex;
            _players[clientId] = data;
            Debug.Log($"[LobbyManager] Player {clientId} material set to index {materialIndex}");
            return materialIndex;
        }
        return 0;
    }

    /// <summary>
    /// Client-side callback when player mesh changes (called from LobbyRPCs).
    /// </summary>
    public void HandlePlayerMeshChanged(ulong clientId, int meshIndex)
    {
        if (_players.ContainsKey(clientId))
        {
            LobbyPlayerData data = _players[clientId];
            data.SelectedMeshIndex = meshIndex;
            _players[clientId] = data;
        }
        OnPlayerMeshChanged?.Invoke(clientId, meshIndex);
    }

    /// <summary>
    /// Client-side callback when player material changes (called from LobbyRPCs).
    /// </summary>
    public void HandlePlayerMaterialChanged(ulong clientId, int materialIndex)
    {
        if (_players.ContainsKey(clientId))
        {
            LobbyPlayerData data = _players[clientId];
            data.SelectedMaterialIndex = materialIndex;
            _players[clientId] = data;
        }
        OnPlayerMaterialChanged?.Invoke(clientId, materialIndex);
    }

    /// <summary>
    /// Client-side callback when a player joins.
    /// </summary>
    public void OnPlayerJoined(ulong clientId, string playerName, PlayerClass playerClass, bool isHost, int meshIndex = 0, int materialIndex = 0)
    {
        Debug.Log($"[LobbyManager] OnPlayerJoined (client-side) clientId={clientId} playerName={playerName} IsClient={IsClient} _players.Count before={_players.Count}");
        var playerData = new LobbyPlayerData(playerName, playerClass, clientId, isHost);
        playerData.SelectedMeshIndex = meshIndex;
        playerData.SelectedMaterialIndex = materialIndex;
        _players[clientId] = playerData;
        OnPlayerAdded?.Invoke(playerData);
        Debug.Log($"[LobbyManager] OnPlayerJoined done, _players.Count={_players.Count}");
    }

    /// <summary>
    /// Client-side callback when a player leaves.
    /// </summary>
    public void OnPlayerLeft(ulong clientId)
    {
        if (_players.ContainsKey(clientId))
        {
            _players.Remove(clientId);
        }
        OnPlayerRemoved?.Invoke(clientId);
    }

    /// <summary>
    /// Send current lobby state to a specific client. Server-side only.
    /// Called when a client connects and when a client requests state (RequestLobbyStateRPC).
    /// </summary>
    public void SendLobbyStateToClient(ulong clientId)
    {
        if (!IsServer) return;

        var count = _players.Count;
        Debug.Log($"[LobbyManager] SendLobbyStateToClient target={clientId} playerCount={count} _lobbyRPCs={(_lobbyRPCs != null ? "ok" : "null")}");

        // Snapshot the list so we don't modify _players while iterating (RPCs can trigger callbacks that modify it)
        var snapshot = new List<LobbyPlayerData>(_players.Values);
        foreach (var playerData in snapshot)
        {
            if (_lobbyRPCs != null)
            {
                _lobbyRPCs.PlayerJoinedLobbyRPC(playerData.NetworkId, playerData.PlayerName,
                    playerData.SelectedClass, playerData.IsHost, playerData.SelectedMeshIndex, playerData.SelectedMaterialIndex);
            }
        }
    }

    /// <summary>
    /// Returns true if RPCs can be sent (client must have NetworkObject spawned).
    /// </summary>
    private bool CanSendRPC()
    {
        if (_lobbyRPCs == null)
        {
            Debug.Log("[LobbyManager] CanSendRPC: false (_lobbyRPCs null)");
            return false;
        }
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsClient)
        {
            Debug.Log($"[LobbyManager] CanSendRPC: false (nm={nm != null} IsClient={nm?.IsClient})");
            return false;
        }
        var ok = NetworkObject != null && NetworkObject.IsSpawned;
        if (!ok)
            Debug.Log($"[LobbyManager] CanSendRPC: false (NetworkObject={NetworkObject != null} IsSpawned={NetworkObject?.IsSpawned})");
        return ok;
    }

    /// <summary>
    /// Request to set this client's display name. Client-side method.
    /// </summary>
    public void RequestSetPlayerName(string playerName)
    {
        if (!CanSendRPC()) return;
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            _lobbyRPCs.SetPlayerNameRPC(nm.LocalClientId, playerName ?? "");
        }
    }

    /// <summary>
    /// Request to change player class. Client-side method.
    /// </summary>
    public void RequestSetPlayerClass(PlayerClass playerClass)
    {
        if (!CanSendRPC()) return;
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            _lobbyRPCs.SetPlayerClassRPC(nm.LocalClientId, playerClass);
        }
    }

    /// <summary>
    /// Request to toggle ready status. Client-side method.
    /// </summary>
    public void RequestSetReadyStatus(bool isReady)
    {
        if (!CanSendRPC()) return;
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            _lobbyRPCs.SetReadyStatusRPC(nm.LocalClientId, isReady);
        }
    }

    /// <summary>
    /// Request to set this client's mesh selection. Client-side method.
    /// </summary>
    public void RequestSetMeshIndex(int meshIndex)
    {
        if (!CanSendRPC()) return;
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            _lobbyRPCs.SetMeshIndexRPC(nm.LocalClientId, meshIndex);
        }
    }

    /// <summary>
    /// Request to set this client's material selection. Client-side method.
    /// </summary>
    public void RequestSetMaterialIndex(int materialIndex)
    {
        if (!CanSendRPC()) return;
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            _lobbyRPCs.SetMaterialIndexRPC(nm.LocalClientId, materialIndex);
        }
    }

    /// <summary>
    /// Request to start the game. Host-only, client-side method.
    /// </summary>
    public void RequestStartGame()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[LobbyManager] Only host can start the game!");
            return;
        }

        if (_lobbyRPCs != null)
        {
            _lobbyRPCs.StartGameRPC();
        }
    }

    /// <summary>
    /// Leave the lobby and return to main menu.
    /// </summary>
    public async void LeaveLobby()
    {
        Debug.Log("[LobbyManager] Leaving lobby...");

        // Disconnect from network
        var networkManager = NetworkManager.Singleton;
        if (networkManager != null && (networkManager.IsServer || networkManager.IsClient))
        {
            networkManager.Shutdown();
        }

        // Leave session
        if (SessionManager.Instance != null && SessionManager.Instance.HasActiveSession)
        {
            await SessionManager.Instance.LeaveSessionAsync();
        }

        // Clear lobby state
        _players.Clear();

        // Load main menu scene
        SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
    }
}
