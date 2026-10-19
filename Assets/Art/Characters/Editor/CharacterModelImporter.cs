using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FratRow.Art.Characters.Editor
{
    /// <summary>
    /// Import rules for character FBX files under Assets/Art/Characters
    /// (design/art/character-pipeline.md).
    ///
    /// Every character FBX: scale 1, blend shapes on, rig transforms kept (sockets
    /// are found by name), no cameras/lights/animation.
    ///
    /// Base models (file name SK_* or CH_*) become Humanoid with an explicit
    /// Mixamo-name bone map and a T-pose reference skeleton. The meshes are bound
    /// in A-pose; Humanoid retargeting measures clips against the avatar's
    /// reference pose, so the reference must be T-pose or every clip plays with
    /// the arms offset. This is what the Avatar window's "Enforce T-Pose" does,
    /// applied automatically on the first import.
    /// </summary>
    public class CharacterModelImporter : AssetPostprocessor
    {
        /// <summary>Folder the rules apply to.</summary>
        public const string Root = "Assets/Art/Characters/";

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
            if (IsBaseModel(assetPath))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
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

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
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
