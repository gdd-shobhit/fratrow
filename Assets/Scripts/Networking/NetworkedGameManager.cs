using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NetworkBehaviour singleton for GameManager.
/// Syncs total metrics (popularity, cash) across the network.
/// Server authoritative for game progression.
/// Campus NetworkBehaviours must be baked on this GameObject (prefab/scene) — do not AddComponent them at runtime.
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
    public event System.Action<FraternityData> OnFraternityChanged;

    [SerializeField]
    public FraternityData fraternityLocal;
    private const string PlaceholderFratName = "Frat Test Name";
    private string fratName = PlaceholderFratName;

    /// <summary>The house name the host chose in the lobby, or "" if they didn't (the campaign then keeps its config/saved name).</summary>
    public string FratName => fratName == PlaceholderFratName ? "" : fratName;
    private static NetworkedGameManager _instance;
    public static NetworkedGameManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // Try to find existing instance in scene
                NetworkedGameManager found = FindFirstObjectByType<NetworkedGameManager>();

                // MonoBehaviours cannot be constructed with new; callers already null-check Instance.
                _instance = found;
            }

            return _instance;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        myFraternity.OnValueChanged += OnMyFraternityChanged;
        _totalPopularity.OnValueChanged += OnTotalPopularityChanged;
        _totalCashValue.OnValueChanged += OnTotalCashValueChanged;
        _totalTroubleMaking.OnValueChanged += OnTotalTroubleMakingChanged;

        if (IsServer)
        {
            InitializeFraternityServer();
        }

        SyncFraternityLocal(myFraternity.Value);
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

        myFraternity.OnValueChanged -= OnMyFraternityChanged;
        _totalPopularity.OnValueChanged -= OnTotalPopularityChanged;
        _totalCashValue.OnValueChanged -= OnTotalCashValueChanged;
        _totalTroubleMaking.OnValueChanged -= OnTotalTroubleMakingChanged;
    }

    void InitializeFraternityServer()
    {
        var data = myFraternity.Value;
        if (FraternityGradeCalculator.IsUninitialized(data))
        {
            int members = data.memberCount;
            data = FraternityGradeCalculator.CreateStarting(fratName);
            data.memberCount = Mathf.Clamp(members, 0, FraternityData.MAX_MEMBERS_ALLOWED);
            Debug.Log($"[Fraternity] Seeded starting grade {FraternityGradeCalculator.ToLetter(data.grade)} rating {data.rating}");
        }
        else if (string.IsNullOrWhiteSpace(data.fratName))
        {
            data.fratName = fratName;
        }

        myFraternity.Value = data;
        SyncFraternityLocal(data);
        // NetworkVariable may not notify subscribers for the first server write in all cases.
        OnFraternityChanged?.Invoke(data);
    }

    /// <summary>
    /// Server-only: recompute rating/grade from team totals and write <see cref="myFraternity"/>.
    /// </summary>
    public void RecalculateFraternityGradeServer()
    {
        if (!IsServer)
        {
            return;
        }

        var data = myFraternity.Value;
        if (string.IsNullOrWhiteSpace(data.fratName))
        {
            data.fratName = fratName;
        }

        FraternityGradeCalculator.ApplyToFraternityData(
            ref data,
            _totalPopularity.Value,
            _totalCashValue.Value,
            data.memberCount);

        myFraternity.Value = data;
        SyncFraternityLocal(data);
        Debug.Log($"[Fraternity] Grade {FraternityGradeCalculator.ToLetter(data.grade)} rating {data.rating} (pop={data.popularity}, cash={data.cashValue}, members={data.memberCount})");
        OnFraternityChanged?.Invoke(data);
    }

    /// <summary>
    /// Server: take the fraternity name the host chose in the lobby (empty resets to the default). Called by
    /// <see cref="LobbyManager.StartGame"/>; the campaign applies it to the player house when it starts.
    /// </summary>
    public void SetFratNameServer(string name)
    {
        fratName = string.IsNullOrWhiteSpace(name) ? PlaceholderFratName : name.Trim();
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        var data = myFraternity.Value;
        if (data.fratName == fratName)
        {
            return;
        }

        data.fratName = fratName;
        myFraternity.Value = data;
        SyncFraternityLocal(data);
        OnFraternityChanged?.Invoke(data);
    }

    void SyncFraternityLocal(FraternityData data)
    {
        fraternityLocal = data;
        if (string.IsNullOrWhiteSpace(fraternityLocal.fratName))
        {
            fraternityLocal.fratName = fratName;
        }
    }

    void OnMyFraternityChanged(FraternityData previous, FraternityData current)
    {
        SyncFraternityLocal(current);
        OnFraternityChanged?.Invoke(current);
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

        // With the campaign running, the house treasury lives in the core (mirrored back next frame).
        var campaign = NetworkedCampaign.Instance;
        if (campaign != null && campaign.Session != null)
        {
            return campaign.TrySpendHouseCashServer(Mathf.CeilToInt(amount));
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

    /// <summary>
    /// Host: mirror the campaign's players' house into the legacy NetworkVariables (popularity 0..100, treasury, heat as
    /// trouble, rating/grade/members). Writes only on change.
    /// </summary>
    public void ApplyCampaignServer(string houseName, float popularity, int cash, float heat, int rating, string grade, int members)
    {
        if (!IsServer)
        {
            return;
        }

        if (!Mathf.Approximately(_totalPopularity.Value, popularity)) _totalPopularity.Value = popularity;
        if (!Mathf.Approximately(_totalCashValue.Value, cash)) _totalCashValue.Value = cash;
        if (!Mathf.Approximately(_totalTroubleMaking.Value, heat)) _totalTroubleMaking.Value = heat;
        var data = myFraternity.Value;
        Grade letter = Grade.C;
        if (!string.IsNullOrEmpty(grade)) System.Enum.TryParse(grade, out letter);
        bool changed = data.fratName != houseName || data.rating != rating || data.grade != letter || data.memberCount != members ||
                       data.popularity != Mathf.RoundToInt(popularity) || data.cashValue != cash;
        if (!changed)
        {
            return;
        }

        data.fratName = houseName;
        data.rating = rating;
        data.grade = letter;
        data.memberCount = Mathf.Clamp(members, 0, FraternityData.MAX_MEMBERS_ALLOWED);
        data.popularity = Mathf.RoundToInt(popularity);
        data.cashValue = cash;
        myFraternity.Value = data;
        SyncFraternityLocal(data);
        OnFraternityChanged?.Invoke(data);
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
    /// Get the in-game player GameObject for a client. Server uses playersInGame;
    /// clients find it via SpawnManager by OwnerClientId.
    /// </summary>
    public GameObject GetPlayerGameObject(ulong clientId) => GetPlayerObject(clientId);

    private GameObject GetPlayerObject(ulong clientId)
    {
        if (playersInGame == null) return null;

        // Server: we have it in playersInGame (PlayerSpawner added it)
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
