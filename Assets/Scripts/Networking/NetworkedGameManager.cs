using System;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// NetworkBehaviour singleton for GameManager.
/// Syncs total metrics (popularity, cash) across the network.
/// Server authoritative for game progression.
/// </summary>
[RequireComponent(typeof(GameManager))]
public class NetworkedGameManager : NetworkBehaviour
{
    // Network synchronized variables
    private NetworkVariable<float> _totalPopularity = new NetworkVariable<float>(0f);
    private NetworkVariable<float> _totalCashValue = new NetworkVariable<float>(0f);
    private NetworkVariable<float> _totalTroubleMaking = new NetworkVariable<float>(0f);

    // Public getters for network variables
    public float TotalPopularity => _totalPopularity.Value;
    public float TotalCashValue => _totalCashValue.Value;
    public float TotalTroubleMaking => _totalTroubleMaking.Value;

    private GameManager _gameManager;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _gameManager = GetComponent<GameManager>();
        if (_gameManager == null)
        {
            Debug.LogError("[NetworkedGameManager] GameManager component not found!");
            return;
        }

        // Subscribe to network variable changes
        _totalPopularity.OnValueChanged += OnTotalPopularityChanged;
        _totalCashValue.OnValueChanged += OnTotalCashValueChanged;
        _totalTroubleMaking.OnValueChanged += OnTotalTroubleMakingChanged;

        // If we're the server, sync initial values
        if (IsServer)
        {
            SyncFromLocalGameManager();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unsubscribe from network variable changes
        _totalPopularity.OnValueChanged -= OnTotalPopularityChanged;
        _totalCashValue.OnValueChanged -= OnTotalCashValueChanged;
        _totalTroubleMaking.OnValueChanged -= OnTotalTroubleMakingChanged;
    }

    /// <summary>
    /// Add to total popularity. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AddTotalPopularityServerRPC(float value)
    {
        if (!IsServer) return;

        _totalPopularity.Value += value;

        // Update local GameManager
        if (_gameManager != null)
        {
            _gameManager.AddTotalPopularityValue(value);
        }
    }

    /// <summary>
    /// Add to total cash value. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AddTotalCashValueServerRPC(float value)
    {
        if (!IsServer) return;

        _totalCashValue.Value += value;

        // Update local GameManager
        if (_gameManager != null)
        {
            _gameManager.AddTotalCashValue(value);
        }
    }

    /// <summary>
    /// Add to total trouble making. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AddTotalTroubleMakingServerRPC(float value)
    {
        if (!IsServer) return;
        _totalTroubleMaking.Value += value;
        if (_gameManager != null)
        {
            _gameManager.AddTotalTroubleMaking(value);
        }
    }   


    /// <summary>
    /// Sync network variables from local GameManager (server-side initialization).
    /// GameManager totals are private, so we initialize to 0; they sync as RPCs add values.
    /// </summary>
    private void SyncFromLocalGameManager()
    {
        if (!IsServer || _gameManager == null) return;
        _totalPopularity.Value = 0f;
        _totalCashValue.Value = 0f;
        _totalTroubleMaking.Value = 0f;
    }

    // Network variable change handlers
    private void OnTotalPopularityChanged(float oldValue, float newValue)
    {
        // Update local GameManager if needed
        // Note: GameManager might not have setters, so this might be read-only on clients
    }

    private void OnTotalCashValueChanged(float oldValue, float newValue)
    {
        // Update local GameManager if needed
        // Note: GameManager might not have setters, so this might be read-only on clients
    }
    private void OnTotalTroubleMakingChanged(float oldValue, float newValue)
    {
        // Update local GameManager if needed
        // Note: GameManager might not have setters, so this might be read-only on clients
    }

    public void HandlePlayerSpawned(ulong clientId, LobbyPlayerData lobbyPlayerData)
    {
        GameObject playerObject = GetPlayerObject(clientId);
        if (playerObject == null) return;

        UserCharacter userCharacter = playerObject.GetComponent<UserCharacter>();
        if (userCharacter != null)
        {
            userCharacter.SetPlayerName(lobbyPlayerData.PlayerName);
            userCharacter.currentClass = lobbyPlayerData.SelectedClass;
            userCharacter.userMesh.sharedMesh = LobbyManager.Instance.MeshList[lobbyPlayerData.SelectedMeshIndex];
            userCharacter.userMesh.sharedMaterial = LobbyManager.Instance.MaterialList[lobbyPlayerData.SelectedMaterialIndex];
            userCharacter.UpdateUIName();
        }

        if(IsOwner)
        {
            playerObject.GetComponentInChildren<Camera>().enabled = true;
        }
    }

    /// <summary>
    /// Get the in-game player GameObject for a client. Server uses GameManager.playersInGame;
    /// clients find it via SpawnManager by OwnerClientId.
    /// </summary>
    private GameObject GetPlayerObject(ulong clientId)
    {
        if (_gameManager?.playersInGame == null) return null;

        // Server: we have it in GameManager (PlayerSpawner added it)
        if (IsServer && _gameManager.playersInGame.TryGetValue(clientId, out GameObject playerObject))
        {
            // Only return if the reference is still valid (not destroyed)
            if (playerObject != null) return playerObject;
        }

        // Client (or late host): find spawned object owned by this client (player prefab has UserCharacter)
        if (NetworkManager.Singleton?.SpawnManager?.SpawnedObjectsList == null) return null;

        foreach (var no in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (no.OwnerClientId != clientId) continue;
            if (no.GetComponent<UserCharacter>() == null) continue;
            return no.gameObject;
        }

        return null;
    }
}
