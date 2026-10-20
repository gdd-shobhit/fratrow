using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FratRow.Art.Characters.Editor
{
    /// <summary>
    /// Materials for textured cosmetics: items made from generated meshes by
    /// tools/blender/charkit/ingest.py (design/art/ai-asset-pipeline.md).
    ///
    /// Their .cosmetic.json sidecar names texture maps per material. For each
    /// material with a base map this creates or updates &lt;folder&gt;/&lt;material&gt;.mat
    /// (URP Lit) and remaps the item FBX's embedded material of the same name to
    /// it, so the textures survive re-exports. Flat-colour items (no maps) keep
    /// the FBX's own materials. Normal maps import as normal maps and the URP
    /// metallic/smoothness map as linear. CosmeticItem assets are still made by
    /// CosmeticSidecar; the two run independently on the same sidecar.
    /// </summary>
    public class CosmeticMaterialImporter : AssetPostprocessor
    {
        /// <summary>File suffix of normal maps written by tools/blender/aikit/textures.py.</summary>
        public const string NormalSuffix = "_Normal.png";

        /// <summary>File suffix of URP metallic (R) / smoothness (A) maps written by aikit.</summary>
        public const string MetallicSmoothnessSuffix = "_MetallicSmoothness.png";

        [Serializable]
        class Sidecar
        {
            public string model;
            public MaterialMaps[] materials;
        }

        [Serializable]
        class MaterialMaps
        {
            public string name;
            public string baseMap;
            public string normalMap;
            public string metallicSmoothnessMap;
        }

        // No OnPreprocessTexture on purpose: a postprocessor hooking an importer type
        // becomes an import dependency of every asset of that type, so adding one
        // re-imports every texture in the project. The two map types that need
        // settings get them here, and only those files are re-imported.
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported.Where(p => p.StartsWith(CharacterModelImporter.Root)))
            {
                if (HasSuffix(path, CharacterModelImporter.SidecarSuffix))
                    Apply(path);
                else if (AssetImporter.GetAtPath(path) is TextureImporter texture && ApplyTextureRules(path, texture))
                    EditorApplication.delayCall += () => AssetImporter.GetAtPath(path)?.SaveAndReimport();
            }
        }

        static bool HasSuffix(string path, string suffix) => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Normal maps import as normal maps and metallic/smoothness maps as linear;
        /// true when a setting changed (the texture needs a re-import).
        /// </summary>
        public static bool ApplyTextureRules(string path, TextureImporter importer)
        {
            if (HasSuffix(path, NormalSuffix) && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                return true;
            }
            if (HasSuffix(path, MetallicSmoothnessSuffix) && importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Creates or updates the URP Lit materials named by the sidecar at
        /// <paramref name="jsonPath"/> and remaps its model to them. The model is
        /// re-imported only when a remap is new.
        /// </summary>
        public static void Apply(string jsonPath)
        {
            Sidecar data = JsonUtility.FromJson<Sidecar>(File.ReadAllText(jsonPath));
            var textured = (data?.materials ?? Array.Empty<MaterialMaps>()).Where(m => !string.IsNullOrEmpty(m.baseMap)).ToArray();
            if (textured.Length == 0 || string.IsNullOrEmpty(data.model))
                return;
            string folder = Path.GetDirectoryName(jsonPath).Replace('\\', '/');
            string modelPath = $"{folder}/{data.model}";
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
            {
                Debug.LogWarning($"[CosmeticMaterialImporter] {jsonPath}: model '{data.model}' not found next to the sidecar");
                return;
            }

            var remaps = importer.GetExternalObjectMap();
            bool changed = false;
            foreach (MaterialMaps maps in textured)
            {
                var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{maps.baseMap}");
                if (baseMap == null)
                {
                    Debug.LogWarning($"[CosmeticMaterialImporter] {maps.name}: base map '{maps.baseMap}' not found in {folder}");
                    continue;
                }
                string matPath = $"{folder}/{maps.name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = maps.name };
                    AssetDatabase.CreateAsset(material, matPath);
                }
                // White base colour: CharacterAppearance tints _BaseColor, which multiplies the map.
                material.SetTexture("_BaseMap", baseMap);
                material.SetColor("_BaseColor", Color.white);
                SetMap(material, folder, maps.normalMap, "_BumpMap", "_NORMALMAP");
                if (SetMap(material, folder, maps.metallicSmoothnessMap, "_MetallicGlossMap", "_METALLICSPECGLOSSMAP"))
                    material.SetFloat("_Smoothness", 1f); // smoothness = map alpha x this
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);

                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), maps.name);
                if (!remaps.TryGetValue(id, out var current) || current != material)
                {
                    importer.AddRemap(id, material);
                    changed = true;
                }
            }
            if (!changed)
                return;
            // Import settings must not change mid-import: apply and re-import afterwards.
            EditorApplication.delayCall += () => AssetImporter.GetAtPath(modelPath)?.SaveAndReimport();
            Debug.Log($"[CosmeticMaterialImporter] {modelPath}: remapped {textured.Length} textured material(s)");
        }

        static bool SetMap(Material material, string folder, string file, string property, string keyword)
        {
            var texture = string.IsNullOrEmpty(file) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{file}");
            material.SetTexture(property, texture);
            if (texture != null)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
            return texture != null;
        }
    }
}
