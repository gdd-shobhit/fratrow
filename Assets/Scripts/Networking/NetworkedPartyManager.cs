using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
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
    [SerializeField]
    private NetworkVariable<int> _dayNumber = new NetworkVariable<int>(1);

    /// <summary>
    /// Whether this PartyManager should persist across scene loads.
    /// </summary>
    [SerializeField] private bool persistAcrossScenes = true;

    [SerializeField] private Transform guestSpawnLocation;
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

        // Find or create NetworkedParty component
        _networkedParty = FindFirstObjectByType<NetworkedParty>();
        if (_networkedParty == null)
        {
            Debug.LogError("This SHOULDNT be null. Check scene if it already is there");
        }
    }

    private void Update()
    {
        // Nothing non-server gets past this
        // Everything after this needs a client side RPC to server to get data.
        if (IsServer)
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
                if (_networkedParty.currentGuests.Count < _networkedParty.guestLimit)
                {
                    _networkedParty.timeSinceGuestSpawned = 0.0f;
                    SpawnGuests();
                }
            }
        }

        // Check if current party should end (server-only in multiplayer)
        if (isServer && IsPartyActive && currentParty.IsPartyOver())
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
            Debug.LogError("Cannot select guests: Party or GameManager is null!");
            return;
        }

        // Check if we've reached the guest limit
        if (_networkedParty.currentGuests.Count >= currentParty.guestLimit)
        {
            Debug.Log($"Party reached guest limit ({currentParty.guestLimit}). Cannot spawn more guests.");
            return;
        }

        // Get current party popularity (updates as guests arrive)
        float partyPopularity = _networkedParty.Popularity;

        // Get guest dictionary from GameManager
        Dictionary<GuestClass, GuestSO> guestDict = GameManager.Instance.guestClassesDict;

        if (guestDict == null || guestDict.Count == 0)
        {
            Debug.LogWarning("No guests available in GameManager dictionary!");
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

    /// <summary>
    /// Start a party. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StartPartyServerRPC(int dayNumber, int guestLimit)
    {
        if (!IsServer) return;

        Debug.Log($"[NetworkedPartyManager] Server starting party - Day {dayNumber}, Guest Limit: {guestLimit}");

        if(_instance != null)
        {
            _instance.StartParty(guestLimit);
        }

        // Sync via NetworkedParty
        if (_networkedParty != null)
        {
            _networkedParty.StartPartyServerRPC(dayNumber, guestLimit);
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
        if (_partyManager != null)
        {
            _partyManager.EndParty();
        }
        // Sync via NetworkedParty
        if (_networkedParty != null)
        {
            _networkedParty.EndPartyServerRPC();
        }
    }

    private void Update()
    {
        // Only server handles party timing and guest spawning
        if (!IsServer) return;

        if (_partyManager != null && _partyManager.IsPartyActive)
        {
            // Update networked party time tracking
            if (_networkedParty != null)
            {
                _networkedParty.UpdateTimeSinceGuestSpawned(Time.deltaTime);
            }
        }
    }
}
