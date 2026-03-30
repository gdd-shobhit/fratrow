using System;
using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory {
    public class InventoryModel { 
        ObservableArray<Item> Items { get; }
        InventoryData inventoryData = new InventoryData();
        readonly int capacity;

        public int Coins {
            get => inventoryData.Coins;
            set => inventoryData.Coins = value;
        }

        public event Action<Item[]> OnModelChanged {
            add => Items.AnyValueChanged += value;
            remove => Items.AnyValueChanged -= value;
        }
        
        public InventoryModel(IEnumerable<ItemDetails> itemDetails, int capacity) {
            this.capacity = capacity;
            Items = new ObservableArray<Item>(capacity);
            foreach (var itemDetail in itemDetails) {
                Items.TryAdd(itemDetail.Create(1));
            }
        }

        public void Bind(InventoryData data) {
            inventoryData = data;
            inventoryData.Capacity = capacity;
            
            bool isNew = inventoryData.Items == null || inventoryData.Items.Length == 0;

            if (isNew) {
                inventoryData.Items = new Item[capacity];
            }
            else {
                for (var i = 0; i < capacity; i++) {
                    if (Items[i] == null) continue;
                    inventoryData.Items[i].details = ItemDatabase.GetDetailsById(Items[i].detailsId);
                }
            }

            if (isNew && Items.Count != 0) {
                for (var i = 0; i < capacity; i++) {
                    if (Items[i] == null) continue;
                    inventoryData.Items[i] = Items[i];
                }
            }
            
            Items.items = inventoryData.Items;
        }
        
        public void AddCoins(int amount) => Coins += amount;
        
        public Item Get(int index) => Items[index];
        public void Clear() => Items.Clear();
        public bool Add(Item item) => Items.TryAdd(item);
        public bool Remove(Item item) => Items.TryRemove(item);
        
        public void Swap(int source, int target) => Items.Swap(source, target);
        
        public int Combine(int source, int target) {
            var total = Items[source].quantity + Items[target].quantity;
            Items[target].quantity = total;
            Remove(Items[source]);
            return total;
        }

        /// <summary>Adds quantity of an item, stacking when possible. Returns false if nothing could fit.</summary>
        public bool TryAddItem(ItemDetails details, int quantity) {
            if (details == null || quantity <= 0) {
                return false;
            }
            for (var n = 0; n < quantity; n++) {
                if (!TryAddSingle(details)) {
                    return false;
                }
            }
            return true;
        }

        bool TryAddSingle(ItemDetails details) {
            for (var i = 0; i < capacity; i++) {
                var slot = Items[i];
                if (slot == null) {
                    continue;
                }
                if (!slot.detailsId.Equals(details.Id)) {
                    continue;
                }
                if (details.maxStack <= 1) {
                    continue;
                }
                if (slot.quantity >= details.maxStack) {
                    continue;
                }
                slot.quantity++;
                Items.NotifyChanged();
                return true;
            }
            return Items.TryAdd(details.Create(1));
        }

        /// <summary>Removes one of an item (un-stacks or clears a slot). Used to roll back a failed purchase after TryAddItem.</summary>
        public bool TryRemoveOne(ItemDetails details) {
            if (details == null) {
                return false;
            }
            for (var i = 0; i < capacity; i++) {
                var slot = Items[i];
                if (slot == null || !slot.detailsId.Equals(details.Id)) {
                    continue;
                }
                if (slot.quantity > 1) {
                    slot.quantity--;
                    Items.NotifyChanged();
                    return true;
                }
                return Items.TryRemoveAt(i);
            }
            return false;
        }
    }
}