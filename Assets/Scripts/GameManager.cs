using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEngine;

/// <summary>
/// Singleton GameManager that persists across scenes and ensures only one instance exists.
/// </summary>
public class GameManager : MonoBehaviour
{
    private static GameManager _instance;
    private static bool _applicationQuitting = false;
    public List<PlayerClassSO> playerClasses;
    public List<GuestSO> guestClasses;

    public Dictionary<PlayerClass,PlayerClassSO> playerClassesDict;
    public Dictionary<GuestClass, GuestSO> guestClassesDict;
    public Dictionary<ulong, GameObject> playersInGame;

    // Metrics for how the team is doing
    private float totalPopularity;
    private float totalCashvalue;
    private float totalTroubleMaking;

    /// <summary>
    /// Public accessor for the singleton instance.
    /// Returns null if the application is quitting.
    /// </summary>
    public static GameManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // Try to find existing instance in scene
                GameManager found = FindFirstObjectByType<GameManager>();
                
                if (found != null)
                {
                    // Found one in scene - use it (Awake will set _instance properly)
                    _instance = found;
                }
                else
                {
                    // If no instance exists, create one
                    GameObject gameManagerObject = new GameObject("GameManager");
                    _instance = gameManagerObject.AddComponent<GameManager>();
                    DontDestroyOnLoad(gameManagerObject);
                    Debug.Log("GameManager instance created via Instance getter.");
                }
            }

            return _instance;
        }
    }

    /// <summary>
    /// Whether this GameManager should persist across scene loads.
    /// </summary>
    [SerializeField] private bool persistAcrossScenes = true;

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

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    protected virtual void Start()
    {
        // Override in derived classes or add initialization logic here
    }

    // Update is called once per frame
    protected virtual void Update()
    {
        // Override in derived classes or add update logic here
    }

    public void AddTotalCashValue(float inCashValue)
    {
        this.totalCashvalue += inCashValue;
    }

    public void AddTotalPopularityValue(float inPopularityValue)
    {
        this.totalPopularity += inPopularityValue;
    }
    public void AddTotalTroubleMaking(float inTroubleMaking)
    {
        this.totalTroubleMaking += inTroubleMaking;
    }
}
