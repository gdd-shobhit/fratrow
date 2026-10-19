using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FratRow.Social;

namespace FratRow.Game
{
    /// <summary>
    /// Atomic, checksummed game-state save (<c>game.json</c> + <c>game.json.bak</c>) next to the social campaign journal.
    /// A corrupt primary falls back to the backup; two corrupt copies fail closed rather than silently resetting a campaign.
    /// Example: <c>var store = new GameSaveStore(dir, codec); var state = store.Load() ?? GameSession.NewCampaign(...); store.Save(state);</c>
    /// </summary>
    public sealed class GameSaveStore
    {
        readonly string path;
        readonly IStateCodec codec;

        public GameSaveStore(string directory, IStateCodec codec)
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "game.json");
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public string FilePath => path;

        public void Save(GameState state)
        {
            string json = codec.Encode(state);
            string line = Hash(json) + "|" + json;
            string temporary = path + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }

        /// <summary>Returns null when no save exists. Throws <see cref="InvalidDataException"/> when every copy is corrupt.</summary>
        public GameState Load()
        {
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return null;
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                var state = TryRead(candidate);
                if (state != null) return state;
            }
            throw new InvalidDataException("Game save and backup are both corrupt: " + path);
        }

        GameState TryRead(string file)
        {
            try
            {
                string text = File.ReadAllText(file, Encoding.UTF8);
                int bar = text.IndexOf('|');
                if (bar <= 0) return null;
                string json = text.Substring(bar + 1);
                if (Hash(json) != text.Substring(0, bar)) return null;
                var state = codec.Decode<GameState>(json);
                state?.Normalize();
                return state;
            }
            catch (Exception) { return null; } // Any decode failure means this copy is unusable; the caller tries the backup.
        }

        static string Hash(string json)
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                var sb = new StringBuilder(64);
                foreach (byte b in digest) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
