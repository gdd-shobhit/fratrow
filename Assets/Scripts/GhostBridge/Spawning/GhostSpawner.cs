#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Hash128 = Unity.Entities.Hash128;

using Unity.GhostBridge;

public interface IGhostSpawnerCustomiser
{
    void OnGhostPrefabSpawned(Entity ghostEntity, EntityCommandBuffer ecb);
}

#if UNITY_EDITOR
public interface IGhostSpawnerEditorVisuals
{
    void OnEditorVisualsRefreshed();
    GhostSpawner.GhostReference GetAdditionalEditorVisualPrefab(GameObject ghostPrefab);
    Vector3 EditorVisualScale { get; }
}

public interface IGhostSpawnerEditorVisualsStripTag
{
    string TagToApply { get; }
}
#endif

[SelectionBase]
public class GhostSpawner : MonoBehaviour
{
    [Serializable]
    public class GhostReference : ISerializationCallbackReceiver
    {
        [field: SerializeField] public AssetReferenceGameObject GhostPrefab { get; set; }

        public Hash128 GhostGuid => m_GhostGuid;
        [SerializeField] [HideInInspector] private Hash128 m_GhostGuid;

        public bool OnValidate()
        {
            bool validReference = GhostPrefab != null && GhostPrefab.RuntimeKeyIsValid();
            m_GhostGuid = validReference ? new Hash128(GhostPrefab.AssetGUID) : new();

            return validReference;
        }

        public void OnBeforeSerialize()
        {
            OnValidate();
        }

        public void OnAfterDeserialize() {}

        public void SetAssetReference(AssetReferenceGameObject prefab)
        {
            GhostPrefab = prefab;
            OnValidate();
        }
    }

    [field: SerializeField] public GhostReference GhostPrefabReference { get; set; } = new();

    [field: SerializeField] public bool RandomiseRotation { get; private set; }
    [field: SerializeField] public bool UniformScaleOverride { get; private set; }
    [field: SerializeField] public bool PersistGameObject { get; private set; } = false;
    [field: SerializeField] public string LockedBehindFeature { get; private set; } = "";

    public Hash128 GhostNetGUID { get; private set; }

    private static readonly List<IGhostSpawnerCustomiser> s_GhostSpawnerCustomisers = new();
    protected bool m_PersistSpawnerAfterSpawn;
    protected bool m_GhostSpawned;
    private string m_cachedName = "";

#if UNITY_EDITOR
    private const HideFlags kDefaultHideValues = HideFlags.NotEditable | HideFlags.DontSave;

    private List<GameObject> m_EditorVisualObjectsToDestroy;
    private GameObject m_EditorVisualObjectForLightbaking;
    private List<Component> m_EditorComponentsToDestroy;

    private AssetReferenceGameObject m_PreviouslyCachedGhostPrefab = new("");
    
    public virtual void OnValidate()
    {
        if (!Application.isPlaying)
        {
            if ((gameObject.scene.name != null) && EditorVisualsNeedUpdate())
            {
                StripEditorVisuals(true);
                //RestoreEditorVisuals();
            }

            if (TryGetComponent<GhostGameObject>(out _))
            {
                Debug.LogError($"[SCENEGHOSTSPAWNER] OnValidate: Spawner {gameObject.name} has a GhostGameObject component. It shouldn't");
            }
        }
    }

    private void OnDestroy()
    {
        EditorApplication.delayCall -= EditorVisualDestroy;
        EditorApplication.update -= EditorLightBakeHeartbeat;
    }

    public void StripEditorVisuals(bool defer)
    {
        MarkEditorOnlyChildrenForDestroy(transform);
        if (defer)
        {
            EditorApplication.delayCall += EditorVisualDestroy;
        }
        else
        {
            EditorVisualDestroy();
        }
    }

    // public void RestoreEditorVisuals()
    // {
    //     EditorApplication.delayCall -= EditorVisualCreate; //CH: strip any previous callbacks so we don't double up
    //     EditorApplication.delayCall += EditorVisualCreate;
    //     // Add callbacks for lightmapping
    //     Lightmapping.bakeStarted -= EditorLightBakeStart;
    //     Lightmapping.bakeStarted += EditorLightBakeStart;
    // }

    private GameObject InstantiateEditorVisual(GameObject prefab, Transform parent)
    {
        var instance = Instantiate(prefab, parent);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var components = instance.GetComponentsInChildren<Component>();
        foreach (var component in components)
        {
            if (component is Collider componentCollider)
            {
                componentCollider.enabled = false;
            }
            else if (!(component is Renderer || component is Transform || component is MeshFilter || component is Light))
            {
                if (m_EditorComponentsToDestroy == null)
                {
                    m_EditorComponentsToDestroy = new List<Component>();
                }

                m_EditorComponentsToDestroy.Add(component);
            }

            // if (component is MovementBase)
            // {
            //     var children = component.GetComponentsInChildren<Transform>(includeInactive: false);
            //     foreach (var child in children)
            //     {
            //         child.gameObject.layer = (int)LayerIndex.ServerMovingBase;
            //     }
            // }

            var stripTag = component as IGhostSpawnerEditorVisualsStripTag;
            if (stripTag != null)
            {
                component.gameObject.tag = stripTag.TagToApply;
            }
        }

        return instance;
    }

    private static void ModifyHideFlagsRecursive(GameObject rootGameObject, HideFlags hideFlagsToSet, HideFlags hideFlagsToClear)
    {
        var rootTransform = rootGameObject.transform;
        int numChildren = rootTransform.childCount;

        rootGameObject.hideFlags |= hideFlagsToSet;
        rootGameObject.hideFlags &= ~hideFlagsToClear;

        for (int i = 0; i < numChildren; i++)
        {
            var childGameObject = rootTransform.GetChild(i).gameObject;

            ModifyHideFlagsRecursive(childGameObject, hideFlagsToSet, hideFlagsToClear);
        }
    }

    protected void EditorVisualCreate_Internal(GhostReference visualPrefab, Transform parent, ref AssetReferenceGameObject cachedReference)
    {
        cachedReference = new(visualPrefab.GhostPrefab.AssetGUID);

        var prefab = visualPrefab.GhostPrefab.editorAsset;

        if (prefab != null && prefab.TryGetComponent<GhostGameObject>(out _))
        {
            Vector3 editorVisualScale = Vector3.one;
            var customisers = parent.GetComponents<IGhostSpawnerEditorVisuals>();

            foreach (var customiser in customisers)
            {
                if (customiser.EditorVisualScale != Vector3.one)
                {
                    editorVisualScale = customiser.EditorVisualScale;
                    break;
                }
            }

            var instance = InstantiateEditorVisual(prefab, parent);
            instance.transform.localScale = editorVisualScale;

            foreach (var customiser in customisers)
            {
                GhostReference editorVisual = customiser.GetAdditionalEditorVisualPrefab(instance);
                var customiserPrefab = editorVisual != null ? editorVisual.GhostPrefab.editorAsset : null;

                if (customiserPrefab != null)
                {
                    InstantiateEditorVisual(customiserPrefab, instance.transform);
                }

                customiser.OnEditorVisualsRefreshed();
            }

            m_EditorVisualObjectForLightbaking = instance;
            var playModeSettings = PlayModeSettingsHandler.GetEditorSettings();
            HideFlags hideFlagsToSet = playModeSettings.ShowEditorVisuals ? HideFlags.None : HideFlags.HideInInspector;
            instance.tag = "EditorOnly";
            instance.name = $"[EDITORVISUAL] {GhostPrefabReference.GhostPrefab.editorAsset.name}.{prefab.name}";

            ModifyHideFlagsRecursive(instance, kDefaultHideValues | hideFlagsToSet, HideFlags.None);
        }
        else
        {
            Debug.LogWarning($"[SCENEGHOSTSPAWNER] EditorVisualCreate_Internal: This ghost prefab in spawner {name} is not a GhostGameObject, aborting");
        }
    }

    protected void MarkEditorOnlyChildrenForDestroy(Transform parent)
    {
        // remove any editor only children
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.gameObject.CompareTag("EditorOnly"))
            {
                if (m_EditorVisualObjectsToDestroy == null)
                {
                    m_EditorVisualObjectsToDestroy = new List<GameObject>();
                }

                m_EditorVisualObjectsToDestroy.Add(child.gameObject);
            }
            else
            {
                MarkEditorOnlyChildrenForDestroy(child);
            }
        }
    }

    protected virtual bool EditorVisualsNeedUpdate()
    {
        return GhostPrefabReference.GhostPrefab != null && GhostPrefabReference.GhostPrefab.AssetGUID != m_PreviouslyCachedGhostPrefab.AssetGUID;
    }

    // protected virtual void EditorVisualCreate()
    // {
    //     if (!Application.isPlaying && this != null && gameObject.activeInHierarchy)
    //     {
    //         EditorVisualCreate_Internal(GhostPrefabReference, transform, ref m_PreviouslyCachedGhostPrefab);
    //
    //         EditorVisualDestroy();
    //     }
    // }

    protected void EditorLightBakeStart()
    {
        if (m_EditorVisualObjectForLightbaking == null)
            return;

        ModifyHideFlagsRecursive(m_EditorVisualObjectForLightbaking, HideFlags.None, kDefaultHideValues);

        // We run a constant update to cover for canceling a lightbake, we only get a callback from Lightmapping when its complete
        // TODO: Use Complete and Cancelled callbacks when switching to 2023.3 / Unity 6
        EditorApplication.update += EditorLightBakeHeartbeat;
    }

    protected void EditorLightBakeHeartbeat()
    {
        if (m_EditorVisualObjectForLightbaking == null || Lightmapping.isRunning)
            return;

        ModifyHideFlagsRecursive(m_EditorVisualObjectForLightbaking, kDefaultHideValues, HideFlags.None);

        EditorApplication.update -= EditorLightBakeHeartbeat;
    }

    protected void EditorVisualDestroy()
    {
        if (m_EditorVisualObjectsToDestroy != null)
        {
            foreach (var go in m_EditorVisualObjectsToDestroy)
            {
                DestroyImmediate(go);
            }
            m_EditorVisualObjectsToDestroy.Clear();
        }

        if (m_EditorComponentsToDestroy != null)
        {
            // remove ghost monobehaviours first
            foreach (var comp in m_EditorComponentsToDestroy)
            {
                if (comp is GhostMonoBehaviour ||
#if WWISE_AUDIO_SUPPORTED
                    comp is AkEvent ||
                    comp is AkAmbient ||
#endif //WWISE_AUDIO_SUPPORTED
                    comp is GhostAuthoringComponent ||
                    comp is Joint)
                {
                    DestroyImmediate(comp);
                }
            }

            // then everything else
            foreach (var comp in m_EditorComponentsToDestroy)
            {
                if (comp != null)
                {
                    DestroyImmediate(comp);
                }
            }
            m_EditorComponentsToDestroy.Clear();
        }
    }
#endif //UNITY_EDITOR

    protected virtual void Awake()
    {
        if (!ClientServerBootstrap.HasServerWorld)
        {
            // we don't want to attempt to spawn ghosts
            // if there is no server world
            DestroySpawner();
        }
        else
        {
            AllocateGhostGUID();
        }
    }

    protected void AllocateGhostGUID()
    {
        if (TryGetComponent<GhostGuidSpawnerCustomiser>(out var guidCustomiser))
        {
            GhostNetGUID = guidCustomiser.Guid;
        }
        else
        {
            GhostNetGUID = GhostGameObject.GenerateRandomHash();
        }
    }

    private void DestroySpawner()
    {
        if (PersistGameObject)
        {
#if UNITY_EDITOR
            // ensure we don't have visuals during runtime
            MarkEditorOnlyChildrenForDestroy(transform);
            EditorVisualDestroy();
#endif

            // we want to keep the gameobject but we want to strip the spawner components
            GetComponentsInChildren(s_GhostSpawnerCustomisers);
            foreach (var componentToDestroy in s_GhostSpawnerCustomisers)
            {
                Destroy((Component)componentToDestroy);
            }

            Destroy(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // we do the spawning in LateUpate rather than Update as we want to avoid any "unlinked" ghost entities
    // we spawn being sent to the client in the "GhostSend" that happens after the entity updates.
    // this means the GhostGameObjectLifetimeSystem can pick up the newly spawned ghost entities
    // at the start of the next frame and initialise them before the next GhostSend
    public virtual void LateUpdate()
    {
        if (GhostEntityPrefabSystem.ServerInstance != null &&
            !GhostEntityPrefabSystem.ServerInstance.PrefabsLoaded)
        {
            return;
        }
        
        if(GhostBridgeManager.Instance.IsServerListening())
        //if (ServerConnectionSystem.TryGetInstance(out var serverConnectionSystem) && serverConnectionSystem.IsPlaying)
        {
            var spawnRotation = transform.rotation;
            if (RandomiseRotation)
            {
                spawnRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            }

            m_GhostSpawned = false;
            if (GhostPrefabReference.GhostGuid.IsValid)
            {
                if (FeatureToggle.IsAllowedByFeature(LockedBehindFeature))
                {
                    var prefabEntity = FindGhostPrefabEntity(GhostPrefabReference);
                    if (prefabEntity != Entity.Null)
                    {
                        Debug.Assert(GhostNetGUID.IsValid, "[SCENEGHOSTSPAWNER] GhostNetGUID is invalid, should have been pre-allocated on Awake");

                        GetComponents(s_GhostSpawnerCustomisers);
                        m_GhostSpawned = SpawnGhostPrefab(prefabEntity, transform.position, spawnRotation, GhostNetGUID, UniformScaleOverride ? transform.localScale.x : 1.0f, (entity, ecb) =>
                        {
                            OnGhostPrefabSpawned(entity, ecb);

                            foreach (var customiser in s_GhostSpawnerCustomisers)
                            {
                                customiser.OnGhostPrefabSpawned(entity, ecb);
                            }
                        });
                    }
                    else
                    {
                        Debug.LogError($"[SCENEGHOSTSPAWNER] Update: GhostPrefabReference entity not found for {GetCachedName()}", this);
                    }
                }
                else
                {
                    // feature is not enabled, but let's treat it as if it had done
                    // a valid spawn
                    m_GhostSpawned = true;

                    // don't persist if the spawner shouldn't exist
                    m_PersistSpawnerAfterSpawn = false;
                    PersistGameObject = false;
                }
            }
            else
            {
                Debug.LogError($"[SCENEGHOSTSPAWNER] Update: GhostPrefabReference invalid for {GetCachedName()}", this);
            }

            if (!m_PersistSpawnerAfterSpawn)
            {
                if (m_GhostSpawned)
                {
                    // spawner no longer needed
                    DestroySpawner();
                }
                else
                {
                    Debug.LogWarning($"[SCENEGHOSTSPAWNER] Unable to spawn scene ghost {GetCachedName()}. Will keep trying", this);
                }
            }
        }
    }
    
    private string GetCachedName()
    {
        if (string.IsNullOrEmpty(m_cachedName))
        {
            m_cachedName = gameObject.name;
        }

        return m_cachedName;
    }

    protected virtual void OnGhostPrefabSpawned(Entity ghostEntity, EntityCommandBuffer ecb) { }
   
    public static Entity FindGhostPrefabEntity(GhostReference ghostReference)
    {
        return FindGhostPrefabEntity(ghostReference.GhostGuid);
    }

    public static Entity FindGhostPrefabEntity(Hash128 ghostGuid)
    {
        if(GhostBridgeManager.Instance.IsServerListening())
        {
            return GhostEntityPrefabSystem.ServerInstance.GetEntityPrefab(ghostGuid);
        }
        return Entity.Null;
    }

    public static GameObject FindGhostPrefab(GhostReference ghostReference)
    {
        GameObject gObj = null;
        if(GhostBridgeManager.Instance.IsServerListening())
        {
            var prefab = GhostEntityPrefabSystem.ServerInstance.GetGameObjectPrefab(ghostReference.GhostGuid);
            if (prefab != null && prefab.Asset != null)
            {
                gObj = prefab.Asset as GameObject;
            }
        }

        if (gObj == null)
        {
#if UNITY_EDITOR
            Debug.LogError($"Failed to find {ghostReference.GhostPrefab.editorAsset.name} - has it been added to the GlobalGhostPrefabs prefab?");
#else
            Debug.LogError($"Failed to find GhostReference with GUID {ghostReference.GhostGuid.ToString()} - has it been added to the GlobalGhostPrefabs prefab?");
#endif
        }

        return gObj;
    }

    public static bool SpawnGhostPrefab(GhostReference ghostPrefab, Vector3 spawnPos, Quaternion spawnRot, Hash128 netGuid, float uniformScale = 1.0f, Action<Entity, EntityCommandBuffer> postSpawnSpecialisation = null)
    {
        var prefabEntity = FindGhostPrefabEntity(ghostPrefab);

        if (prefabEntity != Entity.Null)
        {
            return SpawnGhostPrefab(prefabEntity, spawnPos, spawnRot, netGuid, uniformScale, postSpawnSpecialisation);
        }
        else
        {
            Debug.LogError($"[SCENEGHOSTSPAWNER] SpawnGhostPrefab trying to spawn an entity prefab '{ghostPrefab.GhostPrefab.AssetGUID}' that doesn't seem to exist. Was it added to GhostPrefabs?");
        }

        return false;
    }

    public static bool SpawnGhostPrefab(Entity prefabEntity, Vector3 spawnPos, Quaternion spawnRot, Hash128 netGuid, float uniformScale = 1.0f, Action<Entity, EntityCommandBuffer> postSpawnSpecialisation = null)
    {
        if (prefabEntity == Entity.Null)
        {
            Debug.LogError($"SpawnGhostPrefab trying to spawn an entity prefab that doesn't exist. Was it added to GhostPrefabs?");
            return false;
        }

        // do we have an active ecb? If so, let's use that
        var ecb = GhostGameObject.UpdateEntityCommandBuffer;
        bool ecbNeedsPlayback = false;
        if (!ecb.IsCreated)
        {
            // let's create one and immediately play it back
            ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            ecbNeedsPlayback = true;
        }

        if(GhostBridgeManager.Instance.IsServerListening())
        //if (ServerConnectionSystem.Instance.IsPlaying)
        {
            var instance = ecb.Instantiate(prefabEntity);

            ecb.SetComponent(instance, new LocalTransform
            {
                Position = spawnPos,
                Rotation = spawnRot,
                Scale = uniformScale
            });

            if (netGuid.IsValid)
            {
                ecb.SetComponent(instance, new GhostGameObjectGuid { Guid = netGuid });
            }

            postSpawnSpecialisation?.Invoke(instance, ecb);

            if (ecbNeedsPlayback)
            {
                //ecb.Playback(ServerConnectionSystem.Instance.EntityManager);
                if (GhostBridgeManager.Instance.TryGetServerEntityManager(out var entityManager))
                {
                    ecb.Playback(entityManager);
                }
            }

            return true;
        }
        else
        {
            Debug.LogError($"SpawnGhostPrefab cannot spawn a ghost as the server doesn't appear to be in a playing state yet");
        }

        return false;
    }
}
