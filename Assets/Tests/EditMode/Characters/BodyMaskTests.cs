using System;
using NUnit.Framework;

namespace FratRow.Characters.Tests
{
    public class BodyMaskTests
    {
        [Test]
        public void Decode_SetBits_MarkThoseBodyVerticesHidden()
        {
            // Arrange: bits 0 and 2 of byte 0, bit 7 of byte 1 -> vertices 0, 2, 15
            string base64 = Convert.ToBase64String(new byte[] { 0b0000_0101, 0b1000_0000 });

            // Act
            bool[] mask = BodyMask.Decode(base64, 16);

            // Assert
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(i == 0 || i == 2 || i == 15, mask[i], $"vertex {i}");
        }

        [Test]
        public void Decode_EmptyString_HidesNothing()
        {
            // Act
            bool[] mask = BodyMask.Decode("", 8);

            // Assert
            Assert.AreEqual(8, mask.Length);
            Assert.IsFalse(Array.Exists(mask, hidden => hidden));
        }

        [Test]
        public void Union_TwoItems_HidesSkinCoveredByEither()
        {
            // Arrange
            var top = new[] { true, false, false, false };
            var skirt = new[] { false, false, true, false };

            // Act
            bool[] union = BodyMask.Union(new[] { top, null, skirt }, 4);

            // Assert
            CollectionAssert.AreEqual(new[] { true, false, true, false }, union);
        }

        [Test]
        public void Union_MaskForOtherBody_Throws()
        {
            // Arrange: a mask computed for a body with a different vertex count
            var stale = new bool[3];

            // Act / Assert
            Assert.Throws<ArgumentException>(() => BodyMask.Union(new[] { stale }, 4));
        }

        [Test]
        public void FilterTriangles_TriangleCrossingItemEdge_StaysVisible()
        {
            // Arrange: body vertices 0,1,2 are covered; 3 is not
            int[] triangles = { 0, 1, 2, 1, 2, 3 };
            int[] ids = { 0, 1, 2, 3 };
            var hidden = new[] { true, true, true, false };

            // Act
            int[] kept = BodyMask.FilterTriangles(triangles, ids, hidden);

            // Assert: only the fully covered triangle is dropped
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, kept);
        }

        [Test]
        public void FilterTriangles_SplitVertexCopy_UsesBodyIdNotMeshIndex()
        {
            // Arrange: Unity split body vertex 0 into mesh vertices 0 and 3 (e.g. a UV seam)
            int[] triangles = { 3, 1, 2 };
            int[] ids = { 0, 1, 2, 0 };
            var hidden = new[] { true, true, true };

            // Act
            int[] kept = BodyMask.FilterTriangles(triangles, ids, hidden);

            // Assert
            Assert.IsEmpty(kept);
        }

        [Test]
        public void Key_SameCoverage_SameKey_DifferentCoverage_DifferentKey()
        {
            // Arrange
            var a = new[] { true, false, true };
            var b = new[] { true, false, true };
            var c = new[] { true, true, true };

            // Act / Assert
            Assert.AreEqual(BodyMask.Key(a), BodyMask.Key(b));
            Assert.AreNotEqual(BodyMask.Key(a), BodyMask.Key(c));
        }
    }
}
