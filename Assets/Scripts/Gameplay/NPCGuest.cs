using UnityEngine;

public enum GuestClass
{
    First = 0,
    None = First,
    Nerd,
    MeanGirl,
    Jock,
    Count = Jock
}

/// <summary>
/// NPC Guest character that loads its data from a GuestSO ScriptableObject.
/// </summary>
public class NPCGuest : BaseCharacter
{
    [Header("Guest Data")]
    [SerializeField] private GuestSO guestData;
    [Header("Guest Class Information")]
    public GuestClass guestClass;

    public GuestSO GuestData => guestData;

    protected override void Awake()
    {
        // Only initialize if guestData is already set (from Inspector)
        // If guestData is null, it will be set programmatically via InitializeFromSO()
        if (guestData != null)
        {
            base.Awake();
        }
        // If guestData is null, skip base.Awake() - InitializeFromSO() will call InitializeCharacter() later
    }

    protected override void InitializeCharacter()
    {
        base.InitializeCharacter();

        if (guestData != null)
        {
            // Create stats instance from ScriptableObject data
            stats = guestData.CreateStatsInstance();
            guestClass = guestData.guestClass;
        }
        else
        {
            // Only warn if this is being called and guestData is still null
            // (This shouldn't happen if InitializeFromSO was called properly)
            Debug.LogWarning($"NPCGuest on {gameObject.name} has no GuestSO assigned!", this);
        }
    }

    /// <summary>
    /// Initialize this guest from a GuestSO (used when spawning guests at party start)
    /// </summary>
    public void InitializeFromSO(GuestSO guestSO)
    {
        if (guestSO == null)
        {
            Debug.LogError("Cannot initialize NPCGuest: GuestSO is null!", this);
            return;
        }

        guestData = guestSO;
        guestClass = guestSO.guestClass;
        
        // Initialize character stats
        InitializeCharacter();
    }
}
