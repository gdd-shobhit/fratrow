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

    private int _nextSpawnIndex = 0;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // Subscribe to client connection events
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
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

        // Get spawn position
        Vector3 spawnPosition = GetSpawnPosition();
        Quaternion spawnRotation = Quaternion.identity;

        // Spawn player as NetworkObject
        GameObject playerObject = NetworkObject.Instantiate(playerPrefab, spawnPosition, spawnRotation);
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

                // Initialize NetworkedUserCharacter component
                NetworkedUserCharacter networkedCharacter = playerObject.GetComponent<NetworkedUserCharacter>();
                if (networkedCharacter == null)
                {
                    networkedCharacter = playerObject.AddComponent<NetworkedUserCharacter>();
                }

                // Sync stats to network (use public BaseCharacter properties)
                networkedCharacter.InitializePlayerDataServerRPC(
                    playerClass,
                    userCharacter.Popularity,
                    userCharacter.TroubleMaker,
                    userCharacter.CashValue
                );
            }
            else
            {
                Debug.LogWarning($"[PlayerSpawner] PlayerClassSO not found for class: {playerClass}");
            }
        }
    }

    /// <summary>
    /// Get spawn position for next player.
    /// </summary>
    private Vector3 GetSpawnPosition()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            if (useRandomSpawn)
            {
                int randomIndex = Random.Range(0, spawnPoints.Length);
                return spawnPoints[randomIndex].position;
            }
            else
            {
                // Round-robin spawn points
                Vector3 position = spawnPoints[_nextSpawnIndex].position;
                _nextSpawnIndex = (_nextSpawnIndex + 1) % spawnPoints.Length;
                return position;
            }
        }

        // Default spawn at origin if no spawn points
        return Vector3.zero;
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

            // Spawn player with their selected class
            SpawnPlayer(clientId, playerData.SelectedClass);
        }
    }
}
