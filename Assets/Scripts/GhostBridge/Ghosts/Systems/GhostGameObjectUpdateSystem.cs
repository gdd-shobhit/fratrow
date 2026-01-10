#if UNITY_EDITOR || ENABLE_PROFILING
#define USE_GHOST_GROUP_PROFILE_MARKERS
#endif

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Profiling;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

public enum GhostGameObjectUpdateGroup
{
    Default,
    Managers,
    Players,

    // ADD NEW GROUPS HERE TO AVOID INVALIDATING EXISTING GHOSTS

    // not actually used by any ghosts
    Invalid,
};

public class GhostGameObjectUpdateGroupHelpers
{
// Disable warning for static fields that won't change

#if USE_GHOST_GROUP_PROFILE_MARKERS
    public static readonly ProfilerMarker[] s_UpdateGroupMarkers =
    {
        new ProfilerMarker("Default"),
        new ProfilerMarker("Managers"),
        new ProfilerMarker("Players"),
    };
#endif

    // specifies the update order of the update groups
    // lower means "updated earlier in the frame"
    public static readonly int[] s_UpdateGroupOrder =
    {
        3, //Default
        1, //Managers
        2, //Players
    };
}

[ResetOnPlayMode(resetMethod: "Reset")]
public static class GhostGameObjectUpdateSystem
{
#pragma warning disable UDR0001
    private static Dictionary<Type, SystemBase> s_GhostUpdateSystemByType = new(); // This is cleared from Reset
#pragma warning restore UDR0001

    public static void Reset()
    {
        s_GhostUpdateSystemByType.Clear();
    }

    public static void GatherWorldSystems(World world)
    {
        if (world.IsServer())
        {
            s_GhostUpdateSystemByType[typeof(IEarlyUpdateServer)] = world.GetExistingSystemManaged<GhostGameObjectEarlyUpdateServerSystem>();
            s_GhostUpdateSystemByType[typeof(IUpdateServer)] = world.GetExistingSystemManaged<GhostGameObjectUpdateServerSystem>();
            s_GhostUpdateSystemByType[typeof(IPhysicsUpdateServer)] = world.GetExistingSystemManaged<GhostGameObjectPhysicsUpdateServerSystem>();
        }

        else if (world.IsClient())
        {
            s_GhostUpdateSystemByType[typeof(IEarlyUpdateClient)] = world.GetExistingSystemManaged<GhostGameObjectEarlyUpdateClientSystem>();
            s_GhostUpdateSystemByType[typeof(IUpdateClient)] = world.GetExistingSystemManaged<GhostGameObjectUpdateClientSystem>();
            s_GhostUpdateSystemByType[typeof(ILateUpdateClient)] = world.GetExistingSystemManaged<GhostGameObjectLateUpdateClientSystem>();
        }
    }

    public static GhostGameObjectUpdateSystemBase<T> GetGhostUpdateSystemByType<T>()
        where T : class, IGhostMonoBehaviourUpdate
    {
        var typeofT = typeof(T);
        if (s_GhostUpdateSystemByType.ContainsKey(typeofT))
        {
            return (GhostGameObjectUpdateSystemBase<T>)s_GhostUpdateSystemByType[typeof(T)];
        }
        return null;
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(GhostGameObjectLifetimeSystem))]
public partial class GhostGameObjectEarlyUpdateClientSystem : GhostGameObjectUpdateSystemBase<IEarlyUpdateClient>
{
    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) { updateObject.EarlyUpdateClient(deltaTime); }
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class GhostGameObjectUpdateClientSystem : GhostGameObjectUpdateSystemBase<IUpdateClient>
{
    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) { updateObject.UpdateClient(deltaTime); }
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class GhostGameObjectLateUpdateClientSystem : GhostGameObjectUpdateSystemBase<ILateUpdateClient>
{
    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) { updateObject.LateUpdateClient(deltaTime); }

    protected override void OnAfterGhostUpdates()
    {
        GhostGameObjectLifetimeSystem.ClientInstance.PostUpdateRemoveStaleGhostGameObjectsFromList();
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(GhostGameObjectLifetimeSystem))]
public partial class GhostGameObjectEarlyUpdateServerSystem : GhostGameObjectUpdateSystemBase<IEarlyUpdateServer>
{
    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) { updateObject.EarlyUpdateServer(deltaTime); }
}

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(PredictedSimulationSystemGroup))]
public partial class GhostGameObjectUpdateServerSystem : GhostGameObjectUpdateSystemBase<IUpdateServer>
{
    private List<GhostGameObject> m_ObjectsPendingDestroy = new();

    protected override void OnCreate()
    {
        base.OnCreate();

#if USE_GHOST_GROUP_PROFILE_MARKERS
        Debug.Assert(GhostGameObjectUpdateGroupHelpers.s_UpdateGroupMarkers.Length == (int)GhostGameObjectUpdateGroup.Invalid,
            "There are mismatching entries in s_UpdateGroupMarkers and the UpdateGroup enum");

        Debug.Assert(GhostGameObjectUpdateGroupHelpers.s_UpdateGroupOrder.Length == (int)GhostGameObjectUpdateGroup.Invalid,
            "There are mismatching entries in s_UpdateGroupOrder and the UpdateGroup enum");
#endif
    }

    public static void AddGhostForPendingDestroy(GhostGameObject ghost)
    {
        if (Instance != null)
        {
            var instance = Instance as GhostGameObjectUpdateServerSystem;
            instance.m_ObjectsPendingDestroy.Add(ghost);
        }
    }

    protected override void OnBeforeGhostUpdates()
    {
        // any pending objects to delete?
        if (m_ObjectsPendingDestroy != null && m_ObjectsPendingDestroy.Count > 0)
        {
            var count = m_ObjectsPendingDestroy.Count;
            for (int i = 0; i < count; i++)
            {
                var ghost = m_ObjectsPendingDestroy[i];
                ghost.DestroyEntity();
            }

            m_ObjectsPendingDestroy.Clear();
        }
    }

    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) { updateObject.UpdateServer(deltaTime); }
}

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PredictedSimulationSystemGroup))]
public partial class GhostGameObjectPhysicsUpdateServerSystem : GhostGameObjectUpdateSystemBase<IPhysicsUpdateServer>
{
    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) { updateObject.PhysicsUpdateServer(deltaTime); }

    protected override void OnAfterGhostUpdates()
    {
        GhostGameObjectLifetimeSystem.ServerInstance.PostUpdateRemoveStaleGhostGameObjectsFromList();
    }
}

public interface IGhostGameObjectUpdateSystem
{
    public delegate void OnDelayedCall(float deltaTime);
    public event OnDelayedCall PreEvent;
    public bool Paused { get; }
    public void SetPaused(bool paused);

    public uint GetCurrentServerTick();
    public uint GetCurrentInterpolationTick();
}

public abstract partial class GhostGameObjectUpdateSystemBase<T> : SingletonSystem<GhostGameObjectUpdateSystemBase<T>>, IGhostGameObjectUpdateSystem
    where T : class, IGhostMonoBehaviourUpdate
{
    private List<GhostGameObject> m_UpdateObjects = new();
    private bool m_UpdateObjectsDirty;
    private int m_UpdateObjectIndex;
    public bool Paused { get; private set; }

    private NetworkTick m_ServerTick;
    private NetworkTick m_InterpolationTick;
    
#if USE_GHOST_GROUP_PROFILE_MARKERS
    private static readonly ProfilerMarker s_OnUpdatePrepareMarker = new ProfilerMarker("GhostGameObjectUpdateSystemBase.Prepare");
    private static readonly Dictionary<Hash128, ProfilerMarker> s_GhostUpdateMarker = new();
#endif

    public event IGhostGameObjectUpdateSystem.OnDelayedCall PreEvent;

    protected override void OnDestroy()
    {
        m_UpdateObjects.Clear();

        base.OnDestroy();
    }

    public void RegisterForUpdate(GhostGameObject ghostGameObject)
    {
        m_UpdateObjects.Add(ghostGameObject);

        m_UpdateObjectsDirty = true;
    }

    public void Unregister(GhostGameObject ghostGameObject)
    {
        int index = m_UpdateObjects.IndexOf(ghostGameObject);
        if (index >= 0)
        {
            m_UpdateObjects.RemoveAtSwapBack(index);
            if (index == m_UpdateObjectIndex)
                // we've deleted the object we were updating
                // which is fine, but lets reduce the index so we process
                // the newly swapped in object
            {
                m_UpdateObjectIndex--;
            }

            m_UpdateObjectsDirty = true;
        }
    }

    public uint GetCurrentServerTick()
    {
        return m_ServerTick.IsValid ? m_ServerTick.TickIndexForValidTick : 0;
    }

    public uint GetCurrentInterpolationTick()
    {
        return m_InterpolationTick.IsValid ? m_InterpolationTick.TickIndexForValidTick : 0;
    }

    protected virtual void OnBeforeGhostUpdates() { }
    protected virtual void OnAfterGhostUpdates() { }

    protected override void OnUpdate()
    {
        if (m_UpdateObjects.Count == 0 || Paused)
        {
            return;
        }

#if USE_GHOST_GROUP_PROFILE_MARKERS
        s_OnUpdatePrepareMarker.Begin();
#endif

        if (m_UpdateObjectsDirty)
        {
            // we need to sort the update list into group order
            SortUpdateObjectsList();
            m_UpdateObjectsDirty = false;
        }

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        float dt = World.Time.DeltaTime;

        var networkTime = SystemAPI.GetSingleton<NetworkTime>();
        m_ServerTick = networkTime.ServerTick;
        m_InterpolationTick = networkTime.InterpolationTick;

        GhostGameObject.UpdateInterface = this;
        GhostGameObject.UpdateInterfaceType = typeof(T);
        GhostGameObject.UpdateEntityCommandBuffer = ecb;

        OnBeforeGhostUpdates();

        //cache and clear the event before invoking to allow new callbacks to be requested
        var delayedCalls = PreEvent;
        PreEvent = null;
        delayedCalls?.Invoke(dt);

#if USE_GHOST_GROUP_PROFILE_MARKERS
        s_OnUpdatePrepareMarker.End();
#endif

        var previousGroup = GhostGameObjectUpdateGroup.Invalid;

#if USE_GHOST_GROUP_PROFILE_MARKERS
        ProfilerMarker profilerMarker = default;
#endif
        for (m_UpdateObjectIndex = 0; m_UpdateObjectIndex < m_UpdateObjects.Count; m_UpdateObjectIndex++)
        {
            var updateObject = m_UpdateObjects[m_UpdateObjectIndex];

#if USE_GHOST_GROUP_PROFILE_MARKERS
            if (updateObject.UpdateGroup != previousGroup)
            {
                if (previousGroup != GhostGameObjectUpdateGroup.Invalid)
                {
                    profilerMarker.End();
                }
                profilerMarker = GhostGameObjectUpdateGroupHelpers.s_UpdateGroupMarkers[(int)updateObject.UpdateGroup];
                profilerMarker.Begin();
            }
#endif

            if (!updateObject.Dormant)
            {
#if USE_GHOST_GROUP_PROFILE_MARKERS
                if (!s_GhostUpdateMarker.TryGetValue(updateObject.Guid, out var marker))
                {
                    marker = s_GhostUpdateMarker[updateObject.Guid] = new ProfilerMarker(updateObject.name);
                }

                using (marker.Auto())
#endif
                {
                    UpdateGhostGameObject(updateObject, dt);
                }
            }

            previousGroup = updateObject.UpdateGroup;
        }
#if USE_GHOST_GROUP_PROFILE_MARKERS
        profilerMarker.End();
#endif

        m_UpdateObjectIndex = -1;

        OnAfterGhostUpdates();

        ecb.Playback(EntityManager);

        GhostGameObject.UpdateEntityCommandBuffer = default;
        GhostGameObject.UpdateInterface = null;
        GhostGameObject.UpdateInterfaceType = null;
    }

    private void SortUpdateObjectsList()
    {
        for (int i = 0; i < m_UpdateObjects.Count; i++)
        {
            int smallestIndex = i;
            for (int j = i + 1; j < m_UpdateObjects.Count; j++)
            {
                var a = m_UpdateObjects[smallestIndex];
                var b = m_UpdateObjects[j];

                if (GhostGameObjectUpdateGroupHelpers.s_UpdateGroupOrder[(int)a.UpdateGroup].CompareTo(GhostGameObjectUpdateGroupHelpers.s_UpdateGroupOrder[(int)b.UpdateGroup]) > 0)
                {
                    smallestIndex = j;
                }
            }

            var swap = m_UpdateObjects[i];
            m_UpdateObjects[i] = m_UpdateObjects[smallestIndex];
            m_UpdateObjects[smallestIndex] = swap;
        }
    }

    public void SetPaused(bool paused)
    {
        Paused = paused;
    }

    protected virtual void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) { }
}
