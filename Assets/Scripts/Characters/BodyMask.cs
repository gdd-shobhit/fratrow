using System;
using System.Collections.Generic;

namespace FratRow.Characters
{
    /// <summary>
    /// Skin masks: which body vertices a cosmetic covers, so covered skin is not drawn
    /// (design/art/character-pipeline.md). Body vertices are named by the id stored in
    /// the body mesh's second UV channel (uv2.x), which survives Unity's vertex
    /// reordering and splitting. Pure logic, no engine types, so it is unit-testable.
    /// </summary>
    public static class BodyMask
    {
        /// <summary>Decodes a base64 little-endian bitset (bit i = body vertex i) as written by charkit.</summary>
        public static bool[] Decode(string base64, int vertexCount)
        {
            var mask = new bool[vertexCount];
            if (string.IsNullOrEmpty(base64))
                return mask;
            byte[] bytes = Convert.FromBase64String(base64);
            for (int i = 0; i < vertexCount; i++)
            {
                int b = i >> 3;
                if (b < bytes.Length && (bytes[b] & (1 << (i & 7))) != 0)
                    mask[i] = true;
            }
            return mask;
        }

        /// <summary>Union of several masks of the same length (null entries are skipped).</summary>
        public static bool[] Union(IEnumerable<bool[]> masks, int vertexCount)
        {
            var result = new bool[vertexCount];
            foreach (bool[] mask in masks)
            {
                if (mask == null)
                    continue;
                if (mask.Length != vertexCount)
                    throw new ArgumentException($"mask has {mask.Length} entries, expected {vertexCount}");
                for (int i = 0; i < vertexCount; i++)
                    result[i] |= mask[i];
            }
            return result;
        }

        /// <summary>
        /// Triangles to keep: a triangle is dropped only when all three of its vertices'
        /// body ids are masked, so triangles crossing an item's edge stay visible.
        /// <paramref name="vertexIds"/> maps mesh vertex index to body vertex id.
        /// </summary>
        public static int[] FilterTriangles(int[] triangles, int[] vertexIds, bool[] hidden)
        {
            var kept = new List<int>(triangles.Length);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (IsHidden(vertexIds[a], hidden) && IsHidden(vertexIds[b], hidden) && IsHidden(vertexIds[c], hidden))
                    continue;
                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
            }
            return kept.ToArray();
        }

        /// <summary>Stable key for a mask, used to cache the trimmed body mesh per item combination.</summary>
        public static string Key(bool[] hidden)
        {
            var bytes = new byte[(hidden.Length + 7) / 8];
            for (int i = 0; i < hidden.Length; i++)
                if (hidden[i])
                    bytes[i >> 3] |= (byte)(1 << (i & 7));
            return Convert.ToBase64String(bytes);
        }

        static bool IsHidden(int id, bool[] hidden) => id >= 0 && id < hidden.Length && hidden[id];
    }
}
