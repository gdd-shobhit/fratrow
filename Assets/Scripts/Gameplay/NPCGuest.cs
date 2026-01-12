using UnityEngine;

/// <summary>
/// NPC Guest character that loads its data from a GuestSO ScriptableObject.
/// </summary>
public class NPCGuest : BaseCharacter
{
    [Header("Guest Data")]
    [SerializeField] private GuestSO guestData;

    // Override Stats property to use data from ScriptableObject
    private IHasStats guestStats;
    protected override IHasStats Stats => guestStats ?? base.Stats;

    protected override void InitializeCharacter()
    {
        base.InitializeCharacter();

        if (guestData != null)
        {
            // Create stats instance from ScriptableObject data
            guestStats = guestData.CreateStatsInstance();
        }
        else
        {
            Debug.LogWarning($"NPCGuest on {gameObject.name} has no GuestSO assigned!", this);
        }
    }
}
