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
    /// How the face texture is painted (tools/blender/charkit/faces.py reads these from
    /// the recipe file). Changing them in Unity updates the recipe file; the texture
    /// itself is repainted in Blender.
    /// </summary>
    [Serializable]
    public class FaceStyle
    {
        /// <summary>Brow colour.</summary>
        public Color brows = new Color32(0x6B, 0x4A, 0x35, 0xFF);

        /// <summary>Brow thickness multiplier.</summary>
        [Range(0.5f, 2f)] public float browThickness = 1f;

        /// <summary>Brow arch multiplier.</summary>
        [Range(0f, 2f)] public float browArch = 1f;

        /// <summary>Lash line colour.</summary>
        public Color lashes = new Color32(0x2A, 0x1D, 0x1D, 0xFF);

        /// <summary>Eyeliner wing length (0 = none).</summary>
        [Range(0f, 1.5f)] public float wing = 0.3f;

        /// <summary>Eyeshadow colour.</summary>
        public Color eyeshadow = new Color32(0xC9, 0x9A, 0x8C, 0xFF);

        /// <summary>Eyeshadow strength.</summary>
        [Range(0f, 1f)] public float eyeshadowAlpha;

        /// <summary>Lip colour.</summary>
        public Color lips = new Color32(0xD9, 0x77, 0x7F, 0xFF);

        /// <summary>Lip colour strength.</summary>
        [Range(0f, 1f)] public float lipAlpha = 0.85f;

        /// <summary>Blush colour.</summary>
        public Color blush = new Color32(0xF0, 0xA0, 0xA0, 0xFF);

        /// <summary>Blush strength.</summary>
        [Range(0f, 1f)] public float blushAlpha;

        /// <summary>Freckle strength (0 = none).</summary>
        [Range(0f, 1f)] public float freckles;

        /// <summary>Freckle colour.</summary>
        public Color freckleColor = new Color32(0xB0, 0x67, 0x4A, 0xFF);
    }

    /// <summary>
    /// One character's look (design/art/character-pipeline.md): body sliders, height,
    /// skin, face and eye textures, and the worn items with their colours. Mirrors
    /// Assets/Art/Characters/**/Recipes/&lt;id&gt;.recipe.json (RecipeDocument):
    /// CharacterRecipeImporter fills it from the file and every edit made in Unity is
    /// written back to the file. CharacterAppearance.Load applies it to any character
    /// built on the same skeleton.
    /// </summary>
    [CreateAssetMenu(fileName = "Recipe", menuName = "Characters/Character Recipe")]
    public class CharacterRecipe : ScriptableObject
    {
        /// <summary>Unique id, also the asset, file and prefab name (e.g. CH_F_Blonde).</summary>
        public string id;

        /// <summary>Name shown to designers and players.</summary>
        public string displayName;

        /// <summary>File name of the base character model the prefab is built on.</summary>
        public string baseModel = "SK_Female_Base";

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

        /// <summary>How Blender paints faceTexture.</summary>
        public FaceStyle face = new FaceStyle();

        /// <summary>Iris colour Blender paints eyeTexture with.</summary>
        public Color iris = new Color32(0x6B, 0x8F, 0xB0, 0xFF);

        /// <summary>The value of slider <paramref name="name"/> (0 when not listed).</summary>
        public float Slider(string name)
        {
            foreach (BodySlider s in sliders)
                if (s.name == name)
                    return s.value;
            return 0f;
        }

        /// <summary>Sets slider <paramref name="name"/>; 0 removes it from the list.</summary>
        public void SetSlider(string name, float value)
        {
            int index = sliders.FindIndex(s => s.name == name);
            if (value <= 0f)
            {
                if (index >= 0)
                    sliders.RemoveAt(index);
                return;
            }
            var slider = new BodySlider(name, Mathf.Clamp01(value));
            if (index >= 0)
                sliders[index] = slider; // in place: the file keeps its order
            else
                sliders.Add(slider);
        }

#if UNITY_EDITOR
        /// <summary>Editor only: raised when the recipe is edited in an inspector (or by undo), so its file and previews follow.</summary>
        public static event Action<CharacterRecipe> Edited;

        void OnValidate() => Edited?.Invoke(this);
#endif
    }
}
