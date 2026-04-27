using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory {
    public static class ItemDatabase {
        /// <summary>
        /// Path passed to <see cref="Resources.LoadAll{T}(string)"/> — relative to a <c>Resources</c> folder (omit the folder name).
        /// Loads all <see cref="ItemDetails"/> under <c>.../Resources/ItemDetails/</c> including subfolders (<c>Alcohol</c>, <c>Mixers</c>, <c>Fruits</c>, <c>Liqueurs</c>, etc.).
        /// </summary>
        public const string ItemDetailsResourcesPath = "ItemDetails";

        static Dictionary<SerializableGuid, ItemDetails> itemDetailsDictionary;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Initialize() {
            itemDetailsDictionary = new Dictionary<SerializableGuid, ItemDetails>();

            var itemDetails = Resources.LoadAll<ItemDetails>(ItemDetailsResourcesPath);
            foreach (var item in itemDetails) {
                if (item == null) {
                    continue;
                }
                if (!itemDetailsDictionary.TryAdd(item.Id, item)) {
                    Debug.LogError(
                        $"[ItemDatabase] Duplicate ItemDetails Id {item.Id.ToHexString()} on '{item.name}'. Keeping first entry.");
                }
            }
        }

        public static ItemDetails GetDetailsById(SerializableGuid id) {
            try {
                return itemDetailsDictionary[id];
            } catch {
                Debug.LogError($"Cannot find item details with id {id}");
                return null;
            }
        }
    }
}