using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[UpdateInGroup(typeof(GhostInputSystemGroup), OrderFirst = true)]
public partial class ClientInputInitSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<NetworkId>();
        RequireForUpdate<CommandTarget>();
    }

    protected override void OnUpdate()
    {
        var commandTargetEntity = SystemAPI.GetSingleton<CommandTarget>().targetEntity;
        var connectionId = SystemAPI.GetSingleton<NetworkId>().Value;

        // 1. Check if our current target entity has been destroyed (e.g., on player death)
        if (commandTargetEntity != Entity.Null && !EntityManager.Exists(commandTargetEntity))
        {
            // The entity we were sending input to is gone. Reset the CommandTarget to null
            // so we can search for a new one.
            SystemAPI.SetSingleton(new CommandTarget { targetEntity = Entity.Null });
            commandTargetEntity = Entity.Null;
        }

        // 2. If we don't have a valid target, search for one.
        if (commandTargetEntity == Entity.Null)
        {
            Entity localInputEntity = Entity.Null;

            // Find the entity designated to receive this client's input that hasn't been set up yet.
            foreach (var (playerCommandTarget, entity) in SystemAPI.Query<RefRO<PlayerCommandTarget>>()
                         .WithNone<ClientInput>().WithEntityAccess())
            {
                if (playerCommandTarget.ValueRO.NetworkId == connectionId &&
                    !EntityManager.HasComponent<ClientInput>(entity))
                {
                    localInputEntity = entity;
                    break;
                }
            }

            // 3. If we found a new entity, configure it for input.
            if (localInputEntity != Entity.Null)
            {
                Debug.Log(
                    $"[ClientInputInitSystem] New input target found. Linking client input for entity {localInputEntity.Index.ToString()}");

                EntityManager.AddComponent<ClientInput>(localInputEntity);
                EntityManager.AddComponent<ClientMovementInput>(localInputEntity);
                EntityManager.AddBuffer<ClientCommandInput>(localInputEntity);

                // Update the singleton to point to our new target.
                SystemAPI.SetSingleton(new CommandTarget { targetEntity = localInputEntity });
            }
        }
    }
}