using UnityEngine;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// ScriptableObject containing stats data for NPC guest characters.
    /// Inherits base stats from BaseCharacterStats and adds spawn rate for probability weighting.
    /// </summary>
    [CreateAssetMenu(fileName = "NewNPCGuestData", menuName = "Party Game/NPC Guest Data", order = 2)]
    public class NPCGuestData : BaseCharacterStats
    {
        [Header("Spawning")]
        [Tooltip("Spawn rate weight for this NPC (0.0 to 1.0). Higher values = more likely to spawn. Used as probability weight.")]
        [Range(0f, 1f)]
        public float SpawnRate = 0.5f;

        [Header("NPC Prefab")]
        [Tooltip("Optional: Reference to the ghost prefab for this NPC type")]
        public GhostSpawner.GhostReference GuestGhostPrefab;

        [Header("NPC Specific")]
        [Tooltip("Optional: Description or notes about this NPC guest")]
        [TextArea(2, 5)]
        public string Description = "";

        [Header("Guest Type")]
        [Tooltip("The type/class of this guest, which determines their passive ability")]
        public GuestType GuestType;

        public override void OnValidate()
        {
            base.OnValidate();
            SpawnRate = Mathf.Clamp(SpawnRate, 0f, 1f);
        }

        public override string GetStatsSummary()
        {
            return $"{base.GetStatsSummary()}, SpawnRate={SpawnRate:F2}";
        }
    }
}
