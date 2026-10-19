using NUnit.Framework;

namespace FratRow.Characters.Tests
{
    public class CosmeticSlotsTests
    {
        [Test]
        public void Resolve_DifferentSlots_KeepsEveryItem()
        {
            // Arrange
            var slots = new[] { CosmeticSlot.Hair, CosmeticSlot.Top, CosmeticSlot.Bottom, CosmeticSlot.Shoes, CosmeticSlot.Ears };

            // Act
            bool[] keep = CosmeticSlots.Resolve(slots);

            // Assert
            CollectionAssert.AreEqual(new[] { true, true, true, true, true }, keep);
        }

        [Test]
        public void Resolve_TwoTops_LaterTopWins()
        {
            // Act
            bool[] keep = CosmeticSlots.Resolve(new[] { CosmeticSlot.Top, CosmeticSlot.Hair, CosmeticSlot.Top });

            // Assert
            CollectionAssert.AreEqual(new[] { false, true, true }, keep);
        }

        [Test]
        public void Resolve_FullBodyAfterTopAndBottom_ReplacesBoth()
        {
            // Act
            bool[] keep = CosmeticSlots.Resolve(new[] { CosmeticSlot.Top, CosmeticSlot.Bottom, CosmeticSlot.FullBody });

            // Assert
            CollectionAssert.AreEqual(new[] { false, false, true }, keep);
        }

        [Test]
        public void Resolve_TopAfterFullBody_ReplacesFullBody()
        {
            // Act
            bool[] keep = CosmeticSlots.Resolve(new[] { CosmeticSlot.FullBody, CosmeticSlot.Top });

            // Assert: the dress needs the Top slot the later top took
            CollectionAssert.AreEqual(new[] { false, true }, keep);
        }

        [Test]
        public void Resolve_Empty_ReturnsEmpty()
        {
            // Act
            bool[] keep = CosmeticSlots.Resolve(new CosmeticSlot[0]);

            // Assert
            Assert.AreEqual(0, keep.Length);
        }
    }
}
