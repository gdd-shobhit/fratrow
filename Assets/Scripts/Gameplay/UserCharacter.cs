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
            Debug.LogWarning($"NPCGuest on {gameObject.name} has no PlayerClassSO assigned!", this);
        }
    }
}
