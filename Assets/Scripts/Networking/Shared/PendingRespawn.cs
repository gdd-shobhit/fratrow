using Unity.Entities;

namespace Unity.FPSSample_2
{
    public struct PendingRespawn : IComponentData
    {
        public float RespawnTimer;
    }
}