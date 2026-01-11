using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using Unity.FPSSample_2;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Authoring component for player prefabs to assign default stats.
    /// References a PlayerStatsData ScriptableObject and bakes it into ECS CharacterStats component.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerStatsAuthoring : MonoBehaviour
    {
        [Header("Player Stats Data")]
        [Tooltip("Reference to the Player Stats Data ScriptableObject. If null, default stats will be used.")]
        public PlayerStatsData PlayerStatsData;

        [Header("Override Settings")]
        [Tooltip("If true, stats will be set at spawn time instead of from this component")]
        public bool SetStatsAtSpawnTime = false;
    }

    /// <summary>
    /// Baker that converts PlayerStatsAuthoring to ECS components.
    /// Note: If SetStatsAtSpawnTime is true, stats will be set when the player spawns instead.
    /// </summary>
    public class PlayerStatsBaker : Baker<PlayerStatsAuthoring>
    {
        public override void Bake(PlayerStatsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Only bake stats if not set to be set at spawn time
            if (!authoring.SetStatsAtSpawnTime)
            {
                CharacterStats stats;

                if (authoring.PlayerStatsData != null)
                {
                    stats = new CharacterStats
                    {
                        CharacterName = authoring.PlayerStatsData.CharacterName,
                        Popularity = authoring.PlayerStatsData.Popularity,
                        TroubleMakerRating = authoring.PlayerStatsData.TroubleMakerRating,
                        CashValue = authoring.PlayerStatsData.CashValue
                    };
                }
                else
                {
                    // Default stats
                    stats = new CharacterStats
                    {
                        CharacterName = "Player",
                        Popularity = 50f,
                        TroubleMakerRating = 50f,
                        CashValue = 50f
                    };
                }

                AddComponent(entity, stats);
            }
            else
            {
                // Add a marker to indicate stats should be set at spawn time
                // This will be handled by ServerGameSystem when spawning players
                AddComponent(entity, new CharacterStats
                {
                    CharacterName = "",
                    Popularity = 0f,
                    TroubleMakerRating = 0f,
                    CashValue = 0f
                });
            }
        }
    }
}
