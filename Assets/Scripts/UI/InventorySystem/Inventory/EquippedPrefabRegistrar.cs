using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Systems.Inventory {
  public static class EquippedPrefabRegistrar {
    public static void RegisterAllEquippedPrefabs() {
      var networkManager = NetworkManager.Singleton;
      if (networkManager == null) {
        return;
      }

      var seen = new HashSet<GameObject>();
      var itemDetails = Resources.LoadAll<ItemDetails>(ItemDatabase.ItemDetailsResourcesPath);
      foreach (var details in itemDetails) {
        if (details == null) {
          continue;
        }

        RegisterEquippedPrefab(networkManager, details.EquippedPrefab, details.name, seen);
      }
    }

    static void RegisterEquippedPrefab(
      NetworkManager networkManager,
      GameObject prefab,
      string itemName,
      HashSet<GameObject> seen) {
      if (prefab == null) {
        return;
      }

      // Same prefab can be referenced by multiple ItemDetails — only register once.
      if (!seen.Add(prefab)) {
        return;
      }

      if (prefab.GetComponent<NetworkObject>() == null) {
        Debug.LogWarning($"[Equip] {itemName} equippedPrefab missing NetworkObject — fix in editor.");
        return;
      }

      // Already in DefaultNetworkPrefabs (or registered earlier this session).
      if (networkManager.NetworkConfig.Prefabs.Contains(prefab)) {
        return;
      }

      try {
        networkManager.AddNetworkPrefab(prefab);
      }
      catch (System.Exception e) {
        Debug.LogWarning($"[Equip] Could not register '{prefab.name}': {e.Message}");
      }
    }
  }
}
