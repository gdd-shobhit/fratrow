using UnityEngine;

public enum GuestClass
{
    None,
    Nerd,
    MeanGirl,
    Jock
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

    protected override void InitializeCharacter()
    {
        base.InitializeCharacter();

        if (guestData != null)
        {
            // Create stats instance from ScriptableObject data
            stats = guestData.CreateStatsInstance();
        }
        else
        {
            Debug.LogWarning($"NPCGuest on {gameObject.name} has no GuestSO assigned!", this);
        }
    }
}
