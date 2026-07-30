using Unity.Netcode;
using UnityEngine;

namespace Systems.Inventory {
    public class ItemEquipper : MonoBehaviour {
        [SerializeField] Inventory inventory;
        [SerializeField] InventoryView inventoryView;
        [SerializeField] NetworkedPlayerEquipment networkedEquipment;

        NetworkObject _networkObject;
        int _equippedSlotIndex = -1;

        void Awake() {
            _networkObject = GetComponent<NetworkObject>();

            if (inventory == null) {
                inventory = GetComponentInChildren<Inventory>();
            }

            if (inventoryView == null) {
                inventoryView = GetComponentInChildren<InventoryView>();
            }

            if (networkedEquipment == null) {
                networkedEquipment = GetComponent<NetworkedPlayerEquipment>();
            }
        }

        void OnEnable() {
            if (inventory != null) {
                inventory.OnChanged += HandleInventoryChanged;
            }
        }

        void OnDisable() {
            if (inventory != null) {
                inventory.OnChanged -= HandleInventoryChanged;
            }
        }

        public void EquipSlot(int slotIndex) {
            if (_networkObject != null && !_networkObject.IsOwner) {
                return;
            }

            if (inventory == null || networkedEquipment == null) {
                return;
            }

            if (slotIndex < 0 || slotIndex >= inventory.Capacity) {
                return;
            }

            if (slotIndex == _equippedSlotIndex) {
                Unequip();
                return;
            }

            if (inventory.IsSlotEmpty(slotIndex)) {
                Unequip();
                return;
            }

            var item = inventory.GetItemAt(slotIndex);
            if (item?.details == null) {
                Unequip();
                return;
            }

            networkedEquipment.RequestEquipServerRpc(item.details.Id);
            _equippedSlotIndex = slotIndex;
            inventoryView?.SetEquippedSlotIndex(slotIndex);
        }

        void Unequip() {
            networkedEquipment?.RequestEquipServerRpc(SerializableGuid.Empty);
            _equippedSlotIndex = -1;
            inventoryView?.SetEquippedSlotIndex(-1);
        }

        void HandleInventoryChanged() {
            if (_equippedSlotIndex < 0) {
                return;
            }

            if (inventory.IsSlotEmpty(_equippedSlotIndex)) {
                Unequip();
                return;
            }

            var item = inventory.GetItemAt(_equippedSlotIndex);
            if (item?.details == null) {
                Unequip();
            }
        }
    }
}
