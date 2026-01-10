using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine.Jobs;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class ServerGhostTransformRetrieveSystem : SingletonSystem<ServerGhostTransformRetrieveSystem>
{
    private const int k_BatchTransformSize = 200;

    private NativeArray<LocalTransform> m_GhostGameObjectTransforms;
    public NativeArray<LocalTransform> GhostTransformsArray
    {
        get
        {
            m_GhostTransformWriteHandle.Complete();
            return m_GhostGameObjectTransforms;
        }
    }

    private JobHandle m_GhostTransformWriteHandle;

    protected override void OnDestroy()
    {
        if (m_GhostGameObjectTransforms.IsCreated)
        {
            m_GhostGameObjectTransforms.Dispose();
        }

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        m_GhostTransformWriteHandle.Complete();

        var lifeTimeSystem = GhostGameObjectLifetimeSystem.ServerInstance;

        if (!m_GhostGameObjectTransforms.IsCreated || m_GhostGameObjectTransforms.Length != lifeTimeSystem.GhostGameObjectList.Count)
        {
            if (m_GhostGameObjectTransforms.IsCreated)
            {
                m_GhostGameObjectTransforms.Dispose();
            }
            m_GhostGameObjectTransforms = new NativeArray<LocalTransform>(lifeTimeSystem.GhostGameObjectList.Count, Allocator.Persistent);
        }

        if (lifeTimeSystem.GhostGameObjectTransformAccessArray.isCreated)
        {
            m_GhostTransformWriteHandle = new ServerRetrieveTransformsJob
            {
                GhostTransformsArray = m_GhostGameObjectTransforms,
            }
            .ScheduleReadOnly(lifeTimeSystem.GhostGameObjectTransformAccessArray, k_BatchTransformSize);

            Dependency = m_GhostTransformWriteHandle;
        }
    }

    [BurstCompile]
    struct ServerRetrieveTransformsJob : IJobParallelForTransform
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<LocalTransform> GhostTransformsArray;

        public void Execute(int index, TransformAccess transform)
        {
            if (transform.isValid)
            {
                GhostTransformsArray[index] = new LocalTransform
                {
                    Position = transform.position,
                    Rotation = transform.rotation,
                };
            }
        }
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(ServerGhostTransformRetrieveSystem))]
public partial class ServerGhostTransformWriteSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // update server ghosts with gameobject positions
        var transforms = ServerGhostTransformRetrieveSystem.Instance.GhostTransformsArray;
        if (transforms.Length == 0)
        {
            return;
        }

        foreach(var (localTransform, ghostGuid) 
                in SystemAPI.Query<RefRW<LocalTransform>, RefRO<GhostGameObjectGuid>>() 
                    .WithAll<GhostGameObjectTransformSync, GhostInstance>())
        {
            var transform = transforms[ghostGuid.ValueRO.LocalGhostIndex];
            localTransform.ValueRW.Position = transform.Position;
            localTransform.ValueRW.Rotation = transform.Rotation;
        }
    }
}
