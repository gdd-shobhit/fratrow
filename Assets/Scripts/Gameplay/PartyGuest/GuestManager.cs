using UnityEngine;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Singleton MonoBehaviour that provides static access to the NPC Guest Registry.
    /// Similar to WeaponManager, this should be placed in the Persistents scene.
    /// </summary>
    public class GuestManager : MonoBehaviour
    {
        public static GuestManager Instance { get; private set; }

        [Header("Registry Reference")]
        [Tooltip("Reference to the NPC Guest Registry ScriptableObject")]
        public NPCGuestRegistry GuestRegistry;

        [Header("Spawn Settings")]
        [Tooltip("Reference to the NPC Guest Spawn Settings ScriptableObject")]
        public NPCGuestSpawnSettings SpawnSettings;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"Multiple instances of '{this}' violates the Singleton pattern!", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Validates that the registry is assigned and logs warnings if not.
        /// </summary>
        private void OnValidate()
        {
            if (GuestRegistry == null)
            {
                Debug.LogWarning($"GuestManager on {gameObject.name}: GuestRegistry is not assigned!");
            }
            else
            {
                GuestRegistry.ValidateRegistry();
            }
        }
    }
}
