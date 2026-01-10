using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public class PredictedPlayerGhostAuthoring : MonoBehaviour
{
    public float DisabledPredictionLerpFactor = 10f;
}

public class PredictedPlayerGhostBaker : Baker<PredictedPlayerGhostAuthoring>
{
    public override void Bake(PredictedPlayerGhostAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new PredictedPlayerGhost { DisabledPredictionLerpFactor = authoring.DisabledPredictionLerpFactor });
        AddBuffer<PredictedPlayerGhostState>(entity);

        AddComponent<PlayerInputComponent>(entity);
    }
}
