using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages the party system and game loop.
/// Handles party creation, guest arrivals, and time management.
/// </summary>
public class PartyManager : MonoBehaviour
{
    private static PartyManager _instance;
    private static bool _applicationQuitting = false;

    /// <summary>
    /// Public accessor for the singleton instance.
    /// Returns null if the application is quitting.
    /// </summary>
    public static PartyManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // Try to find existing instance in scene
                PartyManager found = FindFirstObjectByType<PartyManager>();
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
    [SerializeField] private Party currentParty;
    [SerializeField] private float timeBetweenEachGuestArrival = 15.0f;

    [Header("Game State")]
    [SerializeField] private int currentDay = 1;

    /// <summary>
    /// Whether this PartyManager should persist across scene loads.
    /// </summary>
    [SerializeField] private bool persistAcrossScenes = true;

    [SerializeField] private Transform guestSpawnLocation;

    // Properties
    public GameObject guestPrefab;
    public Party CurrentParty => currentParty;
    public int CurrentDay => currentDay;
    public bool IsPartyActive => currentParty != null && currentParty.state == PartyState.Active;
    public float TimeBetweenEachGuestArrival => timeBetweenEachGuestArrival;

    // Accessors
    public float GetPartyDurationInMinutes() { return partyDurationMinutes; }

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

        // Persist across scenes if enabled
        if (persistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnApplicationQuit()
    {
        _applicationQuitting = true;
    }

    private void OnDestroy()
    {
        // Only clear instance if this is the current instance
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void Update()
    {
        // Only server handles party timing and guest spawning
        var networkManager = Unity.Netcode.NetworkManager.Singleton;
        bool isServer = networkManager != null && networkManager.IsServer;

        // Nothing non-server gets past this
        // Everything after this needs a client side RPC to server to get data.
        if(!isServer)
        {
            return;
        }
        
        // Server Logic
        if (currentParty != null && currentParty.state == PartyState.Active)
        {
            currentParty.timeSinceGuestSpawned += Time.deltaTime;

            // Check if it's time to spawn another guest
            if (currentParty.timeSinceGuestSpawned >= timeBetweenEachGuestArrival)
            {
                // Check if we haven't reached the guest limit
                if (currentParty.currentGuests.Count < currentParty.guestLimit)
                {
                    currentParty.timeSinceGuestSpawned = 0.0f;
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

    private void OnServerSpawnGuestsClientRPC()
    {
       
    }

    private void Start()
    {
        // Start code goes here
    }

    /// <summary>
    /// Start a new party for the current day
    /// </summary>
    public void StartParty(int guestLimit = -1)
    {
        // If start party was called with default parameter
        if (guestLimit < 0)
        {
            guestLimit = defaultGuestLimit;
        }

        if (IsPartyActive)
        {
            Debug.LogWarning("Cannot start a new party while one is already active!");
            return;
        }

        // Use provided limit or default
        int limit = guestLimit > 0 ? guestLimit : defaultGuestLimit;

        // Create new party
        currentParty = new Party(currentDay, limit);
        currentParty.Start();

        // Reset guest spawn timer
        currentParty.timeSinceGuestSpawned = 0.0f;

        // First guest will spawn after timeBetweenEachGuestArrival seconds in Update()
    }

    /// <summary>
    /// End the current party
    /// </summary>
    public void EndParty()
    {
        if (currentParty == null || currentParty.state != PartyState.Active)
        {
            Debug.LogWarning("No active party to end.");
            return;
        }

        // Get the time BEFORE changing state (GetCurrentInGameTime returns start time if not Active)
        float inGameTime = currentParty.GetCurrentInGameTime();
        currentParty.state = PartyState.Ended;
        currentParty.End();

        Debug.Log($"Party ended for Day {currentParty.dayNumber}! Final in-game time: {inGameTime:F2}:00");

        // TODO: Clean up guests, calculate results, etc.

        // Advance to next day
        currentDay++;
    }

    /// <summary>
    /// Get the current in-game time if a party is active
    /// </summary>
    public float GetCurrentInGameTime()
    {
        if (currentParty != null && currentParty.state == PartyState.Active)
        {
            return currentParty.GetCurrentInGameTime();
        }
        return 22.0f; // Default to 10pm
    }

    /// <summary>
    /// Format in-game time as a readable string (e.g., "10:30 PM")
    /// </summary>
    public string GetFormattedInGameTime()
    {
        float time = GetCurrentInGameTime();
        int hours = Mathf.FloorToInt(time);
        int minutes = Mathf.FloorToInt((time - hours) * 60f);

        string period = hours >= 12 ? "PM" : "AM";
        int displayHours = hours > 12 ? hours - 12 : (hours == 0 ? 12 : hours);

        return $"{displayHours:D2}:{minutes:D2} {period}";
    }

    /// <summary>
    /// Select and spawn one guest based on probability system.
    /// Randomly selects one guest type and rolls probability for arrival.
    /// Spawns either the selected guest or a "None" guest if probability fails.
    /// </summary>
    private void SpawnGuests()
    {
        if (currentParty == null || GameManager.Instance == null)
        {
            Debug.LogError("Cannot select guests: Party or GameManager is null!");
            return;
        }

        // Check if we've reached the guest limit
        if (currentParty.currentGuests.Count >= currentParty.guestLimit)
        {
            Debug.Log($"Party reached guest limit ({currentParty.guestLimit}). Cannot spawn more guests.");
            return;
        }

        // Get current party popularity (updates as guests arrive)
        float partyPopularity = currentParty.popularity;

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
        if (guestSO == null || currentParty == null)
            return;

        var networkManager = Unity.Netcode.NetworkManager.Singleton;
        bool isServer = networkManager != null && networkManager.IsServer;

        // Create a GameObject for the guest
        GameObject guestObject = null;

        // If networking is enabled, use NetworkObject.Instantiate
        if (networkManager != null && isServer)
        {
            guestObject = Unity.Netcode.NetworkObject.Instantiate(guestSO.prefab, guestSpawnLocation);
        }

        NPCGuest npcGuest = guestObject.GetComponent<NPCGuest>();
        if (npcGuest == null)
        {
            npcGuest = guestObject.AddComponent<NPCGuest>();
        }

        // Initialize the guest with the ScriptableObject data and add to party
        npcGuest.InitializeFromSO(guestSO);
        currentParty.AddGuest(npcGuest);

        // If networked, initialize NetworkedNPCGuest component
        if (networkManager != null)
        {
            var networkedGuest = guestObject.GetComponent<NetworkedNPCGuest>();
            if (networkedGuest == null)
            {
                networkedGuest = guestObject.AddComponent<NetworkedNPCGuest>();
            }
            networkedGuest.InitializeGuestData(guestSO);

            if (isServer)
            {
                guestObject.GetComponent<NetworkObject>().Spawn(true);
                // Update party stats based on guest
                currentParty.popularity += guestSO.popularity;
                currentParty.troubleMaker += guestSO.troubleMaker;
                currentParty.cashValue += guestSO.cashvalue;

                // If networked, sync party stats via NetworkedParty
                var networkedParty = FindFirstObjectByType<NetworkedParty>();
                if (networkedParty != null)
                {
                    var guestNetworkObject = guestObject.GetComponent<Unity.Netcode.NetworkObject>();
                    ulong guestNetworkId = guestNetworkObject != null ? guestNetworkObject.NetworkObjectId : 0;
                    networkedParty.AddGuestServerRPC(guestNetworkId, guestSO.popularity, guestSO.troubleMaker, guestSO.cashvalue);
                }
            }
        }

        if (isServer && npcGuest.Abilities.Count > 0)
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
    public void RequestStartParty()
    {
        if (!NetworkManager.Singleton.IsHost)
        {
            Debug.Log("Client cannot start the game loop");
            return;
        }
        if (!IsPartyActive)
        {
            //GetComponent<NetworkedPartyManager>().StartPartyServerRPC(1, 20);
        }
    }
}
