using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FratRow.Characters.Tests
{
    public class CharacterAppearanceTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
                Object.DestroyImmediate(obj);
            created.Clear();
        }

        T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        CosmeticItem Item(string id, CosmeticSlot slot, params Color[] defaults)
        {
            var item = Track(ScriptableObject.CreateInstance<CosmeticItem>());
            item.id = id;
            item.slot = slot;
            item.model = Track(new GameObject(id));
            item.materials = System.Array.ConvertAll(defaults, c => new CosmeticMaterial { name = id, color = c, recolorable = true });
            return item;
        }

        [Test]
        public void ColorAt_SlotWithOverride_ReturnsOverride()
        {
            // Arrange
            var worn = new WornItem(Item("top", CosmeticSlot.Top, Color.blue, Color.white), Color.red);

            // Act / Assert
            Assert.AreEqual(Color.red, worn.ColorAt(0));
        }

        [Test]
        public void ColorAt_SlotWithoutOverride_ReturnsItemDefault()
        {
            // Arrange: one override for a two-material item
            var worn = new WornItem(Item("pants", CosmeticSlot.Bottom, Color.red, Color.white), Color.black);

            // Act / Assert
            Assert.AreEqual(Color.white, worn.ColorAt(1));
        }

        [Test]
        public void Load_Recipe_CopiesTheLook()
        {
            // Arrange
            var recipe = Track(ScriptableObject.CreateInstance<CharacterRecipe>());
            recipe.height = 1.04f;
            recipe.skinColor = new Color(0.9f, 0.8f, 0.7f);
            recipe.sliders.Add(new BodySlider("Waist_Narrow", 0.5f));
            recipe.items.Add(new WornItem(Item("hair", CosmeticSlot.Hair, Color.yellow)));
            var appearance = Track(new GameObject("character")).AddComponent<CharacterAppearance>();

            // Act
            appearance.Load(recipe);

            // Assert
            Assert.AreEqual(1.04f, appearance.Height);
            Assert.AreEqual(recipe.skinColor, appearance.SkinColor);
            Assert.AreEqual(1, appearance.Sliders.Count);
            Assert.AreEqual("hair", appearance.Items[0].item.id);
        }

        [Test]
        public void Load_ThenChangeItems_LeavesRecipeUntouched()
        {
            // Arrange
            var recipe = Track(ScriptableObject.CreateInstance<CharacterRecipe>());
            recipe.items.Add(new WornItem(Item("hair", CosmeticSlot.Hair)));
            var appearance = Track(new GameObject("character")).AddComponent<CharacterAppearance>();
            appearance.Load(recipe);

            // Act
            appearance.Wear(Item("top", CosmeticSlot.Top));

            // Assert
            Assert.AreEqual(1, recipe.items.Count);
            Assert.AreEqual(2, appearance.Items.Count);
        }

        [Test]
        public void Worn_SecondTopAndMissingItem_KeepsLaterTopAndSkipsMissing()
        {
            // Arrange
            var appearance = Track(new GameObject("character")).AddComponent<CharacterAppearance>();
            appearance.Wear(Item("crop", CosmeticSlot.Top));
            appearance.Wear(null);
            appearance.Wear(Item("cami", CosmeticSlot.Top));
            appearance.Wear(Item("skirt", CosmeticSlot.Bottom));

            // Act
            List<WornItem> worn = appearance.Worn();

            // Assert
            CollectionAssert.AreEqual(new[] { "cami", "skirt" }, worn.ConvertAll(w => w.item.id));
        }
    }
}
