using System;
using UnityEngine;

namespace Systems.Inventory {
    [CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item")]
    [Serializable]
    public class ItemDetails : ScriptableObject {

        [SerializeField]
        public string Name;

        [SerializeField]
        public int maxStack = 1;

        [SerializeField]
        public SerializableGuid Id = SerializableGuid.NewGuid();

        [SerializeField]
        public Sprite Icon;

        [SerializeField]
        public string Description;

        [Tooltip("Shown in shop when set; otherwise Name is used.")]
        [SerializeField]
        string displayName;

        [SerializeField]
        ItemType itemType = ItemType.Misc;

        [SerializeField]
        ItemRarity shopRarity = ItemRarity.Common;

        [SerializeField]
        int shopCost;

        [Tooltip("Initial stock per stack row when this asset is listed in a shop catalog (server).")]
        [SerializeField]
        int startingStock = 99;

        [SerializeField]
        bool isPlaceableInWorld;

        [Tooltip("World/held visual. Required: NetworkObject + NetworkTransform. Register in Default Network Prefabs.")]
        [SerializeField]
        GameObject equippedPrefab;

        public string DisplayName =>
            string.IsNullOrEmpty(displayName) ? Name : displayName;

        public ItemType Type => itemType;

        public ItemRarity Rarity => shopRarity;
        public int ShopCost => shopCost;
        public int StartingStock => Mathf.Max(0, startingStock);
        public bool IsPlaceableInWorld => isPlaceableInWorld;
        public GameObject EquippedPrefab => equippedPrefab;

        public Item Create(int quantity) {
            return new Item(this, quantity);
        }

#if UNITY_EDITOR
        void OnValidate() {
            EquippedPrefabValidator.ValidateInEditor(equippedPrefab, this);
        }
#endif
    }
}
