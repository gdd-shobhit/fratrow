using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FratRow.Art.Props.Editor
{
    /// <summary>
    /// Import rules for static props under Assets/Art/Props, written by
    /// tools/blender/propkit (one folder per prop: SM_&lt;name&gt;.fbx with
    /// SM_&lt;name&gt;_LOD0/1/2 meshes, T_&lt;name&gt;_*.png textures, SM_&lt;name&gt;.prop.json).
    ///
    /// FBX: scale 1, no cameras/lights/animation, rig None, no colliders, normals
    /// from the file. Unity builds the LODGroup itself from the _LODn node names.
    ///
    /// Textures: T_*_Normal is imported as a normal map; T_*_MetallicSmoothness
    /// (metallic in R, smoothness in A) and T_*_Occlusion are linear; T_*_BaseMap is sRGB.
    ///
    /// Material: M_&lt;name&gt;.mat (URP Lit) next to the FBX is created on first import
    /// and reused afterwards; its _BaseMap/_BumpMap/_MetallicGlossMap/_OcclusionMap
    /// are filled from the T_ files that exist, and every material the FBX carries
    /// is remapped to it. Other material settings an artist changes are kept.
    /// </summary>
    public class PropModelImporter : AssetPostprocessor
    {
        /// <summary>Folder the rules apply to.</summary>
        public const string Root = "Assets/Art/Props/";

        /// <summary>Shader every prop material uses.</summary>
        public const string ShaderName = "Universal Render Pipeline/Lit";

        /// <summary>Texture map suffixes written by propkit (T_&lt;name&gt;_&lt;Map&gt;.png).</summary>
        public static readonly string[] Maps = { "BaseMap", "Normal", "MetallicSmoothness", "Occlusion" };

        static readonly Regex ModelFile = new Regex(@"^SM_(?<name>[A-Za-z0-9_]+)\.fbx$", RegexOptions.IgnoreCase);
        static readonly Regex TextureFile = new Regex(@"^T_(?<name>[A-Za-z0-9_]+)_(?<map>BaseMap|Normal|MetallicSmoothness|Occlusion)\.png$");

        /// <summary>True for a prop FBX (Assets/Art/Props/&lt;name&gt;/SM_&lt;name&gt;.fbx); <paramref name="name"/> is &lt;name&gt;.</summary>
        public static bool TryGetPropName(string assetPath, out string name)
        {
            name = null;
            if (assetPath == null || !assetPath.StartsWith(Root))
                return false;
            Match match = ModelFile.Match(Path.GetFileName(assetPath));
            if (!match.Success)
                return false;
            name = match.Groups["name"].Value;
            return true;
        }

        /// <summary>True for a propkit texture (T_&lt;name&gt;_&lt;Map&gt;.png) under <see cref="Root"/>.</summary>
        public static bool TryGetTextureMap(string assetPath, out string name, out string map)
        {
            name = map = null;
            if (assetPath == null || !assetPath.StartsWith(Root))
                return false;
            Match match = TextureFile.Match(Path.GetFileName(assetPath));
            if (!match.Success)
                return false;
            name = match.Groups["name"].Value;
            map = match.Groups["map"].Value;
            return true;
        }

        /// <summary>Path of the prop material: &lt;folder&gt;/M_&lt;name&gt;.mat.</summary>
        public static string MaterialPath(string folder, string name) => $"{folder}/M_{name}.mat";

        /// <summary>Path of one texture map: &lt;folder&gt;/T_&lt;name&gt;_&lt;map&gt;.png.</summary>
        public static string TexturePath(string folder, string name, string map) => $"{folder}/T_{name}_{map}.png";

        // No OnPreprocessModel / OnPreprocessTexture on purpose: Unity counts every
        // postprocessor that hooks an importer type as a dependency of *every* asset
        // of that type, so adding one re-imports all models or textures in the
        // project. The rules are applied to prop files only, from
        // OnPostprocessAllAssets, at the cost of one extra import of a new prop.

        /// <summary>Applies the prop model rules; true when a setting changed (the FBX needs a re-import).</summary>
        public static bool ApplyModelRules(ModelImporter importer)
        {
            bool changed = false;
            void Set<T>(T current, T wanted, System.Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, wanted))
                    return;
                apply(wanted);
                changed = true;
            }
            Set(importer.globalScale, 1f, v => importer.globalScale = v);
            Set(importer.useFileScale, true, v => importer.useFileScale = v);
            Set(importer.importNormals, ModelImporterNormals.Import, v => importer.importNormals = v);
            Set(importer.importBlendShapes, false, v => importer.importBlendShapes = v);
            Set(importer.importVisibility, false, v => importer.importVisibility = v);
            Set(importer.importCameras, false, v => importer.importCameras = v);
            Set(importer.importLights, false, v => importer.importLights = v);
            Set(importer.importAnimation, false, v => importer.importAnimation = v);
            Set(importer.importConstraints, false, v => importer.importConstraints = v);
            Set(importer.animationType, ModelImporterAnimationType.None, v => importer.animationType = v);
            Set(importer.addCollider, false, v => importer.addCollider = v);
            Set(importer.isReadable, false, v => importer.isReadable = v);
            // Remaps only apply to materials imported via the material description.
            Set(importer.materialImportMode, ModelImporterMaterialImportMode.ImportViaMaterialDescription,
                v => importer.materialImportMode = v);
            Set(importer.materialLocation, ModelImporterMaterialLocation.InPrefab, v => importer.materialLocation = v);
            return changed;
        }

        /// <summary>Applies the texture rules for one propkit map; true when a setting changed.</summary>
        public static bool ApplyTextureRules(TextureImporter importer, string map)
        {
            bool changed = false;
            void Set<T>(T current, T wanted, System.Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, wanted))
                    return;
                apply(wanted);
                changed = true;
            }
            switch (map)
            {
                case "Normal":
                    Set(importer.textureType, TextureImporterType.NormalMap, v => importer.textureType = v);
                    Set(importer.convertToNormalmap, false, v => importer.convertToNormalmap = v);
                    break;
                case "MetallicSmoothness":
                case "Occlusion":
                    Set(importer.textureType, TextureImporterType.Default, v => importer.textureType = v);
                    Set(importer.sRGBTexture, false, v => importer.sRGBTexture = v);
                    Set(importer.alphaSource, TextureImporterAlphaSource.FromInput, v => importer.alphaSource = v);
                    Set(importer.alphaIsTransparency, false, v => importer.alphaIsTransparency = v);
                    break;
                default:
                    Set(importer.textureType, TextureImporterType.Default, v => importer.textureType = v);
                    Set(importer.sRGBTexture, true, v => importer.sRGBTexture = v);
                    break;
            }
            return changed;
        }

        static void ReimportLater(string path) =>
            // Import settings must not change mid-import: re-import on the next editor tick.
            EditorApplication.delayCall += () => AssetImporter.GetAtPath(path)?.SaveAndReimport();

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var props = new Dictionary<string, string>();   // fbx path -> prop name
            foreach (string path in imported)
            {
                if (TryGetPropName(path, out string name))
                {
                    if (AssetImporter.GetAtPath(path) is ModelImporter model && ApplyModelRules(model))
                    {
                        ReimportLater(path);   // materials and report follow on that import
                        continue;
                    }
                    props[path] = name;
                }
                else if (TryGetTextureMap(path, out name, out string map))
                {
                    if (AssetImporter.GetAtPath(path) is TextureImporter texture && ApplyTextureRules(texture, map))
                        ReimportLater(path);
                    string fbx = $"{Path.GetDirectoryName(path).Replace('\\', '/')}/SM_{name}.fbx";
                    if (AssetImporter.GetAtPath(fbx) is ModelImporter)
                        props[fbx] = name;
                }
            }

            foreach (var (fbx, name) in props.Select(p => (p.Key, p.Value)))
            {
                string folder = Path.GetDirectoryName(fbx).Replace('\\', '/');
                Material material = EnsureMaterial(folder, name);
                if (material == null)
                    continue;
                RemapMaterials(fbx, material);
                Report(fbx, material);
            }
        }

        /// <summary>
        /// Creates or updates &lt;folder&gt;/M_&lt;name&gt;.mat (URP Lit) and fills its texture
        /// slots from the T_&lt;name&gt;_*.png files that exist. Returns null when URP Lit is missing.
        /// </summary>
        public static Material EnsureMaterial(string folder, string name)
        {
            string path = MaterialPath(folder, name);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[PropModelImporter] shader '{ShaderName}' not found; {path} not created");
                    return null;
                }
                material = new Material(shader) { name = $"M_{name}" };
            }

            Texture2D Load(string map) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(folder, name, map));
            bool hadMetallicMap = !created && material.GetTexture("_MetallicGlossMap") != null;
            Assign(material, "_BaseMap", Load("BaseMap"));
            Assign(material, "_BumpMap", Load("Normal"));
            Assign(material, "_MetallicGlossMap", Load("MetallicSmoothness"));
            Assign(material, "_OcclusionMap", Load("Occlusion"));

            bool metallicMap = material.GetTexture("_MetallicGlossMap") != null;
            if (metallicMap && !hadMetallicMap)
            {
                // With a map, URP Lit multiplies its alpha by _Smoothness: 1 uses the map as authored.
                material.SetFloat("_Smoothness", 1f);
                material.SetFloat("_SmoothnessTextureChannel", 0f);   // metallic alpha
            }
            SetKeyword(material, "_NORMALMAP", material.GetTexture("_BumpMap") != null);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", metallicMap);
            SetKeyword(material, "_OCCLUSIONMAP", material.GetTexture("_OcclusionMap") != null);

            if (created)
                AssetDatabase.CreateAsset(material, path);
            else
                EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        static void Assign(Material material, string property, Texture2D texture)
        {
            if (texture != null && material.HasProperty(property) && material.GetTexture(property) != texture)
                material.SetTexture(property, texture);
        }

        static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        /// <summary>
        /// Remaps every material embedded in the FBX to <paramref name="material"/> and
        /// re-imports it. Import settings must not change mid-import, so this runs on the
        /// next editor tick; once remapped, the FBX carries no embedded materials and
        /// nothing further happens.
        /// </summary>
        static void RemapMaterials(string fbx, Material material)
        {
            string[] sources = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().Select(m => m.name).Distinct().ToArray();
            if (sources.Length == 0)
                return;
            string materialPath = AssetDatabase.GetAssetPath(material);
            EditorApplication.delayCall += () =>
            {
                var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
                var target = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (importer == null || target == null)
                    return;
                foreach (string source in sources)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source), target);
                importer.SaveAndReimport();
            };
        }

        static void Report(string fbx, Material material)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (root == null)
                return;
            var lodGroup = root.GetComponent<LODGroup>();
            string lods = lodGroup == null
                ? "no LODGroup"
                : string.Join(", ", lodGroup.GetLODs().Select((lod, i) =>
                    $"LOD{i} {lod.renderers.Where(r => r != null).Sum(Triangles)} tris @ {lod.screenRelativeTransitionHeight:0.###}"));
            string maps = string.Join(", ", new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" }
                .Where(p => material.GetTexture(p) != null));
            Debug.Log($"[PropModelImporter] {fbx}: {lods}; material {material.name} maps [{maps}]");
        }

        static int Triangles(Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                return 0;
            int total = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                total += (int)mesh.GetIndexCount(i) / 3;
            return total;
        }
    }
}
