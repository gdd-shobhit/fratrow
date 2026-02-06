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
    public event System.Action<LobbyPlayerData> OnPlayerAdded;
    public event System.Action<ulong> OnPlayerRemoved;
    public event System.Action<ulong, PlayerClass> OnPlayerClassChanged;
    public event System.Action<ulong, bool> OnPlayerReadyChanged;
    public event System.Action<ulong, string> OnPlayerNameChanged;

    // Scene names
    private const string GAME_SCENE_NAME = "Game"; // Update with actual scene name

    public IReadOnlyDictionary<ulong, LobbyPlayerData> Players => _players;
    public int PlayerCount => _players.Count;
    public int ReadyPlayerCount => _players.Values.Count(p => p.IsReady);

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Get or create LobbyRPCs component
        _lobbyRPCs = GetComponent<LobbyRPCs>();
        if (_lobbyRPCs == null)
        {
            _lobbyRPCs = gameObject.AddComponent<LobbyRPCs>();
        }

        // If we're the server, register existing players
        if (IsServer)
        {
            RegisterServerPlayer();
        }
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
            _lobbyRPCs.PlayerJoinedLobbyRPC(clientId, playerData.PlayerName, playerData.SelectedClass, false);
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
            _players[clientId].PlayerName = playerName.Trim();
            if (_players[clientId].PlayerName.Length > 24)
                _players[clientId].PlayerName = _players[clientId].PlayerName.Substring(0, 24);
            Debug.Log($"[LobbyManager] Player {clientId} name set to: {_players[clientId].PlayerName}");
        }
    }

    /// <summary>
    /// Client-side callback when player name changes (called from LobbyRPCs).
    /// </summary>
    public void HandlePlayerNameChanged(ulong clientId, string playerName)
    {
        if (_players.ContainsKey(clientId))
        {
            _players[clientId].PlayerName = playerName ?? $"Player {clientId}";
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
            _players[clientId].SelectedClass = playerClass;
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
            _players[clientId].IsReady = isReady;
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
            _players[clientId].SelectedClass = playerClass;
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
            _players[clientId].IsReady = isReady;
        }
        OnPlayerReadyChanged?.Invoke(clientId, isReady);
    }

    /// <summary>
    /// Client-side callback when a player joins.
    /// </summary>
    public void OnPlayerJoined(ulong clientId, string playerName, PlayerClass playerClass, bool isHost)
    {
        var playerData = new LobbyPlayerData(playerName, playerClass, clientId, isHost);
        _players[clientId] = playerData;
        OnPlayerAdded?.Invoke(playerData);
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
    /// </summary>
    private void SendLobbyStateToClient(ulong clientId)
    {
        if (!IsServer) return;

        // Snapshot the list so we don't modify _players while iterating (RPCs can trigger callbacks that modify it)
        var snapshot = new List<LobbyPlayerData>(_players.Values);
        foreach (var playerData in snapshot)
        {
            if (_lobbyRPCs != null)
            {
                _lobbyRPCs.PlayerJoinedLobbyRPC(playerData.NetworkId, playerData.PlayerName,
                    playerData.SelectedClass, playerData.IsHost);
            }
        }
    }

    /// <summary>
    /// Request to set this client's display name. Client-side method.
    /// </summary>
    public void RequestSetPlayerName(string playerName)
    {
        if (_lobbyRPCs != null)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                _lobbyRPCs.SetPlayerNameRPC(networkManager.LocalClientId, playerName ?? "");
            }
        }
    }

    /// <summary>
    /// Request to change player class. Client-side method.
    /// </summary>
    public void RequestSetPlayerClass(PlayerClass playerClass)
    {
        if (_lobbyRPCs != null)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                _lobbyRPCs.SetPlayerClassRPC(networkManager.LocalClientId, playerClass);
            }
        }
    }

    /// <summary>
    /// Request to toggle ready status. Client-side method.
    /// </summary>
    public void RequestSetReadyStatus(bool isReady)
    {
        if (_lobbyRPCs != null)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                _lobbyRPCs.SetReadyStatusRPC(networkManager.LocalClientId, isReady);
            }
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
