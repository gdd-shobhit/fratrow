using Unity.Netcode;
using UnityEngine;

/// <summary>
/// RPC commands for lobby synchronization.
/// These RPCs handle player class selection, ready status, and game start.
/// </summary>
public class LobbyRPCs : NetworkBehaviour
{
    /// <summary>
    /// RPC to update a player's display name.
    /// Called by client, executed on server, then synced to all clients.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetPlayerNameRPC(ulong clientId, string playerName)
    {
        if (playerName == null) playerName = $"Player {clientId}";
        if (string.IsNullOrWhiteSpace(playerName)) playerName = $"Player {clientId}";
        playerName = playerName.Trim();
        if (playerName.Length > 24) playerName = playerName.Substring(0, 24);

        Debug.Log($"[LobbyRPCs] Player {clientId} name set to: {playerName}");

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.SetPlayerName(clientId, playerName);
        }

        SetPlayerNameClientRPC(clientId, playerName);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SetPlayerNameClientRPC(ulong clientId, string playerName)
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.HandlePlayerNameChanged(clientId, playerName);
        }
    }

    /// <summary>
    /// RPC to update a player's class selection.
    /// Called by client, executed on server, then synced to all clients.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetPlayerClassRPC(ulong clientId, PlayerClass playerClass)
    {
        Debug.Log($"[LobbyRPCs] Player {clientId} selected class: {playerClass}");
        
        // Update lobby manager
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.SetPlayerClass(clientId, playerClass);
        }

        // Broadcast to all clients
        SetPlayerClassClientRPC(clientId, playerClass);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SetPlayerClassClientRPC(ulong clientId, PlayerClass playerClass)
    {
        // Update local lobby UI
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.HandlePlayerClassChanged(clientId, playerClass);
        }
    }

    /// <summary>
    /// RPC to toggle a player's ready status.
    /// Called by client, executed on server, then synced to all clients.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetReadyStatusRPC(ulong clientId, bool isReady)
    {
        Debug.Log($"[LobbyRPCs] Player {clientId} ready status: {isReady}");
        
        // Update lobby manager
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.SetPlayerReady(clientId, isReady);
        }

        // Broadcast to all clients
        SetReadyStatusClientRPC(clientId, isReady);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SetReadyStatusClientRPC(ulong clientId, bool isReady)
    {
        // Update local lobby UI
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.HandlePlayerReadyChanged(clientId, isReady);
        }
    }

    /// <summary>
    /// RPC to start the game. Only host can call this.
    /// Validates all players are ready before starting.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void StartGameRPC()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[LobbyRPCs] StartGameRPC called but not server!");
            return;
        }

        Debug.Log("[LobbyRPCs] Host requested game start.");

        // Validate all players are ready
        if (LobbyManager.Instance != null && LobbyManager.Instance.CanStartGame())
        {
            Debug.Log("[LobbyRPCs] All players ready. Starting game...");
            
            // Notify all clients to start game
            StartGameClientRPC();
        }
        else
        {
            Debug.LogWarning("[LobbyRPCs] Cannot start game - not all players are ready!");
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void StartGameClientRPC()
    {
        Debug.Log("[LobbyRPCs] Game start confirmed. Transitioning to game scene...");
        
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.StartGame();
        }
    }

    /// <summary>
    /// RPC to update a player's mesh selection.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetMeshIndexRPC(ulong clientId, int meshIndex)
    {
        int clampedIndex = 0;
        if (LobbyManager.Instance != null)
        {
            clampedIndex = LobbyManager.Instance.SetPlayerMeshIndex(clientId, meshIndex);
        }
        SetMeshIndexClientRPC(clientId, clampedIndex);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SetMeshIndexClientRPC(ulong clientId, int meshIndex)
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.HandlePlayerMeshChanged(clientId, meshIndex);
        }
    }

    /// <summary>
    /// RPC to update a player's material selection.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetMaterialIndexRPC(ulong clientId, int materialIndex)
    {
        int clampedIndex = 0;
        if (LobbyManager.Instance != null)
        {
            clampedIndex = LobbyManager.Instance.SetPlayerMaterialIndex(clientId, materialIndex);
        }
        SetMaterialIndexClientRPC(clientId, clampedIndex);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SetMaterialIndexClientRPC(ulong clientId, int materialIndex)
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.HandlePlayerMaterialChanged(clientId, materialIndex);
        }
    }

    /// <summary>
    /// RPC to notify all clients when a player joins the lobby.
    /// Called by server when a new player connects.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void PlayerJoinedLobbyRPC(ulong clientId, string playerName, PlayerClass playerClass, bool isHost, int meshIndex = 0, int materialIndex = 0)
    {
        Debug.Log($"[LobbyRPCs] Player joined lobby: {playerName} (ID: {clientId}, Class: {playerClass}, Host: {isHost})");
        
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnPlayerJoined(clientId, playerName, playerClass, isHost, meshIndex, materialIndex);
        }
    }

    /// <summary>
    /// RPC to notify all clients when a player leaves the lobby.
    /// Called by server when a player disconnects.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void PlayerLeftLobbyRPC(ulong clientId)
    {
        Debug.Log($"[LobbyRPCs] Player left lobby: {clientId}");
        
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnPlayerLeft(clientId);
        }
    }
}
