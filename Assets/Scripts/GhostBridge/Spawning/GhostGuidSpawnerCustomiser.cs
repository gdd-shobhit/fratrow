using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

public class GhostGuidSpawnerCustomiser : MonoBehaviour,
    IGhostSpawnerCustomiser
{
    [field: SerializeField] public Hash128 Guid { get; private set; }

    public void OnGhostPrefabSpawned(Entity ghostEntity, EntityCommandBuffer ecb)
    {
        // N/A
    }

#if UNITY_EDITOR
    public void OnValidate()
    {
        Guid = GhostGameObject.GenerateNewHashOnNameDepthAndSiblingIndex(gameObject);
    }
#endif
}
