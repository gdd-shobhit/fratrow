using System.Collections;
using System.Collections.Generic;
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
            if (_applicationQuitting)
            {
                Debug.LogWarning("PartyManager instance already destroyed. Application is quitting.");
                return null;
            }

            if (_instance == null)
            {
                // Try to find existing instance in scene
                _instance = FindFirstObjectByType<PartyManager>();

                // If no instance exists, create one
                if (_instance == null)
                {
                    GameObject partyManagerObject = new GameObject("PartyManager");
                    _instance = partyManagerObject.AddComponent<PartyManager>();
                    DontDestroyOnLoad(partyManagerObject);
                    Debug.Log("PartyManager instance created.");
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
    
    [Header("Game State")]
    [SerializeField] private int currentDay = 1;
    
    /// <summary>
    /// Whether this PartyManager should persist across scene loads.
    /// </summary>
    [SerializeField] private bool persistAcrossScenes = true;

    // Properties
    public Party CurrentParty => currentParty;
    public int CurrentDay => currentDay;
    public bool IsPartyActive => currentParty != null && currentParty.state == PartyState.Active;

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
        // Check if current party should end
        if (IsPartyActive && currentParty.IsPartyOver())
        {
            EndParty();
        }
    }

    private void Start()
    {
        StartParty(defaultGuestLimit);

    }

    /// <summary>
    /// Start a new party for the current day
    /// </summary>
    public void StartParty(int guestLimit = -1)
    {
        if (IsPartyActive)
        {
            Debug.LogWarning("Cannot start a new party while one is already active!");
            return;
        }

        // Use provided limit or default
        int limit = guestLimit > 0 ? guestLimit : defaultGuestLimit;
        
        // Create new party
        currentParty = new Party(currentDay, limit);
        currentParty.state = PartyState.Active;
        currentParty.partyStartTime = Time.time;
        float durationInSeconds = partyDurationMinutes * 60f;
        currentParty.partyEndTime = Time.time + durationInSeconds; // Convert minutes to seconds
        
        Debug.Log($"Party started for Day {currentDay}! Guest limit: {limit}. Party will last {partyDurationMinutes} minutes ({durationInSeconds} seconds). Start: {currentParty.partyStartTime:F2}, End: {currentParty.partyEndTime:F2}");
        
        // TODO: Step 2 - Trigger guest arrival logic here
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
        currentParty.DistributePartyEndRewards();

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
}
