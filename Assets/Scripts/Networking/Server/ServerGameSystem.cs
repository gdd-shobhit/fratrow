using UnityEngine;
using System;
using Gameplay.Leaderboard;
using Unity.Burst;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics;
using Random = Unity.Mathematics.Random;
using Unity.Transforms;
using Collider = UnityEngine.Collider;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Processes client join requests and spawns a character for each client.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    //[UpdateInGroup(typeof(SimulationSystemGroup))]  //Default, no explicit declaration is needed;
    [BurstCompile]
    public partial struct ServerGameSystem : ISystem
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            _overlapColliders = new Collider[16];
        }
        
        private static Collider[] _overlapColliders = new Collider[16];
        private ComponentLookup<JoinedClient> _joinedClientLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerEntityPrefabs>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<NetworkStreamDriver>();
            state.RequireForUpdate<ClientsMap>();

            Entity randomEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(randomEntity, new FixedRandom
            {
                Random = Random.CreateFromIndex((uint)DateTime.Now.Millisecond),
            });

            var mapSingleton = state.EntityManager.CreateSingletonBuffer<ClientsMap>();
            state.EntityManager.GetBuffer<ClientsMap>(mapSingleton).Add(default); //The server NetworkId is 0
            _joinedClientLookup = state.GetComponentLookup<JoinedClient>();
        }

        [BurstDiscard]
        public void OnUpdate(ref SystemState state)
        {
            _joinedClientLookup.Update(ref state);

            var ecb = SystemAPI.GetSingletonRW<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .ValueRW.CreateCommandBuffer(state.WorldUnmanaged);

            var clientsMap = SystemAPI.GetSingletonBuffer<ClientsMap>();
            var gameplayMapsEntity = SystemAPI.GetSingletonEntity<ClientsMap>();
            var connectionEventsForTick = SystemAPI.GetSingleton<NetworkStreamDriver>().ConnectionEventsForTick;
            RefreshClientsMap(ref state, ecb, clientsMap, connectionEventsForTick);

            if (!SystemAPI.TryGetSingleton(out PlayerEntityPrefabs playerEntityPrefabs))
            {
                return;
            }

            if (SystemAPI.HasSingleton<DisableCharacterDynamicContacts>())
            {
                state.EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<DisableCharacterDynamicContacts>());
            }

            HandleJoinRequests(ref state, gameplayMapsEntity, playerEntityPrefabs, ecb);
            HandlePlayerDeathAndRespawn(ref state, ecb);
        }

        void RefreshClientsMap(ref SystemState state, EntityCommandBuffer ecb,
            DynamicBuffer<ClientsMap> clientsMap,
            NativeArray<NetCodeConnectionEvent>.ReadOnly connectionEventsForTick)
        {
            //Maintain the clients map size
            foreach (var evt in connectionEventsForTick)
            {
                if (evt.State == ConnectionState.State.Connected)
                {
                    var lengthNeeded = evt.Id.Value + 1;
                    if (clientsMap.Length < lengthNeeded)
                    {
                        clientsMap.Resize(lengthNeeded, NativeArrayOptions.ClearMemory);
                    }

                    clientsMap.ElementAt(evt.Id.Value).ConnectionEntity = evt.ConnectionEntity;
                }

                if (evt.State == ConnectionState.State.Disconnected)
                {
                    var networkId = evt.Id.Value;
                    Debug.Log($"[Server] Client with NetworkId {networkId} has disconnected.");

                    // Find and destroy the player character entity by querying for its GhostOwner.
                    foreach (var (ghostOwner, entity) in SystemAPI.Query<RefRO<GhostOwner>>().WithEntityAccess())
                    {
                        if (ghostOwner.ValueRO.NetworkId == networkId)
                        {
                            Debug.Log($"[Server] Found and destroying PlayerEntity {entity} for disconnected client {networkId}.");
                            ecb.DestroyEntity(entity);
                            break;
                        }
                    }

                    // Find and destroy the clientInputEntity by querying for its PlayerCommandTarget.
                    foreach (var (commandTarget, entity) in SystemAPI.Query<RefRO<PlayerCommandTarget>>().WithEntityAccess())
                    {
                        if (commandTarget.ValueRO.NetworkId == networkId)
                        {
                            Debug.Log($"[Server] Found and destroying ClientInputEntity {entity} for disconnected client {networkId}.");
                            ecb.DestroyEntity(entity);
                            break;
                        }
                    }

                    RemovePlayerFromLeaderboard(networkId);
                    clientsMap.ElementAt(networkId) = default;
                }
            }

            // Entities created via ECB have temporary Entity IDs. Need to refresh this index lookup. So patch them.
            for (var i = clientsMap.Length - 1; i >= 0; --i)
            {
                ref var map = ref clientsMap.ElementAt(i);
                if (map.OwnerNetworkId.Value == default)
                {
                    break;
                }

                ref var dest = ref clientsMap.ElementAt(map.OwnerNetworkId.Value);
                Patch(map.PlayerEntity, ref dest.PlayerEntity);
                Patch(map.CharacterControllerEntity, ref dest.CharacterControllerEntity);
                map = default;

                static void Patch(Entity possibleRemapValue, ref Entity destination)
                {
                    if (possibleRemapValue != Entity.Null)
                    {
                        destination = possibleRemapValue;
                    }
                }

                ;
            }
        }

        Entity GetChildWithComponent<T>(EntityManager em, Entity parentEntity)
            where T : unmanaged, IComponentData
        {
            if (!em.HasComponent<Child>(parentEntity))
            {
                return Entity.Null;
            }

            var children = em.GetBuffer<Child>(parentEntity);
            foreach (var child in children)
            {
                if (em.HasComponent<T>(child.Value))
                    return child.Value;
            }

            return Entity.Null;
        }
        
        [BurstDiscard]
        private void AddDeathToLeaderboard(int networkId)
        {
            LeaderboardManager.Instance.AddDeath(networkId);
        }
        
        // Add near the top of the struct/class:
        [BurstDiscard]
        private void AddPlayerToLeaderboard(int networkId, FixedString64Bytes playerName)
        {
            LeaderboardManager.AddPlayer(networkId, playerName);
        }
        
        [BurstDiscard]
        private void RemovePlayerFromLeaderboard(int networkId)
        {
            LeaderboardManager.Instance.RemovePlayer(networkId);
        }

        private void SpawnPlayerCharacter(ref SystemState state, EntityCommandBuffer ecb, Entity connectionEntity, FixedString64Bytes playerName, int characterIndex)
        {
            var playerEntityPrefabs = SystemAPI.GetSingleton<PlayerEntityPrefabs>();
            var ownerNetworkId = SystemAPI.GetComponent<NetworkId>(connectionEntity);
            
            // Instantiate the client input entity
            var clientInputEntity = ecb.Instantiate(playerEntityPrefabs.ClientInputEntityPrefab);
            ecb.SetComponent(clientInputEntity, new GhostOwner { NetworkId = ownerNetworkId.Value });
            ecb.SetComponent(connectionEntity, new CommandTarget { targetEntity = clientInputEntity });
            ecb.AddBuffer<ClientCommandInput>(clientInputEntity);
            ecb.SetComponent(clientInputEntity, new PlayerCommandTarget { NetworkId = ownerNetworkId.Value });

            // Instantiate the player entity
            var playerEntityPrefab = characterIndex == 0 ? playerEntityPrefabs.PlayerRifleEntityPrefab : playerEntityPrefabs.PlayerShotgunEntityPrefab;
            var playerEntity = ecb.Instantiate(playerEntityPrefab);

            var weaponId = characterIndex == 0 ? (uint)0 : 1;
            
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId);
            var magazineSize = weaponData != null ? weaponData.MagazineSize : 30; // Default to 30 if weapon not found

            ecb.SetComponent(playerEntity, new GhostOwner { NetworkId = ownerNetworkId.Value });
            ecb.AddComponent(playerEntity, new PlayerClientCommandInputLookup { ClientCommandInputEntity = clientInputEntity });
            ecb.SetComponent(playerEntity, new PredictedPlayerGhost
            {
                InputIndex = 0,
                MaxHealth = 100f,
                CurrentHealth = 100f,
                EquippedWeaponID = weaponId,
                CurrentAmmo = magazineSize
            });
            ecb.AddComponent(playerEntity, new PlayerCharacterInitialized());
            ecb.SetComponentEnabled<PlayerCharacterInitialized>(playerEntity, false);

            if (FindSpawnPoint(ref state, out var spawnPoint))
            {
                ecb.SetComponent(playerEntity, new LocalTransform { Position = spawnPoint.Position, Rotation = spawnPoint.Rotation, Scale = 1.0f });
            }

            ecb.SetComponent(playerEntity, new GhostGameObjectGuid { Guid = GhostGameObject.GenerateRandomHash() });
            ecb.SetComponent(playerEntity, new PlayerGhost.PlayerData { Name = playerName });

            // Update the clients map
            var clientsMap = SystemAPI.GetSingletonBuffer<ClientsMap>();
            clientsMap.ElementAt(ownerNetworkId.Value).PlayerEntity = playerEntity;

            if (!SystemAPI.HasComponent<JoinedClient>(connectionEntity))
            {
                // Update the connection's JoinedClient component with the new player entity
                ecb.AddComponent(connectionEntity, new JoinedClient
                {
                    PlayerEntity = playerEntity, PlayerName = playerName, CharacterIndex = characterIndex
                });
            }

            ecb.AppendToBuffer(connectionEntity, new LinkedEntityGroup { Value = playerEntity });
            ecb.AddComponent(connectionEntity, new NetworkStreamInGame());
        }

        void HandlePlayerDeathAndRespawn(ref SystemState state, EntityCommandBuffer ecb)
        {
            var clientsMap = SystemAPI.GetSingletonBuffer<ClientsMap>();

            // --- Part 1: Detect Death and Destroy Player Entity ---
            foreach (var (playerGhost, ghostOwner, entity) in
                     SystemAPI.Query<RefRO<PredictedPlayerGhost>, RefRO<GhostOwner>>().WithEntityAccess())
            {
                if (playerGhost.ValueRO.CurrentHealth <= 0)
                {
                    var networkId = ghostOwner.ValueRO.NetworkId;
                    var connectionEntity = clientsMap[ghostOwner.ValueRO.NetworkId].ConnectionEntity;

                    // Add a respawn timer to the connection
                    if (!SystemAPI.HasComponent<PendingRespawn>(connectionEntity))
                    {
                        Debug.Log($"[Server] Player {entity} has died. Starting respawn timer for connection {connectionEntity}.");
                        ecb.AddComponent(connectionEntity, new PendingRespawn { RespawnTimer = 5f });
                    }
                    
                    if (SystemAPI.HasComponent<PlayerClientCommandInputLookup>(entity))
                    {
                        var inputLookup = SystemAPI.GetComponent<PlayerClientCommandInputLookup>(entity);
                        if (SystemAPI.Exists(inputLookup.ClientCommandInputEntity))
                        {
                            ecb.DestroyEntity(inputLookup.ClientCommandInputEntity);
                        }
                    }

                    // Destroy the player character entity
                    ecb.DestroyEntity(entity);
                }
            }

            // --- Part 2: Countdown Timers and Respawn Players ---
            foreach (var (pendingRespawn, connection, entity) in
                     SystemAPI.Query<RefRW<PendingRespawn>, RefRO<NetworkId>>().WithEntityAccess())
            {
                pendingRespawn.ValueRW.RespawnTimer -= SystemAPI.Time.DeltaTime;

                if (pendingRespawn.ValueRO.RespawnTimer <= 0f)
                {
                    Debug.Log($"[Server] Respawning player for connection {entity}.");

                    if (_joinedClientLookup.HasComponent(entity))
                    {
                        var joinedClientData = _joinedClientLookup[entity];

                        // Call the spawn function again
                        SpawnPlayerCharacter(ref state, ecb, entity, joinedClientData.PlayerName, joinedClientData.CharacterIndex);

                        // Remove the timer component
                        ecb.RemoveComponent<PendingRespawn>(entity);
                    }
                    else
                    {
                        // This would indicate a problem, but we handle it safely.
                        Debug.LogError($"Connection entity {entity} is pending respawn but has no JoinedClient data!");
                        ecb.RemoveComponent<PendingRespawn>(entity);
                    }
                }
            }
        }

        void HandleJoinRequests(ref SystemState state, Entity gameplayMapsEntity, PlayerEntityPrefabs playerEntityPrefabs, EntityCommandBuffer ecb)
        {
            foreach (var (request, rpcReceive, entity) in
                     SystemAPI.Query<RefRO<ClientJoinRequestRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<NetworkId>(rpcReceive.ValueRW.SourceConnection) &&
                    !SystemAPI.HasComponent<NetworkStreamInGame>(rpcReceive.ValueRW.SourceConnection))
                {
                    SpawnPlayerCharacter(ref state, ecb, rpcReceive.ValueRW.SourceConnection, request.ValueRO.PlayerName, request.ValueRO.CharacterIndex);
                    
                    var ownerNetworkId = SystemAPI.GetComponent<NetworkId>(rpcReceive.ValueRW.SourceConnection);
                    AddPlayerToLeaderboard(ownerNetworkId.Value, request.ValueRO.PlayerName);
                }

                ecb.DestroyEntity(entity);
            }
        }

        [BurstDiscard]
        private bool FindSpawnPoint(ref SystemState state, out LocalToWorld spawnPoint)
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

            // Shuffle the list to ensure that if multiple points have the same low number
            // of players, the choice among them is still random.
            for (int i = spawnPoints.Length - 1; i > 0; i--)
            {
                int k = random.Random.NextInt(0, i + 1);
                (spawnPoints[k], spawnPoints[i]) = (spawnPoints[i], spawnPoints[k]);
            }

            int bestSpawnPointIndex = 0;
            int minColliderCount = int.MaxValue;

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                int numColliders = UnityEngine.Physics.OverlapSphereNonAlloc(spawnPoints[i].Position, 2f,
                    _overlapColliders, LayerMask.GetMask("ServerPlayer"));

                if (numColliders == 0)
                {
                    bestSpawnPointIndex = i;
                    break;
                }

                if (numColliders < minColliderCount)
                {
                    minColliderCount = numColliders;
                    bestSpawnPointIndex = i;
                }
            }

            spawnPoint = spawnPoints[bestSpawnPointIndex];
            spawnPoints.Dispose();
            return true;
        }
    }
}