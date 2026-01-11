using UnityEngine;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// ScriptableObject containing configuration for NPC guest spawning behavior.
    /// </summary>
    [CreateAssetMenu(fileName = "NPCGuestSpawnSettings", menuName = "Party Game/NPC Guest Spawn Settings", order = 4)]
    public class NPCGuestSpawnSettings : ScriptableObject
    {
        [Header("Time-Based Spawning")]
        [Tooltip("Enable automatic time-based NPC spawning")]
        public bool EnableTimeBasedSpawning = true;

        [Tooltip("Minimum time between spawns (seconds)")]
        public float SpawnIntervalMin = 5f;

        [Tooltip("Maximum time between spawns (seconds)")]
        public float SpawnIntervalMax = 10f;

        [Header("Event-Based Spawning")]
        [Tooltip("Enable event-based NPC spawning (triggered by game events)")]
        public bool EnableEventBasedSpawning = true;

        [Header("Spawn Limits")]
        [Tooltip("Maximum number of NPC guests that can be alive at once")]
        public int MaxGuestsAlive = 20;

        [Header("Advanced")]
        [Tooltip("Minimum distance from players when spawning (0 = use spawn point system)")]
        public float MinSpawnDistanceFromPlayers = 0f;

        [Tooltip("Use separate spawn points for guests (false = reuse player spawn points)")]
        public bool UseGuestSpecificSpawnPoints = false;
    }
}
