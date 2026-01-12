using System;
using System.Collections.Generic;
using System.Security.Policy;
using UnityEngine;

public enum StatType
{
    Popularity = 0,
    TroubleMaker = 1,
    CashValue = 2
}

/// <summary>
/// Interface for objects that have character stats.
/// Allows different implementations (e.g., BaseCharacterStats, NPCStats from ScriptableObject, etc.)
/// </summary>
public interface IHasStats
{
    float Health { get; set; }
    float Popularity { get; set; }
    float TroubleMaker { get; set; }
    float CashValue { get; set; }
    List<Ability> Abilities { get; }
    
    void ModifyStat(StatType statType, float amount);
    void AddAbility(Ability ability);
    void RemoveAbility(Ability ability);
}

[System.Serializable]
public class BaseCharacterStats : IHasStats
{
    public float health;
    public float popularity;
    public float troubleMaker;
    public float cashvalue;
    public List<Ability> abilities;

    // IHasStats implementation
    public float Health 
    { 
        get => health; 
        set => health = value; 
    }
    
    public float Popularity 
    { 
        get => popularity; 
        set => popularity = value; 
    }
    
    public float TroubleMaker 
    { 
        get => troubleMaker; 
        set => troubleMaker = value; 
    }
    
    public float CashValue 
    { 
        get => cashvalue; 
        set => cashvalue = value; 
    }
    
    public List<Ability> Abilities => abilities;

    public BaseCharacterStats()
    {
        abilities = new List<Ability>();
    }

    public void ModifyStat(StatType statType, float amount)
    {
        switch (statType)
        {
            case StatType.Popularity:
                popularity = Math.Clamp(popularity + amount, 0.0f, 100.0f);
                break;
            case StatType.TroubleMaker:
                troubleMaker = Math.Clamp(troubleMaker + amount, 0.0f, 100.0f);
                break;
            case StatType.CashValue:
                cashvalue = Math.Clamp(cashvalue + amount, 0.0f, 100.0f);
                break;
        }
    }

    public void AddAbility(Ability ability)
    {
        if (ability != null && !abilities.Contains(ability))
        {
            abilities.Add(ability);
        }
    }

    public void RemoveAbility(Ability ability)
    {
        if (ability != null)
        {
            abilities.Remove(ability);
        }
    }
}

public enum AbilityType
{
    Passive,
    Active
}

[System.Serializable]
public class Ability
{
    public string name;
    public string description;
    public float abilityValue;
    public StatType effectsOn;
    public AbilityType AbilityType;

    public Ability(string name, string description, float abilityValue, StatType effectsOn, AbilityType abilityType)
    {
        this.name = name;
        this.description = description;
        this.abilityValue = abilityValue;
        this.effectsOn = effectsOn;
        this.AbilityType = abilityType;
    }
}

/// <summary>
/// Base class for all characters in the game.
/// Can be extended by NPC, Player, or other character types.
/// </summary>
public abstract class BaseCharacter : MonoBehaviour
{
    [Header("Character Stats")]
    [SerializeField] protected BaseCharacterStats stats = new BaseCharacterStats();

    // Protected property to access stats - can be overridden in derived classes
    protected virtual IHasStats Stats => stats;

    // Properties for accessing stats through interface
    public float Health => Stats.Health;
    public float Popularity => Stats.Popularity;
    public float TroubleMaker => Stats.TroubleMaker;
    public float CashValue => Stats.CashValue;
    public List<Ability> Abilities => Stats.Abilities;

    protected virtual void Awake()
    {
        InitializeCharacter();
    }

    protected virtual void Start()
    {
        // Override in derived classes for additional initialization
    }

    /// <summary>
    /// Initialize the character. Override in derived classes to load from ScriptableObjects or other sources.
    /// </summary>
    protected virtual void InitializeCharacter()
    {
        // Base initialization - can be overridden by derived classes
    }

    /// <summary>
    /// Apply damage to the character
    /// </summary>
    public virtual void TakeDamage(float damage)
    {
        Stats.Health = Mathf.Max(0, Stats.Health - damage);
        OnHealthChanged();
    }

    /// <summary>
    /// Heal the character
    /// </summary>
    public virtual void Heal(float amount)
    {
        Stats.Health += amount;
        OnHealthChanged();
    }

    /// <summary>
    /// Called when health changes. Override to add custom behavior.
    /// </summary>
    protected virtual void OnHealthChanged()
    {
        // Override in derived classes if needed
    }

    /// <summary>
    /// Modify a stat by the given amount
    /// </summary>
    public virtual void ModifyStat(StatType statType, float amount)
    {
        Stats.ModifyStat(statType, amount);
    }

    /// <summary>
    /// Add an ability to the character
    /// </summary>
    public virtual void AddAbility(Ability ability)
    {
        Stats.AddAbility(ability);
    }

    /// <summary>
    /// Remove an ability from the character
    /// </summary>
    public virtual void RemoveAbility(Ability ability)
    {
        Stats.RemoveAbility(ability);
    }
}
