using System.Collections.Generic;
using System.Linq;
using FratRow.Characters;
using UnityEditor;
using UnityEngine;

namespace FratRow.Art.Characters.Editor
{
    /// <summary>
    /// Recipe inspector: every body slider of the base body as a named 0–1 slider, plus
    /// height, skin, items and colours, and the face style. Edits save to the recipe
    /// file and re-dress the characters using it (CharacterRecipeSync).
    /// </summary>
    [CustomEditor(typeof(CharacterRecipe))]
    public class CharacterRecipeEditor : UnityEditor.Editor
    {
        static readonly Dictionary<string, string[]> SliderNamesByModel = new Dictionary<string, string[]>();

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            var recipe = (CharacterRecipe)target;
            string file = CharacterRecipeSync.JsonPathFor(recipe);
            EditorGUILayout.HelpBox(file != null
                ? $"Edits save to {file} and update every character wearing this recipe."
                : "Not linked to a recipe file (<id>.asset next to <id>.recipe.json): edits stay in this asset. " +
                  "Use Tools > FratRow > Characters > New Character From Selected Recipe to make linked recipes.",
                MessageType.None);

            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Id", recipe.id);
                EditorGUILayout.TextField("Base Model", recipe.baseModel);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("height"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("skinColor"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Body sliders", EditorStyles.boldLabel);
            foreach (string slider in SliderNames(recipe))
            {
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.Slider(ObjectNames.NicifyVariableName(slider), recipe.Slider(slider), 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(recipe, $"{slider} slider");
                    recipe.SetSlider(slider, value);
                    EditorUtility.SetDirty(recipe);
                    CharacterRecipeSync.MarkEdited(recipe);
                }
            }

            serializedObject.Update();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clothes", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("items"), true);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Face", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("faceTexture"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("eyeTexture"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("face"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("iris"));
            EditorGUILayout.HelpBox("Face style, iris and skin colour are painted into the textures by Blender: " +
                                    "after changing them run  from charkit import tasks; tasks.paint_faces()  " +
                                    "(or /charkit-character).", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Blend shape names of the recipe's base body (the recipe's own slider names if the model is missing).</summary>
        static IEnumerable<string> SliderNames(CharacterRecipe recipe)
        {
            string key = recipe.baseModel ?? "";
            if (!SliderNamesByModel.TryGetValue(key, out string[] names) || names.Length == 0)
            {
                names = new string[0];
                string path = CharacterRecipeImporter.FindModel(recipe.baseModel);
                var model = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                Mesh body = model != null
                    ? model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name.Contains("_Body")).Select(r => r.sharedMesh).FirstOrDefault()
                    : null;
                if (body != null)
                    names = Enumerable.Range(0, body.blendShapeCount).Select(body.GetBlendShapeName).ToArray();
                SliderNamesByModel[key] = names;
            }
            return names.Length > 0 ? names : recipe.sliders.Select(s => s.name);
        }
    }

    /// <summary>
    /// Character inspector: setup fields, Apply/Clear, and, when a recipe is assigned,
    /// the recipe's look edited in place (so tuning a character edits its recipe file).
    /// </summary>
    [CustomEditor(typeof(CharacterAppearance))]
    public class CharacterAppearanceEditor : UnityEditor.Editor
    {
        static readonly string[] Setup = { "body", "eyes", "skinMaterial", "applyOnStart", "previewInEditor" };
        static readonly string[] Look = { "skinColor", "height", "faceTexture", "eyeTexture", "sliders", "items" };
        UnityEditor.Editor recipeEditor;

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            var appearance = (CharacterAppearance)target;
            serializedObject.Update();
            SerializedProperty recipe = serializedObject.FindProperty("recipe");
            EditorGUILayout.PropertyField(recipe);
            foreach (string field in Setup)
                EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
            bool hasRecipe = recipe.objectReferenceValue != null;
            if (!hasRecipe)
                foreach (string field in Look)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(field), true);
            if (serializedObject.ApplyModifiedProperties() && !Application.isPlaying)
            {
                if (appearance.PreviewInEditor)
                    appearance.Refresh();
                else
                    appearance.Clear();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply"))
                    appearance.Refresh();
                if (GUILayout.Button("Clear"))
                    appearance.Clear();
            }

            if (!hasRecipe)
                return;
            var asset = (CharacterRecipe)recipe.objectReferenceValue;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Look — {asset.displayName}", EditorStyles.boldLabel);
            CreateCachedEditor(asset, typeof(CharacterRecipeEditor), ref recipeEditor);
            recipeEditor.OnInspectorGUI();
        }

        void OnDisable()
        {
            if (recipeEditor != null)
                DestroyImmediate(recipeEditor);
        }
    }
}
