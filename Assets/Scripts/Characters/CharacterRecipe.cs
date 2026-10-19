using System;
using System.Collections.Generic;
using UnityEngine;

namespace FratRow.Characters
{
    /// <summary>A worn cosmetic and the colours its material slots should use.</summary>
    [Serializable]
    public struct WornItem
    {
        /// <summary>The cosmetic.</summary>
        public CosmeticItem item;

        /// <summary>Colour per material slot; slots without an entry keep the item's default colour.</summary>
        public Color[] colors;

        /// <summary>Creates a worn item with optional colour overrides (material slot order).</summary>
        public WornItem(CosmeticItem item, params Color[] colors)
        {
            this.item = item;
            this.colors = colors ?? Array.Empty<Color>();
        }

        /// <summary>The colour for material slot <paramref name="slot"/>: the override if given, else the item's default.</summary>
        public Color ColorAt(int slot)
        {
            if (colors != null && slot >= 0 && slot < colors.Length)
                return colors[slot];
            if (item != null && slot >= 0 && slot < item.materials.Length)
                return item.materials[slot].color;
            return Color.white;
        }
    }

    /// <summary>
    /// One character's look (design/art/character-pipeline.md): body sliders, height,
    /// skin, face and eye textures, and the worn items with their colours. Generated
    /// from Assets/Art/Characters/**/Recipes/*.recipe.json by CharacterRecipeImporter;
    /// CharacterAppearance.Load applies it to any character built on the same skeleton.
    /// </summary>
    [CreateAssetMenu(fileName = "Recipe", menuName = "Characters/Character Recipe")]
    public class CharacterRecipe : ScriptableObject
    {
        /// <summary>Unique id, also the generated prefab's name (e.g. CH_F_Blonde).</summary>
        public string id;

        /// <summary>Name shown to designers and players.</summary>
        public string displayName;

        /// <summary>Whole-body scale (1 = the base body's 1.66 m); the pipeline range is 0.95–1.05.</summary>
        [Range(0.95f, 1.05f)]
        public float height = 1f;

        /// <summary>Skin colour; multiplies the face texture.</summary>
        public Color skinColor = new Color(0.87f, 0.68f, 0.56f);

        /// <summary>Body slider values; sliders not listed stay at 0.</summary>
        public List<BodySlider> sliders = new List<BodySlider>();

        /// <summary>Makeup painted into the body UVs (white where there is no feature); null for plain skin.</summary>
        public Texture2D faceTexture;

        /// <summary>Iris texture for the eyes; null keeps the eye material's own.</summary>
        public Texture2D eyeTexture;

        /// <summary>Worn items; when two share a slot the later one wins (see CosmeticSlots).</summary>
        public List<WornItem> items = new List<WornItem>();
    }
}
