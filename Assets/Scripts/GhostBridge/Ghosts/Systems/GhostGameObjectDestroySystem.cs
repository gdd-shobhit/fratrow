using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

// This has to happen before any updates to ensure that partially deleted ghosts
// are fully removed before they are to be updated by the GhostGameObjectUpdateSystem
// otherwise they'll try to access ghost data that has already been removed
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(GhostGameObjectUpdateServerSystem))]
public partial class ServerPreUpdateGhostGameObjectDestroySystem : BaseGhostGameObjectDestroySystem
{
}

// on the server it's important this happens AFTER any update to ensure that
// partially deleted ghosts are fully removed before the GhostSendSystem runs
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateAfter(typeof(GhostGameObjectUpdateServerSystem))]
[UpdateBefore(typeof(PredictedSimulationSystemGroup))]
public partial class ServerPostUpdateGameObjectDestroySystem : BaseGhostGameObjectDestroySystem
{
}

// on the client it's important this happens BEFORE any update to ensure that
// partially deleted ghosts are fully removed before they are to be updated by the GhostGameObjectUpdateSystem
// otherwise they'll try to access ghost data that has already been removed
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(GhostGameObjectUpdateClientSystem))]
public partial class ClientGhostGameObjectDestroySystem : BaseGhostGameObjectDestroySystem
{
    protected override void OnUpdate()
    {
        // it's possible we are trying to destroy items whilst transforms are still being updated
        // so we should force that job to complete first
        ClientGhostTransformApplySystem.Instance.ApplyTransformsJobHandle.Complete();

        base.OnUpdate();
    }
}

public abstract partial class BaseGhostGameObjectDestroySystem : SystemBase
{
    protected override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // set the ecb for the ghostgameobject code to use
        GhostGameObject.UpdateEntityCommandBuffer = ecb;

        var lifeTimeSystem = GhostGameObjectLifetimeSystem.Instance(World);
        
        var ghostGameObjectDeferredActivationQuery = 
            SystemAPI.QueryBuilder()
                .WithAll<GhostGameObjectDeferredActivation, GhostGameObjectLink>()
                .WithNone<GhostInstance>().Build();
        
        foreach (var entity in ghostGameObjectDeferredActivationQuery.ToEntityArray(Allocator.Temp))
        {
            var gameObjectLink = EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
            var obj = gameObjectLink.LinkedInstance;
            if (obj != null)
            {
                if (obj.TryGetComponent<GhostGameObject>(out var ghostGameObject))
                {
                    lifeTimeSystem.OnGhostGameObjectDestroyed(ghostGameObject.Guid);
                }

                Object.DestroyImmediate(obj.gameObject);
            }

            ecb.RemoveComponent<GhostGameObjectLink>(entity);
            ecb.DestroyEntity(entity);
        }

        var ghostGameObjectLinkQuery = SystemAPI.QueryBuilder().WithAll<GhostGameObjectLink>().WithNone<GhostInstance, GhostGameObjectDeferredActivation>().Build();
        foreach (var entity in ghostGameObjectLinkQuery.ToEntityArray(Allocator.Temp))
        {
            var gameObjectLink = EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
            var obj = gameObjectLink.LinkedInstance;
            if (obj != null)
            {
                if (obj.TryGetComponent<GhostGameObject>(out var ghostGameObject))
                {
                    lifeTimeSystem.OnGhostGameObjectDestroyed(ghostGameObject.Guid);
                    ghostGameObject.OnGhostPreDestroy();
                }
                Object.DestroyImmediate(obj.gameObject);
            }

            ecb.RemoveComponent<GhostGameObjectLink>(entity);
            ecb.DestroyEntity(entity);
        }

        ecb.Playback(EntityManager);

        GhostGameObject.UpdateEntityCommandBuffer = default;
    }
}
