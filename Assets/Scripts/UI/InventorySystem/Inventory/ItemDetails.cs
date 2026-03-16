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

        private void AssignNewGuid() {
            Id = SerializableGuid.NewGuid();
        }

        [SerializeField]
        public Sprite Icon;

        [SerializeField]
        public string Description;
        
        public Item Create(int quantity) {
            return new Item(this, quantity);
        }
    }
}