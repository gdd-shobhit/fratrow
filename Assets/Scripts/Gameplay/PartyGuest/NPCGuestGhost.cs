using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Ghost MonoBehaviour component for NPC guests.
    /// Handles visual representation and updates for networked NPC guests.
    /// Similar structure to PlayerGhost but simplified for NPCs.
    /// </summary>
    public partial class NPCGuestGhost : GhostMonoBehaviour, IUpdateServer, IUpdateClient
    {
        [Header("Visuals")]
        [SerializeField] private GameObject m_GuestVisuals;
        [SerializeField] private SoundDef m_SpawnSFX;

        [Header("UI")]
        [Tooltip("Optional: Transform where name tag/UI should be displayed")]
        [SerializeField] private Transform m_NameTagPoint;

        private CharacterStats m_CachedStats;

        public override void OnGhostLinked()
        {
            // Read initial stats
            if (GhostHasComponent<CharacterStats>())
            {
                m_CachedStats = ReadGhostComponentData<CharacterStats>();
            }

            // Spawn SFX
            if (m_SpawnSFX != null && Role != MultiplayerRole.Server)
            {
                GameManager.Instance?.SoundSystem?.CreateEmitter(m_SpawnSFX, transform.position);
            }

            // Set layer based on role
            if (Role == MultiplayerRole.Server)
            {
                gameObject.layer = (int)LayerIndex.ServerPlayer;
            }
            else
            {
                gameObject.layer = (int)LayerIndex.ClientPlayer;
            }

            // Enable visuals based on role
            if (m_GuestVisuals != null)
            {
                m_GuestVisuals.SetActive(Role != MultiplayerRole.Server);
            }
        }

        public override void OnGhostPreDestroy()
        {
            // Cleanup if needed
        }

        public void UpdateServer(float deltaTime)
        {
            // Server-side update logic for NPC guests
            // Could include AI behavior, state updates, etc.
        }

        public void UpdateClient(float deltaTime)
        {
            // Client-side visual updates
            // Update name tags, UI, etc.

            if (GhostHasComponent<CharacterStats>())
            {
                var stats = ReadGhostComponentData<CharacterStats>();
                m_CachedStats = stats;

                // Update name tag or UI if needed
                // This would typically be handled by a UI system
            }
        }

        /// <summary>
        /// Gets the current character stats for this NPC guest.
        /// </summary>
        public CharacterStats GetStats()
        {
            if (GhostHasComponent<CharacterStats>())
            {
                return ReadGhostComponentData<CharacterStats>();
            }
            return m_CachedStats;
        }

        /// <summary>
        /// Gets the NPC ID for this guest.
        /// </summary>
        public uint GetNPCID()
        {
            if (GhostHasComponent<NPCGuestDataID>())
            {
                return ReadGhostComponentData<NPCGuestDataID>().NPCID;
            }
            return 0;
        }
    }
}
