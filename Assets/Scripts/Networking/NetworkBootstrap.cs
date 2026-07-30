using System.Threading.Tasks;
using Systems.Inventory;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Handles initialization of Unity Services and NetCode networking.
/// Must be initialized before creating or joining sessions.
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    private static NetworkBootstrap _instance;
    public static NetworkBootstrap Instance
    {
        get
        {
            if (_instance == null)
            {
                var found = FindFirstObjectByType<NetworkBootstrap>();
                if (found != null)
                {
                    _instance = found;
                }
                else
                {
                    GameObject go = new GameObject("NetworkBootstrap");
                    _instance = go.AddComponent<NetworkBootstrap>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    private bool _isInitialized = false;
    public bool IsInitialized => _isInitialized;

    [Header("Server-spawned objects")]
    [Tooltip("Prefab to spawn when server starts. Must be in Network Prefabs List. Leave empty to skip.")]
    [SerializeField] private GameObject lobbySystemsPrefab;

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

    private void Start()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnServerStarted += OnServerStarted;
        }
    }

    private void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnServerStarted -= OnServerStarted;
        }
    }

    private void OnServerStarted()
    {
        if (lobbySystemsPrefab == null) return;

        var no = lobbySystemsPrefab.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError("[NetworkBootstrap] LobbySystems prefab has no NetworkObject!");
            return;
        }

        var instance = Instantiate(lobbySystemsPrefab);
        instance.GetComponent<NetworkObject>().Spawn();
        Debug.Log("[NetworkBootstrap] LobbySystems spawned on server");
    }

    /// <summary>
    /// Initialize Unity Services (Core and Authentication).
    /// Call this before creating or joining sessions.
    /// </summary>
    public async Task<bool> InitializeServicesAsync()
    {
        if (_isInitialized)
        {
            Debug.Log("[NetworkBootstrap] Services already initialized.");
            return true;
        }

        try
        {
            Debug.Log("[NetworkBootstrap] Initializing Unity Services...");

            // Initialize Unity Services Core
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
                Debug.Log("[NetworkBootstrap] Unity Services Core initialized.");
            }

            // ParrelSync clone must use a different player identity so we're not "already a member of the lobby"
            if (ParrelSyncRuntime.IsParrelSyncClone)
            {
                if (AuthenticationService.Instance.IsSignedIn)
                    AuthenticationService.Instance.SignOut();
                AuthenticationService.Instance.ClearSessionToken();
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[NetworkBootstrap] ParrelSync clone signed in with new anonymous ID. Player ID: {AuthenticationService.Instance.PlayerId}");
            }
            else if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[NetworkBootstrap] Signed in anonymously. Player ID: {AuthenticationService.Instance.PlayerId}");
            }

            _isInitialized = true;
            Debug.Log("[NetworkBootstrap] Services initialization complete.");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[NetworkBootstrap] Failed to initialize services: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Start hosting a game (creates server and client).
    /// </summary>
    public void StartHost()
    {
        if (!_isInitialized)
        {
            Debug.LogError("[NetworkBootstrap] Services not initialized! Call InitializeServicesAsync() first.");
            return;
        }

        var networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            Debug.LogError("[NetworkBootstrap] NetworkManager not found! Make sure NetCode is set up.");
            return;
        }

        if (networkManager.IsServer || networkManager.IsClient)
        {
            Debug.LogWarning("[NetworkBootstrap] Already connected! Disconnect first.");
            return;
        }

        Debug.Log("[NetworkBootstrap] Starting as host...");
        EquippedPrefabRegistrar.RegisterAllEquippedPrefabs();
        networkManager.StartHost();
    }

    /// <summary>
    /// Start as client and connect to a server.
    /// </summary>
    public void StartClient()
    {
        if (!_isInitialized)
        {
            Debug.LogError("[NetworkBootstrap] Services not initialized! Call InitializeServicesAsync() first.");
            return;
        }

        var networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            Debug.LogError("[NetworkBootstrap] NetworkManager not found! Make sure NetCode is set up.");
            return;
        }

        if (networkManager.IsServer || networkManager.IsClient)
        {
            Debug.LogWarning("[NetworkBootstrap] Already connected! Disconnect first.");
            return;
        }

        Debug.Log("[NetworkBootstrap] Starting as client...");
        EquippedPrefabRegistrar.RegisterAllEquippedPrefabs();
        networkManager.StartClient();
    }
}
