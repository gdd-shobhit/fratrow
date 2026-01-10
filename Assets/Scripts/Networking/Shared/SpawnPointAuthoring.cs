using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Unity.FPSSample_2
{
    public class SpawnPointAuthoring : MonoBehaviour
    {
        public class Baker : Unity.Entities.Baker<SpawnPointAuthoring>
        {
            public override void Bake(SpawnPointAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new SpawnPoint());
                AddComponent<LocalToWorld>(entity);
            }
        }
    }

    /// <summary>
    /// Placed in the GameScene subscene, the SpawnPoint components are used by the <see cref="ServerGameSystem"/>
    /// to spawn player characters during a game session.
    /// </summary>
    public struct SpawnPoint : IComponentData
    {
    }
}