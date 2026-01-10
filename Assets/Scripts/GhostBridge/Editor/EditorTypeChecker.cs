using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class EditorTypeChecker
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void CheckTypes()
    {
        var types = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => a.FullName.StartsWith("Assembly-CSharp"))
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsInterface && typeof(IGhostGameObjectDirectedRPC).IsAssignableFrom(t));

        foreach (var type in types)
        {
            if (type.GetField("TargetGuid") == null)
            {
                Debug.LogError($"Type '{type.Name} implements IGhostGameObjectDirectedRPC but does not have a TargetGuid field.");
            }
        }

        // check all ghost types
        var ghostTypes = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => a.FullName.StartsWith("Assembly-CSharp"))
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsInterface && typeof(GhostMonoBehaviour).IsAssignableFrom(t));

        foreach (var ghost in ghostTypes)
        {
            var methods = ghost.GetMethods(BindingFlags.Instance | BindingFlags.Public);
            foreach (var method in methods)
            {
                if (method.Name == "UpdateClient" && !typeof(IUpdateClient).IsAssignableFrom(ghost))
                {
                    Debug.LogError($"{ghost.Name} has UpdateClient but doesn't implement IUpdateClient");
                }
                else if (method.Name == "UpdateServer" && !typeof(IUpdateServer).IsAssignableFrom(ghost))
                {
                    Debug.LogError($"{ghost.Name} has UpdateServer but doesn't implement IUpdateServer");
                }
            }
        }
    }
}
