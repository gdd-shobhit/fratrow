using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// ECS component data containing character stats that are synchronized across the network.
    /// Applies to both players and NPC guests.
    /// </summary>
    public struct CharacterStats : IComponentData
    {
        [GhostField]
        public FixedString64Bytes CharacterName;

        [GhostField]
        public float Popularity;

        [GhostField]
        public float TroubleMakerRating;

        [GhostField]
        public float CashValue;
    }

    /// <summary>
    /// Marker component that identifies an entity as an NPC guest (as opposed to a player).
    /// </summary>
    public struct NPCGuestTag : IComponentData
    {
    }

    /// <summary>
    /// Stores the ID/index of the NPCGuestData ScriptableObject that was used to spawn this NPC.
    /// Used for reference back to the original data.
    /// </summary>
    public struct NPCGuestDataID : IComponentData
    {
        [GhostField]
        public uint NPCID;
    }
}
