using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Marker component that indicates a guest has just entered the party and their ability should be applied.
    /// Added when a guest spawns, removed after ability is processed.
    /// </summary>
    public struct GuestEnteredPartyEvent : IComponentData
    {
    }

    /// <summary>
    /// ECS System that handles applying guest type abilities when guests enter the party.
    /// Runs on the server to apply effects when guests spawn.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct GuestAbilitySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GuestEnteredPartyEvent>();
        }

        [BurstDiscard]
        public void OnUpdate(ref SystemState state)
        {
            if (GuestManager.Instance == null || GuestManager.Instance.GuestTypeRegistry == null)
            {
                return; // Registry not available yet
            }

            var registry = GuestManager.Instance.GuestTypeRegistry;
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // Process guests that have just entered the party (have the event marker)
            foreach (var (guestTypeID, entity) in SystemAPI.Query<RefRO<GuestTypeID>>()
                .WithAll<NPCGuestTag, GuestEnteredPartyEvent>()
                .WithEntityAccess())
            {
                var guestType = registry.GetGuestTypeByID(guestTypeID.ValueRO.TypeID);
                if (guestType != null)
                {
                    ApplyGuestAbility(ref state, entity, guestType, ecb);
                    // Remove the event marker after processing
                    ecb.RemoveComponent<GuestEnteredPartyEvent>(entity);
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[GuestAbilitySystem] Could not find GuestType with ID {guestTypeID.ValueRO.TypeID}!");
                    ecb.RemoveComponent<GuestEnteredPartyEvent>(entity);
                }
            }
        }

        [BurstDiscard]
        private void ApplyGuestAbility(ref SystemState state, Entity guestEntity, GuestType guestType, EntityCommandBuffer ecb)
        {
            // Apply all abilities for this guest type
            if (guestType.Abilities == null || guestType.Abilities.Count == 0)
            {
                UnityEngine.Debug.LogWarning($"[GuestAbilitySystem] Guest type '{guestType.TypeName}' has no abilities defined!");
                return;
            }

            UnityEngine.Debug.Log($"[GuestAbilitySystem] Guest '{guestType.TypeName}' entered party! Applying {guestType.Abilities.Count} ability(ies)");

            foreach (var ability in guestType.Abilities)
            {
                if (ability == null)
                {
                    UnityEngine.Debug.LogWarning($"[GuestAbilitySystem] Guest type '{guestType.TypeName}' has a null ability in its list!");
                    continue;
                }

                ApplyIndividualAbility(ref state, guestEntity, guestType, ability, ecb);
            }
        }

        [BurstDiscard]
        private void ApplyIndividualAbility(ref SystemState state, Entity guestEntity, GuestType guestType, GuestAbility ability, EntityCommandBuffer ecb)
        {
            // TODO: Implement ability application logic here
            // This is where you would apply the passive ability effect based on the ability
            // Examples:
            // - Modify party stats (popularity, cash generation, etc.)
            // - Apply status effects
            // - Trigger events
            // - Update game state
            
            UnityEngine.Debug.Log($"[GuestAbilitySystem] Applying ability '{ability.AbilityName}' (ID: {ability.AbilityID}) - {ability.AbilityDescription}");

            // For now, this is a placeholder that logs the ability application
            // You can extend this to:
            // 1. Create a buffer/singleton of active guest abilities
            // 2. Apply effects based on AbilityID or TypeID (switch statement or data-driven approach)
            // 3. Store ability state for cleanup when guest leaves
            // 4. Modify party-wide stats or systems
            
            // Example structure for future implementation:
            // switch (ability.AbilityID)
            // {
            //     case 1: // Popularity Boost ability
            //         // Increase party popularity
            //         // Modify a party stats singleton or component
            //         break;
            //     case 2: // Cash Generation ability
            //         // Increase cash generation rate
            //         break;
            //     // etc.
            // }
            
            // Or use TypeID + AbilityID combination:
            // var combinedID = (guestType.TypeID << 16) | ability.AbilityID;
            // switch (combinedID)
            // {
            //     // Handle specific ability combinations
            // }
        }
    }
}
