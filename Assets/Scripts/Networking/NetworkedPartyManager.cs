using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NetworkBehaviour singleton for PartyManager.
/// Server-only: guest spawning logic, party timing.
/// Syncs party state changes across the network.
/// </summary>
public class NetworkedPartyManager : NetworkBehaviour
{

    private static NetworkedPartyManager _instance;
    public static NetworkedPartyManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // Try to find existing instance in scene
                NetworkedPartyManager found = FindFirstObjectByType<NetworkedPartyManager>();
                if (found != null)
                {
                    _instance = found;
                }
                else
                {
                    Debug.Log("PartyManager not found");
                }
            }

            return _instance;
        }
    }

    [Header("Party Settings")]
    [SerializeField] private float partyDurationMinutes = 10f; // Real-time minutes for a party
    [SerializeField] private int defaultGuestLimit = 20;

    [Header("Current Party")]
    private NetworkedParty _networkedParty;
    [SerializeField] private float timeBetweenEachGuestArrival = 15.0f;

    [Header("Game State")]
    [SerializeField] private bool persistAcrossScenes = true;
    [SerializeField] private Transform guestSpawnLocation;

    // Action events
    public event System.Action<int> OnDayNumberChanged;
    public event System.Action<float> OnCurrentGuestNumberChanged;
    public event System.Action<float> OnCurrentTimeChanged;


    // Accessors
    public bool IsPartyActive => _networkedParty != null && _networkedParty.State == PartyState.Active;
    public bool IsPartyOver => NetworkManager.ServerTime.Time >= _networkedParty.PartyEndTime;
    public float PartyDurationMinutes => partyDurationMinutes;
    public NetworkedParty NetworkedParty { get => _networkedParty; set => _networkedParty = value; }
    private void Awake()
    {
        // If instance already exists and it's not this, destroy this
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning($"Duplicate PartyManager instance on {gameObject.name} destroyed.");
            Destroy(gameObject);
            return;
        }

        // Set this as the instance
        _instance = this;

        if (IsServer && !_instance.GetComponent<NetworkObject>().IsSpawned)
        {
            _instance.GetComponent<NetworkObject>().Spawn();
        }

        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _instance = GetComponent<NetworkedPartyManager>();
        if (_instance == null)
        {
            Debug.LogError("[NetworkedPartyManager] PartyManager component not found!");
            return;
        }
    }

    private void Update()
    {
        // Nothing non-server gets past this
        // Everything after this needs a client side RPC to server to get data.
        if (!IsServer)
        {
            return;
        }

        // Server Logic
        if (_networkedParty != null && _networkedParty.State == PartyState.Active)
        {
            _networkedParty._timeSinceGuestSpawned += Time.deltaTime;

            // Check if it's time to spawn another guest
            if (_networkedParty._timeSinceGuestSpawned >= timeBetweenEachGuestArrival)
            {
                // Check if we haven't reached the guest limit
                if (_networkedParty.currentGuests.Count < _networkedParty.GuestLimit)
                {
                    _networkedParty._timeSinceGuestSpawned = 0.0f;
                    SpawnGuests();
                }
            }
        }

        // Check if current party should end (server-only in multiplayer)
        if (IsPartyActive && IsPartyOver)
        {
            GetComponent<NetworkedPartyManager>().EndPartyServerRPC();
        }
    }

    /// <summary>
    /// Select and spawn one guest based on probability system.
    /// Randomly selects one guest type and rolls probability for arrival.
    /// Spawns either the selected guest or a "None" guest if probability fails.
    /// </summary>
    private void SpawnGuests()
    {
        if (_networkedParty == null)
        {
            Debug.LogError("Cannot select guests: Party or NetworkedGameManager is null!");
            return;
        }

        // Check if we've reached the guest limit
        if (_networkedParty.currentGuests.Count >= _networkedParty.GuestLimit)
        {
            Debug.Log($"Party reached guest limit ({_networkedParty.GuestLimit}). Cannot spawn more guests.");
            return;
        }

        // Get current party popularity (updates as guests arrive)
        float partyPopularity = _networkedParty.Popularity;

        // Get guest dictionary from NetworkedGameManager
        Dictionary<GuestClass, GuestSO> guestDict = NetworkedGameManager.Instance.guestClassesDict;

        if (guestDict == null || guestDict.Count == 0)
        {
            Debug.LogWarning("No guests available in NetworkedGameManager dictionary!");
            return;
        }

        // Randomly select one guest type other than none
        // TODO: Don't make it random, each class would have its own probability based on the party popularity
        GuestClass selectedGuestClass = (GuestClass)(Random.Range((int)(GuestClass.First + 1), (int)GuestClass.Count));
        GuestSO selectedGuestSO = guestDict[selectedGuestClass];

        // Calculate modified arrival chance based on party popularity
        float arrivalChance = selectedGuestSO.GetModifiedArrivalChance(partyPopularity);

        // Roll probability (0-100)
        float roll = Random.Range(0f, 100f);

        if (roll <= arrivalChance)
        {
            // Guest arrives! Spawn them
            SpawnGuest(selectedGuestSO);
            Debug.Log($"{selectedGuestSO.guestName} arrived! (Roll: {roll:F2} <= {arrivalChance:F2}%)");
        }
        else
        {
            // Guest doesn't arrive - spawn "None" guest instead
            SpawnGuest(guestDict[GuestClass.None]);
            Debug.Log($"{selectedGuestSO.guestName} didn't arrive. (Roll: {roll:F2} > {arrivalChance:F2}%) - Spawning None guest");
        }
    }

    /// <summary>
    /// Spawn a guest from a GuestSO
    /// </summary>
    private void SpawnGuest(GuestSO guestSO)
    {
        if (guestSO == null || _networkedParty == null)
            return;

        // Create a GameObject for the guest
        GameObject guestObject = Unity.Netcode.NetworkObject.Instantiate(guestSO.prefab, guestSpawnLocation);

        NPCGuest npcGuest = guestObject.GetComponent<NPCGuest>();
        if (npcGuest == null)
        {
            npcGuest = guestObject.AddComponent<NPCGuest>();
        }

        // Initialize the guest with the ScriptableObject data and add to party
        npcGuest.InitializeFromSO(guestSO);
        _networkedParty.AddGuest(npcGuest);

        var networkedGuest = guestObject.GetComponent<NetworkedNPCGuest>();
        if (networkedGuest == null)
        {
            networkedGuest = guestObject.AddComponent<NetworkedNPCGuest>();
        }
        networkedGuest.InitializeGuestData(guestSO);

        guestObject.GetComponent<NetworkObject>().Spawn(true);
        // Update party stats based on guest
        _networkedParty._popularity.Value += guestSO.popularity;
        _networkedParty._troubleMaker.Value += guestSO.troubleMaker;
        _networkedParty._cashValue.Value += guestSO.cashvalue;

        // If networked, sync party stats via NetworkedParty
        var networkedParty = FindFirstObjectByType<NetworkedParty>();
        if (networkedParty != null)
        {
            var guestNetworkObject = guestObject.GetComponent<Unity.Netcode.NetworkObject>();
            ulong guestNetworkId = guestNetworkObject != null ? guestNetworkObject.NetworkObjectId : 0;
            networkedParty.AddGuestServerRPC(guestNetworkId, guestSO.popularity, guestSO.troubleMaker, guestSO.cashvalue);
        }
            

        if (IsServer && npcGuest.Abilities.Count > 0)
        {
            //TODO: Implement abilities
            foreach (Ability ability in npcGuest.Abilities)
            {
                if (ability != null && ability.AbilityType == AbilityType.Passive)
                {
                    // TODO: Give passive boost
                }
            }
        }

    }

    public void StartPartyServer()
    {
        if (!IsServer) return;

        Debug.Log($"[NetworkedPartyManager] Server starting party");
        // If start party was called with default parameter

        // Sync via NetworkedParty
        if (_networkedParty != null)
        {
            _networkedParty.StartPartyServerRPC();
        }
    }

    /// <summary>
    /// End the party. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void EndPartyServerRPC()
    {
        if (!IsServer) return;

        Debug.Log("[NetworkedPartyManager] Server ending party.");

        // Sync via NetworkedParty
        if (_networkedParty != null)
        {
            _networkedParty.EndPartyServerRPC();
        }
    }
}
