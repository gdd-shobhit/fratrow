using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Hash128 = Unity.Entities.Hash128;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class GhostEntityPrefabSystem : ClientServerSingletonSystem<GhostEntityPrefabSystem>
{
    public struct EntityPrefabProcessed : IComponentData {}

    private Dictionary<Hash128, Entity> m_EntityPrefabsByGuid = new();
    private Dictionary<Hash128, AssetReferenceGameObject> m_GameObjectPrefabsByGuid = new();

#if !NGS_SUBMISSION_BUILD
    private Dictionary<string, Entity> m_EntityPrefabsByName = new();
#endif

    private int m_LoadedPrefabs = 0;

    public bool PrefabsLoaded
    {
        get
        {
            return (m_LoadedPrefabs > 0 && m_LoadedPrefabs >= m_GameObjectPrefabsByGuid.Count);
        }
    }

    public AssetReferenceGameObject GetGameObjectPrefab(Hash128 hash)
    {
        if (m_GameObjectPrefabsByGuid.TryGetValue(hash, out var prefabReference))
        {
            return prefabReference;
        }

        return null;
    }

#if !NGS_SUBMISSION_BUILD
    public bool TryFindPrefabByPartialName(string partialName, out Entity prefab)
    {
        // any complete matches?
        foreach (var entry in m_EntityPrefabsByName)
        {
            if (String.Equals(entry.Key.ToString(), partialName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"Found complete match for ghost {entry.Key} and string {partialName}");
                prefab = entry.Value;
                return true;
            }
        }

        // any partial matches?
        foreach (var entry in m_EntityPrefabsByName)
        {
            if (entry.Key.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log($"Found a partial match for ghost {entry.Key} and string {partialName}");
                prefab = entry.Value;
                return true;
            }
        }

        prefab = default;
        return false;
    }
#endif

    public Entity GetEntityPrefab(Hash128 entityPrefabGuid)
    {
        if (m_EntityPrefabsByGuid.TryGetValue(entityPrefabGuid, out Entity entity))
        {
            return entity;
        }

        return Entity.Null;
    }

    protected override void OnDestroy()
    {
        foreach (var entry in m_GameObjectPrefabsByGuid)
        {
            if (entry.Value.IsValid())
            {
                entry.Value.ReleaseAsset();
            }
        }
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        int numRegistered = 0;

        foreach(var (ghostPrefabReference, entity) 
                in SystemAPI.Query<RefRO<GhostGameObjectPrefabReference>>() 
                    .WithNone<EntityPrefabProcessed>() 
                    .WithAll<GhostInstance, Prefab>() 
                    .WithOptions(EntityQueryOptions.IncludePrefab)
                    .WithEntityAccess())
        {
            var prefabGuid = ghostPrefabReference.ValueRO.PrefabGuid;
            if (!m_EntityPrefabsByGuid.ContainsKey(prefabGuid))
            {
                var assetReference = new AssetReferenceGameObject(prefabGuid.ToString());
                var operation = assetReference.LoadAssetAsync();

                operation.Completed += OnPrefabLoaded;

                m_EntityPrefabsByGuid.Add(prefabGuid, entity);
                m_GameObjectPrefabsByGuid.Add(prefabGuid, assetReference);

#if !NGS_SUBMISSION_BUILD
                m_EntityPrefabsByName.Add(ghostPrefabReference.ValueRO.PrefabName.ToString(), entity);
#endif
            }

            ecb.AddComponent<EntityPrefabProcessed>(entity);

            numRegistered++;
        }
        
        ecb.Playback(EntityManager);
        ecb.Dispose();

        if (numRegistered > 0)
        {
            Debug.Log($"[GhostEntityPrefabSystem] : Ghosts registered {numRegistered.ToString()} this frame, total ghosts {m_EntityPrefabsByGuid.Count.ToString()}.");
        }
    }

    private void OnPrefabLoaded(AsyncOperationHandle<GameObject> operation)
    {
        Debug.Assert(operation.Result != null, "[GhostEntityPrefabSystem] OnPrefabLoaded : Asset is null, loading failed!");

        m_LoadedPrefabs++;
    }
}
