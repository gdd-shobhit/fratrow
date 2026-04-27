using System;
using System.Collections.Generic;
using UnityEngine;

public enum StatType
{
    Popularity = 0,
    TroubleMaker = 1,
    CashValue = 2
}

[System.Serializable]
public class BaseCharacterStats
{
    public string name;
    public float health;
    public float popularity;
    public float troubleMaker;
    public float cashvalue;
    public bool isRecruitable;
    public List<Ability> abilities;

    public BaseCharacterStats()
    {
        abilities = new List<Ability>();
    }

    public void ModifyStat(StatType statType, float amount)
    {
        switch (statType)
        {
            case StatType.Popularity:
                popularity = Math.Clamp(popularity + amount, -100.0f, 100.0f);
                break;
            case StatType.TroubleMaker:
                troubleMaker = Math.Clamp(troubleMaker + amount, -100.0f, 100.0f);
                break;
            case StatType.CashValue:
                cashvalue = Math.Clamp(cashvalue + amount, -100.0f, 100.0f);
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

    // Properties for accessing stats
    public string Name => stats.name;
    public float Health => stats.health;
    public float Popularity => stats.popularity;
    public float TroubleMaker => stats.troubleMaker;
    public float CashValue => stats.cashvalue;
    public bool IsRecruitable => stats.isRecruitable;
    public List<Ability> Abilities => stats.abilities;

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
        stats.health = Mathf.Max(0, stats.health - damage);
        OnHealthChanged();
    }

    /// <summary>
    /// Heal the character
    /// </summary>
    public virtual void Heal(float amount)
    {
        stats.health += amount;
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
    /// Set the three main stats (e.g. when syncing from network).
    /// </summary>
    public virtual void SetCharacterStats(float popularity, float troubleMaker, float cashValue)
    {
        stats.popularity = popularity;
        stats.troubleMaker = troubleMaker;
        stats.cashvalue = cashValue;
    }

    /// <summary>
    /// Modify a stat by the given amount
    /// </summary>
    public virtual void ModifyStat(StatType statType, float amount)
    {
        stats.ModifyStat(statType, amount);
    }

    /// <summary>
    /// Add an ability to the character
    /// </summary>
    public virtual void AddAbility(Ability ability)
    {
        stats.AddAbility(ability);
    }

    /// <summary>
    /// Remove an ability from the character
    /// </summary>
    public virtual void RemoveAbility(Ability ability)
    {
        stats.RemoveAbility(ability);
    }
}
