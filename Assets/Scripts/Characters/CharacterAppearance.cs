using System.Collections.Generic;
using UnityEngine;

namespace FratRow.Characters
{
    /// <summary>
    /// Dresses a modular character (design/art/character-pipeline.md): spawns each
    /// CosmeticItem onto this character's skeleton, syncs body sliders onto the body
    /// and every worn item, hides skin covered by worn items, tints skin and items,
    /// applies the face and eye textures, and scales the character to its height.
    ///
    /// Skinned items are re-bound to the character's bones by name (every charkit
    /// export shares the SK_Human skeleton). Rigid items are parented to their Socket_*
    /// bones at the placement they were authored with on the rest-pose body. The body
    /// mesh is replaced by a copy without covered triangles, merged into one submesh
    /// so skin is a single draw call; copies are cached per item combination.
    ///
    /// The look comes from a CharacterRecipe (loaded on Start when assigned) or from
    /// the fields set directly; Apply always uses the current fields. Height is this
    /// transform's uniform scale, so place the character with a parent if gameplay
    /// needs to scale it too.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterAppearance : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly Dictionary<Mesh, Dictionary<string, Mesh>> TrimmedBodies = new Dictionary<Mesh, Dictionary<string, Mesh>>();

        [SerializeField] CharacterRecipe recipe;
        [SerializeField] SkinnedMeshRenderer body;
        [SerializeField] SkinnedMeshRenderer eyes;
        [SerializeField] Material skinMaterial;
        [SerializeField] Color skinColor = new Color(0.87f, 0.68f, 0.56f);
        [SerializeField, Range(0.9f, 1.1f)] float height = 1f;
        [SerializeField] Texture2D faceTexture;
        [SerializeField] Texture2D eyeTexture;
        [SerializeField] List<BodySlider> sliders = new List<BodySlider>();
        [SerializeField] List<WornItem> items = new List<WornItem>();
        [SerializeField] bool applyOnStart = true;

        [SerializeField, HideInInspector] Mesh sourceBodyMesh;
        [SerializeField, HideInInspector] Material[] sourceBodyMaterials;
        readonly List<GameObject> spawned = new List<GameObject>();

        /// <summary>The body renderer (SKM_F_Body), found by name if not assigned.</summary>
        public SkinnedMeshRenderer Body => body ? body : (body = FindRenderer("_Body"));

        /// <summary>The eyes renderer (SKM_F_Eyes), found by name if not assigned.</summary>
        public SkinnedMeshRenderer Eyes => eyes ? eyes : (eyes = FindRenderer("_Eyes"));

        /// <summary>Recipe loaded on Start (before Apply); null to set the look field by field.</summary>
        public CharacterRecipe Recipe
        {
            get => recipe;
            set => recipe = value;
        }

        /// <summary>Items to wear on the next Apply; later items win slot conflicts.</summary>
        public List<WornItem> Items => items;

        /// <summary>Body slider values to use on the next Apply.</summary>
        public List<BodySlider> Sliders => sliders;

        /// <summary>Skin colour (replaces the skin material's base colour via a property block).</summary>
        public Color SkinColor
        {
            get => skinColor;
            set => skinColor = value;
        }

        /// <summary>Whole-body scale applied to this transform (1 = the base body's height).</summary>
        public float Height
        {
            get => height;
            set => height = value;
        }

        /// <summary>Face makeup texture (body UVs, multiplied by the skin colour); null for plain skin.</summary>
        public Texture2D FaceTexture
        {
            get => faceTexture;
            set => faceTexture = value;
        }

        /// <summary>Iris texture for the eyes; null keeps the eye material's own.</summary>
        public Texture2D EyeTexture
        {
            get => eyeTexture;
            set => eyeTexture = value;
        }

        /// <summary>Material used for the merged body (falls back to the body's first material).</summary>
        public Material SkinMaterial
        {
            get => skinMaterial;
            set => skinMaterial = value;
        }

        void Start()
        {
            if (recipe != null)
                Load(recipe);
            if (applyOnStart)
                Apply();
        }

        /// <summary>Copies a recipe's look into this character's fields (call Apply to show it).</summary>
        public void Load(CharacterRecipe source)
        {
            if (source == null)
                return;
            skinColor = source.skinColor;
            height = source.height;
            faceTexture = source.faceTexture;
            eyeTexture = source.eyeTexture;
            sliders = new List<BodySlider>(source.sliders);
            items = new List<WornItem>(source.items);
        }

        /// <summary>Adds an item (replacing anything in its slots on the next Apply).</summary>
        public void Wear(CosmeticItem item, params Color[] colors) => items.Add(new WornItem(item, colors));

        /// <summary>Removes previously applied items and dresses the character from the current settings.</summary>
        [ContextMenu("Apply")]
        public void Apply()
        {
            Clear();
            SkinnedMeshRenderer bodyRenderer = Body;
            if (bodyRenderer == null)
            {
                Debug.LogError($"[CharacterAppearance] {name}: no body SkinnedMeshRenderer", this);
                return;
            }
            if (sourceBodyMesh == null)
            {
                sourceBodyMesh = bodyRenderer.sharedMesh;
                sourceBodyMaterials = bodyRenderer.sharedMaterials;
            }

            Dictionary<string, Transform> bones = IndexBones();
            int idCount = CountBodyIds(sourceBodyMesh);
            var masks = new List<bool[]>();
            foreach (WornItem worn in Worn())
            {
                CosmeticItem item = worn.item;
                if (item.kind == CosmeticKind.Skinned)
                {
                    SpawnSkinned(worn, bones);
                    if (item.bodyVertexCount == idCount)
                        masks.Add(item.HiddenMask());
                    else
                        Debug.LogWarning($"[CharacterAppearance] {item.id}: skin mask is for a {item.bodyVertexCount}-vertex body, " +
                                         $"this body has {idCount}; rebuild the item", item);
                }
                else
                {
                    SpawnRigid(worn, bones, bodyRenderer);
                }
            }

            bodyRenderer.sharedMesh = TrimmedBody(sourceBodyMesh, BodyMask.Union(masks, idCount));
            bodyRenderer.sharedMaterials = new[] { skinMaterial ? skinMaterial : sourceBodyMaterials[0] };
            SetLook(bodyRenderer, 0, skinColor, faceTexture);
            ApplySliders(bodyRenderer);
            SkinnedMeshRenderer eyesRenderer = Eyes;
            if (eyesRenderer != null)
            {
                for (int m = 0; m < eyesRenderer.sharedMaterials.Length; m++)
                    SetLook(eyesRenderer, m, Color.white, eyeTexture);
                ApplySliders(eyesRenderer);
            }
            transform.localScale = Vector3.one * height;
        }

        /// <summary>The items that survive slot resolution, in wearing order (missing items skipped).</summary>
        public List<WornItem> Worn()
        {
            var valid = items.FindAll(w => w.item != null && w.item.model != null);
            bool[] keep = CosmeticSlots.Resolve(valid.ConvertAll(w => w.item.slot));
            var worn = new List<WornItem>(valid.Count);
            for (int i = 0; i < valid.Count; i++)
                if (keep[i])
                    worn.Add(valid[i]);
            return worn;
        }

        /// <summary>Removes spawned items and restores the untrimmed body.</summary>
        [ContextMenu("Clear")]
        public void Clear()
        {
            foreach (GameObject go in spawned)
                DestroySafe(go);
            spawned.Clear();
            if (body != null && sourceBodyMesh != null)
            {
                body.sharedMesh = sourceBodyMesh;
                body.sharedMaterials = sourceBodyMaterials;
                body.SetPropertyBlock(null);
            }
            if (eyes != null)
                eyes.SetPropertyBlock(null);
        }

        SkinnedMeshRenderer FindRenderer(string nameSuffix)
        {
            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name.Contains(nameSuffix))
                    return smr;
            return null;
        }

        Dictionary<string, Transform> IndexBones()
        {
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name))
                    bones.Add(t.name, t);
            return bones;
        }

        void SpawnSkinned(WornItem worn, Dictionary<string, Transform> bones)
        {
            CosmeticItem item = worn.item;
            GameObject instance = Instantiate(item.model, transform);
            SkinnedMeshRenderer[] renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
                Debug.LogWarning($"[CharacterAppearance] {item.id} is skinned but its model has no SkinnedMeshRenderer; " +
                                 "it must import with a rig (Generic)", item);
            foreach (SkinnedMeshRenderer smr in renderers)
            {
                Transform[] sourceBones = smr.bones; // the getter copies the array
                var remapped = new Transform[sourceBones.Length];
                for (int i = 0; i < remapped.Length; i++)
                {
                    Transform source = sourceBones[i];
                    if (source == null || !bones.TryGetValue(source.name, out remapped[i]))
                        Debug.LogWarning($"[CharacterAppearance] {item.id}: bone '{source?.name}' not on {name}", this);
                }
                smr.bones = remapped;
                if (smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out Transform root))
                    smr.rootBone = root;
                smr.transform.SetParent(transform, false);
                smr.name = item.id;
                for (int m = 0; m < smr.sharedMaterials.Length; m++)
                    SetLook(smr, m, worn.ColorAt(m), null);
                ApplySliders(smr);
                spawned.Add(smr.gameObject);
            }
            DestroySafe(instance); // the item's own skeleton copy
        }

        void SpawnRigid(WornItem worn, Dictionary<string, Transform> bones, SkinnedMeshRenderer bodyRenderer)
        {
            CosmeticItem item = worn.item;
            GameObject instance = Instantiate(item.model, transform); // same scene as the character
            foreach (CosmeticAttachment attachment in item.attachments)
            {
                Transform node = FindChild(instance.transform, attachment.node);
                if (node == null || !bones.TryGetValue(attachment.socket, out Transform socket))
                {
                    Debug.LogWarning($"[CharacterAppearance] {item.id}: cannot attach '{attachment.node}' to '{attachment.socket}'", this);
                    continue;
                }
                // The node was authored in character rest space; keep that placement relative to the socket.
                Matrix4x4 nodeRest = instance.transform.worldToLocalMatrix * node.localToWorldMatrix;
                Matrix4x4 local = SocketRest(socket, bodyRenderer).inverse * nodeRest;
                node.SetParent(socket, false);
                node.localPosition = local.GetColumn(3);
                node.localRotation = local.rotation;
                node.localScale = local.lossyScale;
                node.name = $"{item.id}:{attachment.socket}";
                Renderer renderer = node.GetComponent<Renderer>();
                for (int m = 0; renderer != null && m < renderer.sharedMaterials.Length; m++)
                    SetLook(renderer, m, worn.ColorAt(m), null);
                spawned.Add(node.gameObject);
            }
            DestroySafe(instance);
        }

        /// <summary>
        /// The socket's rest-pose transform in character space, from its nearest skinned
        /// ancestor's bind pose (sockets never deform, so they have no bind pose of their own).
        /// </summary>
        Matrix4x4 SocketRest(Transform socket, SkinnedMeshRenderer bodyRenderer)
        {
            Matrix4x4 chain = Matrix4x4.identity;
            Transform t = socket;
            Mesh mesh = sourceBodyMesh;
            Transform[] bodyBones = bodyRenderer.bones;
            while (t != null && t != transform)
            {
                int index = System.Array.IndexOf(bodyBones, t);
                if (index >= 0)
                {
                    Matrix4x4 meshToRoot = transform.worldToLocalMatrix * bodyRenderer.transform.localToWorldMatrix;
                    return meshToRoot * mesh.bindposes[index].inverse * chain;
                }
                chain = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * chain;
                t = t.parent;
            }
            return chain;
        }

        static Transform FindChild(Transform root, string childName)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName)
                    return t;
            return null;
        }

        void ApplySliders(SkinnedMeshRenderer smr)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null)
                return;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                smr.SetBlendShapeWeight(i, 0f);
            foreach (BodySlider slider in sliders)
            {
                int index = mesh.GetBlendShapeIndex(slider.name);
                if (index >= 0)
                    smr.SetBlendShapeWeight(index, Mathf.Clamp01(slider.value) * 100f);
            }
        }

        static void SetLook(Renderer renderer, int materialIndex, Color color, Texture texture)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, materialIndex);
            block.SetColor(BaseColor, color);
            if (texture != null)
                block.SetTexture(BaseMap, texture);
            renderer.SetPropertyBlock(block, materialIndex);
        }

        static int CountBodyIds(Mesh mesh)
        {
            var ids = new List<Vector2>();
            mesh.GetUVs(1, ids);
            int max = -1;
            foreach (Vector2 id in ids)
                max = Mathf.Max(max, Mathf.RoundToInt(id.x));
            return max + 1;
        }

        static Mesh TrimmedBody(Mesh source, bool[] hidden)
        {
            string key = BodyMask.Key(hidden);
            if (!TrimmedBodies.TryGetValue(source, out Dictionary<string, Mesh> perMask))
                TrimmedBodies[source] = perMask = new Dictionary<string, Mesh>();
            if (perMask.TryGetValue(key, out Mesh cached) && cached != null)
                return cached;

            var uv2 = new List<Vector2>();
            source.GetUVs(1, uv2);
            if (uv2.Count != source.vertexCount)
            {
                Debug.LogError($"[CharacterAppearance] {source.name} has no body vertex ids (uv2); skin cannot be masked");
                return source;
            }
            var ids = new int[uv2.Count];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = Mathf.RoundToInt(uv2[i].x);

            var triangles = new List<int>();
            for (int s = 0; s < source.subMeshCount; s++)
                triangles.AddRange(source.GetTriangles(s));

            Mesh trimmed = Instantiate(source);
            trimmed.name = source.name + "_dressed";
            if (!Application.isPlaying)
                trimmed.hideFlags = HideFlags.DontSave; // an edit-mode preview must not end up inside a saved scene
            trimmed.subMeshCount = 1;
            trimmed.SetTriangles(BodyMask.FilterTriangles(triangles.ToArray(), ids, hidden), 0);
            perMask[key] = trimmed;
            return trimmed;
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
