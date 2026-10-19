using System;
using UnityEngine;

namespace FratRow.Characters
{
    /// <summary>Where a cosmetic is worn. One item per slot; FullBody occupies Top and Bottom.</summary>
    public enum CosmeticSlot
    {
        Hair,
        Top,
        Bottom,
        FullBody,
        Legwear,
        Shoes,
        Ears,
        Face,
        HeadTop,
        Hand,
        Back,
    }

    /// <summary>Skinned items deform with the body; rigid items ride on a Socket_* bone.</summary>
    public enum CosmeticKind
    {
        Skinned,
        Rigid,
    }

    /// <summary>A rigid item's node and the socket bone it attaches to.</summary>
    [Serializable]
    public struct CosmeticAttachment
    {
        /// <summary>Name of the mesh node inside the item's model.</summary>
        public string node;

        /// <summary>Name of the Socket_* bone on the character.</summary>
        public string socket;
    }

    /// <summary>Default colour of one of the item's materials (matched by material slot index).</summary>
    [Serializable]
    public struct CosmeticMaterial
    {
        /// <summary>Material name as authored in Blender.</summary>
        public string name;

        /// <summary>Default base colour.</summary>
        public Color color;

        /// <summary>Whether recipes may override the colour.</summary>
        public bool recolorable;
    }

    /// <summary>
    /// One wearable item. Generated from the item's .cosmetic.json sidecar by
    /// CharacterModelImporter (Assets/Art/Characters/Editor); do not edit generated
    /// fields by hand — rebuild the item in Blender (tools/blender/charkit/items).
    /// </summary>
    [CreateAssetMenu(fileName = "Cosmetic", menuName = "Characters/Cosmetic Item")]
    public class CosmeticItem : ScriptableObject
    {
        /// <summary>Unique id, the model's file name (e.g. SKM_F_Top_OffShoulderCrop).</summary>
        public string id;

        /// <summary>Slot the item occupies.</summary>
        public CosmeticSlot slot;

        /// <summary>Skinned or rigid.</summary>
        public CosmeticKind kind;

        /// <summary>The item's imported model (FBX).</summary>
        public GameObject model;

        /// <summary>Body vertex count the skin mask was computed for.</summary>
        public int bodyVertexCount;

        /// <summary>Body topology the skin mask was computed for; must match the base body's.</summary>
        public string bodyTopologyHash;

        /// <summary>Skin mask: base64 bitset over body vertex ids (empty for rigid items).</summary>
        [TextArea(1, 3)]
        public string hiddenBodyVertices;

        /// <summary>Socket attachments (rigid items only).</summary>
        public CosmeticAttachment[] attachments = Array.Empty<CosmeticAttachment>();

        /// <summary>Default material colours, in material slot order.</summary>
        public CosmeticMaterial[] materials = Array.Empty<CosmeticMaterial>();

        /// <summary>The decoded skin mask (all false when the item hides nothing).</summary>
        public bool[] HiddenMask() => BodyMask.Decode(hiddenBodyVertices, bodyVertexCount);
    }

    /// <summary>A named body slider value (blend shape name, 0..1).</summary>
    [Serializable]
    public struct BodySlider
    {
        /// <summary>Blend shape name, e.g. Bust_Large.</summary>
        public string name;

        /// <summary>Weight 0..1 (Unity blend shapes take 0..100).</summary>
        [Range(0f, 1f)]
        public float value;

        /// <summary>Creates a slider value.</summary>
        public BodySlider(string name, float value)
        {
            this.name = name;
            this.value = value;
        }
    }
}
