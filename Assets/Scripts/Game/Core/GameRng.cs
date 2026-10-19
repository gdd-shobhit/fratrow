using System;

namespace FratRow.Game
{
    /// <summary>
    /// Deterministic SplitMix64 stream. Every draw in the core comes from a stream keyed by
    /// (campaign seed, purpose, a, b), so results are reproducible across runs and reloads and never
    /// depend on <c>string.GetHashCode</c> (randomized per process on .NET Core).
    /// Example: <c>var rng = GameRng.For(state.seed, "party", state.week, party.tick); if (rng.Chance(0.2)) ...</c>
    /// </summary>
    public sealed class GameRng
    {
        ulong state;

        public GameRng(ulong seed) { state = seed; }

        /// <summary>Stream for one purpose and coordinate pair.</summary>
        public static GameRng For(int seed, string purpose, long a = 0, long b = 0)
        {
            ulong h = 1469598103934665603UL; // FNV-1a 64
            foreach (char c in purpose ?? "") { h ^= c; h *= 1099511628211UL; }
            ulong mixed = Mix((ulong)(uint)seed ^ h) ^ Mix((ulong)a + 0x632BE59BD9B4E019UL) ^ Mix((ulong)b * 0x9E3779B97F4A7C15UL + 7);
            return new GameRng(mixed);
        }

        static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong() { state += 0x9E3779B97F4A7C15UL; return Mix(state); }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        public float Range(float min, float max) => (float)(min + (max - min) * NextDouble());

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
        }

        public bool Chance(double probability) => NextDouble() < probability;

        /// <summary>Floor of expected plus a Bernoulli draw of the fraction.</summary>
        public int Poissonish(double expected)
        {
            if (expected <= 0) return 0;
            int whole = (int)Math.Floor(expected);
            return whole + (Chance(expected - whole) ? 1 : 0);
        }

        /// <summary>Index chosen proportionally to non-negative weights; -1 if all are zero.</summary>
        public int Weighted(float[] weights)
        {
            double total = 0;
            foreach (var w in weights) total += Math.Max(0, w);
            if (total <= 0) return -1;
            double roll = NextDouble() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= Math.Max(0, weights[i]);
                if (roll < 0) return i;
            }
            return weights.Length - 1;
        }
    }
}
