using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GuestSO", menuName = "Scriptable Objects/GuestSO")]
public class GuestSO : ScriptableObject
{
    [Header("Guest Information")]
    public string guestName;
    public GuestClass guestClass;
    [TextArea(3, 5)]
    public string description;
    public GameObject prefab;
    
    [Header("Stats")]
    public float health;
    [Range(-100f, 100f)]
    public float popularity;
    [Range(-100f, 100f)]
    public float troubleMaker;
    [Range(-100f, 100f)]
    public float cashvalue;
    public bool isRecruitable;
    
    [Header("Abilities")]
    public List<Ability> abilities = new List<Ability>();

    [Header("Arrival Probability")]
    [Range(0f, 100f)]
    [Tooltip("Base chance (0-100%) for this guest to arrive at a party")]
    public float baseArrivalChance = 50f;
    
    [Range(-1f, 1f)]
    [Tooltip("How much party popularity affects arrival chance. Positive = higher popularity increases chance, Negative = lower popularity increases chance. Range: -1 to 1")]
    public float popularityModifier = 0f;

    /// <summary>
    /// Calculate the modified arrival chance based on party popularity.
    /// </summary>
    /// <param name="partyPopularity">Current party popularity (-100 to 100)</param>
    /// <returns>Modified arrival chance (0-100%)</returns>
    public float GetModifiedArrivalChance(float partyPopularity)
    {
        // Normalize party popularity to -1 to 1 range (from -100 to 100)
        float normalizedPopularity = partyPopularity / 100f;
        
        // Apply modifier: popularityModifier * normalizedPopularity gives the adjustment
        // Example: popularityModifier = 0.5, partyPopularity = 50
        // normalizedPopularity = 0.5, adjustment = 0.5 * 0.5 = 0.25 = 25% increase
        float adjustment = popularityModifier * normalizedPopularity * 100f;
        
        // Clamp final chance between 0 and 100
        float modifiedChance = Mathf.Clamp(baseArrivalChance + adjustment, 0f, 100f);
        
        return modifiedChance;
    }

    /// <summary>
    /// Creates stats instance.
    /// </summary>
    public BaseCharacterStats CreateStatsInstance()
    {
        var stats = new BaseCharacterStats
        {
            health = this.health,
            popularity = this.popularity,
            troubleMaker = this.troubleMaker,
            cashvalue = this.cashvalue,
            isRecruitable = this.isRecruitable,
            abilities = new List<Ability>(this.abilities)
        };
        return stats;
    }
}
