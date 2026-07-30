using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Systems.Inventory {
    public static class EquippedPrefabValidator {
        public static void EnsureComponentsForSpawn(GameObject instance, string prefabName) {
            if (instance == null) {
                return;
            }

            if (instance.GetComponent<NetworkObject>() == null) {
                instance.AddComponent<NetworkObject>();
                Debug.LogWarning(
                    $"[Equip] '{prefabName}' was missing NetworkObject. Added at runtime — add it to the prefab asset and register in Default Network Prefabs.");
            }

            if (instance.GetComponent<NetworkTransform>() == null) {
                instance.AddComponent<NetworkTransform>();
                Debug.LogWarning(
                    $"[Equip] '{prefabName}' was missing NetworkTransform. Added at runtime — add it to the prefab asset in the editor.");
            }
        }

#if UNITY_EDITOR
        public static void ValidateInEditor(GameObject prefab, Object context) {
            if (prefab == null) {
                return;
            }

            if (prefab.GetComponent<NetworkObject>() == null) {
                Debug.LogWarning(
                    $"[ItemDetails] '{prefab.name}' equippedPrefab is missing NetworkObject on the prefab root.",
                    context);
            }

            if (prefab.GetComponent<NetworkTransform>() == null) {
                Debug.LogWarning(
                    $"[ItemDetails] '{prefab.name}' equippedPrefab is missing NetworkTransform on the prefab root.",
                    context);
            }

            if (!IsRegisteredInNetworkPrefabs(prefab)) {
                Debug.LogWarning(
                    $"[ItemDetails] '{prefab.name}' is not in Default Network Prefabs. Add it for multiplayer equip visuals.",
                    context);
            }
        }

        static bool IsRegisteredInNetworkPrefabs(GameObject prefab) {
            var lists = Resources.FindObjectsOfTypeAll<NetworkPrefabsList>();
            foreach (var list in lists) {
                if (list != null && list.Contains(prefab)) {
                    return true;
                }
            }

            return false;
        }
#endif
    }
}
