using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FratRow.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FratRow.Art.Characters.Editor
{
    /// <summary>
    /// M4 check (design/art/character-pipeline.md): every character prefab
    /// (Prefabs/CH_*.prefab) dressed from its recipe through CharacterAppearance, as
    /// walk filmstrips plus a line-up (front, three-quarter, back) and face close-ups,
    /// written to production/qa/evidence/m4-trio. Runs in hidden preview scenes; the
    /// open scene is never touched. RunBatch is the batch-mode entry point
    /// (tools/unity/unity_batch.py --execute FratRow.Art.Characters.Editor.CharacterTrioCheck.RunBatch).
    /// </summary>
    public static class CharacterTrioCheck
    {
        /// <summary>Folder holding the generated character prefabs.</summary>
        public const string PrefabDir = "Assets/Art/Characters/Female/Prefabs";

        /// <summary>Evidence folder, relative to the project root.</summary>
        public const string EvidenceDir = "production/qa/evidence/m4-trio";

        const float Spacing = 0.8f;
        const float FaceHeight = 1.52f; // eye level of the base body, scaled by each character's height

        /// <summary>Runs the check on every character prefab.</summary>
        [MenuItem("Tools/FratRow/Characters/Run Trio Check (M4)")]
        public static void RunMenu() => Run();

        /// <summary>Batch mode: re-imports sidecars and recipes, rebuilds prefabs, runs the check, exits 0 on success.</summary>
        public static void RunBatch()
        {
            int failures = 0;
            try
            {
                AssetDatabase.Refresh();
                foreach (string sidecar in Directory.GetFiles(CharacterModelImporter.Root, "*" + CharacterModelImporter.SidecarSuffix,
                             SearchOption.AllDirectories))
                    failures += CosmeticSidecar.Import(sidecar.Replace('\\', '/')) == null ? 1 : 0;
                List<string> prefabs = CharacterRecipeImporter.RebuildAll();
                failures += CharacterRecipeImporter.RecipePaths().Length - prefabs.Count;
                List<string> written = Run();
                failures += written.Count(p => p == null) + (written.Count == 0 ? 1 : 0);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                failures++;
            }
            Debug.Log($"[TrioCheck] batch finished with {failures} failure(s)");
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>Writes walk filmstrips, the line-up and face close-ups; returns the PNG paths (null entries failed).</summary>
        public static List<string> Run()
        {
            List<string> prefabs = PrefabPaths();
            var written = new List<string>();
            foreach (string path in prefabs)
                written.Add(CharacterHumanoidCheck.Run(path, CharacterHumanoidCheck.DefaultClip, EvidenceDir, 6, Dress));
            written.AddRange(RenderLineup(prefabs, EvidenceDir));
            return written;
        }

        /// <summary>The character prefabs, by name.</summary>
        public static List<string> PrefabPaths() =>
            Directory.Exists(PrefabDir)
                ? Directory.GetFiles(PrefabDir, "CH_*.prefab").Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToList()
                : new List<string>();

        /// <summary>Loads the character's recipe and dresses it (Start does not run in edit mode).</summary>
        public static void Dress(GameObject character)
        {
            var appearance = character.GetComponent<CharacterAppearance>();
            if (appearance == null || appearance.Recipe == null)
            {
                Debug.LogError($"[TrioCheck] {character.name}: no CharacterAppearance with a recipe");
                return;
            }
            appearance.Refresh();
            long tris = character.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => !r.forceRenderingOff)
                            .Sum(r => CharacterHumanoidCheck.TriangleCount(r.sharedMesh))
                        + character.GetComponentsInChildren<MeshFilter>(true).Sum(f => CharacterHumanoidCheck.TriangleCount(f.sharedMesh));
            Debug.Log($"[TrioCheck] {appearance.Recipe.id}: wearing {string.Join(", ", appearance.Worn().Select(w => w.item.id))}; " +
                      $"{tris} tris dressed, height {appearance.Height:0.00}");
        }

        static IEnumerable<string> RenderLineup(List<string> prefabPaths, string outDir)
        {
            var written = new List<string>();
            if (prefabPaths.Count == 0)
            {
                Debug.LogError($"[TrioCheck] no CH_*.prefab in {PrefabDir}; run Tools/FratRow/Characters/Rebuild Recipes and Prefabs");
                return written;
            }
            Scene scene = EditorSceneManager.NewPreviewScene();
            var proxies = new List<CharacterHumanoidCheck.Proxy>();
            var heads = new List<(string id, Vector3 position)>();
            var wide = new RenderTexture(1500, 1000, 24);
            var square = new RenderTexture(600, 600, 24);
            var wideImage = new Texture2D(wide.width, wide.height, TextureFormat.RGB24, false);
            var squareImage = new Texture2D(square.width, square.height, TextureFormat.RGB24, false);
            bool fog = RenderSettings.fog;
            Unsupported.SetRenderSettingsUseFogNoDirty(false);
            try
            {
                for (int i = 0; i < prefabPaths.Count; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[i]);
                    var character = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    character.transform.position = new Vector3((i - (prefabPaths.Count - 1) / 2f) * Spacing, 0f, 0f);
                    Dress(character);
                    var appearance = character.GetComponent<CharacterAppearance>();
                    float height = appearance != null ? appearance.Height : 1f;
                    heads.Add((prefab.name, character.transform.position + Vector3.up * FaceHeight * height));
                    proxies.AddRange(CharacterHumanoidCheck.MakeProxies(character, scene));
                }
                CharacterHumanoidCheck.AddStudioLights(scene);
                CharacterHumanoidCheck.BakeProxies(proxies);

                // Characters face +Z.
                Vector3 centre = new Vector3(0f, 0.9f, 0f);
                Camera camera = CharacterHumanoidCheck.MakeCamera(scene, wide, orthographicSize: 1.0f);
                foreach (var (view, offset) in new[]
                {
                    ("front", new Vector3(0f, 0.1f, 6f)),
                    ("three_quarter", new Vector3(3.5f, 1.4f, 4.5f)),
                    ("back", new Vector3(0f, 0.1f, -6f)),
                })
                {
                    camera.transform.position = centre + offset;
                    camera.transform.LookAt(centre);
                    CharacterHumanoidCheck.RenderInto(camera, wide, wideImage, 0, 0);
                    written.Add(CharacterHumanoidCheck.WritePng(wideImage, outDir, $"unity_trio_{view}.png"));
                }

                camera.targetTexture = square;
                camera.orthographicSize = 0.16f;
                foreach (var (id, head) in heads)
                {
                    foreach (var (view, offset) in new[] { ("front", new Vector3(0f, 0f, 3f)), ("three_quarter", new Vector3(2f, 0.3f, 2.2f)) })
                    {
                        camera.transform.position = head + offset;
                        camera.transform.LookAt(head);
                        CharacterHumanoidCheck.RenderInto(camera, square, squareImage, 0, 0);
                        written.Add(CharacterHumanoidCheck.WritePng(squareImage, outDir, $"unity_{id}_face_{view}.png"));
                    }
                }
            }
            finally
            {
                Unsupported.SetRenderSettingsUseFogNoDirty(fog);
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (CharacterHumanoidCheck.Proxy p in proxies)
                    Object.DestroyImmediate(p.baked);
                RenderTexture.active = null;
                Object.DestroyImmediate(wide);
                Object.DestroyImmediate(square);
                Object.DestroyImmediate(wideImage);
                Object.DestroyImmediate(squareImage);
            }
            return written;
        }
    }
}
