using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class ResetOnPlayModeAttribute : Attribute
{
    /// <summary>
    /// Method name to be called on Domain Reload, must be static
    /// </summary>
    public string ResetMethodName { get; private set; }

    public ResetOnPlayModeAttribute(string resetMethod)
    {
        ResetMethodName = resetMethod;
    }
}

#if UNITY_EDITOR
public static class DomainReloadGuard
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnEnterRuntime()
    {
        if (!EditorSettings.enterPlayModeOptionsEnabled ||
            !EditorSettings.enterPlayModeOptions.HasFlag(EnterPlayModeOptions.DisableDomainReload))
        {
            return;
        }

        ResetAllStaticState();
    }

    public static void ResetAllStaticState()
    {
        var classes = FindClassesNeedingReset();
        var resetCount = 0;
        foreach (var t in classes)
        {
            var attrs = t.GetCustomAttributes<ResetOnPlayModeAttribute>();
            foreach (var a in attrs)
            {
                var method = t.GetMethod(a.ResetMethodName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                if (method != null)
                {
                    //Debug.Log($"Reset class state: {t.Name}.{a.ResetMethodName}");
                    method?.Invoke(null, null);
                }
                else
                {
                    Debug.LogWarning(
                        $"Couldn't find reset method '{a.ResetMethodName}' for class: {t.Name}. Maybe it's not static?");
                }
            }

            resetCount++;
        }

        Debug.Log($"No Domain Reload: {resetCount} class(es) reset");
    }

    private static IEnumerable<Type> FindClassesNeedingReset()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            // unfortunately this is the only "adequate" way to filter lots of out-of-project assemblies (that get loaded via references)
            .Where(a => a.FullName.StartsWith("Assembly-CSharp"))
            .SelectMany(a => a.GetTypes())
            .Where(t =>
                !t.ContainsGenericParameters
                && (!t.IsAbstract || IsStaticClass(t))
                && t.GetCustomAttributes<ResetOnPlayModeAttribute>().Any());
    }

    private static bool IsStaticClass(Type t) => t.IsClass && t.IsSealed && t.IsAbstract;
}
#endif
