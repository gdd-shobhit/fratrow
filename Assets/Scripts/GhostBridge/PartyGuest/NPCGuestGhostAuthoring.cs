using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using Unity.FPSSample_2;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Authoring component for NPC Guest Ghost prefabs.
    /// References an NPCGuestData ScriptableObject and bakes the stats into ECS components.
    /// </summary>
    [DisallowMultipleComponent]
    public class NPCGuestGhostAuthoring : MonoBehaviour
    {
        [Header("NPC Guest Data")]
        [Tooltip("Reference to the NPC Guest Data ScriptableObject containing this NPC's stats")]
        public NPCGuestData GuestData;

        [Header("NPC ID")]
        [Tooltip("The ID/index of this NPC type in the registry. Set automatically if GuestData is assigned, or manually if needed.")]
        public uint NPCID = 0;
    }

    /// <summary>
    /// Baker that converts NPCGuestGhostAuthoring to ECS components.
    /// </summary>
    public class NPCGuestGhostBaker : Baker<NPCGuestGhostAuthoring>
    {
        public override void Bake(NPCGuestGhostAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Add NPC marker tag
            AddComponent(entity, new NPCGuestTag());

            // Add NPC ID component
            AddComponent(entity, new NPCGuestDataID
            {
                NPCID = authoring.NPCID
            });

            // Add guest type ID if GuestData and GuestType are assigned
            if (authoring.GuestData != null && authoring.GuestData.GuestType != null)
            {
                AddComponent(entity, new GuestTypeID
                {
                    TypeID = authoring.GuestData.GuestType.TypeID
                });
            }

            // If GuestData is assigned, bake the stats
            if (authoring.GuestData != null)
            {
                var stats = new CharacterStats
                {
                    CharacterName = authoring.GuestData.CharacterName,
                    Popularity = authoring.GuestData.Popularity,
                    TroubleMakerRating = authoring.GuestData.TroubleMakerRating,
                    CashValue = authoring.GuestData.CashValue
                };

                AddComponent(entity, stats);
            }
            else
            {
                // Add default stats if no data assigned (will be set at spawn time)
                AddComponent(entity, new CharacterStats
                {
                    CharacterName = "Unknown Guest",
                    Popularity = 50f,
                    TroubleMakerRating = 50f,
                    CashValue = 50f
                });

                Debug.LogWarning($"NPCGuestGhostAuthoring on {authoring.gameObject.name}: No GuestData assigned! Using default stats.");
            }
        }
    }
}
