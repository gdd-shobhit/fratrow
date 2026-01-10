using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace Unity.FPSSample_2
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial class ClientInterpolatedPlayerMovementSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            float deltaTime = World.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (predictedGhost, ghost, entity) in
                     SystemAPI.Query<RefRO<PredictedPlayerGhost>, RefRO<GhostInstance>>()
                         .WithNone<PlayerControllerLink, PredictedGhost>().WithAll<GhostGameObjectLink>()
                         .WithEntityAccess())
            {
                var gameObjectLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                if (gameObjectLink.LinkedInstance != null)
                {
                    // get controller
                    if (gameObjectLink.LinkedInstance.TryGetComponent<FirstPersonController>(out var controller))
                    {
                        ecb.AddComponent(entity, new PlayerControllerLink { Controller = controller });
                    }
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();

            foreach (var (predictedGhost, transform, controllerConsts, entity) in SystemAPI.Query<
                             RefRW<PredictedPlayerGhost>,
                             RefRO<LocalTransform>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithEntityAccess()
                         .WithNone<PredictedGhost>().WithAll<PlayerControllerLink>())
            {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                controllerLink.Controller.ApplyInterpolatedClientState(ref predictedGhost.ValueRW.ControllerState,
                    controllerConsts.ValueRO.ControllerConsts, transform.ValueRO, deltaTime, true);
                controllerLink.Controller.ApplyAnimatorState(predictedGhost.ValueRO.ControllerState,
                    controllerConsts.ValueRO.ControllerConsts, deltaTime);
                controllerLink.Controller.UpdateGround(ref predictedGhost.ValueRW.ControllerState,
                    controllerConsts.ValueRO.ControllerConsts);
            }
        }

        protected override void OnDestroy()
        {
            var query = GetEntityQuery(typeof(PlayerControllerLink));
            foreach (var entity in query.ToEntityArray(Allocator.Temp))
            {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                controllerLink.Controller = null;
                EntityManager.SetComponentData(entity, controllerLink);
            }

            base.OnDestroy();
        }
    }
}