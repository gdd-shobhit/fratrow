using System.Collections.Generic;
using System.IO;
using System.Linq;
using FratRow.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FratRow.Art.Characters.Editor
{
    /// <summary>
    /// Plays a Humanoid clip on a character in a hidden preview scene and saves a
    /// filmstrip (bind pose + sampled frames) to production/qa/evidence. The open
    /// scene is never touched. Used as the M2 evidence for the character pipeline
    /// (design/art/character-pipeline.md) and reusable for later characters; the
    /// preview-scene helpers (baked proxies, lights, render into an image) are shared
    /// with CharacterTrioCheck.
    /// </summary>
    public static class CharacterHumanoidCheck
    {
        /// <summary>Base character checked by the menu item.</summary>
        public const string DefaultModel = "Assets/Art/Characters/Female/SK_Female_Base.fbx";

        /// <summary>CityPeople female walk, a Humanoid clip made for a different skeleton.</summary>
        public const string DefaultClip = "Assets/DenysAlmaral/CityPeople/Animations/locom_f_basicWalk_30f.fbx";

        /// <summary>Evidence folder, relative to the project root.</summary>
        public const string DefaultEvidenceDir = "production/qa/evidence/m2-unity-humanoid";

        const int TileWidth = 480;
        const int TileHeight = 720;

        /// <summary>Folder holding the female CosmeticItem assets.</summary>
        public const string CosmeticsDir = "Assets/Art/Characters/Female/Cosmetics";

        /// <summary>Shared skin material (URP Lit, tinted per character by CharacterAppearance).</summary>
        public const string SkinMaterialPath = "Assets/Art/Characters/Materials/M_Skin.mat";

        /// <summary>Background colour of every check render.</summary>
        public static readonly Color Background = new Color(0.23f, 0.23f, 0.25f);

        /// <summary>Runs the check on the default character and clip.</summary>
        [MenuItem("Tools/FratRow/Characters/Run Humanoid Check (M2)")]
        public static void RunDefault() => Run(DefaultModel, DefaultClip, DefaultEvidenceDir, 6);

        /// <summary>
        /// M3 check: dresses the base character with the blonde's crop top and hoops through
        /// CharacterAppearance (re-bound bones, synced sliders, skin mask, sockets, tints) on
        /// the Base and Curvy body presets, and writes walk filmstrips.
        /// </summary>
        [MenuItem("Tools/FratRow/Characters/Run Dressed Check (M3)")]
        public static void RunDressed()
        {
            var items = new[] { "SKM_F_Top_OffShoulderCrop", "SM_F_Ear_HoopSmall" }
                .Select(id => AssetDatabase.LoadAssetAtPath<CosmeticItem>($"{CosmeticsDir}/{id}.asset")).ToList();
            if (items.Any(i => i == null))
            {
                Debug.LogError("[HumanoidCheck] cosmetic assets missing; re-import Assets/Art/Characters/Female/Cosmetics");
                return;
            }
            Material skin = EnsureSkinMaterial();
            var presets = new (string label, BodySlider[] sliders)[]
            {
                ("Base", new BodySlider[0]),
                ("Curvy", new[]
                {
                    new BodySlider("Bust_Large", 0.7f), new BodySlider("Hips_Wide", 0.9f), new BodySlider("Butt_Large", 0.9f),
                    new BodySlider("Thighs_Thick", 0.8f), new BodySlider("Waist_Narrow", 0.5f), new BodySlider("Calves_Thick", 0.4f),
                    new BodySlider("Arms_Thick", 0.3f), new BodySlider("Belly_Soft", 0.2f),
                }),
            };
            foreach (var (label, sliders) in presets)
            {
                Run(DefaultModel, DefaultClip, "production/qa/evidence/m3-cosmetic-pipeline", 6, character =>
                {
                    var appearance = character.AddComponent<CharacterAppearance>();
                    foreach (CosmeticItem item in items)
                        appearance.Wear(item);
                    appearance.Sliders.AddRange(sliders);
                    appearance.SkinMaterial = skin;
                    appearance.SkinColor = new Color(0.93f, 0.78f, 0.68f);
                    appearance.Apply();
                    var mesh = appearance.Body.sharedMesh;
                    Debug.Log($"[HumanoidCheck] dressed '{label}': body now {TriangleCount(mesh)} tris in {mesh.subMeshCount} submesh, " +
                              $"{character.GetComponentsInChildren<Renderer>().Length} renderers");
                }, label);
            }
        }

        /// <summary>Loads the shared skin material, creating it (URP Lit, matte) if missing.</summary>
        public static Material EnsureSkinMaterial()
        {
            var skin = AssetDatabase.LoadAssetAtPath<Material>(SkinMaterialPath);
            if (skin != null)
                return skin;
            Directory.CreateDirectory(Path.GetDirectoryName(SkinMaterialPath));
            skin = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Skin" };
            skin.SetColor("_BaseColor", Color.white);
            skin.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(skin, SkinMaterialPath);
            return skin;
        }

        /// <summary>
        /// Samples <paramref name="frames"/> evenly spaced frames of the first clip in
        /// <paramref name="clipAssetPath"/> on <paramref name="modelPath"/> (a model or a
        /// character prefab) and writes &lt;outDir&gt;/&lt;model&gt;[_label]__&lt;clip&gt;.png.
        /// <paramref name="dress"/> runs on the instantiated character before sampling
        /// (e.g. to add or apply a CharacterAppearance). Returns the PNG path, or null on failure.
        /// </summary>
        public static string Run(string modelPath, string clipAssetPath, string outDir, int frames,
                                 System.Action<GameObject> dress = null, string label = null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Avatar avatar = FindAvatar(modelPath, model);
            var clip = AssetDatabase.LoadAllAssetsAtPath(clipAssetPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (model == null || avatar == null || clip == null)
            {
                Debug.LogError($"[HumanoidCheck] missing input: model={model != null} avatar={avatar != null} clip={clip != null}");
                return null;
            }
            string reference = AssetImporter.GetAtPath(modelPath) is ModelImporter importer
                ? $"{importer.humanDescription.human.Length} human bones, reference={importer.userData}"
                : "prefab";
            Debug.Log($"[HumanoidCheck] {model.name}: avatar valid={avatar.isValid} human={avatar.isHuman}, {reference}; " +
                      $"clip '{clip.name}' {clip.length:0.00}s humanMotion={clip.humanMotion}");

            Scene scene = EditorSceneManager.NewPreviewScene();
            var proxies = new List<Proxy>();
            var target = new RenderTexture(TileWidth, TileHeight, 24);
            var strip = new Texture2D(TileWidth * (frames + 1), TileHeight, TextureFormat.RGB24, false);
            // The preview scene renders with the open scene's global fog; turn it off
            // without marking that scene dirty (what PreviewRenderUtility does).
            bool fog = RenderSettings.fog;
            Unsupported.SetRenderSettingsUseFogNoDirty(false);
            try
            {
                var character = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                var animator = character.GetComponent<Animator>();
                if (animator == null) // Unity's fake-null: `??` would not catch a missing component
                    animator = character.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                dress?.Invoke(character);
                ReportMesh(character);
                proxies.AddRange(MakeProxies(character, scene));
                AddStudioLights(scene);

                Camera camera = MakeCamera(scene, target, orthographicSize: 0.95f);
                // Side-leaning three-quarter view (character faces +Z) so the stride reads.
                Vector3 focus = new Vector3(0f, 0.85f, 0f);
                camera.transform.position = focus + new Vector3(3.6f, 0.4f, 2.0f);
                camera.transform.LookAt(focus);

                BakeProxies(proxies);
                RenderInto(camera, target, strip, 0, 0); // bind pose, before any animation

                // AnimationMode sampling, not a PlayableGraph: a manually evaluated
                // graph does not move the skeleton in a preview scene.
                AnimationMode.StartAnimationMode();
                try
                {
                    for (int i = 0; i < frames; i++)
                    {
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(character, clip, clip.length * i / frames);
                        AnimationMode.EndSampling();
                        BakeProxies(proxies);
                        RenderInto(camera, target, strip, (i + 1) * TileWidth, 0);
                    }
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                }

                string file = string.IsNullOrEmpty(label) ? model.name : $"{model.name}_{label}";
                return WritePng(strip, outDir, $"{file}__{clip.name}.png");
            }
            finally
            {
                Unsupported.SetRenderSettingsUseFogNoDirty(fog);
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (Proxy p in proxies)
                    Object.DestroyImmediate(p.baked);
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(strip);
            }
        }

        /// <summary>The Humanoid avatar for a model (its sub-asset) or a character prefab (its Animator's).</summary>
        public static Avatar FindAvatar(string assetPath, GameObject asset)
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Avatar>().FirstOrDefault();
            if (avatar != null || asset == null)
                return avatar;
            var animator = asset.GetComponent<Animator>();
            return animator != null ? animator.avatar : null;
        }

        /// <summary>A SkinnedMeshRenderer drawn through a static, CPU-baked copy.</summary>
        public struct Proxy
        {
            /// <summary>The hidden source renderer.</summary>
            public SkinnedMeshRenderer source;

            /// <summary>Mesh the pose is baked into.</summary>
            public Mesh baked;

            /// <summary>Transform of the drawn copy.</summary>
            public Transform transform;
        }

        /// <summary>
        /// A manually rendered preview camera does not re-skin SkinnedMeshRenderers, so each
        /// one is hidden and drawn through a static proxy that BakeProxies updates.
        /// Property blocks (skin colour, item colours, face/eye textures) are carried over.
        /// </summary>
        public static List<Proxy> MakeProxies(GameObject character, Scene scene)
        {
            var proxies = new List<Proxy>();
            foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var proxy = new GameObject(smr.name + "_baked");
                SceneManager.MoveGameObjectToScene(proxy, scene);
                var baked = new Mesh { name = proxy.name };
                proxy.AddComponent<MeshFilter>().sharedMesh = baked;
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();
                proxyRenderer.sharedMaterials = smr.sharedMaterials;
                for (int m = 0; m < smr.sharedMaterials.Length; m++)
                {
                    var block = new MaterialPropertyBlock();
                    smr.GetPropertyBlock(block, m);
                    proxyRenderer.SetPropertyBlock(block, m);
                }
                smr.enabled = false;
                proxies.Add(new Proxy { source = smr, baked = baked, transform = proxy.transform });
            }
            return proxies;
        }

        /// <summary>Bakes each proxy's current pose (world scale included) and places it.</summary>
        public static void BakeProxies(IEnumerable<Proxy> proxies)
        {
            foreach (Proxy p in proxies)
            {
                p.source.BakeMesh(p.baked, true);
                p.transform.SetPositionAndRotation(p.source.transform.position, p.source.transform.rotation);
            }
        }

        /// <summary>
        /// White key from the camera side plus a soft fill, so colours read true instead
        /// of being dominated by the open scene's sky ambient.
        /// </summary>
        public static void AddStudioLights(Scene scene)
        {
            foreach (var (lightName, euler, intensity) in new[]
            {
                ("Key", new Vector3(30f, 225f, 0f), 1.4f),
                ("Fill", new Vector3(15f, 120f, 0f), 0.5f),
            })
            {
                var light = new GameObject(lightName).AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = intensity;
                light.transform.rotation = Quaternion.Euler(euler);
                SceneManager.MoveGameObjectToScene(light.gameObject, scene);
            }
        }

        /// <summary>An orthographic preview camera in <paramref name="scene"/> rendering into <paramref name="target"/>.</summary>
        public static Camera MakeCamera(Scene scene, RenderTexture target, float orthographicSize)
        {
            var camera = new GameObject("Camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.scene = scene;
            camera.cameraType = CameraType.Preview;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 20f;
            camera.targetTexture = target;
            return camera;
        }

        /// <summary>Renders <paramref name="camera"/> and copies the frame into <paramref name="image"/> at (x, y).</summary>
        public static void RenderInto(Camera camera, RenderTexture target, Texture2D image, int x, int y)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
                RenderPipeline.SubmitRenderRequest(camera, request);
            else
                camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), x, y);
            image.Apply();
            RenderTexture.active = null;
        }

        /// <summary>Writes <paramref name="image"/> to &lt;outDir&gt;/&lt;file&gt; and returns the path.</summary>
        public static string WritePng(Texture2D image, string outDir, string file)
        {
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, file).Replace('\\', '/');
            File.WriteAllBytes(path, image.EncodeToPNG());
            Debug.Log($"[HumanoidCheck] wrote {Path.GetFullPath(path)}");
            return path;
        }

        static void ReportMesh(GameObject character)
        {
            foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = smr.sharedMesh;
                Debug.Log($"[HumanoidCheck] {smr.name}: {mesh.vertexCount} verts, {TriangleCount(mesh)} tris, " +
                          $"{mesh.subMeshCount} submeshes, {mesh.blendShapeCount} blend shapes, {smr.bones.Length} bones, " +
                          $"bind-pose height {mesh.bounds.size.y:0.000} m");
            }
        }

        /// <summary>Triangle count that works on non-readable meshes too.</summary>
        public static long TriangleCount(Mesh mesh)
        {
            long indices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
                indices += mesh.GetIndexCount(s);
            return indices / 3;
        }
    }
}
