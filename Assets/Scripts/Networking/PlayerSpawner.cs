using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles spawning of player characters when they join the game.
/// Server spawns players based on their lobby class selection.
/// </summary>
public class PlayerSpawner : NetworkBehaviour
{
    [Header("Player Prefab")]
    [SerializeField] private GameObject playerPrefab;

    [Header("Spawn Settings")]
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private bool useRandomSpawn = true;
    public bool IsTesting = false;

    private int _nextSpawnIndex = 0;

    public void Start()
    {

    }
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Testing so i dont have to go through the whole flow
        if (IsTesting)
        {
            SpawnPlayer(0, PlayerClass.President);
            return;
        }

        if (IsServer)
        {
            // Subscribe to client connection events (for late joiners)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            // Spawn players for everyone already in the lobby (host + clients who connected before Game scene)
            SpawnAllLobbyPlayers();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"[PlayerSpawner] Client connected: {clientId}. Spawning player character...");

        // Get player's class selection from lobby
        PlayerClass playerClass = GetPlayerClassFromLobby(clientId);

        // Spawn player character
        SpawnPlayer(clientId, playerClass);
    }

    /// <summary>
    /// Get player's class selection from lobby data.
    /// </summary>
    private PlayerClass GetPlayerClassFromLobby(ulong clientId)
    {
        if (LobbyManager.Instance != null && LobbyManager.Instance.Players.ContainsKey(clientId))
        {
            return LobbyManager.Instance.Players[clientId].SelectedClass;
        }

        // Default class if not found
        return PlayerClass.President;
    }

    /// <summary>
    /// Spawn a player character for the given client.
    /// </summary>
    private void SpawnPlayer(ulong clientId, PlayerClass playerClass)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[PlayerSpawner] Player prefab not assigned!");
            return;
        }

        // Get spawn position (pass clientId so we don't stack when no spawn points)
        Vector3 spawnPosition = GetSpawnPosition(clientId);
        Quaternion spawnRotation = Quaternion.identity;

        // Spawn player as NetworkObject
        GameObject playerObject = NetworkObject.Instantiate(playerPrefab, spawnPosition, spawnRotation);
        GameManager.Instance.playersInGame.Add(clientId, playerObject);

        NetworkObject networkObject = playerObject.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Assign ownership to the client
            networkObject.SpawnWithOwnership(clientId);

            // Initialize player character with class data
            InitializePlayerCharacter(playerObject, clientId, playerClass);
            
            Debug.Log($"[PlayerSpawner] Spawned player for client {clientId} with class {playerClass} at {spawnPosition}");
        }
        else
        {
            Debug.LogError("[PlayerSpawner] Player prefab does not have NetworkObject component!");
            Destroy(playerObject);
        }
    }

    /// <summary>
    /// Initialize player character with class data from GameManager.
    /// </summary>
    private void InitializePlayerCharacter(GameObject playerObject, ulong clientId, PlayerClass playerClass)
    {
        UserCharacter userCharacter = playerObject.GetComponent<UserCharacter>();
        if (userCharacter == null)
        {
            Debug.LogWarning("[PlayerSpawner] Player prefab does not have UserCharacter component!");
            return;
        }

        // Get PlayerClassSO from GameManager
        if (GameManager.Instance != null && GameManager.Instance.playerClassesDict != null)
        {
            if (GameManager.Instance.playerClassesDict.TryGetValue(playerClass, out PlayerClassSO playerClassSO))
            {
                // Initialize character with class data
                userCharacter.InitializeFromSO(playerClassSO);

                if(!IsTesting)
                {
                    SetPlayerDataOnClientRPC(clientId, LobbyManager.Instance.Players[clientId]);
                }
            }
            else
            {
                Debug.LogWarning($"[PlayerSpawner] PlayerClassSO not found for class: {playerClass}");
            }
        }
    }

    /// <summary>
    /// Get spawn position for next player. Uses clientId so players don't overlap when no spawn points are set.
    /// </summary>
    private Vector3 GetSpawnPosition(ulong clientId)
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            if (useRandomSpawn)
            {
                // Use clientId as seed so each client gets a deterministic "random" slot (avoids overlap)
                int index = (int)(clientId % (ulong)spawnPoints.Length);
                return spawnPoints[index].position;
            }
            else
            {
                // Round-robin spawn points
                Vector3 position = spawnPoints[_nextSpawnIndex].position;
                _nextSpawnIndex = (_nextSpawnIndex + 1) % spawnPoints.Length;
                return position;
            }
        }

        // No spawn points: offset by clientId so host and clients don't stack at origin
        float spacing = 3f;
        return new Vector3((int)clientId * spacing, 0f, 0f);
    }

    /// <summary>
    /// Spawn all players in the lobby when game starts.
    /// Called when transitioning from lobby to game scene.
    /// </summary>
    public void SpawnAllLobbyPlayers()
    {
        if (!IsServer) return;

        if (LobbyManager.Instance == null)
        {
            Debug.LogWarning("[PlayerSpawner] LobbyManager not found!");
            return;
        }

        Debug.Log("[PlayerSpawner] Spawning all lobby players...");

        foreach (var kvp in LobbyManager.Instance.Players)
        {
            ulong clientId = kvp.Key;
            LobbyPlayerData playerData = kvp.Value;

            // Spawn player with their selected class (position varies per clientId)
            SpawnPlayer(clientId, playerData.SelectedClass);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SetPlayerDataOnClientRPC(ulong clientId, LobbyPlayerData lobbyPlayerData)
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.GetComponent<NetworkedGameManager>().HandlePlayerSpawned(clientId, lobbyPlayerData);
        }
    }
}
