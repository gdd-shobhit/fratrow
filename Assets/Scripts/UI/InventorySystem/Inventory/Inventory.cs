using System.Collections.Generic;
using Systems.Persistence;
using UnityEngine;

namespace Systems.Inventory {
    public class Inventory : MonoBehaviour, IBind<InventoryData> {
        [SerializeField] InventoryView view;
        [SerializeField] int capacity = 20;
        [SerializeField] List<ItemDetails> startingItems = new List<ItemDetails>();
        [field: SerializeField] public SerializableGuid Id { get; set; } = SerializableGuid.NewGuid();

        InventoryController controller;

        void Awake() {
            controller = new InventoryController.Builder(view)
                .WithStartingItems(startingItems)
                .WithCapacity(capacity)
                .Build();
        }
         
        public void Bind(InventoryData data) {
            controller.Bind(data);
            data.Id = Id;
        }

        /// <summary>Add items (e.g. after a shop purchase). Uses stacking rules from <see cref="InventoryModel"/>.</summary>
        public bool TryAddItem(ItemDetails details, int quantity = 1) {
            return controller != null && controller.TryAddItem(details, quantity);
        }

        public bool TryRemoveOne(ItemDetails details) {
            return controller != null && controller.TryRemoveOne(details);
        }
    }
}