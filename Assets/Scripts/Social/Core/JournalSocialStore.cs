using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FratRow.Social
{
    /// <summary>Write-ahead full-state transactions. Events inside the state remain immutable.
    /// A durable journal entry is the commit point; snapshots are only an optimization.</summary>
    public sealed class JournalSocialStore : ISocialStore
    {
        readonly object sync = new object();
        readonly object stateSync = new object();
        readonly string directory;
        readonly IStateCodec codec;
        readonly Action<string> fault;
        CampaignState state;
        bool poisoned;
        public string LastCheckpointError { get; private set; }

        public JournalSocialStore(string directory, IStateCodec codec, Func<CampaignState> create,
            Action<string> faultInjection = null)
        {
            this.directory = directory;
            this.codec = codec;
            fault = faultInjection;
            Directory.CreateDirectory(directory);
            state = Recover() ?? create();
            if (state.schema != SocialRules.Schema) throw new InvalidDataException("Unsupported social schema");
            if (state.sequence == 0) Transact(_ => { });
        }
        public CampaignState Read() { lock (stateSync) return Copy(state); }
        // Transact mutates a copy and then swaps the reference, so a published state is immutable.
        public CampaignState ReadShared() { lock (stateSync) return state; }
        CampaignState Copy(CampaignState value) => codec.Decode<CampaignState>(codec.Encode(value));
        string JournalPath => Path.Combine(directory, "social.journal");
        string SnapshotPath => Path.Combine(directory, "social.snapshot");

        public void Transact(Action<CampaignState> change)
        {
            lock (sync)
            {
                if (poisoned) throw new IOException("Reload social store after a journal write failure");
                var next = Read();
                change(next);
                next.sequence++;
                var bytes = Encoding.UTF8.GetBytes(Pack(next) + "\n");
                fault?.Invoke("before_append");
                try
                {
                    using (var file = new FileStream(JournalPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        file.Write(bytes, 0, bytes.Length);
                        file.Flush(true);
                    }
                }
                catch { poisoned = true; throw; }
                lock (stateSync) state = next;
                fault?.Invoke("after_append");
                if (next.sequence % 32 == 0)
                {
                    try { Checkpoint(); LastCheckpointError = null; }
                    catch (Exception e) { LastCheckpointError = e.Message; } // journal already committed
                }
            }
        }
        void Checkpoint()
        {
            fault?.Invoke("before_snapshot");
            string temporary = SnapshotPath + ".tmp";
            var data = Encoding.UTF8.GetBytes(Pack(state));
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { file.Write(data, 0, data.Length); file.Flush(true); }
            if (File.Exists(SnapshotPath)) File.Replace(temporary, SnapshotPath, SnapshotPath + ".bak");
            else File.Move(temporary, SnapshotPath);
            fault?.Invoke("after_snapshot");
            // Keep the complete journal in v1, allowing recovery even if both snapshots are lost.
        }
        CampaignState Recover()
        {
            CampaignState latest = null;
            foreach (string path in new[] { SnapshotPath, SnapshotPath + ".bak" })
            {
                if (!File.Exists(path)) continue;
                try { latest = Unpack(File.ReadAllText(path)); break; }
                catch (Exception e) when (e is InvalidDataException || e is FormatException || e is ArgumentException) { }
            }
            if (!File.Exists(JournalPath)) return latest;
            byte[] bytes = File.ReadAllBytes(JournalPath);
            int begin = 0;
            long previousSequence = 0;
            for (int end = 0; end < bytes.Length; end++)
            {
                if (bytes[end] != 10) continue;
                var entry = Unpack(Encoding.UTF8.GetString(bytes, begin, end - begin));
                if (entry.sequence != previousSequence + 1) throw new InvalidDataException("Social journal sequence gap");
                previousSequence = entry.sequence;
                if (latest != null && latest.campaignId != entry.campaignId)
                    throw new InvalidDataException("Mixed campaigns in social journal");
                if (latest == null || entry.sequence > latest.sequence) latest = entry;
                begin = end + 1;
            }
            if (begin != bytes.Length)
            {
                // Only an unterminated final record is considered a torn write.
                using (var file = new FileStream(JournalPath, FileMode.Open, FileAccess.Write, FileShare.Read))
                { file.SetLength(begin); file.Flush(true); }
            }
            return latest;
        }
        string Pack(CampaignState value)
        {
            byte[] payload = Encoding.UTF8.GetBytes(codec.Encode(value));
            return Hash(payload) + "|" + Convert.ToBase64String(payload);
        }
        CampaignState Unpack(string line)
        {
            int split = line.IndexOf('|');
            if (split != 64) throw new InvalidDataException("Invalid social journal record");
            byte[] payload = Convert.FromBase64String(line.Substring(split + 1));
            if (Hash(payload) != line.Substring(0, split)) throw new InvalidDataException("Social journal checksum mismatch");
            CampaignState value;
            try { value = codec.Decode<CampaignState>(Encoding.UTF8.GetString(payload)); }
            catch (Exception e) { throw new InvalidDataException("Invalid social state JSON", e); }
            if (value != null && value.schema != SocialRules.Schema) throw new NotSupportedException("Unsupported social state schema");
            if (value == null || value.sequence < 1 || string.IsNullOrEmpty(value.campaignId))
                throw new InvalidDataException("Invalid social state");
            return value;
        }
        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
