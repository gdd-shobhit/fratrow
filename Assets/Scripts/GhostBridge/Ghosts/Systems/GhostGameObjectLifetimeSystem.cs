#if !ENABLE_PROFILING && !NGS_SUBMISSION_BUILD
#define TRY_CATCH_GHOSTOBJECT_EXCEPTIONS
#endif

//#define VERBOSE_GHOST_LIFETIME_EVENTS

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Jobs;
using Hash128 = Unity.Entities.Hash128;
using Object = UnityEngine.Object;

public class GhostGameObjectLink : ICleanupComponentData
{
    public GhostGameObject LinkedInstance;
}

// added to a ghost when it's activation is deferred until conditions are met
public class GhostGameObjectDeferredActivation : ICleanupComponentData
{
}

// added to all active ghosts. It is automatically removed when a ghost is marked as destroyed
// (but potentially before the gameobject has been destroyed)
public class GhostGameObjectActive : IComponentData
{
}

public enum ComponentStripBehaviour
{
    DisableComponent,
    DestroyComponent,
    DestroyGameObject,
};

public interface IStripComponent
{
    // native MonoBehaviour overrides
    GameObject gameObject { get; }
    bool enabled { get; set; }

    ComponentStripBehaviour StripBehaviour { get; }
}

public interface IClientOnlyComponent : IStripComponent
{
}

public interface IServerOnlyComponent : IStripComponent
{
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class GhostGameObjectLifetimeSystem : ClientServerSingletonSystem<GhostGameObjectLifetimeSystem>
{
    private const int k_InitialGhostListCapacity = 100;

    private Dictionary<Hash128, GhostGameObject> m_GhostGameObjects = new();
    private List<GhostGameObject> m_GhostGameObjectList = new(k_InitialGhostListCapacity);
    public List<GhostGameObject> GhostGameObjectList => m_GhostGameObjectList;
    private TransformAccessArray m_TransformAccessArray;
    public TransformAccessArray GhostGameObjectTransformAccessArray => m_TransformAccessArray;
    private NativeList<Entity> m_GhostEntityList;
    public NativeList<Entity> GhostEntityList => m_GhostEntityList;

    public delegate void OnGhostPrefabSpawnedCallback(Hash128 guid, GhostGameObject obj, GhostGameObjectLifetimeSystem lifetimeSystem);

    public event OnGhostPrefabSpawnedCallback OnGhostPrefabSpawned;

    private int m_NumManagersCreated;
    private int m_NumLocalPlayersCreated;
    private int m_NumDeferredGhosts;

    private bool m_RebuildGhostGameObjectTransformAccessArray;

    private List<Animator> m_RetrievedAnimators = new();
    private List<Renderer> m_RetrievedRenderers = new();
#if WWISE_AUDIO_SUPPORTED
    private List<AkGameObj> m_RetrievedAkGameObjs = new();
#endif
    private List<IClientOnlyComponent> m_RetrievedClientOnlyComponents = new();
    private List<IServerOnlyComponent> m_RetrievedServerOnlyComponents = new();
    private List<(GhostGameObject ghost, Entity entity, MultiplayerRole role)> _ghostsToLink = new();

    protected override void OnCreate()
    {
        base.OnCreate();

        m_GhostEntityList = new NativeList<Entity>(k_InitialGhostListCapacity, Allocator.Persistent);

        RequireForUpdate<NetworkId>();
    }

    protected override void OnDestroy()
    {
        var query = GetEntityQuery(typeof(GhostGameObjectLink));
        foreach (var entity in query.ToEntityArray(Allocator.Temp))
        {
            var ghostLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
            ghostLink.LinkedInstance = null;
            EntityManager.SetComponentData(entity, ghostLink);
        }

        m_GhostEntityList.Dispose();
        if (m_TransformAccessArray.isCreated)
        {
            m_TransformAccessArray.Dispose();
        }

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        bool isClient = World.IsClient();
        bool isServer = World.IsServer();
        int networkId = isClient ? SystemAPI.GetSingleton<NetworkId>().Value : 0;
        var prefabSystem = GhostEntityPrefabSystem.Instance(World);

        if (!prefabSystem.PrefabsLoaded || ManagerGhostsSpawner.Instance == null)
        {
            return; // other systems not ready yet
        }

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (ghostGuid,
                     ghostPrefabReference,
                     ghost,
                     localTransform, entity)
                 in SystemAPI
                     .Query<RefRW<GhostGameObjectGuid>, RefRO<GhostGameObjectPrefabReference>, RefRO<GhostInstance>,
                         RefRO<LocalTransform>>()
                     .WithNone<GhostGameObjectLink>()
                     .WithEntityAccess())
        {
            var ghostPrefabGuid = ghostPrefabReference.ValueRO.PrefabGuid;
            var ghostRootPrefabGuid = ghostPrefabReference.ValueRO.PrefabRootGuid;

            if (ghostPrefabGuid.IsValid)
            {
                var ghostGameObjectPrefab = prefabSystem.GetGameObjectPrefab(ghostPrefabGuid);

                if (ghostGameObjectPrefab != null)
                {
                    var ghostPrefab = ghostGameObjectPrefab.Asset as GameObject;
                    var instance = Object.Instantiate(ghostPrefab, localTransform.ValueRO.Position,
                        localTransform.ValueRO.Rotation);

#if UNITY_EDITOR || DEBUG
                    instance.name = $"{(isClient ? "[Client]" : "[Server]")} {ghostPrefab.name}";
#endif
                    instance.transform.parent = isClient
                        ? GhostBridgeBootstrap.Instance.ClientGameObjectHierarchy.transform
                        : GhostBridgeBootstrap.Instance.ServerGameObjectHierarchy.transform;

                    if (localTransform.ValueRO.Scale != 1.0f)
                    {
                        instance.transform.localScale = new Vector3(localTransform.ValueRO.Scale,
                            localTransform.ValueRO.Scale, localTransform.ValueRO.Scale);
                    }

                    var ghostGameObject = instance.GetComponent<GhostGameObject>();

                    ghostGameObject.SetPrefabAssetGuid(ghostPrefabGuid, ghostRootPrefabGuid);
                    ghostGameObject.SetGuid(ghostGuid.ValueRO.Guid);

                    ghostGuid.ValueRW.LocalGhostIndex = m_GhostGameObjectList.Count;

#if UNITY_EDITOR || DEBUG
                    instance.name += $" [{ghostGuid.ValueRO.Guid}]";

                    if (m_GhostGameObjects.TryGetValue(ghostGuid.ValueRO.Guid, out var matchingGhost))
                    {
                        Debug.LogError(
                            $"Trying to add {ghostGameObject.gameObject.name} but it has the same GUID as {matchingGhost.gameObject.name}");
                    }
#endif

#if VERBOSE_GHOST_LIFETIME_EVENTS
                        Debug.Log($"Spawning {instance.name} with local index {ghostGuid.ValueRO.LocalGhostIndex}");
#endif

                    m_GhostGameObjects.Add(ghostGuid.ValueRO.Guid, ghostGameObject);
                    m_GhostGameObjectList.Add(ghostGameObject);
                    m_GhostEntityList.Add(entity);

                    m_RebuildGhostGameObjectTransformAccessArray = true;

                    if (isServer)
                    {
                        instance.GetComponentsInChildren(true, m_RetrievedRenderers);
                        foreach (var renderer in m_RetrievedRenderers)
                        {
                            renderer.enabled = false;
                        }

                        instance.GetComponentsInChildren(m_RetrievedAnimators);
                        foreach (var animator in m_RetrievedAnimators)
                        {
                            animator.enabled = false;
                        }

#if WWISE_AUDIO_SUPPORTED
                            instance.GetComponentsInChildren(m_RetrievedAkGameObjs);
                            foreach (var akGameObj in m_RetrievedAkGameObjs)
                            {
                                GameObject.Destroy(akGameObj);
                            }
#endif

                        instance.GetComponentsInChildren(m_RetrievedClientOnlyComponents);
                        foreach (var clientOnlyComponent in m_RetrievedClientOnlyComponents)
                        {
                            if (clientOnlyComponent != null)
                            {
                                StripComponent(clientOnlyComponent, (MonoBehaviour)clientOnlyComponent);
                            }
                        }
                    }
                    else
                    {
                        instance.GetComponentsInChildren(m_RetrievedServerOnlyComponents);
                        foreach (var serverOnlyComponent in m_RetrievedServerOnlyComponents)
                        {
                            if (serverOnlyComponent != null)
                            {
                                StripComponent(serverOnlyComponent, (MonoBehaviour)serverOnlyComponent);
                            }
                        }
                    }

                    ecb.AddComponent(entity, new GhostGameObjectLink { LinkedInstance = ghostGameObject });

                    ecb.AddComponent(entity, new GhostGameObjectDeferredActivation());
                    m_NumDeferredGhosts++;

                    instance.SetActive(false);
                }
                else if (ghostGameObjectPrefab == null)
                {
                    Debug.LogError(
                        $"[GHOSTGAMEOBJECTLIFETIMESYSTEM]: Prefab {ghostPrefabGuid.ToString()} is not registered with GhostEntityPrefabSystem");
                }
                else
                {
                    Debug.Log($"[GHOSTGAMEOBJECTLIFETIMESYSTEM]: Prefab {ghostPrefabGuid.ToString()} is still loading");
                }
            }
            else
            {
                Debug.LogError(
                    $"[GHOSTGAMEOBJECTLIFETIMESYSTEM]: Wanted to spawn a ghost but it doesn't have a valid prefab reference");
            }
        }

        // process all deferred ghosts in order
        // on the server:
        // 1. Managers
        // 2. Non-managers
        // on the client:
        // 1. Managers
        // 2. Local player ghosts
        // 3. Everything else
        if (m_NumDeferredGhosts > 0)
        {
            int numExpectedManagers = ManagerGhostsSpawner.TryGetInstance(out var managerSpawner) ? managerSpawner.ManagersToSpawn.Count : 0;
            int numExpectedLocalPlayers = isClient ? 1 : 0;

            var deferredGhostsQuery = SystemAPI.QueryBuilder()
                .WithAll<GhostGameObjectGuid, GhostGameObjectLink, GhostGameObjectDeferredActivation>().Build();

            // This loop now correctly processes the currently available deferred ghosts.
            // Dependencies are resolved frame-by-frame as managers and players are linked.
            foreach (var entity in deferredGhostsQuery.ToEntityArray(Allocator.Temp))
            {
                var ghostGuid = SystemAPI.GetComponent<GhostGameObjectGuid>(entity);
                var link = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                var ghostGameObject = link.LinkedInstance;

                // This check is crucial. If the GameObject was destroyed by another system this frame, skip it.
                if (ghostGameObject == null)
                {
                    m_NumDeferredGhosts--;
                    ecb.RemoveComponent<GhostGameObjectDeferredActivation>(entity); // Clean up the component
                    continue;
                }

                bool isManager = ghostGameObject.TryGetComponent<IGhostManager>(out _);
                var role = isServer ? MultiplayerRole.Server : MultiplayerRole.ClientProxy;
                if (isClient && SystemAPI.HasComponent<GhostOwner>(entity))
                {
                    if (SystemAPI.GetComponent<GhostOwner>(entity).NetworkId == networkId)
                    {
                        role = MultiplayerRole.ClientOwned;
                    }
                }

                bool allowedToActivate = false;
                if (isServer) {
                    allowedToActivate = isManager || (m_NumManagersCreated >= numExpectedManagers);
                } else { // isClient
                    if (ghostGuid.ServerLinked) {
                        bool isLocalPlayer = role == MultiplayerRole.ClientOwned && SystemAPI.HasComponent<PredictedPlayerGhost>(entity);
                        allowedToActivate = isManager || 
                                            (m_NumManagersCreated >= numExpectedManagers && isLocalPlayer) || 
                                            (m_NumManagersCreated >= numExpectedManagers && m_NumLocalPlayersCreated >= numExpectedLocalPlayers);
                    }
                }

                if (allowedToActivate)
                {
                    try
                    {
                        if (isManager) m_NumManagersCreated++;
                        else if (role == MultiplayerRole.ClientOwned && SystemAPI.HasComponent<PredictedPlayerGhost>(entity)) m_NumLocalPlayersCreated++;
                            
                        ghostGameObject.LinkGhost(World, entity, role);
                        m_NumDeferredGhosts--;
                        OnGhostPrefabSpawned?.Invoke(ghostGameObject.Guid, ghostGameObject, this);

                        ecb.RemoveComponent<GhostGameObjectDeferredActivation>(entity);
                        ecb.AddComponent<GhostGameObjectActive>(entity);

                        if (ghostGameObject.ActivateOnLinked)
                        {
                            ghostGameObject.gameObject.SetActive(true);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[GHOSTGAMEOBJECTLIFETIMESYSTEM] Link And Activate Ghost failed on '{ghostGameObject.name}'. Destroying entity. Exception: {e.Message}\\r\\n{e.StackTrace}");
                        ecb.DestroyEntity(entity); // Destroy the problematic entity to prevent repeated errors.
                        m_NumDeferredGhosts--;
                    }
                }
            }
        }

        var parentSetupQuery = SystemAPI.QueryBuilder()
            .WithAll<GhostGameObjectParentSetup, GhostGameObjectLink, GhostGameObjectActive>().Build();

        foreach (var entity in parentSetupQuery.ToEntityArray(Allocator.Temp))
        {
            var link = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);

            // This robust check prevents the MissingReferenceException.
            // We check both the C# wrapper and the underlying Unity GameObject.
            if (link.LinkedInstance != null && link.LinkedInstance.gameObject != null)
            {
                link.LinkedInstance.UpdateParentReference();
            }
            else
            {
                // The GameObject was destroyed before it could be parented. Just clean up the component.
                ecb.RemoveComponent<GhostGameObjectParentSetup>(entity);
            }
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();

        if (m_RebuildGhostGameObjectTransformAccessArray)
        {
            RebuildGhostGameObjectTransformAccessArray();
        }
    }

    private void RebuildGhostGameObjectTransformAccessArray()
    {
        var transforms = new Transform[m_GhostGameObjectList.Count];
        int transformCount = 0;
        for (int i = 0; i < m_GhostGameObjectList.Count; i++)
        {
            if (m_GhostGameObjectList[i] != null)
            {
                transforms[transformCount++] = m_GhostGameObjectList[i].transform;
            }
        }

        if (transformCount != transforms.Length)
        {
            Array.Resize(ref transforms, transformCount);
        }

        if (m_TransformAccessArray.isCreated)
        {
            m_TransformAccessArray.SetTransforms(transforms);
        }
        else
        {
            m_TransformAccessArray = new TransformAccessArray(transforms);
        }

        m_RebuildGhostGameObjectTransformAccessArray = false;
    }

    private void StripComponent<T>(T componentInterface, MonoBehaviour component)
        where T : IStripComponent
    {
        switch (componentInterface.StripBehaviour)
        {
            case ComponentStripBehaviour.DisableComponent:
                component.enabled = false;
                break;

            case ComponentStripBehaviour.DestroyComponent:
                GameObject.DestroyImmediate(component);
                break;

            case ComponentStripBehaviour.DestroyGameObject:
                GameObject.DestroyImmediate(component.gameObject);
                break;
        }
    }

    public bool IsSpawningManagers()
    {
        return ManagerGhostsSpawner.TryGetInstance(out var managersSpawner)
               && managersSpawner.ManagersToSpawn.Count > m_NumManagersCreated;
    }

    public GhostGameObject GetGhostGameObjectByGuid(Hash128 guid)
    {
        if (m_GhostGameObjects.TryGetValue(guid, out var go))
        {
            return go;
        }

        return null;
    }

    public bool DoesGhostExistOfType(GhostSpawner.GhostReference prefabType)
    {
        foreach (var ghost in m_GhostGameObjects.Values)
        {
            if (ghost != null
                && ghost.PrefabAssetGuid == prefabType.GhostGuid)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryFindGhostByPartialName(string partialName, out GhostGameObject ghostGameObject)
    {
        ghostGameObject = null;

        foreach (var ghost in m_GhostGameObjects.Values)
        {
            if (ghost != null
                && ghost.name.Contains(partialName))
            {
                if (ghostGameObject == null)
                {
                    ghostGameObject = ghost;
                }
                else
                {
                    // we already have a matching ghost
                    // let's fire a warning and not select it
                    Debug.LogWarning($"Detected multiple ghosts when looking for partial name '{partialName}'. Not returning a match");
                    ghostGameObject = null;
                    return false;
                }
            }
        }

        return ghostGameObject != null;
    }

    public bool TryGetGhostGameObjectByGuid(Hash128 guid, out GhostGameObject ghostGameObject)
    {
        if (m_GhostGameObjects.TryGetValue(guid, out ghostGameObject))
        {
            return ghostGameObject != null;
        }

        return false;
    }

    public void OnGhostGameObjectDestroyed(Hash128 guid)
    {
#if VERBOSE_GHOST_LIFETIME_EVENTS
        Debug.Log($"{World.Name} OnGhostGameObjectDestroyed {guid}");
#endif

        m_GhostGameObjects.Remove(guid);
        for (int i = 0; i < m_GhostGameObjectList.Count; i++)
        {
            if (m_GhostGameObjectList[i] != null && m_GhostGameObjectList[i].Guid == guid)
            {
                m_GhostGameObjectList[i] = null;
                m_GhostEntityList[i] = Entity.Null;
                break;
            }
        }

        m_RebuildGhostGameObjectTransformAccessArray = true;
    }

    public void PostUpdateRemoveStaleGhostGameObjectsFromList()
    {
        if (m_GhostGameObjects.Count == m_GhostGameObjectList.Count)
        {
            // the list is not dirty
            // no work to do
            return;
        }

        // 1. Clear the old, potentially out-of-sync lists.
        m_GhostGameObjectList.Clear();
        m_GhostEntityList.Clear();

        // 2. Repopulate the lists from the authoritative dictionary.
        // This guarantees the order and indices are always correct.
        foreach (var ghost in m_GhostGameObjects.Values)
        {
            if (ghost != null && ghost.GhostEntityExists())
            {
                m_GhostGameObjectList.Add(ghost);
                m_GhostEntityList.Add(ghost.LinkedEntity);

                // 3. Update the local index stored on the ghost's entity component.
                var ghostGuidComponent = ghost.ReadGhostComponentData<GhostGameObjectGuid>();
                ghostGuidComponent.LocalGhostIndex = m_GhostGameObjectList.Count - 1;
                ghost.WriteGhostComponentData(ghostGuidComponent);
            }
        }

        // 4. Mark the TransformAccessArray to be rebuilt with the new, correct data.
        m_RebuildGhostGameObjectTransformAccessArray = true;
    }

#if !NGS_SUBMISSION_BUILD
    private static void LogToConsoleAndDebug(string msg)
    {
        Debug.Log(msg);
        Console.Write(msg);
    }

    private static void LogDormacy(List<GhostGameObject> ghostList, World world, bool logAwakeGhosts = false)
    {
        Type[] types;
        if (world == ClientServerBootstrap.ServerWorld)
        {
            types = new Type[]
            {
                typeof(IEarlyUpdateServer),
                typeof(IUpdateServer),
            };
        }
        else
        {
            types = new Type[]
            {
                typeof(IUpdateClient),
                typeof(ILateUpdateClient),
            };
        }

        var numDormant = 0;
        foreach (var ghost in ghostList)
        {
            if (ghost == null || ghost.Dormant)
            {
                // for the purpose of logging
                // we'll consider pending deleted ghosts
                // to be dormant too
                numDormant++;
            }
            else
            {
                var detailsLog = "";
                // our ghost is awake, but does it have any updates registered?
                var willUpdate = false;
                foreach (var type in types)
                {
                    if (ghost.IsUpdateRegisteredForBehaviour(type, out var updates))
                    {
                        if (logAwakeGhosts)
                        {
                            if (!willUpdate)
                            {
                                // first update, gather details
                                detailsLog += $"<b>{ghost.name}</b>\n";
                                // if (DormancyManager.TryGetInstanceFromWorld(world, out var dormanyManager))
                                // {
                                //     if (dormanyManager.IsGhostRegisteredForDormancy(ghost, out var registeredType))
                                //     {
                                //         detailsLog += $" -- Dormancy check: <color=\"#8EBDFF\">{registeredType}</color> but currently AWAKE\n";
                                //     }
                                //     else
                                //     {
                                //         detailsLog += $" -- <color=\"red\">NOT registered for dormancy</color>\n";
                                //     }
                                // }
                            }

                            foreach (var update in updates)
                            {
                                detailsLog += $"   -- {update.GetType().Name}.{type.Name}\n";
                            }
                        }

                        willUpdate = true;
                    }
                }

                if (!willUpdate)
                {
                    numDormant++;
                }
                else if (logAwakeGhosts)
                {
                    Debug.Log(detailsLog);
                }
            }
        }

        LogToConsoleAndDebug($" - Dormant {numDormant}");
        LogToConsoleAndDebug($" - Awake {ghostList.Count - numDormant}");
    }

    [UnityEngine.Scripting.Preserve]
    public static void LogGhostStats(string[] args)
    {
        if (args.Length == 0 || (args.Length == 1 && args[0] == "details"))
        {
            LogGhostStats(new string[]
            {
                "server",
                args.Length == 1 ? args[0] : ""
            });
            LogGhostStats(new string[]
            {
                "client",
                args.Length == 1 ? args[0] : ""
            });
        }
        else
        {
            var world = ClientServerBootstrap.ServerWorld;
            if (args[0] == "client")
            {
                world = ClientServerBootstrap.ClientWorld;
            }

            if (world != null)
            {
                var lifetimeSystem = Instance(world);
                var ghostList = lifetimeSystem.GhostGameObjectList;
                LogToConsoleAndDebug($"{world.Name} has {ghostList.Count} ghosts");
                LogDormacy(ghostList, world, logAwakeGhosts: args.Length == 2 && args[1] == "details");
            }
        }
    }

    [UnityEngine.Scripting.Preserve]
    public static void SpawnGhost(string[] args)
    {
        if (args.Length == 0)
        {
            return;
        }

        if (ClientServerBootstrap.ServerWorld == null)
        {
            Debug.LogWarning("The command 'spawn' is only valid on the host");
            return;
        }

        if (args.Length <= 2) // spawn <object> [optional <count>]
        {
            // if (PlayerGhostManager.TryGetServerInstance(out var playerGhostManager))
            // {
            //     const float forwardOffset = 0.5f;
            //
            //     var players = playerGhostManager.GetPlayersByRole(MultiplayerRole.Server);
            //     if (players.Count > 0)
            //     {
            //         var spawnPos = players[0].transform.position;
            //
            //         // add an offset so things fall down nicely
            //         float heightOffset = 1f;
            //         spawnPos += heightOffset * Vector3.up;
            //
            //         // and a slight forward offset
            //         spawnPos += forwardOffset * players[0].transform.forward;
            //
            //         int count = 1;
            //         if (args.Length == 2 && Int32.TryParse(args[1], out var parsedCount))
            //         {
            //             count = parsedCount;
            //         }
            //         SpawnGhostAtPosition(args[0], spawnPos, count);
            //     }
            // }
        }

        // spawn with positions. This could be
        // 3 args: spawn wood 10 23,45,65,90
        else if (args.Length >= 3)
        {
            var spawnObject = args[0];
            int count = 1;
            if (Int32.TryParse(args[1], out var parsedCount))
            {
                count = parsedCount;
            }

            // combine remaining arguments together
            // this is so we can effectively strip out any white space
            string wholeString = "";
            for (int i = 0; i < args.Length - 2; i++)
            {
                wholeString += args[2 + i];
            }

            var stringParams = wholeString.Split(',');
            float[] spawnParams = new float[4];

            var spawnPos = new Vector3(spawnParams[0], spawnParams[1], spawnParams[2]);
            float yaw = 0f;
            if (spawnParams.Length == 4)
            {
                yaw = spawnParams[3];
            }

            SpawnGhostAtPosition(spawnObject, spawnPos, count, yaw);
        }
    }

    private static void SpawnGhostAtPosition(string spawnObjectName, Vector3 spawnPos, int count, float yaw = 0f)
    {
        if (GhostEntityPrefabSystem.ServerInstance.TryFindPrefabByPartialName(spawnObjectName, out var prefab))
        {
            var spawnRot = Quaternion.Euler(0f, yaw, 0f);

            const float stackOffset = 0.2f;
            for (int i = 0; i < count; i++)
            {
                GhostSpawner.SpawnGhostPrefab(prefab, spawnPos + (Vector3.up * i * stackOffset), spawnRot, GhostGameObject.GenerateRandomHash());
            }
        }
        else
        {
            Debug.LogWarning($"Could not find ghost prefab from the string {spawnObjectName}");
        }
    }
#endif
}