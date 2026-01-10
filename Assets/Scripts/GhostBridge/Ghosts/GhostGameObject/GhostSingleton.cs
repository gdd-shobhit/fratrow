using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[ResetOnPlayMode(resetMethod: "InvokeAndClearStaticCallbacks")]
public abstract class GhostSingleton : GhostMonoBehaviour
{
#pragma warning disable UDR0001
    protected static Action OnResetStaticState; // This is cleared from InvokeAndClearStaticCallbacks
#pragma warning restore UDR0001

    public static void InvokeAndClearStaticCallbacks()
    {
        OnResetStaticState?.Invoke();
        OnResetStaticState = null;
    }
}

[ResetOnPlayMode(resetMethod: "ResetStaticState")]
public abstract class GhostSingleton<T> : GhostSingleton
    where T : GhostMonoBehaviour
{
    public delegate void OnInitialiseCallback(T singleton);

#pragma warning disable UDR0001
    // These are reset from ResetOnPlayMode attribute
    private static Dictionary<MultiplayerRole, T> s_InstanceLookup = new();
    private static Dictionary<MultiplayerRole, OnInitialiseCallback> s_InitialiseLookup = new();
#pragma warning restore UDR0001

    protected static void ResetStaticState()
    {
        s_InitialiseLookup.Clear();
        s_InstanceLookup.Clear();
    }

    public static T ClientInstance => s_InstanceLookup.ContainsKey(MultiplayerRole.ClientProxy) ? s_InstanceLookup[MultiplayerRole.ClientProxy] : null;

    public static T ServerInstance => s_InstanceLookup.ContainsKey(MultiplayerRole.Server) ? s_InstanceLookup[MultiplayerRole.Server] : null;

    public static bool TryGetClientInstance(out T clientInstance)
    {
        if (s_InstanceLookup.ContainsKey(MultiplayerRole.ClientProxy))
        {
            clientInstance = s_InstanceLookup[MultiplayerRole.ClientProxy];
            return true;
        }

        clientInstance = null;
        return false;
    }

    public static bool TryGetServerInstance(out T serverInstance)
    {
        if (s_InstanceLookup.ContainsKey(MultiplayerRole.Server))
        {
            serverInstance = s_InstanceLookup[MultiplayerRole.Server];
            return true;
        }

        serverInstance = null;
        return false;
    }

    public static T GetInstanceByRole(MultiplayerRole role)
    {
        //all clients should be obtaining the same client instance regardless of if they are local or remote
        if (role != MultiplayerRole.Server)
        {
            role = MultiplayerRole.ClientProxy;
        }

        return s_InstanceLookup.ContainsKey(role) ? s_InstanceLookup[role] : null;
    }

    public static bool TryGetInstanceByRole(MultiplayerRole role, out T instance)
    {
        //all clients should be obtaining the same client instance regardless of if they are local or remote
        if (role != MultiplayerRole.Server)
        {
            role = MultiplayerRole.ClientProxy;
        }

        if (s_InstanceLookup.ContainsKey(role))
        {
            instance = s_InstanceLookup[role];
            return true;
        }

        instance = null;
        return false;
    }

    public static bool TryGetInstanceFromWorld(World world, out T instance)
    {
        foreach (var lookupInstance in s_InstanceLookup)
        {
            if (lookupInstance.Value.World == world)
            {
                instance = lookupInstance.Value;
                return true;
            }
        }

        instance = null;
        return false;
    }

    public static void OnInitialise(MultiplayerRole role, OnInitialiseCallback callback)
    {
        if (TryGetInstanceByRole(role, out var instance))
        {
            callback(instance);
        }
        else
        {
            if (s_InitialiseLookup.TryGetValue(role, out var existingCallback))
            {
                callback = existingCallback + callback;
            }
            s_InitialiseLookup[role] = callback;
#pragma warning disable UDR0001
            OnResetStaticState += ResetStaticState; // This is reset from InvokeAndClearStaticCallbacks
#pragma warning restore UDR0001
        }
    }

    public override void OnGhostLinked()
    {
        Debug.Assert(Role == MultiplayerRole.Server || Role == MultiplayerRole.ClientProxy, "GhostSingletons are only valid for server-owned ghosts");

        if (s_InstanceLookup.ContainsKey(Role))
        {
            Debug.LogError($"[GhostSingleton::OnGhostLinked] {typeof(T).ToString()} GhostSingleton instance already exists for role {Role}");
        }

        s_InstanceLookup.Add(Role, this as T);

        if (s_InitialiseLookup.TryGetValue(Role, out var callback))
        {
            callback(this as T);

            s_InitialiseLookup.Remove(Role);
        }
    }

    public override void OnGhostPreDestroy()
    {
        // Determine the correct key for the dictionary. All client types (Owned, Proxy)
        // should use the same key to represent the single client-side instance.
        var roleToUnregister = Role;
        if (roleToUnregister != MultiplayerRole.Server)
        {
            roleToUnregister = MultiplayerRole.ClientProxy;
        }

        // This is the instance-specific cleanup. It only removes itself.
        if (s_InstanceLookup.ContainsKey(roleToUnregister) && s_InstanceLookup[roleToUnregister] == this)
        {
            s_InstanceLookup.Remove(roleToUnregister);
        }
    
        // Mark as called to prevent OnDestroy from running this logic a second time.
        if (GhostGameObject != null)
        {
            GhostGameObject.GhostPreDestroyCalled = true;
        }
    }

    protected virtual void OnDestroy()
    {
        // This ensures that if the object is destroyed abruptly (like during world teardown),
        // it still cleans up its own entry from the static dictionary.
        if (GhostGameObject == null || !GhostGameObject.GhostPreDestroyCalled)
        {
            OnGhostPreDestroy();
        }
    }
}
