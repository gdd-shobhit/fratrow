using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FratRow.Characters;
using UnityEditor;
using UnityEngine;

namespace FratRow.Art.Characters.Editor
{
    /// <summary>
    /// Import rules for character FBX files under Assets/Art/Characters
    /// (design/art/character-pipeline.md).
    ///
    /// Every character FBX: scale 1, blend shapes on, rig transforms kept (sockets
    /// are found by name), no cameras/lights/animation, and no generated lightmap
    /// UVs (uv2 carries the body vertex ids skin masks rely on).
    ///
    /// Base models (file name SK_* or CH_*) become Humanoid with an explicit
    /// Mixamo-name bone map and a T-pose reference skeleton. The meshes are bound
    /// in A-pose; Humanoid retargeting measures clips against the avatar's
    /// reference pose, so the reference must be T-pose or every clip plays with
    /// the arms offset. This is what the Avatar window's "Enforce T-Pose" does,
    /// applied automatically on the first import. Skinned cosmetics (SKM_*) import
    /// as Generic without an avatar (rig type None would strip the skin);
    /// CharacterAppearance re-binds them to the character. Rigid cosmetics (SM_*)
    /// import as static meshes.
    ///
    /// Each cosmetic's &lt;name&gt;.cosmetic.json sidecar becomes a CosmeticItem asset
    /// (&lt;name&gt;.asset) in the same folder, created or updated on import; each
    /// &lt;id&gt;.recipe.json becomes a CharacterRecipe and a character prefab
    /// (CharacterRecipeImporter). Textures in a Textures folder import as clamped sRGB.
    /// </summary>
    public class CharacterModelImporter : AssetPostprocessor
    {
        /// <summary>Folder the rules apply to.</summary>
        public const string Root = "Assets/Art/Characters/";

        /// <summary>File suffix of cosmetic sidecars written by tools/blender/charkit/cosmetic.py.</summary>
        public const string SidecarSuffix = ".cosmetic.json";

        /// <summary>
        /// Written to the importer's userData once the T-pose avatar is applied.
        /// Bump it when the bone map or T-pose logic changes to re-bake every base model.
        /// </summary>
        public const string AvatarVersion = "charkit-avatar-v1";

        /// <summary>True for files that carry their own Humanoid avatar (SK_*, CH_*).</summary>
        public static bool IsBaseModel(string assetPath)
        {
            string file = System.IO.Path.GetFileName(assetPath);
            return assetPath.StartsWith(Root)
                && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
                && (file.StartsWith("SK_") || file.StartsWith("CH_"));
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root))
                return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importBlendShapes = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.optimizeGameObjects = false;
            importer.generateSecondaryUV = false;
            // CharacterAppearance copies and trims the base body mesh, so it must be
            // CPU-readable; cosmetics are only re-bound, so they stay GPU-only.
            importer.isReadable = IsBaseModel(assetPath);
            if (IsBaseModel(assetPath))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            else if (Path.GetFileName(assetPath).StartsWith("SKM_"))
            {
                // Rig type None would drop the skin and import a static mesh.
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None; // rigid SM_* items
            }
        }

        void OnPostprocessModel(GameObject root)
        {
            var importer = (ModelImporter)assetImporter;
            if (!IsBaseModel(assetPath) || importer.userData == AvatarVersion)
                return;

            HumanDescription description = importer.humanDescription;
            description.human = HumanBoneMap.Build(root);
            description.skeleton = TPoseSkeleton.Build(root);
            description.upperArmTwist = 0.5f;
            description.lowerArmTwist = 0.5f;
            description.upperLegTwist = 0.5f;
            description.lowerLegTwist = 0.5f;
            description.armStretch = 0.05f;
            description.legStretch = 0.05f;
            description.feetSpacing = 0f;
            description.hasTranslationDoF = false;

            // Import settings must not change mid-import: apply and re-import afterwards.
            string path = assetPath;
            EditorApplication.delayCall += () =>
            {
                var again = (ModelImporter)AssetImporter.GetAtPath(path);
                if (again == null)
                    return;
                again.humanDescription = description;
                again.userData = AvatarVersion;
                again.SaveAndReimport();
            };
        }

        void OnPreprocessTexture()
        {
            // Face and eye textures (charkit.faces) are painted per character in body/eye UVs.
            if (!assetPath.StartsWith(Root) || !assetPath.Contains("/Textures/"))
                return;
            var importer = (TextureImporter)assetImporter;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 2;
            importer.alphaSource = TextureImporterAlphaSource.None;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported.Where(p => p.StartsWith(Root) && p.EndsWith(SidecarSuffix)))
                CosmeticSidecar.Import(path);
            // After the sidecars, so recipes find the items imported in the same batch.
            CharacterRecipeImporter.OnImported(imported.Where(p => p.StartsWith(Root) && p.EndsWith(CharacterRecipeImporter.RecipeSuffix)));
            foreach (string path in imported.Where(IsBaseModel))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                string state = importer.userData == AvatarVersion ? "T-pose reference" : "pending T-pose re-import";
                if (avatar == null)
                    Debug.LogWarning($"[CharacterModelImporter] {path}: no avatar ({state})");
                else
                    Debug.Log($"[CharacterModelImporter] {path}: avatar valid={avatar.isValid} human={avatar.isHuman}, " +
                              $"{importer.humanDescription.human.Length} human bones mapped ({state})");
            }
        }
    }

    /// <summary>Turns a charkit .cosmetic.json sidecar into a CosmeticItem asset next to it.</summary>
    public static class CosmeticSidecar
    {
        [Serializable]
        class Sidecar
        {
            public int schema;
            public string id;
            public string model;
            public BodyInfo body;
            public string slot;
            public string kind;
            public string hiddenBodyVertices;
            public Attachment[] attachments;
            public MaterialInfo[] materials;
        }

        [Serializable]
        class BodyInfo
        {
            public int vertexCount;
            public string topologyHash;
        }

        [Serializable]
        class Attachment
        {
            public string node;
            public string socket;
        }

        [Serializable]
        class MaterialInfo
        {
            public string name;
            public float[] color;
            public bool recolorable;
        }

        const int SupportedSchema = 1;

        /// <summary>Creates or updates &lt;folder&gt;/&lt;id&gt;.asset from the sidecar at <paramref name="jsonPath"/>.</summary>
        public static CosmeticItem Import(string jsonPath)
        {
            Sidecar data = JsonUtility.FromJson<Sidecar>(File.ReadAllText(jsonPath));
            if (data == null || data.schema != SupportedSchema || string.IsNullOrEmpty(data.id))
            {
                Debug.LogError($"[CosmeticSidecar] {jsonPath}: unreadable or unsupported schema {data?.schema}");
                return null;
            }
            string folder = Path.GetDirectoryName(jsonPath).Replace('\\', '/');
            string assetPath = $"{folder}/{data.id}.asset";
            var item = AssetDatabase.LoadAssetAtPath<CosmeticItem>(assetPath);
            bool created = item == null;
            if (created)
                item = ScriptableObject.CreateInstance<CosmeticItem>();

            item.id = data.id;
            if (!Enum.TryParse(data.slot, out CosmeticSlot slot))
            {
                Debug.LogError($"[CosmeticSidecar] {jsonPath}: unknown slot '{data.slot}' " +
                               $"(expected one of {string.Join(", ", Enum.GetNames(typeof(CosmeticSlot)))})");
                return null;
            }
            item.slot = slot;
            item.kind = string.Equals(data.kind, "rigid", StringComparison.OrdinalIgnoreCase) ? CosmeticKind.Rigid : CosmeticKind.Skinned;
            item.model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{data.model}");
            item.bodyVertexCount = data.body?.vertexCount ?? 0;
            item.bodyTopologyHash = data.body?.topologyHash;
            item.hiddenBodyVertices = data.hiddenBodyVertices ?? "";
            item.attachments = (data.attachments ?? Array.Empty<Attachment>())
                .Select(a => new CosmeticAttachment { node = a.node, socket = a.socket }).ToArray();
            item.materials = (data.materials ?? Array.Empty<MaterialInfo>())
                .Select(m => new CosmeticMaterial
                {
                    name = m.name,
                    color = m.color != null && m.color.Length >= 3 ? new Color(m.color[0], m.color[1], m.color[2]) : Color.white,
                    recolorable = m.recolorable,
                }).ToArray();

            if (item.model == null)
                Debug.LogWarning($"[CosmeticSidecar] {data.id}: model '{data.model}' not found next to the sidecar");
            if (created)
                AssetDatabase.CreateAsset(item, assetPath);
            else
                EditorUtility.SetDirty(item);
            AssetDatabase.SaveAssetIfDirty(item);
            Debug.Log($"[CosmeticSidecar] {assetPath}: {item.slot} {item.kind}, model={(item.model ? item.model.name : "missing")}, " +
                      $"mask={item.HiddenMask().Count(h => h)} body vertices");
            return item;
        }
    }

    /// <summary>Mixamo bone names (as exported by tools/blender/charkit) to Unity human bone names.</summary>
    public static class HumanBoneMap
    {
        static readonly (string bone, string human)[] Core =
        {
            ("Hips", "Hips"), ("Spine", "Spine"), ("Spine1", "Chest"), ("Spine2", "UpperChest"),
            ("Neck", "Neck"), ("Head", "Head"),
        };

        static readonly (string bone, string human)[] PerSide =
        {
            ("{0}Eye", "{0}Eye"), ("{0}Shoulder", "{0}Shoulder"),
            ("{0}Arm", "{0}UpperArm"), ("{0}ForeArm", "{0}LowerArm"), ("{0}Hand", "{0}Hand"),
            ("{0}UpLeg", "{0}UpperLeg"), ("{0}Leg", "{0}LowerLeg"), ("{0}Foot", "{0}Foot"),
            ("{0}ToeBase", "{0}Toes"),
        };

        static readonly (string mixamo, string unity)[] Fingers =
        {
            ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Pinky", "Little"),
        };

        static readonly string[] Segments = { "Proximal", "Intermediate", "Distal" };

        /// <summary>All (bone, human) pairs for the SK_Human skeleton.</summary>
        public static IEnumerable<(string bone, string human)> Pairs()
        {
            foreach (var pair in Core)
                yield return pair;
            foreach (string side in new[] { "Left", "Right" })
            {
                foreach (var (bone, human) in PerSide)
                    yield return (string.Format(bone, side), string.Format(human, side));
                foreach (var (mixamo, unity) in Fingers)
                    for (int i = 0; i < 3; i++)
                        yield return ($"{side}Hand{mixamo}{i + 1}", $"{side} {unity} {Segments[i]}");
            }
        }

        /// <summary>HumanBone entries for the bones that exist under <paramref name="root"/>.</summary>
        public static HumanBone[] Build(GameObject root)
        {
            var names = new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            var valid = new HashSet<string>(HumanTrait.BoneName);
            var bones = new List<HumanBone>();
            foreach (var (bone, human) in Pairs())
            {
                if (!names.Contains(bone))
                    continue;
                if (!valid.Contains(human))
                {
                    Debug.LogError($"[HumanBoneMap] '{human}' is not a Unity human bone name");
                    continue;
                }
                bones.Add(new HumanBone { boneName = bone, humanName = human, limit = new HumanLimit { useDefaultValues = true } });
            }
            return bones.ToArray();
        }
    }

    /// <summary>
    /// Computes a T-pose reference skeleton from an A-pose hierarchy without
    /// touching the imported objects: arms, forearms and hands are aimed straight
    /// out sideways, legs straight down. Everything else keeps its rest rotation.
    /// </summary>
    public static class TPoseSkeleton
    {
        /// <summary>SkeletonBone entries (every transform under <paramref name="root"/>) in T-pose.</summary>
        public static SkeletonBone[] Build(GameObject root)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            // The pose math below ignores scale; charkit exports bake every transform to scale 1.
            foreach (var t in transforms.Where(t => (t.localScale - Vector3.one).sqrMagnitude > 1e-6f))
                Debug.LogWarning($"[TPoseSkeleton] {t.name} has scale {t.localScale}; the T-pose reference may be off");
            var byName = transforms.GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var localRotation = transforms.ToDictionary(t => t, t => t.localRotation);

            Quaternion WorldRotation(Transform t) =>
                t == root.transform ? localRotation[t] : WorldRotation(t.parent) * localRotation[t];

            Vector3 WorldPosition(Transform t) =>
                t == root.transform ? t.localPosition : WorldPosition(t.parent) + WorldRotation(t.parent) * t.localPosition;

            void Aim(string bone, string child, Vector3 direction)
            {
                if (!byName.TryGetValue(bone, out var b) || !byName.TryGetValue(child, out var c))
                    return;
                Vector3 current = (WorldPosition(c) - WorldPosition(b)).normalized;
                Quaternion world = Quaternion.FromToRotation(current, direction) * WorldRotation(b);
                localRotation[b] = Quaternion.Inverse(WorldRotation(b.parent)) * world;
            }

            if (byName.TryGetValue("LeftArm", out var leftArm) && byName.TryGetValue("RightArm", out var rightArm))
            {
                Vector3 across = WorldPosition(leftArm) - WorldPosition(rightArm);
                across.y = 0f;
                Vector3 left = across.normalized;
                foreach (var (side, outward) in new[] { ("Left", left), ("Right", -left) })
                {
                    Aim($"{side}Arm", $"{side}ForeArm", outward);
                    Aim($"{side}ForeArm", $"{side}Hand", outward);
                    Aim($"{side}Hand", $"{side}HandMiddle1", outward);
                    Aim($"{side}UpLeg", $"{side}Leg", Vector3.down);
                    Aim($"{side}Leg", $"{side}Foot", Vector3.down);
                }
            }

            return transforms.Select(t => new SkeletonBone
            {
                name = t.name,
                position = t.localPosition,
                rotation = localRotation[t],
                scale = t.localScale,
            }).ToArray();
        }
    }
}
