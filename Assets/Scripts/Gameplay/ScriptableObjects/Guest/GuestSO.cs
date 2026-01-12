using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GuestSO", menuName = "Scriptable Objects/GuestSO")]
public class GuestSO : ScriptableObject
{
    [Header("Guest Information")]
    public string guestName;
    [TextArea(3, 5)]
    public string description;
    
    [Header("Stats")]
    public float health;
    [Range(0f, 100f)]
    public float popularity;
    [Range(0f, 100f)]
    public float troubleMaker;
    [Range(0f, 100f)]
    public float cashvalue;
    
    [Header("Abilities")]
    public List<Ability> abilities = new List<Ability>();

    /// <summary>
    /// Creates an IHasStats implementation from this ScriptableObject's data.
    /// This allows NPCGuest to use the data while maintaining the interface pattern.
    /// </summary>
    public IHasStats CreateStatsInstance()
    {
        var stats = new BaseCharacterStats
        {
            health = this.health,
            popularity = this.popularity,
            troubleMaker = this.troubleMaker,
            cashvalue = this.cashvalue,
            abilities = new List<Ability>(this.abilities)
        };
        return stats;
    }
}
