using System.Collections.Generic;
using System.IO;
using System.Linq;
using FratRow.Characters;
using UnityEditor;
using UnityEngine;

namespace FratRow.Art.Characters.Editor
{
    /// <summary>
    /// Keeps recipe files and Unity in step both ways (design/art/character-pipeline.md):
    /// every edit to a CharacterRecipe asset (inspector, undo/redo) is written back to its
    /// &lt;id&gt;.recipe.json, and every character using the recipe re-dresses at once.
    /// The file stays the source of truth shared with Blender; edits made to it outside
    /// Unity come back through CharacterRecipeImporter. Writes are debounced to one per
    /// editor update, skipped when the text is unchanged, and recognised when they come
    /// back as an import, so Unity never re-imports its own write over newer edits.
    /// </summary>
    [InitializeOnLoad]
    public static class CharacterRecipeSync
    {
        static readonly HashSet<CharacterRecipe> Pending = new HashSet<CharacterRecipe>();
        static readonly Dictionary<string, string> Known = new Dictionary<string, string>(); // file -> text last written or imported

        static CharacterRecipeSync()
        {
            CharacterRecipe.Edited += MarkEdited;
            Undo.undoRedoPerformed += () =>
            {
                foreach (CharacterRecipe recipe in Resources.FindObjectsOfTypeAll<CharacterRecipe>())
                    MarkEdited(recipe);
            };
        }

        /// <summary>Queues a recipe asset for write-back and preview refresh (once per editor update).</summary>
        public static void MarkEdited(CharacterRecipe recipe)
        {
            if (recipe == null || !EditorUtility.IsPersistent(recipe))
                return;
            if (Pending.Add(recipe) && Pending.Count == 1)
                EditorApplication.delayCall += Flush;
        }

        static void Flush()
        {
            CharacterRecipe[] recipes = Pending.ToArray();
            Pending.Clear();
            foreach (CharacterRecipe recipe in recipes)
            {
                if (recipe == null)
                    continue;
                Write(recipe);
                AssetDatabase.SaveAssetIfDirty(recipe); // the asset and its file agree on disk, even if Unity quits
                RefreshCharacters(recipe);
            }
        }

        /// <summary>The recipe file of a recipe asset (&lt;id&gt;.asset beside &lt;id&gt;.recipe.json), or null.</summary>
        public static string JsonPathFor(CharacterRecipe recipe)
        {
            string assetPath = recipe != null ? AssetDatabase.GetAssetPath(recipe) : null;
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(recipe.id)
                || Path.GetFileNameWithoutExtension(assetPath) != recipe.id)
                return null;
            return $"{Path.GetDirectoryName(assetPath).Replace('\\', '/')}/{recipe.id}{CharacterRecipeImporter.RecipeSuffix}";
        }

        /// <summary>
        /// Writes the recipe's file. Refuses (with a warning) when the file names items or
        /// textures Unity has not imported, since saving would drop them. Returns whether the file changed.
        /// </summary>
        public static bool Write(CharacterRecipe recipe)
        {
            string path = JsonPathFor(recipe);
            if (path == null)
            {
                Debug.LogWarning($"[CharacterRecipe] {AssetDatabase.GetAssetPath(recipe)}: not linked to a recipe file " +
                                 "(<id>.asset next to <id>.recipe.json); edits stay in the asset", recipe);
                return false;
            }
            string current = File.Exists(path) ? File.ReadAllText(path) : null;
            if (current != null)
            {
                string missing = CharacterRecipeImporter.Unresolved(RecipeDocument.Parse(current));
                if (missing != null)
                {
                    Debug.LogWarning($"[CharacterRecipe] {path} names {missing} that Unity has not imported; " +
                                     "not saving Unity edits until they exist (saving would drop them)", recipe);
                    return false;
                }
            }
            string text = RecipeDocument.From(recipe).ToJson();
            Known[path] = text;
            if (current == text)
                return false;
            File.WriteAllText(path, text);
            Debug.Log($"[CharacterRecipe] saved {path}");
            return true;
        }

        /// <summary>True when the file holds exactly what this editor last wrote or imported (nothing new to import).</summary>
        internal static bool IsKnown(string path, string text) => Known.TryGetValue(path, out string known) && known == text;

        /// <summary>Records text just imported from a file.</summary>
        internal static void Imported(string path, string text) => Known[path] = text;

        /// <summary>Re-dresses every character using <paramref name="recipe"/> (scenes, prefab mode, Play mode).</summary>
        public static void RefreshCharacters(CharacterRecipe recipe)
        {
            foreach (CharacterAppearance appearance in Resources.FindObjectsOfTypeAll<CharacterAppearance>())
            {
                if (appearance == null || appearance.Recipe != recipe || EditorUtility.IsPersistent(appearance)
                    || !appearance.isActiveAndEnabled || !appearance.gameObject.scene.IsValid())
                    continue;
                if (Application.isPlaying || appearance.PreviewInEditor)
                    appearance.Refresh();
            }
        }
    }
}
