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
    /// Turns recipe files (&lt;id&gt;.recipe.json, RecipeDocument; shared with
    /// tools/blender/charkit) into a CharacterRecipe asset beside the file, and a
    /// character prefab in the Prefabs folder beside Recipes: a variant of the base
    /// model whose CharacterAppearance uses the recipe. Edits made in Unity go back to
    /// the file through CharacterRecipeSync, so either side can be edited.
    ///
    /// Items are found by CosmeticItem id, textures and the base model by file name,
    /// anywhere under Assets/Art/Characters. The prefab only references the recipe, so
    /// it is built once (when missing) and never needs rebuilding for recipe changes;
    /// Tools/FratRow/Characters/Rebuild Recipes and Prefabs rebuilds everything.
    /// </summary>
    public static class CharacterRecipeImporter
    {
        /// <summary>File suffix of recipe files.</summary>
        public const string RecipeSuffix = ".recipe.json";

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
                string prefab = Import(path) != null ? BuildPrefab(path) : null;
                if (prefab != null)
                    built.Add(prefab);
            }
            AssetDatabase.SaveAssets();
            return built;
        }

        /// <summary>Creates or updates &lt;folder&gt;/&lt;id&gt;.asset from the recipe file at <paramref name="jsonPath"/>.</summary>
        public static CharacterRecipe Import(string jsonPath)
        {
            string text = File.ReadAllText(jsonPath);
            RecipeDocument doc = RecipeDocument.Parse(text);
            if (doc == null)
            {
                Debug.LogError($"[CharacterRecipe] {jsonPath}: unreadable, unsupported schema (expected {RecipeDocument.CurrentSchema}) or no id");
                return null;
            }
            string assetPath = AssetPathFor(jsonPath, doc.id);
            var recipe = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(assetPath);
            bool created = recipe == null;
            if (created)
                recipe = ScriptableObject.CreateInstance<CharacterRecipe>();

            var problems = new List<string>();
            Dictionary<string, CosmeticItem> items = CosmeticItemsById();
            recipe.id = doc.id;
            recipe.displayName = string.IsNullOrEmpty(doc.displayName) ? doc.id : doc.displayName;
            recipe.baseModel = doc.baseModel;
            recipe.height = doc.height;
            recipe.skinColor = RecipeDocument.ColorOr(doc.skin, Color.white, problems);
            recipe.sliders = doc.sliders.Select(s => new BodySlider(s.name, s.value)).ToList();
            recipe.faceTexture = FindTexture(doc.faceTexture, problems);
            recipe.eyeTexture = FindTexture(doc.eyeTexture, problems);
            recipe.items = new List<WornItem>();
            foreach (RecipeDocument.Item item in doc.items)
            {
                if (!items.TryGetValue(item.id ?? "", out CosmeticItem cosmetic))
                {
                    problems.Add($"item '{item.id}' not found (no CosmeticItem with that id)");
                    continue;
                }
                recipe.items.Add(new WornItem(cosmetic, item.colors.Select(c => RecipeDocument.ColorOr(c, Color.white, problems)).ToArray()));
            }
            recipe.face = doc.ToFaceStyle(problems);
            recipe.iris = RecipeDocument.ColorOr(doc.iris, recipe.iris, problems);

            if (created)
                AssetDatabase.CreateAsset(recipe, assetPath);
            else
                EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
            CharacterRecipeSync.Imported(jsonPath, text);
            foreach (string problem in problems)
                Debug.LogError($"[CharacterRecipe] {jsonPath}: {problem}", recipe);
            Debug.Log($"[CharacterRecipe] {assetPath}: {recipe.items.Count} items, {recipe.sliders.Count} sliders, " +
                      $"height {recipe.height:0.00}, face={(recipe.faceTexture ? recipe.faceTexture.name : "none")}, " +
                      $"eyes={(recipe.eyeTexture ? recipe.eyeTexture.name : "none")}");
            CharacterRecipeSync.RefreshCharacters(recipe);
            return recipe;
        }

        /// <summary>
        /// Saves the character prefab for the recipe file at <paramref name="jsonPath"/>
        /// (import it first): a variant of the recipe's base model with a CharacterAppearance
        /// using the recipe. Returns the prefab path, or null on failure.
        /// </summary>
        public static string BuildPrefab(string jsonPath)
        {
            RecipeDocument doc = RecipeDocument.Parse(File.ReadAllText(jsonPath));
            var recipe = doc != null ? AssetDatabase.LoadAssetAtPath<CharacterRecipe>(AssetPathFor(jsonPath, doc.id)) : null;
            string modelPath = recipe != null ? FindModel(recipe.baseModel) : null;
            if (recipe == null || modelPath == null)
            {
                Debug.LogError($"[CharacterRecipe] {jsonPath}: cannot build prefab (recipe asset={recipe != null}, " +
                               $"base model '{doc?.baseModel}' found={modelPath != null})");
                return null;
            }
            string prefabPath = PrefabPathFor(jsonPath, recipe.id);
            Directory.CreateDirectory(Path.GetDirectoryName(prefabPath));

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
                appearance.Clear(); // the editor preview is never saved, but keep the source body untouched too
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

        /// <summary>
        /// Duplicates the selected recipe (asset, character or file) as a new character:
        /// new id, copies of its face and eye textures to repaint, recipe asset and prefab.
        /// </summary>
        [MenuItem("Tools/FratRow/Characters/New Character From Selected Recipe")]
        public static void NewFromSelected()
        {
            CharacterRecipe source = Selection.activeObject as CharacterRecipe;
            if (source == null && Selection.activeGameObject != null)
            {
                var appearance = Selection.activeGameObject.GetComponentInParent<CharacterAppearance>();
                source = appearance != null ? appearance.Recipe : null;
            }
            string sourceFile = CharacterRecipeSync.JsonPathFor(source);
            if (sourceFile == null)
            {
                Debug.LogError("[CharacterRecipe] select a linked recipe asset (or a character using one) to copy");
                return;
            }
            string folder = Path.GetDirectoryName(sourceFile).Replace('\\', '/');
            string id = UniqueId(folder, source.id + "_Variant");
            string tag = id.StartsWith("CH_F_") ? id.Substring("CH_F_".Length) : id;
            RecipeDocument doc = RecipeDocument.Parse(File.ReadAllText(sourceFile));
            doc.id = id;
            doc.displayName = $"{source.displayName} (variant)";
            doc.faceTexture = CopyTexture(source.faceTexture, $"T_F_Face_{tag}") ?? doc.faceTexture;
            doc.eyeTexture = CopyTexture(source.eyeTexture, $"T_F_Eyes_{tag}") ?? doc.eyeTexture;
            string file = $"{folder}/{id}{RecipeSuffix}";
            File.WriteAllText(file, doc.ToJson());
            AssetDatabase.ImportAsset(file); // creates the recipe asset; the prefab follows (OnImported)
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(AssetPathFor(file, id));
            Debug.Log($"[CharacterRecipe] new character {id}: edit it in the inspector; repaint its face in Blender " +
                      "(tasks.paint_faces) after changing face, iris or skin colours");
        }

        /// <summary>
        /// The asset names in <paramref name="doc"/> Unity cannot resolve (items, textures), as
        /// a readable list, or null when everything resolves.
        /// </summary>
        public static string Unresolved(RecipeDocument doc)
        {
            if (doc == null)
                return null;
            Dictionary<string, CosmeticItem> items = CosmeticItemsById();
            var missing = doc.items.Select(i => i.id).Where(id => !items.ContainsKey(id ?? "")).Select(id => $"item '{id}'").ToList();
            foreach (string texture in new[] { doc.faceTexture, doc.eyeTexture })
                if (!string.IsNullOrEmpty(texture) && FindByFileName($"{texture} t:Texture2D", texture) == null)
                    missing.Add($"texture '{texture}'");
            return missing.Count > 0 ? string.Join(", ", missing) : null;
        }

        /// <summary>Path of the base model named <paramref name="name"/> under Assets/Art/Characters, or null.</summary>
        public static string FindModel(string name) => string.IsNullOrEmpty(name) ? null : FindByFileName($"{name} t:Model", name);

        /// <summary>Called by CharacterModelImporter after an import batch, once sidecars are imported.</summary>
        internal static void OnImported(IEnumerable<string> recipePaths)
        {
            var needPrefab = new List<string>();
            foreach (string path in recipePaths)
            {
                string text = File.ReadAllText(path);
                if (CharacterRecipeSync.IsKnown(path, text))
                    continue; // this editor wrote (or already imported) exactly this text
                CharacterRecipe recipe = Import(path);
                if (recipe != null && !File.Exists(PrefabPathFor(path, recipe.id)))
                    needPrefab.Add(path);
            }
            if (needPrefab.Count > 0) // prefabs are saved outside the import, once the batch is done
                EditorApplication.delayCall += () =>
                {
                    foreach (string path in needPrefab.Where(File.Exists))
                        BuildPrefab(path);
                };
        }

        static string AssetPathFor(string jsonPath, string id) =>
            $"{Path.GetDirectoryName(jsonPath).Replace('\\', '/')}/{id}.asset";

        static string PrefabPathFor(string jsonPath, string id) =>
            $"{Path.GetDirectoryName(Path.GetDirectoryName(jsonPath)).Replace('\\', '/')}/Prefabs/{id}.prefab";

        static string UniqueId(string folder, string stem)
        {
            for (int n = 1; ; n++)
            {
                string id = n == 1 ? stem : $"{stem}{n}";
                if (!File.Exists($"{folder}/{id}{RecipeSuffix}"))
                    return id;
            }
        }

        static string CopyTexture(Texture2D texture, string name)
        {
            string path = texture != null ? AssetDatabase.GetAssetPath(texture) : null;
            if (string.IsNullOrEmpty(path))
                return null;
            string copy = $"{Path.GetDirectoryName(path).Replace('\\', '/')}/{name}{Path.GetExtension(path)}";
            return File.Exists(copy) || AssetDatabase.CopyAsset(path, copy) ? name : null;
        }

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
                problems.Add($"texture '{name}' not found under {CharacterModelImporter.Root} (paint it in Blender: tasks.paint_faces)");
            return path != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;
        }

        static string FindByFileName(string filter, string name) =>
            AssetDatabase.FindAssets(filter, new[] { CharacterModelImporter.Root.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);
    }
}
