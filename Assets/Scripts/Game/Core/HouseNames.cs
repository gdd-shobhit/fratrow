using System.Text;

namespace FratRow.Game
{
    /// <summary>
    /// Rules for the players' house name the host types in the lobby (design: core-game-loop §3.2).
    /// Example: <c>HouseNames.Sanitize("  Alpha\tBeta  ")</c> returns <c>"Alpha Beta"</c>.
    /// </summary>
    public static class HouseNames
    {
        /// <summary>Longest name the lobby accepts. Also fits the 64-byte replicated house name.</summary>
        public const int MaxLength = 32;

        /// <summary>
        /// Control characters become spaces, then the name is trimmed and capped at <see cref="MaxLength"/> characters
        /// (never splitting a surrogate pair). Null or blank gives "".
        /// </summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name) sb.Append(char.IsControl(c) ? ' ' : c);

            string clean = sb.ToString().Trim();
            if (clean.Length <= MaxLength) return clean;

            int cut = char.IsHighSurrogate(clean[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
            return clean.Substring(0, cut).TrimEnd();
        }
    }
}
