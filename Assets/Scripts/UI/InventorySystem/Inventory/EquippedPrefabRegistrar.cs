using Unity.Netcode;
using UnityEngine;

namespace Systems.Inventory {
  public static class EquippedPrefabRegistrar {
    public static void RegisterAllEquippedPrefabs() {
      var itemDetails = Resources.LoadAll<ItemDetails>(ItemDatabase.ItemDetailsResourcesPath);
      foreach (var details in itemDetails) {
        if (details == null) {
          continue;
        }

        RegisterEquippedPrefab(details.EquippedPrefab, details.name);
      }
    }

    static void RegisterEquippedPrefab(GameObject prefab, string itemName) {
      if (prefab == null) {
        return;
      }

      if (prefab.GetComponent<NetworkObject>() == null) {
        Debug.LogWarning($"[Equip] {itemName} equippedPrefab missing NetworkObject — fix in editor.");
        return;
      }

      var networkManager = NetworkManager.Singleton;
      if (networkManager == null) {
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
