using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Registry ScriptableObject that stores all NPC guest data assets.
    /// Provides methods to retrieve NPCs by ID or by weighted random selection.
    /// </summary>
    [CreateAssetMenu(fileName = "NPCGuestRegistry", menuName = "Party Game/NPC Guest Registry", order = 3)]
    public class NPCGuestRegistry : ScriptableObject
    {
        [Header("NPC Guest List")]
        [Tooltip("List of all NPC guest data assets that can spawn")]
        public List<NPCGuestData> NPCGuests = new List<NPCGuestData>();

        /// <summary>
        /// Gets an NPC guest data by index/ID.
        /// </summary>
        /// <param name="id">The index in the NPCGuests list</param>
        /// <returns>The NPCGuestData at the given index, or null if invalid</returns>
        public NPCGuestData GetNPCByID(uint id)
        {
            if (id < NPCGuests.Count && NPCGuests[(int)id] != null)
            {
                return NPCGuests[(int)id];
            }

            return null;
        }

        /// <summary>
        /// Gets the total weight of all NPC spawn rates.
        /// Used for normalizing weighted random selection.
        /// </summary>
        /// <returns>Sum of all SpawnRate values in NPCGuests list</returns>
        public float GetTotalWeight()
        {
            float totalWeight = 0f;
            foreach (var npc in NPCGuests)
            {
                if (npc != null)
                {
                    totalWeight += npc.SpawnRate;
                }
            }
            return totalWeight;
        }

        /// <summary>
        /// Gets a random NPC guest using weighted random selection based on SpawnRate values.
        /// Uses Unity.Mathematics.Random for consistent results.
        /// </summary>
        /// <param name="random">The random number generator to use</param>
        /// <returns>A randomly selected NPCGuestData based on spawn rate weights, or null if list is empty</returns>
        public NPCGuestData GetRandomNPCByWeight(ref Random random)
        {
            if (NPCGuests == null || NPCGuests.Count == 0)
            {
                Debug.LogWarning("NPCGuestRegistry: No NPC guests in registry!");
                return null;
            }

            float totalWeight = GetTotalWeight();

            if (totalWeight <= 0f)
            {
                Debug.LogWarning("NPCGuestRegistry: Total spawn weight is 0 or negative! Using uniform random instead.");
                // Fallback to uniform random if weights are invalid
                int randomIndex = random.NextInt(0, NPCGuests.Count);
                return NPCGuests[randomIndex];
            }

            // Generate random value between 0 and totalWeight
            float randomValue = random.NextFloat(0f, totalWeight);

            // Find which NPC this value corresponds to
            float accumulatedWeight = 0f;
            foreach (var npc in NPCGuests)
            {
                if (npc == null) continue;

                accumulatedWeight += npc.SpawnRate;
                if (randomValue <= accumulatedWeight)
                {
                    return npc;
                }
            }

            // Fallback to last valid NPC (shouldn't reach here, but safety check)
            for (int i = NPCGuests.Count - 1; i >= 0; i--)
            {
                if (NPCGuests[i] != null)
                {
                    return NPCGuests[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a random NPC guest using weighted random selection.
        /// Creates a temporary random generator (not recommended for repeated calls).
        /// </summary>
        /// <returns>A randomly selected NPCGuestData</returns>
        public NPCGuestData GetRandomNPCByWeight()
        {
            var random = Random.CreateFromIndex((uint)System.DateTime.Now.Millisecond);
            return GetRandomNPCByWeight(ref random);
        }

        /// <summary>
        /// Validates the registry and logs warnings for issues.
        /// </summary>
        public void ValidateRegistry()
        {
            if (NPCGuests == null || NPCGuests.Count == 0)
            {
                Debug.LogWarning("NPCGuestRegistry: Registry is empty!");
                return;
            }

            int nullCount = 0;
            foreach (var npc in NPCGuests)
            {
                if (npc == null)
                {
                    nullCount++;
                }
            }

            if (nullCount > 0)
            {
                Debug.LogWarning($"NPCGuestRegistry: Found {nullCount} null entries in NPCGuests list!");
            }

            float totalWeight = GetTotalWeight();
            if (totalWeight <= 0f)
            {
                Debug.LogWarning("NPCGuestRegistry: Total spawn weight is 0 or negative! NPCs won't spawn properly.");
            }
        }

        private void OnValidate()
        {
            ValidateRegistry();
        }
    }
}
