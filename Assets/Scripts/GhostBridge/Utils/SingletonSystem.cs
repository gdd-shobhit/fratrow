using Unity.Entities;
using UnityEngine;

[ResetOnPlayMode(resetMethod: "ResetStaticState")]
public abstract partial class SingletonSystem<T> : SystemBase
    where T : SystemBase
{
    public static T Instance { get; private set; }

    protected static void ResetStaticState()
    {
        Instance = null;
    }

    public static bool TryGetInstance(out T instance)
    {
        instance = Instance;
        return Instance != null;
    }

    protected override void OnCreate()
    {
        Debug.Assert(Instance == null, $"[SingletonSystem::OnCreate] {typeof(T)} instance already exists");

        Instance = this as T;
    }

    protected override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}