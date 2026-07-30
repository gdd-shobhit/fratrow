using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Inventory {
    public class InventoryView : StorageView {
        bool _editModeActive;
        int _equippedSlotIndex = -1;

        public bool IsEditModeActive => _editModeActive;

        public void ToggleEditMode() {
            _editModeActive = !_editModeActive;
            if (_editModeActive) {
                container.AddToClassList("edit-mode");
            } else {
                container.RemoveFromClassList("edit-mode");
            }
        }

        public void SetEquippedSlotIndex(int slotIndex) {
            if (Slots == null) {
                _equippedSlotIndex = slotIndex;
                return;
            }

            if (_equippedSlotIndex >= 0 && _equippedSlotIndex < Slots.Length) {
                Slots[_equippedSlotIndex].RemoveFromClassList("equipped");
            }

            _equippedSlotIndex = slotIndex;

            if (slotIndex >= 0 && slotIndex < Slots.Length) {
                Slots[slotIndex].AddToClassList("equipped");
            }
        }

        public override IEnumerator InitializeView(ViewModel viewModel) {
            Slots = new Slot[viewModel.Capacity];
            root = document.rootVisualElement;
            root.Clear();

            root.styleSheets.Add(styleSheet);

            container = root.CreateChild("container");

            var inventory = container.CreateChild("inventory");
            var slotsContainer = inventory.CreateChild("slotsContainer");
            for (int i = 0; i < viewModel.Capacity; i++) {
                var slot = slotsContainer.CreateChild<Slot>("slot");
                Slots[i] = slot;
            }

            ghostIcon = container.CreateChild("ghostIcon");

            container.style.display = DisplayStyle.Flex;

            yield return null;
        }
    }
}
