using System;
using System.Collections.Generic;
using System.Text;
using FratRow.Social;

namespace FratRow.Game
{
    /// <summary>
    /// Rewrites what a drunk brother says before anyone hears it (design/gdd/drunk-mechanic.md §3.3). Pure and deterministic
    /// for a given <see cref="GameRng"/> stream. Runs on the host only: clients receive the rewritten line, so they never need
    /// to agree on the randomness, and a modified client cannot opt out.
    /// Example: <c>string said = DrunkSpeech.Rewrite("want to come to our party?", 0.6f, config.drunk.speech, rng);</c>
    /// </summary>
    public static class DrunkSpeech
    {
        /// <summary>
        /// Speech intensity 0..1 for a drunk level: 0 below <see cref="DrunkTuning.threshold"/>, then
        /// startIntensity + (1 − startIntensity) × ((drunk − threshold) / (max − threshold)) ^ curve.
        /// </summary>
        public static float Intensity(float drunk, DrunkTuning t)
        {
            if (t == null || drunk < t.threshold || t.max <= t.threshold) return 0f;
            double x = Math.Min(1.0, (drunk - t.threshold) / (t.max - t.threshold));
            var s = t.speech;
            return (float)(s.startIntensity + (1 - s.startIntensity) * Math.Pow(x, s.curve));
        }

        /// <summary>
        /// Returns the line as a drunk speaker says it. <paramref name="cosmeticOnly"/> (invite/rush button lines) keeps every
        /// word's letters intact so keyword-based intent still reads; free text gets the full treatment. Never longer than
        /// <paramref name="maxLength"/> and never empty for non-empty input.
        /// </summary>
        public static string Rewrite(string text, float intensity, DrunkSpeechTuning t, GameRng rng, bool cosmeticOnly = false,
            int maxLength = SocialRules.InputCharacters)
        {
            if (string.IsNullOrWhiteSpace(text) || intensity <= 0 || t == null || rng == null) return text;
            double k = Math.Min(1.0, intensity) * (cosmeticOnly ? t.cosmeticScale : 1.0);
            if (k <= 0) return text;
            var words = new List<string>(text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            bool lower = k >= t.lowercaseAt;

            // Long thoughts get lost halfway through.
            string trail = null;
            if (!cosmeticOnly && words.Count >= 8 && t.trailOffs.Count > 0 && rng.Chance(t.trailOff * k))
            {
                int keep = Math.Max(4, (int)Math.Round(words.Count * rng.Range(0.5f, 0.8f)));
                words.RemoveRange(keep, words.Count - keep);
                trail = Pick(t.trailOffs, rng);
            }

            var output = new List<string>(words.Count * 2);
            for (int w = 0; w < words.Count; w++)
            {
                if (w > 0 && t.interjections.Count > 0 && rng.Chance(t.interjection * k)) output.Add(Pick(t.interjections, rng));
                Split(words[w], out string lead, out string core, out string tail);
                if (core.Length == 0) { output.Add(words[w]); continue; }
                if (lower) core = core.ToLowerInvariant();
                if (!cosmeticOnly) core = Garble(core, k, t, rng);
                if (rng.Chance(t.stretch * k)) core = Stretch(core, rng);
                if (rng.Chance(t.shout * k)) core = core.ToUpperInvariant();
                string word = lead + core + tail;
                output.Add(word);
                if (rng.Chance(t.repeatWord * k)) output.Add(lead + core);
            }

            var sb = new StringBuilder(text.Length + 64);
            sb.Append(string.Join(" ", output));
            if (trail != null) sb.Append(trail);
            if (t.tangents.Count > 0 && rng.Chance(t.tangent * k))
            {
                string tangent = Pick(t.tangents, rng);
                sb.Append(EndsWithPunctuation(sb) ? " " : "... ").Append(lower ? tangent.ToLowerInvariant() : tangent);
            }
            string result = Clamp(sb.ToString(), maxLength);
            return string.IsNullOrWhiteSpace(result) ? Clamp(text, maxLength) : result;
        }

        /// <summary>Letter-level damage: slurring, a swapped pair, then per-letter typos, drops and doubles.</summary>
        static string Garble(string core, double k, DrunkSpeechTuning t, GameRng rng)
        {
            if (rng.Chance(t.slur * k)) core = Slur(core);
            var chars = new StringBuilder(core);
            if (chars.Length >= 4 && rng.Chance(t.swapLetters * k))
            {
                int i = rng.Range(1, chars.Length - 2);
                if (char.IsLetter(chars[i]) && char.IsLetter(chars[i + 1])) { char c = chars[i]; chars[i] = chars[i + 1]; chars[i + 1] = c; }
            }
            var sb = new StringBuilder(chars.Length + 4);
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetter(c) || char.IsSurrogate(c)) { sb.Append(c); continue; }
                if (chars.Length > 2 && rng.Chance(t.dropLetter * k)) continue;
                if (rng.Chance(t.typo * k)) c = Neighbour(c, rng);
                sb.Append(c);
                if (rng.Chance(t.doubleLetter * k)) sb.Append(c);
            }
            return sb.Length > 0 ? sb.ToString() : core;
        }

        /// <summary>s → sh (unless already "sh"), and a closing "ing" → "in'".</summary>
        static string Slur(string core)
        {
            var sb = new StringBuilder(core.Length + 4);
            for (int i = 0; i < core.Length; i++)
            {
                char c = core[i];
                sb.Append(c);
                bool next = i + 1 < core.Length;
                if ((c == 's' || c == 'S') && !(next && (core[i + 1] == 'h' || core[i + 1] == 'H')))
                    sb.Append(char.IsUpper(c) && next && char.IsUpper(core[i + 1]) ? 'H' : 'h');
            }
            string s = sb.ToString();
            if (s.Length > 4 && s.EndsWith("ing", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 1) + "'";
            return s;
        }

        /// <summary>Repeats the last letter 2–4 times ("so" → "sooo"); keeps every original letter.</summary>
        static string Stretch(string core, GameRng rng)
        {
            int last = core.Length - 1;
            while (last >= 0 && !char.IsLetter(core[last])) last--;
            if (last < 0 || char.IsSurrogate(core[last])) return core;
            return core.Substring(0, last + 1) + new string(core[last], rng.Range(2, 5)) + core.Substring(last + 1);
        }

        static readonly string[] KeyRows = { "qwertyuiop", "asdfghjkl", "zxcvbnm" };

        /// <summary>A key next to <paramref name="c"/> on a QWERTY keyboard (same case); non-ASCII letters are unchanged.</summary>
        static char Neighbour(char c, GameRng rng)
        {
            char lowerC = char.ToLowerInvariant(c);
            for (int row = 0; row < KeyRows.Length; row++)
            {
                int col = KeyRows[row].IndexOf(lowerC);
                if (col < 0) continue;
                var near = new List<char>(6);
                if (col > 0) near.Add(KeyRows[row][col - 1]);
                if (col + 1 < KeyRows[row].Length) near.Add(KeyRows[row][col + 1]);
                foreach (int r in new[] { row - 1, row + 1 })
                    if (r >= 0 && r < KeyRows.Length)
                        for (int dc = -1; dc <= 0; dc++)
                        {
                            // Rows are staggered: the keys above/below sit at col and col−1 (shifted by row).
                            int cc = col + dc + (r < row ? 1 : 0);
                            if (cc >= 0 && cc < KeyRows[r].Length) near.Add(KeyRows[r][cc]);
                        }
                char n = near[rng.Range(0, near.Count)];
                return char.IsUpper(c) ? char.ToUpperInvariant(n) : n;
            }
            return c;
        }

        /// <summary>Splits "(party?!" into lead "(", core "party", tail "?!"; the core keeps inner apostrophes/hyphens.</summary>
        static void Split(string word, out string lead, out string core, out string tail)
        {
            int start = 0, end = word.Length - 1;
            while (start <= end && !char.IsLetterOrDigit(word[start])) start++;
            while (end >= start && !char.IsLetterOrDigit(word[end])) end--;
            lead = word.Substring(0, start);
            core = start <= end ? word.Substring(start, end - start + 1) : "";
            tail = start <= end ? word.Substring(end + 1) : "";
        }

        static bool EndsWithPunctuation(StringBuilder sb) => sb.Length > 0 && ".!?".IndexOf(sb[sb.Length - 1]) >= 0;

        static string Pick(List<string> list, GameRng rng) => list[rng.Range(0, list.Count)];

        /// <summary>Cuts at the last space before the limit, never inside a surrogate pair.</summary>
        static string Clamp(string s, int maxLength)
        {
            if (maxLength <= 0 || s.Length <= maxLength) return s;
            int cut = s.LastIndexOf(' ', maxLength - 1);
            if (cut <= 0) cut = maxLength;
            if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;
            return s.Substring(0, cut).TrimEnd();
        }
    }
}
