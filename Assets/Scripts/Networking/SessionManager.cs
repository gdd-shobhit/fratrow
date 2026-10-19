using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Systems.Inventory;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// Manages Unity Services Multiplayer sessions for lobby and game sessions.
/// Handles session creation, joining, and player management.
/// </summary>
public class SessionManager : MonoBehaviour
{
    private static SessionManager _instance;
    public static SessionManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var found = FindFirstObjectByType<SessionManager>();
                if (found != null)
                {
                    _instance = found;
                }
                else
                {
                    GameObject go = new GameObject("SessionManager");
                    _instance = go.AddComponent<SessionManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    private ISession _currentSession;
    public ISession CurrentSession => _currentSession;
    public bool HasActiveSession => _currentSession != null;

    // Events (SDK uses string player ID for PlayerJoined / PlayerLeaving)
    public event Action<string> OnPlayerJoined;
    public event Action<string> OnPlayerLeft;
    public event Action OnSessionEnded;

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

    /// <summary>
    /// Create a new multiplayer session as host.
    /// </summary>
    /// <param name="sessionName">Name for the session</param>
    /// <param name="maxPlayers">Maximum number of players (default: 4)</param>
    /// <returns>True if session was created successfully</returns>
    public async Task<bool> CreateSessionAsync(string sessionName = "PartyGame", int maxPlayers = 4)
    {
        try
        {
            if (_currentSession != null)
            {
                Debug.LogWarning("[SessionManager] Session already exists. Leaving current session first.");
                await LeaveSessionAsync();
            }

            Debug.Log($"[SessionManager] Creating session '{sessionName}' with max {maxPlayers} players...");

            if (NetworkBootstrap.Instance != null && !NetworkBootstrap.Instance.EnsureNetworkManagerReady())
            {
                Debug.LogError("[SessionManager] NetworkManager.Singleton is not ready; cannot create a Relay session.");
                return false;
            }

            EquippedPrefabRegistrar.RegisterAllEquippedPrefabs();

            var options = new SessionOptions
            {
                Name = sessionName,
                MaxPlayers = maxPlayers
            }.WithRelayNetwork(); // Use Relay for NAT traversal

            _currentSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            
            Debug.Log($"[SessionManager] Session created! Code: {_currentSession.Code}, ID: {_currentSession.Id}");

            // Subscribe to session events
            _currentSession.PlayerJoined += HandlePlayerJoined;
            _currentSession.PlayerLeaving += HandlePlayerLeft;
            _currentSession.Deleted += HandleSessionEnded;

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SessionManager] Failed to create session: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Join an existing session by session code.
    /// </summary>
    /// <param name="sessionCode">The session code to join</param>
    /// <returns>True if joined successfully</returns>
    public async Task<bool> JoinSessionAsync(string sessionCode)
    {
        try
        {
            if (_currentSession != null)
            {
                Debug.LogWarning("[SessionManager] Already in a session. Leaving current session first.");
                await LeaveSessionAsync();
            }

            Debug.Log($"[SessionManager] Joining session with code: {sessionCode}...");

            if (NetworkBootstrap.Instance != null && !NetworkBootstrap.Instance.EnsureNetworkManagerReady())
            {
                Debug.LogError("[SessionManager] NetworkManager.Singleton is not ready; cannot join a Relay session.");
                return false;
            }

            EquippedPrefabRegistrar.RegisterAllEquippedPrefabs();

            var options = new JoinSessionOptions();
            _currentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(sessionCode, options);

            Debug.Log($"[SessionManager] Joined session! Code: {_currentSession.Code}, ID: {_currentSession.Id}");

            // Subscribe to session events
            _currentSession.PlayerJoined += HandlePlayerJoined;
            _currentSession.PlayerLeaving += HandlePlayerLeft;
            _currentSession.Deleted += HandleSessionEnded;

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SessionManager] Failed to join session: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Leave the current session.
    /// </summary>
    public async Task LeaveSessionAsync()
    {
        if (_currentSession == null)
        {
            Debug.LogWarning("[SessionManager] No active session to leave.");
            return;
        }

        try
        {
            Debug.Log("[SessionManager] Leaving session...");

            // Unsubscribe from events
            if (_currentSession != null)
            {
                _currentSession.PlayerJoined -= HandlePlayerJoined;
                _currentSession.PlayerLeaving -= HandlePlayerLeft;
                _currentSession.Deleted -= HandleSessionEnded;
            }

            await _currentSession.LeaveAsync();
            _currentSession = null;

            Debug.Log("[SessionManager] Left session successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SessionManager] Error leaving session: {e.Message}");
            _currentSession = null; // Clear reference even on error
        }
    }

    /// <summary>
    /// Get the current session code for sharing with friends.
    /// </summary>
    public string GetSessionCode()
    {
        return _currentSession?.Code ?? string.Empty;
    }

    /// <summary>
    /// Check if the current player is the host of the session.
    /// </summary>
    public bool IsHost()
    {
        return _currentSession?.IsHost ?? false;
    }

    /// <summary>
    /// Get all players currently in the session.
    /// </summary>
    public IReadOnlyList<IReadOnlyPlayer> GetPlayers()
    {
        return _currentSession?.Players ?? new List<IReadOnlyPlayer>();
    }

    /// <summary>
    /// Get the current player.
    /// </summary>
    public IReadOnlyPlayer GetCurrentPlayer()
    {
        return _currentSession?.CurrentPlayer;
    }

    private void HandlePlayerJoined(string playerId)
    {
        Debug.Log($"[SessionManager] Player joined: {playerId}");
        OnPlayerJoined?.Invoke(playerId);
    }

    private void HandlePlayerLeft(string playerId)
    {
        Debug.Log($"[SessionManager] Player left: {playerId}");
        OnPlayerLeft?.Invoke(playerId);
    }

    private void HandleSessionEnded()
    {
        Debug.Log("[SessionManager] Session ended.");
        _currentSession = null;
        OnSessionEnded?.Invoke();
    }

    private void OnDestroy()
    {
        if (_currentSession != null)
        {
            // Unsubscribe from events
            _currentSession.PlayerJoined -= HandlePlayerJoined;
            _currentSession.PlayerLeaving -= HandlePlayerLeft;
            _currentSession.Deleted -= HandleSessionEnded;
        }
    }
}
