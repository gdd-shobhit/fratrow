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
        EnsureNetworkManagerReady();
    }

    private void Start()
    {
        EnsureNetworkManagerReady();
        SubscribeServerStarted();
    }

    private void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnServerStarted -= OnServerStarted;
        }
    }

    /// <summary>
    /// NetworkManager.Singleton is assigned in OnEnable. With Enter Play Mode Options
    /// (no domain/scene reload) that can be skipped after a script recompile, leaving
    /// Singleton null until something re-registers it. Multiplayer Sessions then fail with
    /// "Cannot start when NetworkManager.Singleton is not set."
    /// </summary>
    public bool EnsureNetworkManagerReady()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
            return true;

        nm = FindFirstObjectByType<NetworkManager>();
        if (nm == null)
        {
            Debug.LogError("[NetworkBootstrap] No NetworkManager found in loaded scenes.");
            return false;
        }

        if (!nm.gameObject.activeInHierarchy)
            nm.gameObject.SetActive(true);

        if (NetworkManager.Singleton == null)
            nm.SetSingleton();

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[NetworkBootstrap] Failed to register NetworkManager.Singleton.");
            return false;
        }

        Debug.Log("[NetworkBootstrap] NetworkManager.Singleton was missing; re-registered from scene instance.");
        SubscribeServerStarted();
        return true;
    }

    private void SubscribeServerStarted()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return;

        nm.OnServerStarted -= OnServerStarted;
        nm.OnServerStarted += OnServerStarted;
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
            return EnsureNetworkManagerReady();
        }

        try
        {
            if (!EnsureNetworkManagerReady())
                return false;

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
            LastServicesError = string.Empty;
            Debug.Log("[NetworkBootstrap] Services initialization complete.");
            return true;
        }
        catch (System.Exception e)
        {
            LastServicesError = DescribeServicesFailure(e);
            Debug.LogError($"[NetworkBootstrap] Failed to initialize services: {LastServicesError}\n{e}");
            return false;
        }
    }

    /// <summary>Human-readable reason for the last Unity Services failure (empty when none).</summary>
    public string LastServicesError { get; private set; } = string.Empty;

    /// <summary>
    /// UnityServices.InitializeAsync wraps the real cause ("Some services couldn't be initialized"). Surface the inner
    /// exceptions and the most common cause: the editor project is not linked to a Unity Cloud project.
    /// </summary>
    static string DescribeServicesFailure(System.Exception e)
    {
        var reasons = new System.Collections.Generic.List<string>();
        if (string.IsNullOrEmpty(Application.cloudProjectId))
            reasons.Add("This project is not linked to a Unity Cloud project (Edit > Project Settings > Services > link the existing 'fratrow' project).");
        for (var inner = e; inner != null; inner = inner.InnerException)
        {
            if (inner is System.AggregateException aggregate)
            {
                foreach (var each in aggregate.Flatten().InnerExceptions) reasons.Add(each.GetType().Name + ": " + each.Message);
                break;
            }
            reasons.Add(inner.GetType().Name + ": " + inner.Message);
        }
        return string.Join(" | ", reasons);
    }

    // ---------------- Local (no Unity Services) networking for development ----------------

    /// <summary>True when this session was started with direct transport instead of a Relay session.</summary>
    public bool IsLocalMode { get; private set; }

    /// <summary>Default direct-transport port for local development sessions.</summary>
    public const ushort LocalPort = 7777;

    /// <summary>
    /// Development fallback: host over direct transport (no Unity Services / Relay). Clients on this machine or LAN join
    /// with <see cref="StartLocalClient"/>. Returns false when the NetworkManager is unavailable or already running.
    /// </summary>
    public bool StartLocalHost(ushort port = LocalPort)
    {
        var networkManager = NetworkManager.Singleton;
        if (!EnsureNetworkManagerReady() || networkManager == null || networkManager.IsServer || networkManager.IsClient) return false;
        var transport = networkManager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport == null) { Debug.LogError("[NetworkBootstrap] UnityTransport missing; cannot host locally."); return false; }
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
        EquippedPrefabRegistrar.RegisterAllEquippedPrefabs();
        IsLocalMode = networkManager.StartHost();
        Debug.Log(IsLocalMode ? $"[NetworkBootstrap] Local host started on port {port} (no Unity Services)." : "[NetworkBootstrap] Local host failed to start.");
        return IsLocalMode;
    }

    /// <summary>Development fallback: connect directly to a local/LAN host at <paramref name="address"/>.</summary>
    public bool StartLocalClient(string address, ushort port = LocalPort)
    {
        var networkManager = NetworkManager.Singleton;
        if (!EnsureNetworkManagerReady() || networkManager == null || networkManager.IsServer || networkManager.IsClient) return false;
        var transport = networkManager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport == null) { Debug.LogError("[NetworkBootstrap] UnityTransport missing; cannot join locally."); return false; }
        transport.SetConnectionData(address, port);
        EquippedPrefabRegistrar.RegisterAllEquippedPrefabs();
        IsLocalMode = networkManager.StartClient();
        Debug.Log(IsLocalMode ? $"[NetworkBootstrap] Connecting directly to {address}:{port} (no Unity Services)." : "[NetworkBootstrap] Local client failed to start.");
        return IsLocalMode;
    }

    /// <summary>Local fallback is offered only in the editor and development builds.</summary>
    public static bool LocalFallbackAllowed => Application.isEditor || Debug.isDebugBuild;

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
