using NUnit.Framework.Interfaces;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerClass
{
    President,
    SocialChair,
    Chad,
    RichGuy
}

public class UserCharacter : BaseCharacter
{
    [Header("Player Data")]
    [SerializeField] private PlayerClassSO userData;
    public PlayerClass currentClass;

    public PlayerClassSO UserData => userData;

    protected override void InitializeCharacter()
    {
        base.InitializeCharacter();

        if (userData != null)
        {
            // Create stats instance from ScriptableObject data
            stats = userData.CreateStatsInstance();
        }
        else
        {
            Debug.LogWarning($"UserCharacter on {gameObject.name} has no PlayerClassSO assigned!", this);
        }
    }

    /// <summary>
    /// Initialize this character from a PlayerClassSO (used when spawning players)
    /// </summary>
    public void InitializeFromSO(PlayerClassSO playerClassSO)
    {
        if (playerClassSO == null)
        {
            Debug.LogError("Cannot initialize UserCharacter: PlayerClassSO is null!", this);
            return;
        }

        userData = playerClassSO;
        currentClass = playerClassSO.playerClass;
        
        // Initialize character stats
        InitializeCharacter();
    }
}
