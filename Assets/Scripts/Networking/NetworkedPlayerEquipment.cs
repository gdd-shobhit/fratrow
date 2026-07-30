using Systems.Inventory;
using Unity.Netcode;
using UnityEngine;

public class NetworkedPlayerEquipment : NetworkBehaviour {
    const string EquipSlotName = "EquipSlot";

    [SerializeField] Transform equipSlot;

    NetworkObject _spawnedEquippedObject;
    SerializableGuid _equippedItemId = SerializableGuid.Empty;

    public SerializableGuid EquippedItemId => _equippedItemId;

    void Awake() {
        EnsureEquipSlot();
    }

    void EnsureEquipSlot() {
        if (equipSlot != null) {
            return;
        }

        var existing = transform.Find(EquipSlotName);
        if (existing != null) {
            equipSlot = existing;
            return;
        }

        var slotObject = new GameObject(EquipSlotName);
        equipSlot = slotObject.transform;
        equipSlot.SetParent(transform, false);
        equipSlot.localPosition = new Vector3(0.35f, 1.1f, 0.45f);
        equipSlot.localRotation = Quaternion.identity;
    }

    public override void OnNetworkDespawn() {
        DespawnEquipped();
        base.OnNetworkDespawn();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void RequestEquipServerRpc(SerializableGuid itemDetailsId) {
        if (itemDetailsId == SerializableGuid.Empty) {
            DespawnEquipped();
            return;
        }

        if (itemDetailsId == _equippedItemId && _spawnedEquippedObject != null && _spawnedEquippedObject.IsSpawned) {
            return;
        }

        DespawnEquipped(logUnequip: false);

        var details = ItemDatabase.GetDetailsById(itemDetailsId);
        if (details?.EquippedPrefab == null) {
            _equippedItemId = itemDetailsId;
            Debug.Log($"[Equip] {gameObject.name} equipped {details?.Name ?? "unknown"} (no visual)");
            return;
        }

        var prefab = details.EquippedPrefab;
        var instance = Instantiate(prefab, equipSlot.position, equipSlot.rotation);
        EquippedPrefabValidator.EnsureComponentsForSpawn(instance, prefab.name);

        var netObj = instance.GetComponent<NetworkObject>();
        if (netObj == null) {
            Destroy(instance);
            Debug.LogError($"[NetworkedPlayerEquipment] Failed to spawn equipped visual for '{prefab.name}'.");
            return;
        }

        netObj.Spawn();

        // NGO only allows parenting under another NetworkObject, so parent to the
        // player root and apply the equip slot offset as a local transform.
        if (!netObj.TrySetParent(NetworkObject, false)) {
            Debug.LogError(
                $"[NetworkedPlayerEquipment] Could not parent '{prefab.name}' under the player. " +
                "Check that the equipped prefab has AutoObjectParentSync enabled on its NetworkObject.");
            netObj.Despawn(true);
            return;
        }

        instance.transform.localPosition = equipSlot.localPosition;
        instance.transform.localRotation = equipSlot.localRotation;

        _spawnedEquippedObject = netObj;
        _equippedItemId = itemDetailsId;
        Debug.Log($"[Equip] {gameObject.name} equipped {details.Name}");
    }

    void DespawnEquipped(bool logUnequip = true) {
        if (logUnequip && _equippedItemId != SerializableGuid.Empty) {
            var details = ItemDatabase.GetDetailsById(_equippedItemId);
            var itemName = details != null ? details.Name : _equippedItemId.ToHexString();
            Debug.Log($"[Equip] {gameObject.name} unequipped {itemName}");
        }

        if (_spawnedEquippedObject != null && _spawnedEquippedObject.IsSpawned) {
            _spawnedEquippedObject.Despawn(true);
        }

        _spawnedEquippedObject = null;
        _equippedItemId = SerializableGuid.Empty;
    }
}
