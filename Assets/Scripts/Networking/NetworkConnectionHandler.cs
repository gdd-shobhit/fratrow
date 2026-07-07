using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles network connection events: player connected, disconnected.
/// Updates lobby state when clients join or leave.
/// </summary>
public class NetworkConnectionHandler : MonoBehaviour
{
    private NetworkManager _networkManager;

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

        if (_networkManager != null && _networkManager.IsServer)
        {
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnClientConnected(clientId);
            }
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        Debug.Log($"[NetworkConnectionHandler] Client disconnected: {clientId}");

        if (_networkManager != null && _networkManager.IsServer)
        {
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnClientDisconnected(clientId);
            }

            if (_networkManager.IsHost && clientId == _networkManager.LocalClientId)
            {
                HandleHostDisconnected();
            }
        }
    }

    private void HandleHostDisconnected()
    {
        Debug.LogWarning("[NetworkConnectionHandler] Host disconnected!");

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.LeaveLobby();
        }
        else if (_networkManager != null)
        {
            _networkManager.Shutdown();
        }
    }
}
