using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEditor.ShaderGraph.Internal.KeywordDependentCollection;

/// <summary>
/// NetworkBehaviour singleton for GameManager.
/// Syncs total metrics (popularity, cash) across the network.
/// Server authoritative for game progression.
/// </summary>
public class NetworkedGameManager : NetworkBehaviour
{
    public List<PlayerClassSO> playerClasses;
    public List<GuestSO> guestClasses;

    public Dictionary<PlayerClass, PlayerClassSO> playerClassesDict;
    public Dictionary<GuestClass, GuestSO> guestClassesDict;
    public Dictionary<ulong, GameObject> playersInGame;

    // Metrics for how the team is doing
    public NetworkVariable<FraternityData> myFraternity = new NetworkVariable<FraternityData>();

    // Network synchronized variables
    public NetworkVariable<float> _totalPopularity = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _totalCashValue = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _totalTroubleMaking = new NetworkVariable<float>(0f);

    // Public getters for network variables
    public float TotalPopularity => _totalPopularity.Value;
    public float TotalCashValue => _totalCashValue.Value;
    public float TotalTroubleMaking => _totalTroubleMaking.Value;

    public event System.Action<float> OnPopularityChanged;
    public event System.Action<float> OnCashValueChanged;
    public event System.Action<float> OnTroubleChanged;

    [SerializeField]
    public FraternityData fraternityLocal;
    private string fratName = "Frat Test Name";
    private static NetworkedGameManager _instance;
    public static NetworkedGameManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // Try to find existing instance in scene
                NetworkedGameManager found = FindFirstObjectByType<NetworkedGameManager>();

                if (found != null)
                {
                    // Found one in scene - use it (Awake will set _instance properly)
                    _instance = found;
                }
                else
                {
                    _instance = new NetworkedGameManager();
                }
            }

            return _instance;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if(IsClient)
        {
            Debug.LogWarning($"[NetoworkedGameManager] OnNetworkSpawn on {NetworkManager.Singleton.LocalClientId}");
            // Subscribe to network variable changes
            _totalPopularity.OnValueChanged += OnTotalPopularityChanged;
            _totalCashValue.OnValueChanged += OnTotalCashValueChanged;
            _totalTroubleMaking.OnValueChanged += OnTotalTroubleMakingChanged;   
        }

        fraternityLocal = myFraternity.Value;
        fraternityLocal.fratName = fratName;
    }

    private void Awake()
    {
        // If instance already exists and it's not this, destroy this
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning($"Duplicate GameManager instance on {gameObject.name} destroyed.");
            Destroy(gameObject);
            return;
        }

        // Set this as the instance
        _instance = this;

        playerClassesDict = new Dictionary<PlayerClass, PlayerClassSO>();
        guestClassesDict = new Dictionary<GuestClass, GuestSO>();
        playersInGame = new Dictionary<ulong, GameObject>();

        // Intialize the dictionaries from our database of players and npc classes
        for (int i = 0; i < playerClasses.Count; i++)
        {
            playerClassesDict.Add(playerClasses[i].playerClass, playerClasses[i]);
        }

        for (int i = 0; i < guestClasses.Count; i++)
        {
            guestClassesDict.Add(guestClasses[i].guestClass, guestClasses[i]);
        }

        Debug.Log($"Players and Guest classes are loaded");

        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unsubscribe from network variable changes
        _totalPopularity.OnValueChanged -= OnTotalPopularityChanged;
        _totalCashValue.OnValueChanged -= OnTotalCashValueChanged;
        _totalTroubleMaking.OnValueChanged -= OnTotalTroubleMakingChanged;
    }

    // Network variable change handlers
    private void OnTotalPopularityChanged(float oldValue, float newValue)
    {
        OnPopularityChanged?.Invoke(newValue);
    }

    private void OnTotalCashValueChanged(float oldValue, float newValue)
    {
        OnCashValueChanged?.Invoke(newValue);
    }
    private void OnTotalTroubleMakingChanged(float oldValue, float newValue)
    {
        OnTroubleChanged?.Invoke(newValue);
    }
    public void AddTotalCashValue(float inCashValue)
    {
        this._totalCashValue.Value += inCashValue;
    }

    /// <summary>
    /// Server only. Deducts from the team's cumulative <see cref="_totalCashValue"/> if funds are sufficient.
    /// </summary>
    /// <returns>True if the charge was applied (or cost was zero).</returns>
    public bool TrySpendTeamCash(float amount)
    {
        if (!IsServer)
        {
            return false;
        }
        if (amount <= 0f)
        {
            return true;
        }
        if (_totalCashValue.Value < amount)
        {
            return false;
        }
        _totalCashValue.Value -= amount;
        return true;
    }

    public void AddTotalPopularityValue(float inPopularityValue)
    {
        this._totalPopularity.Value += inPopularityValue;
    }
    public void AddTotalTroubleMaking(float inTroubleMaking)
    {
        this._totalTroubleMaking.Value += inTroubleMaking;
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

        // Enable camera only on this machine's own player (client sees their camera, host sees theirs)
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            var cam = playerObject.GetComponentInChildren<Camera>();
            if (cam != null) cam.enabled = true;
        }
    }

    /// <summary>
    /// Get the in-game player GameObject for a client. Server uses GameManager.playersInGame;
    /// clients find it via SpawnManager by OwnerClientId.
    /// </summary>
    public GameObject GetPlayerGameObject(ulong clientId) => GetPlayerObject(clientId);

    private GameObject GetPlayerObject(ulong clientId)
    {
        if (playersInGame == null) return null;

        // Server: we have it in GameManager (PlayerSpawner added it)
        if (IsServer && playersInGame.TryGetValue(clientId, out GameObject playerObject))
        {
            // Only return if the reference is still valid (not destroyed)
            if (playerObject != null) return playerObject;
        }

        // Client (or late host): playersInGame is server-only — walk spawned objects.
        if (NetworkManager.Singleton?.SpawnManager?.SpawnedObjectsList == null) return null;

        foreach (var no in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (no.OwnerClientId != clientId) continue;
            if (no.IsPlayerObject) return no.gameObject;
        }

        foreach (var no in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (no.OwnerClientId != clientId) continue;
            if (no.GetComponent<UserCharacter>() == null) continue;

            return no.gameObject;
        }

        return null;
    }
}
