using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

public enum MultiplayerRole
{
    Server,
    ClientProxy,
    ClientOwned,

    // this is never set directly
    // but we can use this as a key when looking up
    // all client objects (proxy or owned)
    ClientAll
};

public struct GhostGameObjectGuid : IComponentData
{
    [GhostField] public Hash128 Guid;
    [GhostField] public bool ServerLinked;

    [GhostField] public Hash128 ParentGuid;
    [GhostField] public bool ParentToMovingBase;

    public int LocalGhostIndex;
}

public struct GhostGameObjectTransformSync : IComponentData
{
    public bool DisableTransformSync;

    // interpolate error
    public float ErrorTriggeredTime;
    public float3 ErrorOffset;
    public float ErrorBlendTime;
}

public struct GhostGameObjectParentSetup : IComponentData { }
public struct RequireMovementContextCalculation : IComponentData { }

public interface IGhostGameObjectDirectedRPC {}

public class GhostGameObject : MonoBehaviour
{
    public struct MovementContext
    {
        public float MinDistSqrdFromAPlayer;
        public float3 Velocity;
        public float VelocitySqrd;
    }

    // not serialised
    public Hash128 Guid { get; private set; }
    public Hash128 PrefabAssetGuid { get; private set; }
    public Hash128 RootPrefabAssetGuid { get; private set; }
    public bool ActivateOnLinked { get; set; } = true;
    public bool Dormant { get; set; } = false;
    public bool RequireMovementContextCalculation { get; set; } = false;

    private MovementContext m_MovementContext;
    public float3 Velocity => m_MovementContext.Velocity;
    public float VelocitySqrd => m_MovementContext.VelocitySqrd;
    public float MinDistSqrdFromAPlayer => m_MovementContext.MinDistSqrdFromAPlayer;

    public bool IsPendingDestroy => m_PendingDestroy;

    // parent/children
    public GhostGameObject Parent { get; private set; }
    public List<GhostGameObject> Children { get; private set; } // note this is initialised to null and only allocated if children are allocated

    [field: SerializeField] public bool RequireTransformSync { get; set; } = false;
    [field: SerializeField] public GhostGameObjectUpdateGroup UpdateGroup { get; set; } = GhostGameObjectUpdateGroup.Default;
    [SerializeField] private bool logDestroyBeforeLinked = false;

#pragma warning disable UDR0001
#pragma warning disable UDR0002
    // static vars lifecycle is handled by the GhostGameObjectUpdateSystem
    // and GhostGameObjectDestroySystem
    public static EntityCommandBuffer UpdateEntityCommandBuffer;
    public static IGhostGameObjectUpdateSystem UpdateInterface;
    public static Type UpdateInterfaceType;
#pragma warning restore UDR0001
#pragma warning restore UDR0002

    private MultiplayerRole m_Role = MultiplayerRole.ClientOwned;
    public MultiplayerRole Role => m_Role;

    private World m_World;
    public World World => m_World;
    public EntityManager EntityManager => m_World.EntityManager;

    private Entity m_LinkedEntity;
    public Entity LinkedEntity => m_LinkedEntity;

    private bool m_PendingDestroy;
    private int m_OwningNetworkId;

    private List<GhostMonoBehaviour> m_GhostMonoBehaviours = new();
    public int Owner => m_OwningNetworkId;

    private Dictionary<Type, IEnumerable> m_UpdateGhostMonoBehavioursByType = new();

    private int m_UpdateBehaviourIndex;

    private bool m_GhostTransformUpdatesEnabled = true;
    public bool GhostTransformUpdatesEnabled => m_GhostTransformUpdatesEnabled;

    private bool m_GhostPreDestroyCalled;
    public bool GhostPreDestroyCalled { get => m_GhostPreDestroyCalled; set => m_GhostPreDestroyCalled = value; }

    private List<IUpdateServer> m_CachedUpdateServerBehaviours = null;
    private List<IEarlyUpdateServer> m_CachedEarlyUpdateServerBehaviours = null;
    private List<IUpdateClient> m_CachedEarlyUpdateClientBehaviours = null;
    private List<IUpdateClient> m_CachedUpdateClientBehaviours = null;
    private List<IPhysicsUpdateServer> m_CachedPhysicsUpdateServerBehaviours = null;
    private List<ILateUpdateClient> m_CachedLateUpdateClientBehaviours = null;

    public void SetGuid(Hash128 guid)
    {
        Guid = guid;
    }

    public void SetRequireMovementContextCalculation()
    {
        if (!RequireMovementContextCalculation)
        {
            EntityManager.AddComponent<RequireMovementContextCalculation>(LinkedEntity);
            RequireMovementContextCalculation = true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetMovementContext(MovementContext context)
    {
        m_MovementContext = context;
    }

    public void SetPrefabAssetGuid(Hash128 guid, Hash128 rootGuid = default)
    {
        PrefabAssetGuid = guid;
        RootPrefabAssetGuid = rootGuid;
    }

    public static Hash128 GenerateRandomHash()
    {
        var hash128 = new UnityEngine.Hash128();
        hash128.Append(System.Guid.NewGuid().ToString());
        return (Unity.Entities.Hash128)hash128;
    }

    public static Hash128 GenerateNewHash(GameObject go, int uniqueCode = 0)
    {
        string hash = go.scene.name;
        if (go.transform.parent != null)
        {
            hash += go.transform.parent.name;
        }
        hash += go.name;
        hash += uniqueCode;

        var hash128 = new UnityEngine.Hash128();
        hash128.Append(hash);

        return hash128;
    }

    public static Hash128 GenerateNewHashOnNameDepthAndSiblingIndex(GameObject go)
    {
        int depth = 0;
        var parent = go.transform;
        while (parent.transform.parent != null)
        {
            parent = parent.transform.parent;
            depth++;
        }
        string hash = $"{go.name}_{depth}_{go.transform.GetSiblingIndex()}";

        var hash128 = new UnityEngine.Hash128();
        hash128.Append(hash);

        return hash128;
    }

    public static bool BroadClientServerRolesMatch(MultiplayerRole roleA, MultiplayerRole roleB)
    {
        if (roleA == roleB)
        {
            return true;
        }
        else if ((roleA == MultiplayerRole.ClientOwned && roleB == MultiplayerRole.ClientProxy)
            || (roleA == MultiplayerRole.ClientProxy && roleB == MultiplayerRole.ClientOwned))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public void MarkAsPendingDestroy()
    {
        m_PendingDestroy = true;
    }

#if WWISE_AUDIO_SUPPORTED
    private static List<AkGameObj> s_CollectedAkGameObjs = new();
#endif
    public void LinkGhost(World world, Entity linkedGhostEntity, MultiplayerRole role)
    {
        m_World = world;
        m_LinkedEntity = linkedGhostEntity;
        m_Role = role;

        if (EntityManager.HasComponent<GhostOwner>(linkedGhostEntity))
        {
            // let's store this owner
            var owner = EntityManager.GetComponentData<GhostOwner>(linkedGhostEntity);
            m_OwningNetworkId = owner.NetworkId;
        }
        // collect ghost monobehaviour components
        GetComponentsInChildren(m_GhostMonoBehaviours);
        foreach (var ghostBehaviour in m_GhostMonoBehaviours)
        {
            ghostBehaviour.SetGhostGameObject(this);
        }

        if (RequireTransformSync)
        {
            EntityManager.AddComponent<GhostGameObjectTransformSync>(linkedGhostEntity);
        }

        if (role == MultiplayerRole.Server)
        {
            var ghostGuid = ReadGhostComponentData<GhostGameObjectGuid>();
            ghostGuid.ServerLinked = true;

            // do we have a parent specified?
            // if (GhostHasComponent<ParentGhostSetup>())
            // {
            //     var parentSetup = ReadGhostComponentData<ParentGhostSetup>();
            //     ghostGuid.ParentGuid = parentSetup.ParentGuid;
            //     ghostGuid.ParentToMovingBase = parentSetup.ParentToMovingBase;
            //     if (ghostGuid.ParentGuid.IsValid)
            //     {
            //         EntityManager.AddComponent<GhostGameObjectParentSetup>(linkedGhostEntity);
            //     }
            //     else
            //     {
            //         Debug.LogError($"{name} is spawned with a parent setup but the parent guid is invalid");
            //     }
            // }

            WriteGhostComponentData(ghostGuid);
        }
        else
        {
            var ghostGuid = ReadGhostComponentData<GhostGameObjectGuid>();
            if (ghostGuid.ParentGuid.IsValid)
            {
                EntityManager.AddComponent<GhostGameObjectParentSetup>(linkedGhostEntity);
            }

#if WWISE_AUDIO_SUPPORTED
            if (!RequireTransformSync)
            {
                // this is essentially a static client object
                // ensure any audio position updates are disabled
                GetComponentsInChildren(s_CollectedAkGameObjs);
                foreach (var obj in s_CollectedAkGameObjs)
                {
                    obj.SetDoesNotNeedAutomaticUpdates();
                }

                s_CollectedAkGameObjs.Clear();
            }
#endif
        }

        OnGhostLinked();
    }

    public void OnGhostLinked()
    {
        foreach (var ghostBehaviour in m_GhostMonoBehaviours)
        {
            if (ghostBehaviour.AllowAutoRegisterForUpdates())
            {
                if (Role == MultiplayerRole.Server)
                {
                    AddBehaviourToUpdates<IEarlyUpdateServer>(ghostBehaviour);
                    AddBehaviourToUpdates<IUpdateServer>(ghostBehaviour);
                    AddBehaviourToUpdates<IPhysicsUpdateServer>(ghostBehaviour);
                }
                else
                {
                    AddBehaviourToUpdates<IEarlyUpdateClient>(ghostBehaviour);
                    AddBehaviourToUpdates<IUpdateClient>(ghostBehaviour);
                    AddBehaviourToUpdates<ILateUpdateClient>(ghostBehaviour);
                }
            }

            ghostBehaviour.OnGhostLinked();
        }
    }

    public void UpdateParentReference()
    {
        var ghostGuid = ReadGhostComponentData<GhostGameObjectGuid>();

        Debug.Assert(ghostGuid.ParentGuid.IsValid);
        Debug.Assert(Parent == null);

        // we need to look up the parent and make sure it's attached properly
        if (TryFindWorldGhost(ghostGuid.ParentGuid, out var instance) && instance.IsGhostLinked())
        {
            Parent = instance;
            Parent.AddChildGhost(this);

            if (ghostGuid.ParentToMovingBase)
            {
                // find the moving base in the heirarchy
                // var movingBase = Parent.GetComponentInChildren<MovementBase>();
                // if (movingBase)
                // {
                //     transform.parent = movingBase.transform;
                // }
                // else
                // {
                //     Debug.LogError($"{name} wants to be parented to a moving base on {Parent} but it doesn't contain one");
                // }
            }
            else
            {
                // parent to root
                transform.parent = Parent.transform;
            }

            EntityManager.RemoveComponent<GhostGameObjectParentSetup>(m_LinkedEntity);
        }
    }

    public void AddChildGhost(GhostGameObject child)
    {
        if (Children == null)
        {
            Children = new List<GhostGameObject>();
        }

        Children.Add(child);
    }

    public bool IsGhostLinked()
    {
        return World != null;
    }

    private List<T> GetUpdateGhostMonoBehavioursByType<T>()
        where T : IGhostMonoBehaviourUpdate
    {
        return (List<T>)m_UpdateGhostMonoBehavioursByType[typeof(T)];
    }

    public bool IsUpdateRegisteredForBehaviour<T>(GhostMonoBehaviour ghostBehaviour)
        where T : class, IGhostMonoBehaviourUpdate
    {
        var type = typeof(T);
        var update = ghostBehaviour as T;
        if (update != null)
        {
            if (m_UpdateGhostMonoBehavioursByType.TryGetValue(type, out var updates))
            {
                return ((List<T>)updates).Contains(update);
            }
        }

        return false;
    }

    public bool IsUpdateRegisteredForBehaviour(Type updateType, out IEnumerable updates)
    {
        if (m_UpdateGhostMonoBehavioursByType.TryGetValue(updateType, out updates))
        {
            var count = 0;
            foreach (var update in updates)
            {
                count++;
            }

            return count > 0;
        }

        return false;
    }

    public void AddBehaviourToUpdates<T>(GhostMonoBehaviour ghostBehaviour)
        where T : class, IGhostMonoBehaviourUpdate
    {
        var type = typeof(T);
        var update = ghostBehaviour as T;
        // since we call this speculatively, a null result is fairly common
        if (update != null)
        {
            if (!m_UpdateGhostMonoBehavioursByType.ContainsKey(type))
            {
                m_UpdateGhostMonoBehavioursByType.Add(type, new List<T>());

                // one off call to set the cached lists
                SetCachedLists<T>();
            }

            var updates = GetUpdateGhostMonoBehavioursByType<T>();
            Debug.Assert(!updates.Contains(update), $"Behaviour {ghostBehaviour.GetType().Name} on {ghostBehaviour.name} already registered for updates with {type.Name}");
            updates.Add(update);

            if (updates.Count == 1)
            {
                var system = GhostGameObjectUpdateSystem.GetGhostUpdateSystemByType<T>();
                system.RegisterForUpdate(this);
            }
        }
    }

    private void SetCachedLists<T>()
        where T : class, IGhostMonoBehaviourUpdate
    {
        if (typeof(T) == typeof(IEarlyUpdateServer))
        {
            m_CachedEarlyUpdateServerBehaviours = GetUpdateGhostMonoBehavioursByType<IEarlyUpdateServer>();
        }
        else if (typeof(T) == typeof(IUpdateServer))
        {
            m_CachedUpdateServerBehaviours = GetUpdateGhostMonoBehavioursByType<IUpdateServer>();
        }
        else if (typeof(T) == typeof(IUpdateClient))
        {
            m_CachedUpdateClientBehaviours = GetUpdateGhostMonoBehavioursByType<IUpdateClient>();
        }
        else if (typeof(T) == typeof(IPhysicsUpdateServer))
        {
            m_CachedPhysicsUpdateServerBehaviours = GetUpdateGhostMonoBehavioursByType<IPhysicsUpdateServer>();
        }
        else if (typeof(T) == typeof(ILateUpdateClient))
        {
            m_CachedLateUpdateClientBehaviours = GetUpdateGhostMonoBehavioursByType<ILateUpdateClient>();
        }
    }

    public void RemoveBehaviourFromUpdates<T>(GhostMonoBehaviour ghostBehaviour)
        where T : class, IGhostMonoBehaviourUpdate
    {
        var type = typeof(T);
        var update = ghostBehaviour as T;
        // since we call this speculatively, a null result is fairly common
        if (update != null)
        {
            var updates = GetUpdateGhostMonoBehavioursByType<T>();
            int index = updates.IndexOf(update);
            if (index >= 0)
            {
                updates.RemoveAtSwapBack(index);

                if (UpdateInterfaceType == type && index == m_UpdateBehaviourIndex)
                    // we've deleted the object we were updating
                    // which is fine, but lets reduce the index so we process
                    // the newly swapped in object
                {
                    m_UpdateBehaviourIndex--;
                }

                if (updates.Count == 0)
                {
                    var system = GhostGameObjectUpdateSystem.GetGhostUpdateSystemByType<T>();
                    system.Unregister(this);
                }
            }
            else
            {
                Debug.LogWarning($"Trying to unregister {ghostBehaviour.name} {gameObject.name} from updates for type {type.Name} but it was never registered");
            }
        }
    }

    private void UnregisterAllUpdates<T>()
        where T : class, IGhostMonoBehaviourUpdate
    {
        var type = typeof(T);
        if (m_UpdateGhostMonoBehavioursByType.ContainsKey(type))
        {
            var updates = GetUpdateGhostMonoBehavioursByType<T>();
            if (updates.Count > 0)
            {
                var system = GhostGameObjectUpdateSystem.GetGhostUpdateSystemByType<T>();
                system.Unregister(this);
            }
            updates.Clear();
        }
    }

    public void OnDestroy()
    {
        if (!m_GhostPreDestroyCalled)
        {
            OnGhostPreDestroy();
        }
    }

    public void OnGhostPreDestroy()
    {
        m_GhostPreDestroyCalled = true;

        // disconnect any children
        if (Children != null)
        {
            foreach (var child in Children)
            {
                if (child != null)
                {
                    // disconnect from hierarchy
                    // so that when this ghost gets destroyed, it doesn't destroy
                    // the child ghost (until it's ready)
                    child.transform.parent = null;
                }
            }
        }

        if (m_GhostMonoBehaviours != null)
        {
            foreach (var ghostBehaviour in m_GhostMonoBehaviours)
            {
                ghostBehaviour.OnGhostPreDestroy();
            }

            if (Role == MultiplayerRole.Server)
            {
                UnregisterAllUpdates<IEarlyUpdateServer>();
                UnregisterAllUpdates<IUpdateServer>();
                UnregisterAllUpdates<IPhysicsUpdateServer>();
            }
            else
            {
                UnregisterAllUpdates<IUpdateClient>();
                UnregisterAllUpdates<ILateUpdateClient>();
            }
        }
        else
        {
            if (!logDestroyBeforeLinked)
                return;

            Debug.Log($"{name} being destroyed before it has been linked. This is handled");
        }
    }

    public void EarlyUpdateServer(float deltaTime)
    {
        Debug.Assert(m_CachedEarlyUpdateServerBehaviours != null && m_CachedEarlyUpdateServerBehaviours.Count > 0, "A ghost game object shouldn't be updated if there are no behaviours registered to update");

        var updates = m_CachedEarlyUpdateServerBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++)
        {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.EarlyUpdateServer(deltaTime);
        }

        m_UpdateBehaviourIndex = -1;
    }

    public void UpdateServer(float deltaTime)
    {
        Debug.Assert(m_CachedUpdateServerBehaviours != null && m_CachedUpdateServerBehaviours.Count > 0, "A ghost game object shouldn't be updated if there are no behaviours registered to update");

        var updates = m_CachedUpdateServerBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++)
        {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.UpdateServer(deltaTime);
        }

        m_UpdateBehaviourIndex = -1;
    }

    public void PhysicsUpdateServer(float deltaTime)
    {
        Debug.Assert(m_CachedPhysicsUpdateServerBehaviours != null && m_CachedPhysicsUpdateServerBehaviours.Count > 0, "A ghost game object shouldn't be updated if there are no behaviours registered to update");

        var updates = m_CachedPhysicsUpdateServerBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++)
        {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.PhysicsUpdateServer(deltaTime);
        }

        m_UpdateBehaviourIndex = -1;
    }

    public void EarlyUpdateClient(float deltaTime)
    {
        Debug.Assert(m_CachedUpdateClientBehaviours != null && m_CachedUpdateClientBehaviours.Count > 0, "A ghost game object shouldn't be updated if there are no behaviours registered to update");

        var updates = m_CachedEarlyUpdateClientBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++)
        {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.UpdateClient(deltaTime);
        }

        m_UpdateBehaviourIndex = -1;
    }

    public void UpdateClient(float deltaTime)
    {
        Debug.Assert(m_CachedUpdateClientBehaviours != null && m_CachedUpdateClientBehaviours.Count > 0, "A ghost game object shouldn't be updated if there are no behaviours registered to update");

        var updates = m_CachedUpdateClientBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++)
        {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.UpdateClient(deltaTime);
        }

        m_UpdateBehaviourIndex = -1;
    }

    public void LateUpdateClient(float deltaTime)
    {
        Debug.Assert(m_CachedLateUpdateClientBehaviours != null && m_CachedLateUpdateClientBehaviours.Count > 0, "A ghost game object shouldn't be updated if there are no behaviours registered to update");

        var updates = m_CachedLateUpdateClientBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++)
        {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.LateUpdateClient(deltaTime);
        }

        m_UpdateBehaviourIndex = -1;
    }

    public void BroadcastRPC<T>(T rpcCommand)
        where T : unmanaged, IRpcCommand
    {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "BroadcastRPC is only valid during an update");

        if (Role == MultiplayerRole.Server)
        {
            var rpcEntity = UpdateEntityCommandBuffer.CreateEntity();
            UpdateEntityCommandBuffer.AddComponent(rpcEntity, (T)rpcCommand);
            UpdateEntityCommandBuffer.AddComponent(rpcEntity, new SendRpcCommandRequest());
        }
        else
        {
            Debug.LogError($"Broadcasting RPCs is only valid on the server");
        }
    }

    public bool ConsumeRPC<T>(out T rpc)
        where T : unmanaged, IRpcCommand
    {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "ConsumeRPC is only valid during an update");

        if (GhostGameObjectRPCReceiveSystem.TryGetInstance(World, out var rpcSystem))
        {
            var rpcEntities = rpcSystem.ReceivedRPCEntities;
            for (int i = 0; i < rpcEntities.Length; i++)
            {
                var rpcEntity = rpcEntities[i];
                if (EntityManager.HasComponent<T>(rpcEntity))
                {
                    var typeIndex = TypeManager.GetTypeIndex<T>();
                    if (!TypeManager.IsZeroSized(typeIndex))
                    {
                        rpc = EntityManager.GetComponentData<T>(rpcEntity);
                    }
                    else
                    {
                        rpc = default;
                    }
                    UpdateEntityCommandBuffer.DestroyEntity(rpcEntity);
                    rpcEntities.RemoveAtSwapBack(i);
                    return true;
                }
            }
        }

        rpc = default;
        return false;
    }

    public void SendDirectedRPC<T>(T rpcCommand)
        where T : unmanaged, IRpcCommand, IGhostGameObjectDirectedRPC
    {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "SendRPC is only valid during an update");

        // inject owner GUID
        var targetProperty = typeof(T).GetField("TargetGuid");
        if (targetProperty != null)
        {
            object rpcBoxed = (object)rpcCommand;
            targetProperty.SetValue(rpcBoxed, Guid);

            var rpcEntity = UpdateEntityCommandBuffer.CreateEntity();
            UpdateEntityCommandBuffer.AddComponent(rpcEntity, (T)rpcBoxed);
            UpdateEntityCommandBuffer.AddComponent(rpcEntity, new SendRpcCommandRequest());
        }
        else
        {
            Debug.LogError($"Type '{typeof(T).Name} implements IGhostGameObjectDirectedRPC but does not have a TargetGuid field.");
        }
    }

    public bool ConsumeDirectedRPC<T>(out T rpc)
        where T : unmanaged, IRpcCommand, IGhostGameObjectDirectedRPC
    {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "ConsumeDirectedRPC is only valid during an update");

        if (GhostGameObjectRPCReceiveSystem.TryGetInstance(World, out var rpcSystem))
        {
            var rpcEntities = rpcSystem.ReceivedRPCEntities;
            for (int i = 0; i < rpcEntities.Length; i++)
            {
                var rpcEntity = rpcEntities[i];

                if (EntityManager.HasComponent<T>(rpcEntity))
                {
                    var receivedRPC = EntityManager.GetComponentData<T>(rpcEntity);
                    var targetProperty = typeof(T).GetField("TargetGuid");
                    if (targetProperty != null)
                    {
                        var targetGUID = (Hash128)targetProperty.GetValue(receivedRPC);

                        // is this RPC directed at this object?
                        if (targetGUID == Guid)
                        {
                            rpc = receivedRPC;
                            UpdateEntityCommandBuffer.DestroyEntity(rpcEntity);
                            rpcEntities.RemoveAtSwapBack(i);
                            return true;
                        }
                    }
                    else
                    {
                        Debug.LogError($"Type '{typeof(T).Name} implements IGhostGameObjectDirectedRPC but does not have a TargetGuid field.");
                    }
                }
            }
        }

        rpc = default;
        return false;
    }

    public void IterateRPC<T>(Action<T> callback)
        where T : unmanaged, IRpcCommand
    {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "IterateRPC is only valid during an update");

        if (GhostGameObjectRPCReceiveSystem.TryGetInstance(World, out var rpcSystem))
        {
            var rpcEntities = rpcSystem.ReceivedRPCEntities;
            for (int i = 0; i < rpcEntities.Length; i++)
            {
                var rpcEntity = rpcEntities[i];
                if (EntityManager.HasComponent<T>(rpcEntity))
                {
                    var typeIndex = TypeManager.GetTypeIndex<T>();
                    if (!TypeManager.IsZeroSized(typeIndex))
                    {
                        callback.Invoke(EntityManager.GetComponentData<T>(rpcEntity));
                    }
                }
            }
        }
    }

    public void EnableGhostTransformUpdates(bool enable, bool interpolateError = false, float interpolationRate = 1f)
    {
        Debug.Assert(Role != MultiplayerRole.Server, $"EnableGhostTransformUpdates is only valid on client objects");
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "EnableGhostTransformUpdates is only valid during UpdateClient");

        m_GhostTransformUpdatesEnabled = enable;

        var transformSync = ReadGhostComponentData<GhostGameObjectTransformSync>();
        transformSync.DisableTransformSync = !enable;

        if (enable && interpolateError)
        {
            Debug.Assert(EntityManager.HasComponent<LocalTransform>(m_LinkedEntity), "[GHOSTGAMEOBJECT] EnableGhostTransformUpdates - ghost doesn't have a LocalTransform component");

            var localTransform = ReadGhostComponentData<LocalTransform>();
            transformSync.ErrorTriggeredTime = Time.time;
            transformSync.ErrorOffset = localTransform.Position - (float3)transform.position;
            transformSync.ErrorBlendTime = 1f / interpolationRate;
        }

        UpdateEntityCommandBuffer.SetComponent(m_LinkedEntity, transformSync);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_RendererComponents = new();
    }
    
    private static List<Renderer> s_RendererComponents = new();

    public void DisableRenderers()
    {
        GetComponentsInChildren(s_RendererComponents);
        foreach (var renderer in s_RendererComponents)
        {
            renderer.enabled = false;
        }
    }

    public bool GhostEntityExists()
    {
        if (!m_World.IsCreated)
        {
            return false;
        }

        // checks that the entity exists
        // but also that it isn't partially deleted
        // eg. if it still has a GhostGameObjectGuid then it isn't midway through a delete
        // because it's possible for the entity exist, but with only the ICleanupComponents left
        return EntityManager.Exists(m_LinkedEntity) && EntityManager.HasComponent<GhostGameObjectGuid>(m_LinkedEntity);
    }

    public T ReadGhostComponentData<T>()
        where T : unmanaged, IComponentData
    {
        return EntityManager.GetComponentData<T>(LinkedEntity);
    }

    public void ReadGhostComponentData<T>(out T data)
        where T : unmanaged, IComponentData
    {
        data = EntityManager.GetComponentData<T>(LinkedEntity);
    }

    public void WriteGhostComponentData<T>(in T data)
        where T : unmanaged, IComponentData
    {
        EntityManager.SetComponentData(LinkedEntity, data);
    }

    public void WriteGhostComponentDataECB<T>(in T data)
        where T : unmanaged, IComponentData
    {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "WriteGhostComponentDataECB is only valid during an Update");
        UpdateEntityCommandBuffer.SetComponent(LinkedEntity, data);
    }

    public bool GhostHasComponent<T>()
        where T : unmanaged, IComponentData
    {
        return World.IsCreated ? EntityManager.HasComponent<T>(LinkedEntity) : false;
    }

    public bool GhostHasDynamicBuffer<T>()
        where T : unmanaged, IBufferElementData
    {
        return World.IsCreated ? EntityManager.HasComponent<T>(LinkedEntity) : false;
    }

    public DynamicBuffer<T> GetGhostDynamicBuffer<T>()
        where T : unmanaged, IBufferElementData
    {
        return EntityManager.GetBuffer<T>(LinkedEntity);
    }

    public void DestroyEntity()
    {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "DestroyEntity is only valid during an Update");
        Debug.Assert(Role == MultiplayerRole.Server, "DestroyEntity is only valid on the server");
        Debug.Assert(UpdateInterfaceType == typeof(IUpdateServer), "DestroyEntity is only valid from an UpdateServer call");

        OnGhostPreDestroy();

        UpdateEntityCommandBuffer.DestroyEntity(m_LinkedEntity);
        m_LinkedEntity = Entity.Null;

        GhostGameObjectLifetimeSystem.ServerInstance.OnGhostGameObjectDestroyed(Guid);
        DestroyImmediate(gameObject);
    }

    public T ReadSingleton<T>()
        where T : unmanaged, IComponentData
    {
#pragma warning disable 0618 // obsolete warning
        return GhostGameObjectLifetimeSystem.Instance(World).GetSingleton<T>();
#pragma warning restore 0618
    }

    public bool TryReadSingleton<T>(out T singleton)
        where T : unmanaged, IComponentData
    {
#pragma warning disable 0618 // obsolete warning
        if (GhostGameObjectLifetimeSystem.Instance(World).HasSingleton<T>())
        {
            singleton = GhostGameObjectLifetimeSystem.Instance(World).GetSingleton<T>();
            return true;
        }
#pragma warning restore 0618

        singleton = default;
        return false;
    }

    public uint GetCurrentTick()
    {
        if (UpdateEntityCommandBuffer.IsCreated)
        {
            // on the server this is the server tick
            // but on the client, this will be the interpolation tick
            return Role == MultiplayerRole.Server
                ? UpdateInterface.GetCurrentServerTick()
                : UpdateInterface.GetCurrentInterpolationTick();
        }
        else
        {
            // we aren't in an update, but we can get this information direct from a system
            if (TryReadSingleton<NetworkTime>(out var networkTime))
            {
                return Role == MultiplayerRole.Server
                    ? networkTime.ServerTick.TickIndexForValidTick
                    : networkTime.InterpolationTick.TickIndexForValidTick;
            }
            else
            {
                return 0;
            }
        }
    }

    public static bool TryFindGhostGameObject(GameObject currentObject, out GhostGameObject ghost)
    {
        ghost = null;

        // Go up through the object hierarchy to find the ghost game object
        var trans = currentObject.transform;
        while (trans != null && !trans.gameObject.TryGetComponent(out ghost))
        {
            trans = trans.parent;
        }

        return (ghost != null);
    }

    public bool TryFindWorldGhost(Hash128 guid, out GhostGameObject worldGhost, bool includeUnlinkedGhosts = false)
    {
        if (World.IsCreated && GhostGameObjectLifetimeSystem.TryGetInstance(World, out var ghostGameObjectSystem))
        {
            if (ghostGameObjectSystem.TryGetGhostGameObjectByGuid(guid, out worldGhost)
                && (includeUnlinkedGhosts || worldGhost.IsGhostLinked()))
            {
                return true;
            }
        }

        worldGhost = null;
        return false;
    }

    public uint SecondsToTicks(float seconds)
    {
        if (TryReadSingleton<ClientServerTickRate>(out var tickRate))
        {
            return (uint)(seconds * tickRate.SimulationTickRate);
        }

        Debug.LogWarning($"Could not read the ClientServerTickRate to convert seconds to ticks");
        return 0;
    }

#if UNITY_EDITOR
    public void OnDrawGizmos()
    {
        if (Dormant)
        {
            Handles.color = Color.red;
            var style = EditorStyles.largeLabel;
            style.normal.textColor = Color.red;
            style.fontStyle = FontStyle.Bold;
            Handles.Label(transform.position, "DORMANT", style);
        }
    }
#endif
}
