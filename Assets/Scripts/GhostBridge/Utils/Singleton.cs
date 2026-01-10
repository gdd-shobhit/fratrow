using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[ResetOnPlayMode(resetMethod: "ResetStaticState")]
public abstract class Singleton<T> : MonoBehaviour
    where T : MonoBehaviour
{
    public delegate void OnInitialiseCallback(T singleton);

#pragma warning disable UDR0001
    // Reset by ResetStaticState method
    private static T s_Instance;
    private static List<OnInitialiseCallback> s_InitialisationCallbacks = new();
#pragma warning restore UDR0001

    public static bool TryGetInstance(out T instance)
    {
        instance = s_Instance;
        return instance != null;
    }

    protected virtual bool Persistent => false;

    protected static void ResetStaticState()
    {
        s_Instance = null;
        s_InitialisationCallbacks?.Clear();
    }

    public static T Instance => s_Instance;

    public virtual void Awake()
    {
        if (s_Instance != null)
        {
            Debug.LogError($"[Singleton::Awake] {typeof(T).ToString()} instance already exists");
        }

        s_Instance = this as T;

        foreach (var callback in s_InitialisationCallbacks)
        {
            callback(s_Instance);
        }
        s_InitialisationCallbacks.Clear();

        if (Persistent)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    public virtual void OnDestroy()
    {
        if (s_Instance == this)
        {
            s_Instance = null;
            s_InitialisationCallbacks.Clear();
        }
    }

    public static void OnInitialise(OnInitialiseCallback callback)
    {
        if (s_Instance != null)
        {
            callback(s_Instance);
        }
        else
        {
            s_InitialisationCallbacks.Add(callback);
        }
    }
}

public abstract class AutoSingleton<T> : Singleton<T>
    where T : MonoBehaviour
{
    protected override bool Persistent => true;

    public static new T Instance
    {
        get
        {
            if (!TryGetInstance(out var instance)

#if UNITY_EDITOR
                && EditorApplication.isPlayingOrWillChangePlaymode //don't allow instantiation in edit mode
#endif
               )
            {
                Debug.Log($"Creating new instance of type {typeof(T).Name}");
                GameObject singleton = new GameObject();
                var newInstance = singleton.AddComponent<T>();
                singleton.name = typeof(T).ToString();
                return newInstance;
            }
            else
            {
                return instance;
            }
        }
    }
}