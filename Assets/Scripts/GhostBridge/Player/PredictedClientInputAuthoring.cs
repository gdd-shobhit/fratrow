using UnityEngine;
using Unity.NetCode;
using Unity.Entities;
using Unity.Mathematics;

[DisallowMultipleComponent]
public class PredictedClientInputAuthoring : MonoBehaviour
{
}

public class PredictedClientInputBaker : Baker<PredictedClientInputAuthoring>
{
    public override void Bake(PredictedClientInputAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent<PredictedClientInput>(entity);
    }
}
