using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles network connection events: player connected, disconnected, reconnected.
/// Updates lobby UI accordingly and handles host migration if host leaves.
/// </summary>
public class NetworkConnectionHandler : MonoBehaviour
{
    private NetworkManager _networkManager;

    // Events
    public event System.Action<ulong> OnClientConnected;
    public event System.Action<ulong> OnClientDisconnected;

    private void Awake()
    {
        _networkManager = NetworkManager.Singleton;
        if (_networkManager == null)
        {
            Debug.LogWarning("[NetworkConnectionHandler] NetworkManager not found. Will subscribe when available.");
        }
    }

    private void Start()
    {
        SubscribeToNetworkEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkEvents();
    }

    private void SubscribeToNetworkEvents()
    {
        if (_networkManager == null)
        {
            _networkManager = NetworkManager.Singleton;
            if (_networkManager == null) return;
        }

        _networkManager.OnClientConnectedCallback += HandleClientConnected;
        _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        _networkManager.OnServerStarted += HandleServerStarted;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (_networkManager == null) return;

        _networkManager.OnClientConnectedCallback -= HandleClientConnected;
        _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        _networkManager.OnServerStarted -= HandleServerStarted;
    }

    private void HandleServerStarted()
    {
        Debug.Log("[NetworkConnectionHandler] Server started.");
        // Host is registered by LobbyManager.OnNetworkSpawn -> RegisterServerPlayer().
        // Do not call OnClientConnected(host) here or it would overwrite the host entry with isHost=false.
    }

    private void HandleClientConnected(ulong clientId)
    {
        Debug.Log($"[NetworkConnectionHandler] Client connected: {clientId}");

        // If we're the server, register the new client in the lobby
        if (_networkManager != null && _networkManager.IsServer)
        {
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnClientConnected(clientId);
            }
        }

        OnClientConnected?.Invoke(clientId);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        Debug.Log($"[NetworkConnectionHandler] Client disconnected: {clientId}");

        // If we're the server, remove the client from the lobby
        if (_networkManager != null && _networkManager.IsServer)
        {
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnClientDisconnected(clientId);
            }

            // Check if the disconnected client was the host
            if (_networkManager.IsHost && clientId == _networkManager.LocalClientId)
            {
                // Host disconnected - handle host migration or end session
                HandleHostDisconnected();
            }
        }

        OnClientDisconnected?.Invoke(clientId);
    }

    /// <summary>
    /// Handle host disconnection. Either migrate host or end session.
    /// </summary>
    private void HandleHostDisconnected()
    {
        Debug.LogWarning("[NetworkConnectionHandler] Host disconnected!");

        // For now, end the session
        // TODO: Implement host migration if desired
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.LeaveLobby();
        }
        else
        {
            // Fallback: disconnect all clients
            if (_networkManager != null)
            {
                _networkManager.Shutdown();
            }
        }
    }

    /// <summary>
    /// Handle player reconnection (if supported by NetCode).
    /// </summary>
    public void HandlePlayerReconnected(ulong clientId)
    {
        Debug.Log($"[NetworkConnectionHandler] Player reconnected: {clientId}");
        
        // Re-sync lobby state to reconnected player
        if (_networkManager != null && _networkManager.IsServer)
        {
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnClientConnected(clientId);
            }
        }
    }
}
