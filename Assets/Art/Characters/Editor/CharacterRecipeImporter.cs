using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FratRow.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FratRow.Art.Characters.Editor
{
    /// <summary>
    /// Turns charkit recipe files (&lt;id&gt;.recipe.json, written by hand or with
    /// tools/blender/charkit/recipes.py; design/art/character-pipeline.md) into a
    /// CharacterRecipe asset next to the file, and a character prefab in the
    /// Prefabs folder beside Recipes: a variant of the base model whose
    /// CharacterAppearance loads the recipe on Start.
    ///
    /// Items are found by CosmeticItem id, textures and the base model by file name,
    /// anywhere under Assets/Art/Characters. Colours are sRGB hex (#RRGGBB). The
    /// recipe's "face" and "iris" blocks are Blender painting parameters and are
    /// ignored here. Recipe assets update on import; prefabs are rebuilt right after
    /// (delayed, outside the import) or with Tools/FratRow/Characters/Rebuild Recipes.
    /// </summary>
    public static class CharacterRecipeImporter
    {
        /// <summary>File suffix of recipe files.</summary>
        public const string RecipeSuffix = ".recipe.json";

        const int SupportedSchema = 1;

        [Serializable]
        class RecipeData
        {
            public int schema;
            public string id;
            public string displayName;
            public string baseModel;
            public float height = 1f;
            public string skin;
            public SliderData[] sliders;
            public string faceTexture;
            public string eyeTexture;
            public ItemData[] items;
        }

        [Serializable]
        class SliderData
        {
            public string name;
            public float value;
        }

        [Serializable]
        class ItemData
        {
            public string id;
            public string[] colors;
        }

        /// <summary>Every recipe file under Assets/Art/Characters.</summary>
        public static string[] RecipePaths() =>
            Directory.GetFiles(CharacterModelImporter.Root, "*" + RecipeSuffix, SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        /// <summary>Imports every recipe and rebuilds its prefab. Returns the prefab paths built.</summary>
        [MenuItem("Tools/FratRow/Characters/Rebuild Recipes and Prefabs")]
        public static List<string> RebuildAll()
        {
            var built = new List<string>();
            foreach (string path in RecipePaths())
            {
                CharacterRecipe recipe = Import(path);
                string prefab = recipe != null ? BuildPrefab(path) : null;
                if (prefab != null)
                    built.Add(prefab);
            }
            AssetDatabase.SaveAssets();
            return built;
        }

        /// <summary>Creates or updates &lt;folder&gt;/&lt;id&gt;.asset from the recipe file at <paramref name="jsonPath"/>.</summary>
        public static CharacterRecipe Import(string jsonPath)
        {
            RecipeData data = JsonUtility.FromJson<RecipeData>(File.ReadAllText(jsonPath));
            if (data == null || data.schema != SupportedSchema || string.IsNullOrEmpty(data.id))
            {
                Debug.LogError($"[CharacterRecipe] {jsonPath}: unreadable or unsupported schema {data?.schema}");
                return null;
            }
            string assetPath = AssetPathFor(jsonPath, data.id);
            var recipe = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(assetPath);
            bool created = recipe == null;
            if (created)
                recipe = ScriptableObject.CreateInstance<CharacterRecipe>();

            var problems = new List<string>();
            Dictionary<string, CosmeticItem> items = CosmeticItemsById();
            recipe.id = data.id;
            recipe.displayName = string.IsNullOrEmpty(data.displayName) ? data.id : data.displayName;
            recipe.height = data.height;
            recipe.skinColor = ParseColor(data.skin, Color.white, problems);
            recipe.sliders = (data.sliders ?? Array.Empty<SliderData>()).Select(s => new BodySlider(s.name, s.value)).ToList();
            recipe.faceTexture = FindTexture(data.faceTexture, problems);
            recipe.eyeTexture = FindTexture(data.eyeTexture, problems);
            recipe.items = new List<WornItem>();
            foreach (ItemData item in data.items ?? Array.Empty<ItemData>())
            {
                if (!items.TryGetValue(item.id ?? "", out CosmeticItem cosmetic))
                {
                    problems.Add($"item '{item.id}' not found (no CosmeticItem with that id)");
                    continue;
                }
                Color[] colors = (item.colors ?? Array.Empty<string>()).Select(c => ParseColor(c, Color.white, problems)).ToArray();
                recipe.items.Add(new WornItem(cosmetic, colors));
            }

            if (created)
                AssetDatabase.CreateAsset(recipe, assetPath);
            else
                EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
            foreach (string problem in problems)
                Debug.LogError($"[CharacterRecipe] {jsonPath}: {problem}", recipe);
            Debug.Log($"[CharacterRecipe] {assetPath}: {recipe.items.Count} items, {recipe.sliders.Count} sliders, " +
                      $"height {recipe.height:0.00}, face={(recipe.faceTexture ? recipe.faceTexture.name : "none")}, " +
                      $"eyes={(recipe.eyeTexture ? recipe.eyeTexture.name : "none")}");
            return recipe;
        }

        /// <summary>
        /// Saves the character prefab for the recipe file at <paramref name="jsonPath"/>
        /// (imported first): a variant of the recipe's base model with a CharacterAppearance
        /// that loads the recipe on Start. Returns the prefab path, or null on failure.
        /// </summary>
        public static string BuildPrefab(string jsonPath)
        {
            RecipeData data = JsonUtility.FromJson<RecipeData>(File.ReadAllText(jsonPath));
            var recipe = data != null ? AssetDatabase.LoadAssetAtPath<CharacterRecipe>(AssetPathFor(jsonPath, data.id)) : null;
            string modelPath = data != null ? FindModel(data.baseModel) : null;
            if (recipe == null || modelPath == null)
            {
                Debug.LogError($"[CharacterRecipe] {jsonPath}: cannot build prefab (recipe asset={recipe != null}, " +
                               $"base model '{data?.baseModel}' found={modelPath != null})");
                return null;
            }
            string folder = Path.GetDirectoryName(Path.GetDirectoryName(jsonPath)).Replace('\\', '/') + "/Prefabs";
            Directory.CreateDirectory(folder);
            string prefabPath = $"{folder}/{recipe.id}.prefab";

            // Built in a preview scene so the open scene is never touched.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                instance.name = recipe.id;
                var appearance = instance.GetComponent<CharacterAppearance>();
                if (appearance == null)
                    appearance = instance.AddComponent<CharacterAppearance>();
                appearance.Recipe = recipe;
                appearance.SkinMaterial = CharacterHumanoidCheck.EnsureSkinMaterial();
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool saved);
                if (!saved)
                {
                    Debug.LogError($"[CharacterRecipe] {prefabPath}: save failed");
                    return null;
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Debug.Log($"[CharacterRecipe] {prefabPath}: variant of {Path.GetFileName(modelPath)} wearing {recipe.items.Count} items");
            return prefabPath;
        }

        /// <summary>Called by CharacterModelImporter after an import batch, once sidecars are imported.</summary>
        internal static void OnImported(IEnumerable<string> recipePaths)
        {
            string[] paths = recipePaths.ToArray();
            foreach (string path in paths)
                Import(path);
            if (paths.Length > 0) // prefabs are saved outside the import, once the batch is done
                EditorApplication.delayCall += () =>
                {
                    foreach (string path in paths.Where(File.Exists))
                        BuildPrefab(path);
                };
        }

        static string AssetPathFor(string jsonPath, string id) =>
            $"{Path.GetDirectoryName(jsonPath).Replace('\\', '/')}/{id}.asset";

        static Dictionary<string, CosmeticItem> CosmeticItemsById()
        {
            var byId = new Dictionary<string, CosmeticItem>();
            foreach (string guid in AssetDatabase.FindAssets("t:CosmeticItem"))
            {
                var item = AssetDatabase.LoadAssetAtPath<CosmeticItem>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null && !string.IsNullOrEmpty(item.id))
                    byId[item.id] = item;
            }
            return byId;
        }

        static Texture2D FindTexture(string name, List<string> problems)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            string path = FindByFileName($"{name} t:Texture2D", name);
            if (path == null)
                problems.Add($"texture '{name}' not found under {CharacterModelImporter.Root}");
            return path != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;
        }

        static string FindModel(string name) => string.IsNullOrEmpty(name) ? null : FindByFileName($"{name} t:Model", name);

        static string FindByFileName(string filter, string name) =>
            AssetDatabase.FindAssets(filter, new[] { CharacterModelImporter.Root.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);

        static Color ParseColor(string hex, Color fallback, List<string> problems)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color color))
                return color;
            problems.Add($"colour '{hex}' is not #RRGGBB");
            return fallback;
        }
    }
}
