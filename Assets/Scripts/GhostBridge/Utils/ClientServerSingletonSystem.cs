using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[ResetOnPlayMode(resetMethod: "ResetStaticState")]
public abstract partial class ClientServerSingletonSystem<T> : SystemBase
    where T : SystemBase
{
#pragma warning disable UDR0001
    // Reset by ResetStaticState method
    private static Dictionary<World, T> s_InstanceLookup = new();
#pragma warning restore UDR0001
    
    protected static void ResetStaticState()
    {
        s_InstanceLookup.Clear();
    }

    public static T ClientInstance =>
        ClientServerBootstrap.ClientWorld != null &&
        s_InstanceLookup.TryGetValue(ClientServerBootstrap.ClientWorld, out var instance)
            ? instance
            : null;

    public static T ServerInstance =>
        ClientServerBootstrap.ServerWorld != null &&
        s_InstanceLookup.TryGetValue(ClientServerBootstrap.ServerWorld, out var instance)
            ? instance
            : null;

    public static bool TryGetClientInstance(out T clientInstance)
    {
        if (ClientServerBootstrap.ClientWorld != null)
        {
            return s_InstanceLookup.TryGetValue(ClientServerBootstrap.ClientWorld, out clientInstance);
        }

        clientInstance = null;
        return false;
    }

    public static bool TryGetServerInstance(out T serverInstance)
    {
        // invert this if
        if (ClientServerBootstrap.ServerWorld != null)
        {
            return s_InstanceLookup.TryGetValue(ClientServerBootstrap.ServerWorld, out serverInstance);
        }

        serverInstance = null;
        return false;
    }

    public static T Instance(World world)
    {
        return world != null && s_InstanceLookup.TryGetValue(world, out var instance) ? instance : null;
    }

    public static bool TryGetInstance(World world, out T worldInstance)
    {
        Debug.Assert(world != null);
        return s_InstanceLookup.TryGetValue(world, out worldInstance);
    }

    protected override void OnCreate()
    {
        Debug.Assert(!s_InstanceLookup.ContainsKey(World),
            $"{typeof(T)} ClientServerSingletonSystem instance already exists for world {World.Name}");

        s_InstanceLookup.Add(World, this as T);
    }

    protected override void OnDestroy()
    {
        Debug.Assert(s_InstanceLookup.ContainsKey(World),
            "ClientServerSingletonSystem is being destroyed but does not contain a valid instance for its world");

        s_InstanceLookup.Remove(World);
    }
}