using NUnit.Framework;
using UnityEngine;

namespace FratRow.Characters.Tests
{
    public class RecipeDocumentTests
    {
        // The layout ToJson writes; the recipe files in the repo use exactly this.
        const string Canonical =
            "{\n" +
            "  \"schema\": 1,\n" +
            "  \"id\": \"CH_F_Test\",\n" +
            "  \"displayName\": \"Test\",\n" +
            "  \"baseModel\": \"SK_Female_Base\",\n" +
            "  \"height\": 0.96,\n" +
            "  \"skin\": \"#FADDD0\",\n" +
            "  \"sliders\": [\n" +
            "    { \"name\": \"Hips_Wide\", \"value\": 0.45 },\n" +
            "    { \"name\": \"Bust_Small\", \"value\": 0.5 }\n" +
            "  ],\n" +
            "  \"faceTexture\": \"T_F_Face_Test\",\n" +
            "  \"eyeTexture\": \"T_F_Eyes_Test\",\n" +
            "  \"items\": [\n" +
            "    { \"id\": \"SKM_F_Hair_Bob\", \"colors\": [\"#1C1C22\"] },\n" +
            "    { \"id\": \"SM_F_Ear_HoopSmall\", \"colors\": [] }\n" +
            "  ],\n" +
            "  \"face\": {\n" +
            "    \"brows\": \"#A57B57\", \"browThickness\": 0.9, \"browArch\": 1.1,\n" +
            "    \"lashes\": \"#3A2727\", \"wing\": 0.35,\n" +
            "    \"eyeshadow\": \"#EFAAA2\", \"eyeshadowAlpha\": 0.3,\n" +
            "    \"lips\": \"#EC8C97\", \"lipAlpha\": 0.85,\n" +
            "    \"blush\": \"#F6A0AA\", \"blushAlpha\": 0.4,\n" +
            "    \"freckles\": 0.0, \"freckleColor\": \"#B0674A\"\n" +
            "  },\n" +
            "  \"iris\": \"#4C8FE6\"\n" +
            "}\n";

        [Test]
        public void ToJson_ParsedCanonicalText_ReproducesItExactly()
        {
            // Act
            string written = RecipeDocument.Parse(Canonical).ToJson();

            // Assert: editing in Unity must not reformat the file
            Assert.AreEqual(Canonical, written);
        }

        [Test]
        public void Parse_NoFaceBlock_UsesBlenderDefaults()
        {
            // Arrange
            const string minimal = "{ \"schema\": 1, \"id\": \"CH_F_Min\" }";

            // Act
            RecipeDocument doc = RecipeDocument.Parse(minimal);

            // Assert: the defaults of tools/blender/charkit/faces.py FACE_DEFAULTS
            Assert.AreEqual("#6B4A35", doc.face.brows);
            Assert.AreEqual(0.85f, doc.face.lipAlpha);
            Assert.AreEqual(0f, doc.face.freckles);
            Assert.AreEqual(0, doc.items.Length);
        }

        [Test]
        public void Parse_OtherSchema_ReturnsNull()
        {
            // Act / Assert
            Assert.IsNull(RecipeDocument.Parse("{ \"schema\": 2, \"id\": \"CH_F_Future\" }"));
            Assert.IsNull(RecipeDocument.Parse("not json"));
        }

        [Test]
        public void From_Recipe_OmitsZeroSlidersAndWritesHexColours()
        {
            // Arrange
            var recipe = ScriptableObject.CreateInstance<CharacterRecipe>();
            recipe.id = "CH_F_Test";
            recipe.skinColor = new Color32(0xC9, 0x93, 0x75, 0xFF);
            recipe.sliders.Add(new BodySlider("Hips_Wide", 0.45f));
            recipe.sliders.Add(new BodySlider("Butt_Large", 0f));

            // Act
            RecipeDocument doc = RecipeDocument.From(recipe);

            // Assert
            Assert.AreEqual("#C99375", doc.skin);
            Assert.AreEqual(1, doc.sliders.Length);
            Assert.AreEqual("Hips_Wide", doc.sliders[0].name);
            Object.DestroyImmediate(recipe);
        }

        [Test]
        public void SetSlider_ExistingSlider_KeepsItsPlaceInTheList()
        {
            // Arrange
            var recipe = ScriptableObject.CreateInstance<CharacterRecipe>();
            recipe.SetSlider("Hips_Wide", 0.9f);
            recipe.SetSlider("Butt_Large", 0.9f);

            // Act
            recipe.SetSlider("Hips_Wide", 0.45f);
            recipe.SetSlider("Butt_Large", 0f);

            // Assert: in place (no diff churn in the file), zero removes
            Assert.AreEqual(1, recipe.sliders.Count);
            Assert.AreEqual("Hips_Wide", recipe.sliders[0].name);
            Assert.AreEqual(0.45f, recipe.Slider("Hips_Wide"));
            Object.DestroyImmediate(recipe);
        }
    }
}
