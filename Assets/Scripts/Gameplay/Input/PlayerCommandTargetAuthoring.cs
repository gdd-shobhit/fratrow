using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public struct PlayerCommandTarget : IComponentData
{
    [GhostField]
    public int NetworkId;
}

public class PlayerCommandTargetAuthoring : MonoBehaviour
{
}

public class PlayerCommandTargetBaker : Baker<PlayerCommandTargetAuthoring>
{
    public override void Bake(PlayerCommandTargetAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new PlayerCommandTarget {NetworkId = -1});
    }
}
