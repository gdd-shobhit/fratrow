using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerClassSO", menuName = "Scriptable Objects/PlayerClassSO")]
public class PlayerClassSO : ScriptableObject
{
    [Header("Player Class Information")]
    public PlayerClass playerClass;
    [TextArea(3, 5)]
    public string description;
    
    [Header("Stats")]
    public float health;
    [Range(0f, 100f)]
    public float popularity;
    [Range(-100f, 100f)]
    public float troubleMaker;
    [Range(0f, 100f)]
    public float cashvalue;
    
    [Header("Abilities")]
    public List<Ability> abilities = new List<Ability>();

    /// <summary>
    /// Creates stats instance.
    /// </summary>
    public BaseCharacterStats CreateStatsInstance()
    {
        var stats = new BaseCharacterStats
        {
            name = "defaultName",
            health = this.health,
            popularity = this.popularity,
            troubleMaker = this.troubleMaker,
            cashvalue = this.cashvalue,
            abilities = new List<Ability>(this.abilities)
        };
        return stats;
    }
}
