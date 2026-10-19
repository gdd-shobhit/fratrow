using System.Collections.Generic;

namespace FratRow.Characters
{
    /// <summary>
    /// Slot rules for worn items: one item per slot, and a FullBody item occupies both
    /// Top and Bottom. When items conflict the later one in the list wins, so adding an
    /// item replaces whatever it collides with. Pure logic, no engine types.
    /// </summary>
    public static class CosmeticSlots
    {
        /// <summary>
        /// Which items to keep, given each item's slot in wearing order:
        /// <c>keep[i]</c> is false when a later item takes any slot item i needs.
        /// </summary>
        public static bool[] Resolve(IReadOnlyList<CosmeticSlot> slots)
        {
            var keep = new bool[slots.Count];
            var taken = new HashSet<CosmeticSlot>();
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                CosmeticSlot[] needs = Occupies(slots[i]);
                bool free = true;
                foreach (CosmeticSlot slot in needs)
                    free &= !taken.Contains(slot);
                if (!free)
                    continue;
                keep[i] = true;
                foreach (CosmeticSlot slot in needs)
                    taken.Add(slot);
            }
            return keep;
        }

        /// <summary>The slots an item in <paramref name="slot"/> takes up.</summary>
        public static CosmeticSlot[] Occupies(CosmeticSlot slot) =>
            slot == CosmeticSlot.FullBody ? new[] { CosmeticSlot.Top, CosmeticSlot.Bottom } : new[] { slot };
    }
}
