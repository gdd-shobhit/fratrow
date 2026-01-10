using System;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public abstract class GhostMonoBehaviour : MonoBehaviour
{
    private GhostGameObject m_GhostGameObject;

    public GhostGameObject GhostGameObject => m_GhostGameObject;
    public MultiplayerRole Role => m_GhostGameObject.Role;
    public World World => GhostGameObject.World;

    public void SetGhostGameObject(GhostGameObject ghostGameObject)
    {
        m_GhostGameObject = ghostGameObject;
    }

    // internal accessors
    protected bool TryReadGhostComponentData<T>(out T data)
        where T : unmanaged, IComponentData
    {
        if (GhostHasComponent<T>())
        {
            data = ReadGhostComponentData<T>();
            return true;
        }

        data = default;
        return false;
    }

    protected T ReadGhostComponentData<T>()
        where T : unmanaged, IComponentData
    {
        return m_GhostGameObject.ReadGhostComponentData<T>();
    }

    protected void ReadGhostComponentData<T>(out T data)
        where T : unmanaged, IComponentData
    {
        m_GhostGameObject.ReadGhostComponentData(out data);
    }

    protected void WriteGhostComponentData<T>(in T data)
        where T : unmanaged, IComponentData
    {
        m_GhostGameObject.WriteGhostComponentData<T>(data);
    }

    protected bool GhostHasComponent<T>()
        where T : unmanaged, IComponentData
    {
        return m_GhostGameObject.GhostHasComponent<T>();
    }

    public DynamicBuffer<T> GetGhostDynamicBuffer<T>()
        where T : unmanaged, IBufferElementData
    {
        return m_GhostGameObject.GetGhostDynamicBuffer<T>();
    }

    public virtual bool AllowAutoRegisterForUpdates() { return true; }

    public virtual void OnGhostLinked() { }
    public virtual void OnGhostPreDestroy() { }

    protected void BroadcastRPC<T>(T rpcCommand)
        where T : unmanaged, IRpcCommand
    {
        GhostGameObject.BroadcastRPC(rpcCommand);
    }

    protected bool ConsumeRPC<T>(out T rpc)
        where T : unmanaged, IRpcCommand
    {
        return GhostGameObject.ConsumeRPC(out rpc);
    }

    protected void IterateRPC<T>(Action<T> callback)
        where T : unmanaged, IRpcCommand
    {
        GhostGameObject.IterateRPC(callback);
    }

    protected void SendDirectedRPC<T>(T rpcCommand)
        where T : unmanaged, IRpcCommand, IGhostGameObjectDirectedRPC
    {
        GhostGameObject.SendDirectedRPC(rpcCommand);
    }

    protected bool ConsumeDirectedRPC<T>(out T rpc)
        where T : unmanaged, IRpcCommand, IGhostGameObjectDirectedRPC
    {
        return GhostGameObject.ConsumeDirectedRPC(out rpc);
    }

    protected T GetRequiredComponent<T>()
        where T : Component
    {
        if (!TryGetComponent<T>(out T component))
        {
            Debug.LogError($"{name} does not contain required component <{typeof(T).Name}>");
        }

        return component;
    }

    protected void GetRequiredComponent<T>(out T component)
        where T : Component
    {
        if (!TryGetComponent<T>(out component))
        {
            Debug.LogError($"{name} does not contain required component <{typeof(T).Name}>");
        }
    }

    protected T GetRequiredComponentInChildren<T>()
        where T : Component
    {
        var component = GetComponentInChildren<T>();
        Debug.Assert(component != null, $"{name} does not contain required component in children <{typeof(T).Name}>");

        return component;
    }

    protected void GetRequiredComponentInChildren<T>(out T component)
        where T : Component
    {
        component = GetComponentInChildren<T>();
        Debug.Assert(component != null, $"{name} does not contain required component in children <{typeof(T).Name}>");
    }
}
