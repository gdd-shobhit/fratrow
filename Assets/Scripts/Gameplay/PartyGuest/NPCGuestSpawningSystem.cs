using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Buffer element data for triggering NPC guest spawns via events.
    /// </summary>
    public struct SpawnNPCGuestEvent : IBufferElementData
    {
        /// <summary>
        /// Optional: Specific NPC ID to spawn. If 0, a random NPC will be selected.
        /// </summary>
        public uint SpecificNPCID;

        /// <summary>
        /// Optional: Specific spawn position. If default, uses spawn point system.
        /// </summary>
        public float3 SpawnPosition;

        /// <summary>
        /// Whether to use the specified spawn position (true) or find a spawn point (false).
        /// </summary>
        public bool UseSpecificPosition;
    }

    /// <summary>
    /// Component that stores spawn timer and settings for the NPC spawning system.
    /// </summary>
    public struct NPCGuestSpawner : IComponentData
    {
        public float NextSpawnTime;
        public float CurrentSpawnInterval;
    }

    /// <summary>
    /// ECS System that handles spawning NPC guests on the server.
    /// Supports both time-based and event-based spawning.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct NPCGuestSpawningSystem : ISystem
    {
        private static Collider[] s_OverlapColliders = new Collider[16];

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NPCGuestSpawner>();
            state.RequireForUpdate<FixedRandom>();
            state.RequireForUpdate<NetworkStreamDriver>();
        }

        [BurstDiscard]
        public void OnUpdate(ref SystemState state)
        {
            if (GuestManager.Instance == null || GuestManager.Instance.GuestRegistry == null)
            {
                return; // Registry not available yet
            }

            var registry = GuestManager.Instance.GuestRegistry;
            var spawnSettings = GetSpawnSettings();

            if (spawnSettings == null)
            {
                return; // No spawn settings configured
            }

            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            ref var spawner = ref SystemAPI.GetSingletonRW<NPCGuestSpawner>().ValueRW;
            ref var random = ref SystemAPI.GetSingletonRW<FixedRandom>().ValueRW;
            var deltaTime = SystemAPI.Time.DeltaTime;

            // Count current NPC guests alive
            int currentNPCsAlive = 0;
            foreach (var _ in SystemAPI.Query<RefRO<NPCGuestTag>>())
            {
                currentNPCsAlive++;
            }

            // Handle time-based spawning
            if (spawnSettings.EnableTimeBasedSpawning && currentNPCsAlive < spawnSettings.MaxGuestsAlive)
            {
                spawner.NextSpawnTime -= deltaTime;

                if (spawner.NextSpawnTime <= 0f)
                {
                    TrySpawnNPCGuest(ref state, ref random, ecb, registry, spawnSettings, 0, default, false);
                    
                    // Calculate next spawn interval
                    spawner.CurrentSpawnInterval = random.Random.NextFloat(
                        spawnSettings.SpawnIntervalMin,
                        spawnSettings.SpawnIntervalMax
                    );
                    spawner.NextSpawnTime = spawner.CurrentSpawnInterval;
                }
            }

            // Handle event-based spawning
            if (spawnSettings.EnableEventBasedSpawning)
            {
                var eventQuery = SystemAPI.QueryBuilder()
                    .WithAll<SpawnNPCGuestEvent>()
                    .Build();

                var eventEntities = eventQuery.ToEntityArray(Allocator.Temp);
                
                foreach (var eventEntity in eventEntities)
                {
                    var buffer = SystemAPI.GetBuffer<SpawnNPCGuestEvent>(eventEntity);
                    
                    foreach (var spawnEvent in buffer)
                    {
                        if (currentNPCsAlive < spawnSettings.MaxGuestsAlive)
                        {
                            TrySpawnNPCGuest(
                                ref state,
                                ref random,
                                ecb,
                                registry,
                                spawnSettings,
                                spawnEvent.SpecificNPCID,
                                spawnEvent.SpawnPosition,
                                spawnEvent.UseSpecificPosition
                            );
                            currentNPCsAlive++;
                        }
                    }

                    // Clear the buffer after processing
                    buffer.Clear();
                }

                eventEntities.Dispose();
            }
        }

        [BurstDiscard]
        private void TrySpawnNPCGuest(
            ref SystemState state,
            ref FixedRandom random,
            EntityCommandBuffer ecb,
            NPCGuestRegistry registry,
            NPCGuestSpawnSettings spawnSettings,
            uint specificNPCID,
            float3 specificPosition,
            bool useSpecificPosition
        )
        {
            NPCGuestData npcData = null;

            // Get NPC data
            if (specificNPCID > 0)
            {
                npcData = registry.GetNPCByID(specificNPCID - 1); // Convert to 0-based index
            }
            else
            {
                npcData = registry.GetRandomNPCByWeight(ref random.Random);
            }

            if (npcData == null)
            {
                Debug.LogWarning("[NPCGuestSpawningSystem] Failed to get NPC data for spawning!");
                return;
            }

            // Get spawn position
            float3 spawnPosition;
            quaternion spawnRotation;

            if (useSpecificPosition && specificPosition.x != 0f && specificPosition.z != 0f)
            {
                // Use provided position
                spawnPosition = specificPosition;
                spawnRotation = quaternion.identity;
            }
            else
            {
                // Find spawn point
                if (!FindGuestSpawnPoint(ref state, out var spawnPoint))
                {
                    Debug.LogWarning("[NPCGuestSpawningSystem] No available spawn point found!");
                    return;
                }

                spawnPosition = spawnPoint.Position;
                spawnRotation = spawnPoint.Rotation;
            }

            // Get NPC prefab reference
            if (npcData.GuestGhostPrefab == null || !npcData.GuestGhostPrefab.GhostGuid.IsValid)
            {
                Debug.LogWarning($"[NPCGuestSpawningSystem] NPC '{npcData.CharacterName}' has no valid ghost prefab assigned!");
                return;
            }

            // Spawn the NPC ghost
            var netGuid = GhostGameObject.GenerateRandomHash();
            var ghostPrefabGuid = npcData.GuestGhostPrefab.GhostGuid;

            // Find the entity prefab
            var prefabSystem = GhostEntityPrefabSystem.ServerInstance;
            if (prefabSystem == null || !prefabSystem.PrefabsLoaded)
            {
                Debug.LogWarning("[NPCGuestSpawningSystem] Ghost prefab system not ready!");
                return;
            }

            var entityPrefab = prefabSystem.GetEntityPrefab(ghostPrefabGuid);
            if (entityPrefab == Entity.Null)
            {
                Debug.LogWarning($"[NPCGuestSpawningSystem] Could not find entity prefab for NPC '{npcData.CharacterName}' with GUID {ghostPrefabGuid}!");
                return;
            }

            // Instantiate the entity
            var npcEntity = ecb.Instantiate(entityPrefab);

            // Set transform
            ecb.SetComponent(npcEntity, new LocalTransform
            {
                Position = spawnPosition,
                Rotation = spawnRotation,
                Scale = 1f
            });

            // Set ghost GUID
            ecb.SetComponent(npcEntity, new GhostGameObjectGuid { Guid = netGuid });

            // Set character stats
            var stats = new CharacterStats
            {
                CharacterName = npcData.CharacterName,
                Popularity = npcData.Popularity,
                TroubleMakerRating = npcData.TroubleMakerRating,
                CashValue = npcData.CashValue
            };
            ecb.SetComponent(npcEntity, stats);

            // Find NPC ID in registry
            uint npcID = 0;
            for (uint i = 0; i < registry.NPCGuests.Count; i++)
            {
                if (registry.NPCGuests[(int)i] == npcData)
                {
                    npcID = i + 1; // 1-based ID for easier reference
                    break;
                }
            }

            // Set NPC ID
            ecb.SetComponent(npcEntity, new NPCGuestDataID { NPCID = npcID });

            // Ensure NPC tag is present
            ecb.AddComponent<NPCGuestTag>(npcEntity);

            Debug.Log($"[NPCGuestSpawningSystem] Spawned NPC guest: {npcData.CharacterName} at {spawnPosition}");
        }

        [BurstDiscard]
        private bool FindGuestSpawnPoint(ref SystemState state, out LocalToWorld spawnPoint)
        {
            var spawnPointsQuery = SystemAPI.QueryBuilder().WithAll<SpawnPoint, LocalToWorld>().Build();
            var spawnPoints = spawnPointsQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
            ref FixedRandom random = ref SystemAPI.GetSingletonRW<FixedRandom>().ValueRW;

            if (spawnPoints.Length == 0)
            {
                spawnPoint = default;
                spawnPoints.Dispose();
                return false;
            }

            // Pick a random spawn point (guests don't need collision checking like players)
            int randomIndex = random.Random.NextInt(0, spawnPoints.Length);
            spawnPoint = spawnPoints[randomIndex];
            spawnPoints.Dispose();

            return true;
        }

        [BurstDiscard]
        private NPCGuestSpawnSettings GetSpawnSettings()
        {
            if (GuestManager.Instance != null && GuestManager.Instance.SpawnSettings != null)
            {
                return GuestManager.Instance.SpawnSettings;
            }

            // Fallback: return default settings if not configured
            var defaultSettings = ScriptableObject.CreateInstance<NPCGuestSpawnSettings>();
            Debug.LogWarning("[NPCGuestSpawningSystem] SpawnSettings not configured in GuestManager, using defaults!");
            return defaultSettings;
        }
    }

    /// <summary>
    /// System to initialize the NPC spawner component on the server.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [BurstCompile]
    public partial struct NPCGuestSpawnerInitSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FixedRandom>();
        }

        [BurstDiscard]
        public void OnUpdate(ref SystemState state)
        {
            // Check if spawner already exists
            if (SystemAPI.HasSingleton<NPCGuestSpawner>())
            {
                return;
            }

            // Create spawner entity
            var spawnerEntity = state.EntityManager.CreateEntity();
            ref var random = ref SystemAPI.GetSingletonRW<FixedRandom>().ValueRW;

            var spawnSettings = GetDefaultSpawnSettings();
            float initialInterval = random.Random.NextFloat(
                spawnSettings.SpawnIntervalMin,
                spawnSettings.SpawnIntervalMax
            );

            state.EntityManager.AddComponentData(spawnerEntity, new NPCGuestSpawner
            {
                NextSpawnTime = initialInterval,
                CurrentSpawnInterval = initialInterval
            });

            Debug.Log("[NPCGuestSpawningSystem] Initialized NPC spawner");
        }

        [BurstDiscard]
        private NPCGuestSpawnSettings GetDefaultSpawnSettings()
        {
            if (GuestManager.Instance != null && GuestManager.Instance.SpawnSettings != null)
            {
                return GuestManager.Instance.SpawnSettings;
            }

            // Return default settings if not configured
            return ScriptableObject.CreateInstance<NPCGuestSpawnSettings>();
        }
    }
}
