using System;
using Unity.Burst;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Random = Unity.Mathematics.Random;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// This system handles the client side of the player connection and character spawning.
    /// It creates the first player join request so the server knows it has to spawn a character.
    /// It handles the Spectator prefab spawn if the player is a spectator.
    /// It creates the NameTagProxy on any spawned character that is not the active player.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    [BurstCompile]
    public partial struct ClientGameSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerEntityPrefabs>();

            var randomSeed = (uint)DateTime.Now.Millisecond;
            var randomEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(randomEntity, new FixedRandom
            {
                Random = Random.CreateFromIndex(randomSeed),
            });
        }

        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<DisableCharacterDynamicContacts>())
            {
                state.EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<DisableCharacterDynamicContacts>());
            }

            HandleSendJoinRequest(ref state);
        }

        void HandleSendJoinRequest(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<NetworkId>(out var clientEntity)
                || SystemAPI.HasComponent<NetworkStreamInGame>(clientEntity))
            {
                return;
            }

            var joinRequestEntity = state.EntityManager.CreateEntity(ComponentType.ReadOnly<ClientJoinRequestRpc>(),
                ComponentType.ReadWrite<SendRpcCommandRequest>());
            var playerName = GameSettings.Instance.PlayerName;
            if (state.WorldUnmanaged.IsThinClient()) // Random names for thin clients.
            {
                ref var random = ref SystemAPI.GetSingletonRW<FixedRandom>().ValueRW;
                playerName = $"[Bot {random.Random.NextInt(1, 999):000}] {playerName}";
            }

            var clientJoinRequestRpc = new ClientJoinRequestRpc();
            clientJoinRequestRpc.PlayerName.CopyFromTruncated(playerName);
            clientJoinRequestRpc.CharacterIndex = GameSettings.Instance.PlayerCharacter;
            state.EntityManager.SetComponentData(joinRequestEntity, clientJoinRequestRpc);
            state.EntityManager.AddComponentData(clientEntity, new NetworkStreamInGame());
        }
    }
}
