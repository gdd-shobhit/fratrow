using UnityEditor;
using UnityEngine;

namespace Systems.Inventory {
    [CustomEditor(typeof(ItemDetails))]
    public class ItemDetailsEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            EditorGUILayout.HelpBox(
                "Equipped prefab must include NetworkObject and NetworkTransform on the prefab root, and be registered in Default Network Prefabs.",
                MessageType.Info);

            DrawDefaultInspector();

            var details = (ItemDetails)target;
            if (details.EquippedPrefab == null) {
                return;
            }

            var prefab = details.EquippedPrefab;
            bool hasNetworkObject = prefab.GetComponent<Unity.Netcode.NetworkObject>() != null;
            bool hasNetworkTransform = prefab.GetComponent<Unity.Netcode.Components.NetworkTransform>() != null;

            if (!hasNetworkObject) {
                EditorGUILayout.HelpBox("Missing NetworkObject on equipped prefab root.", MessageType.Error);
            }

            if (!hasNetworkTransform) {
                EditorGUILayout.HelpBox("Missing NetworkTransform on equipped prefab root.", MessageType.Warning);
            }

            EquippedPrefabValidator.ValidateInEditor(prefab, details);
        }
    }
}
