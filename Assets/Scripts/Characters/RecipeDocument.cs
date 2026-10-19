using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FratRow.Characters
{
    /// <summary>
    /// The <c>&lt;id&gt;.recipe.json</c> file format, shared with the Blender tools
    /// (tools/blender/charkit/recipes.py, faces.py): a CharacterRecipe with its assets
    /// named instead of referenced. Parse and ToJson are inverses for every value the
    /// format holds (colours as 8-bit sRGB hex, numbers to three decimals), so a recipe
    /// edited in Unity, in the file or in Blender round-trips without diff noise.
    /// </summary>
    [Serializable]
    public class RecipeDocument
    {
        /// <summary>Format version this code reads and writes.</summary>
        public const int CurrentSchema = 1;

        /// <summary>Format version of this document.</summary>
        public int schema = CurrentSchema;

        /// <summary>Recipe id, also the asset and prefab name.</summary>
        public string id;

        /// <summary>Name shown to people.</summary>
        public string displayName;

        /// <summary>File name of the base character model.</summary>
        public string baseModel = "SK_Female_Base";

        /// <summary>Whole-body scale.</summary>
        public float height = 1f;

        /// <summary>Skin colour, #RRGGBB (sRGB).</summary>
        public string skin = "#DEAD8F";

        /// <summary>Non-zero body slider values.</summary>
        public Slider[] sliders = Array.Empty<Slider>();

        /// <summary>Face texture file name (no extension).</summary>
        public string faceTexture;

        /// <summary>Iris texture file name (no extension).</summary>
        public string eyeTexture;

        /// <summary>Worn items in wearing order.</summary>
        public Item[] items = Array.Empty<Item>();

        /// <summary>Face painting parameters (used by Blender to paint faceTexture).</summary>
        public Face face = new Face();

        /// <summary>Iris colour, #RRGGBB (used by Blender to paint eyeTexture).</summary>
        public string iris = "#6B8FB0";

        /// <summary>A body slider value.</summary>
        [Serializable]
        public class Slider
        {
            /// <summary>Blend shape name.</summary>
            public string name;

            /// <summary>Weight 0..1.</summary>
            public float value;
        }

        /// <summary>A worn item.</summary>
        [Serializable]
        public class Item
        {
            /// <summary>CosmeticItem id.</summary>
            public string id;

            /// <summary>Colour per material slot (#RRGGBB); missing slots keep the item's default.</summary>
            public string[] colors = Array.Empty<string>();
        }

        /// <summary>Face painting parameters; defaults match FACE_DEFAULTS in tools/blender/charkit/faces.py.</summary>
        [Serializable]
        public class Face
        {
            /// <summary>Brow colour.</summary>
            public string brows = "#6B4A35";

            /// <summary>Brow thickness multiplier.</summary>
            public float browThickness = 1f;

            /// <summary>Brow arch multiplier.</summary>
            public float browArch = 1f;

            /// <summary>Lash line colour.</summary>
            public string lashes = "#2A1D1D";

            /// <summary>Eyeliner wing length (0 = none).</summary>
            public float wing = 0.3f;

            /// <summary>Eyeshadow colour.</summary>
            public string eyeshadow = "#C99A8C";

            /// <summary>Eyeshadow strength 0..1.</summary>
            public float eyeshadowAlpha;

            /// <summary>Lip colour.</summary>
            public string lips = "#D9777F";

            /// <summary>Lip colour strength 0..1.</summary>
            public float lipAlpha = 0.85f;

            /// <summary>Blush colour.</summary>
            public string blush = "#F0A0A0";

            /// <summary>Blush strength 0..1.</summary>
            public float blushAlpha;

            /// <summary>Freckle strength 0..1 (0 = none).</summary>
            public float freckles;

            /// <summary>Freckle colour.</summary>
            public string freckleColor = "#B0674A";
        }

        /// <summary>Reads a recipe file; null when the text is not a recipe of a supported schema.</summary>
        public static RecipeDocument Parse(string json)
        {
            RecipeDocument doc;
            try
            {
                doc = JsonUtility.FromJson<RecipeDocument>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
            if (doc == null || doc.schema != CurrentSchema || string.IsNullOrEmpty(doc.id))
                return null;
            doc.sliders ??= Array.Empty<Slider>();
            doc.items ??= Array.Empty<Item>();
            doc.face ??= new Face();
            foreach (Item item in doc.items)
                item.colors ??= Array.Empty<string>();
            return doc;
        }

        /// <summary>The document as a recipe's look (items, textures and the base model are named, not resolved).</summary>
        public static RecipeDocument From(CharacterRecipe recipe)
        {
            FaceStyle f = recipe.face ?? new FaceStyle();
            return new RecipeDocument
            {
                id = recipe.id,
                displayName = recipe.displayName,
                baseModel = recipe.baseModel,
                height = recipe.height,
                skin = Hex(recipe.skinColor),
                sliders = recipe.sliders
                    .Where(s => !string.IsNullOrEmpty(s.name) && Round(s.value) > 0f)
                    .Select(s => new Slider { name = s.name, value = s.value }).ToArray(),
                faceTexture = recipe.faceTexture ? recipe.faceTexture.name : null,
                eyeTexture = recipe.eyeTexture ? recipe.eyeTexture.name : null,
                items = recipe.items
                    .Where(w => w.item != null)
                    .Select(w => new Item { id = w.item.id, colors = (w.colors ?? Array.Empty<Color>()).Select(Hex).ToArray() })
                    .ToArray(),
                face = new Face
                {
                    brows = Hex(f.brows), browThickness = f.browThickness, browArch = f.browArch,
                    lashes = Hex(f.lashes), wing = f.wing,
                    eyeshadow = Hex(f.eyeshadow), eyeshadowAlpha = f.eyeshadowAlpha,
                    lips = Hex(f.lips), lipAlpha = f.lipAlpha,
                    blush = Hex(f.blush), blushAlpha = f.blushAlpha,
                    freckles = f.freckles, freckleColor = Hex(f.freckleColor),
                },
                iris = Hex(recipe.iris),
            };
        }

        /// <summary>The face parameters as colours; <paramref name="problems"/> collects unreadable colours.</summary>
        public FaceStyle ToFaceStyle(List<string> problems)
        {
            var defaults = new FaceStyle();
            Face f = face ?? new Face();
            return new FaceStyle
            {
                brows = ColorOr(f.brows, defaults.brows, problems), browThickness = f.browThickness, browArch = f.browArch,
                lashes = ColorOr(f.lashes, defaults.lashes, problems), wing = f.wing,
                eyeshadow = ColorOr(f.eyeshadow, defaults.eyeshadow, problems), eyeshadowAlpha = f.eyeshadowAlpha,
                lips = ColorOr(f.lips, defaults.lips, problems), lipAlpha = f.lipAlpha,
                blush = ColorOr(f.blush, defaults.blush, problems), blushAlpha = f.blushAlpha,
                freckles = f.freckles, freckleColor = ColorOr(f.freckleColor, defaults.freckleColor, problems),
            };
        }

        /// <summary>The recipe file text: fixed layout, two-space indent, one slider or item per line.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"schema\": ").Append(schema.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"id\": ").Append(Str(id)).Append(",\n");
            sb.Append("  \"displayName\": ").Append(Str(displayName)).Append(",\n");
            sb.Append("  \"baseModel\": ").Append(Str(baseModel)).Append(",\n");
            sb.Append("  \"height\": ").Append(Num(height)).Append(",\n");
            sb.Append("  \"skin\": ").Append(Str(skin)).Append(",\n");
            sb.Append("  \"sliders\": [");
            for (int i = 0; i < sliders.Length; i++)
                sb.Append(i == 0 ? "\n" : ",\n")
                  .Append("    { \"name\": ").Append(Str(sliders[i].name)).Append(", \"value\": ").Append(Num(sliders[i].value)).Append(" }");
            sb.Append(sliders.Length > 0 ? "\n  ],\n" : "],\n");
            sb.Append("  \"faceTexture\": ").Append(Str(faceTexture)).Append(",\n");
            sb.Append("  \"eyeTexture\": ").Append(Str(eyeTexture)).Append(",\n");
            sb.Append("  \"items\": [");
            for (int i = 0; i < items.Length; i++)
                sb.Append(i == 0 ? "\n" : ",\n")
                  .Append("    { \"id\": ").Append(Str(items[i].id)).Append(", \"colors\": [")
                  .Append(string.Join(", ", (items[i].colors ?? Array.Empty<string>()).Select(Str))).Append("] }");
            sb.Append(items.Length > 0 ? "\n  ],\n" : "],\n");
            Face f = face ?? new Face();
            sb.Append("  \"face\": {\n");
            sb.Append("    \"brows\": ").Append(Str(f.brows)).Append(", \"browThickness\": ").Append(Num(f.browThickness))
              .Append(", \"browArch\": ").Append(Num(f.browArch)).Append(",\n");
            sb.Append("    \"lashes\": ").Append(Str(f.lashes)).Append(", \"wing\": ").Append(Num(f.wing)).Append(",\n");
            sb.Append("    \"eyeshadow\": ").Append(Str(f.eyeshadow)).Append(", \"eyeshadowAlpha\": ").Append(Num(f.eyeshadowAlpha)).Append(",\n");
            sb.Append("    \"lips\": ").Append(Str(f.lips)).Append(", \"lipAlpha\": ").Append(Num(f.lipAlpha)).Append(",\n");
            sb.Append("    \"blush\": ").Append(Str(f.blush)).Append(", \"blushAlpha\": ").Append(Num(f.blushAlpha)).Append(",\n");
            sb.Append("    \"freckles\": ").Append(Num(f.freckles)).Append(", \"freckleColor\": ").Append(Str(f.freckleColor)).Append("\n");
            sb.Append("  },\n");
            sb.Append("  \"iris\": ").Append(Str(iris)).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>#RRGGBB for a colour (8-bit sRGB).</summary>
        public static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

        /// <summary>Parses #RRGGBB (or any HTML colour) into a colour.</summary>
        public static bool TryColor(string hex, out Color color) => ColorUtility.TryParseHtmlString(hex ?? "", out color);

        /// <summary>The colour for <paramref name="hex"/>, or <paramref name="fallback"/> with a problem noted.</summary>
        public static Color ColorOr(string hex, Color fallback, List<string> problems)
        {
            if (TryColor(hex, out Color color))
                return color;
            problems?.Add($"colour '{hex}' is not #RRGGBB");
            return fallback;
        }

        static float Round(float value) => (float)Math.Round(value, 3, MidpointRounding.AwayFromZero);

        static string Num(float value) => Round(value).ToString("0.0##", CultureInfo.InvariantCulture);

        static string Str(string value)
        {
            if (value == null)
                return "\"\"";
            var sb = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\')
                    sb.Append('\\').Append(c);
                else if (c < ' ')
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else
                    sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
