using UnityEditor;
using UnityEngine;

namespace Systems.Shop.Editor {
    [CustomEditor(typeof(ShopOwner))]
    public class ShopOwnerEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            var owner = (ShopOwner)target;

            using (new EditorGUI.DisabledScope(!Application.isPlaying)) {
                if (GUILayout.Button("Open Shop")) {
                    owner.Interact();
                }
            }

            if (!Application.isPlaying) {
                EditorGUILayout.HelpBox("Enter Play Mode, then click Open Shop to show the UI without using in-game interact.", MessageType.Info);
            }
        }
    }
}
