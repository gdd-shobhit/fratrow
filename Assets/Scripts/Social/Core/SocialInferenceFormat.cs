using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FratRow.Social
{
    /// <summary>Ephemeral wire aliases reduce decoding work. Campaign IDs and host validation stay canonical.</summary>
    public sealed class SocialInferenceFormat
    {
        readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> display = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly ModelContext Context;
        public readonly string Grammar;
        public SocialInferenceFormat(ModelContext source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            int player = 1;
            foreach (string id in source.KnownPlayerIds.OrderBy(x => x))
                Add(id, source.KnownPlayerAliases.TryGetValue(id, out var stable) ? stable : "person_" + player++);
            if (!aliases.ContainsKey(source.PlayerId)) Add(source.PlayerId, "speaker");
            Add(source.FraternityId, "h1"); Add(source.PartyId, "party1");
            int evidence = 1;
            foreach (string id in source.EvidenceIds.Concat(source.GrievanceIds).Distinct().OrderBy(x => x)) Add(id, "e" + evidence++);
            foreach (var entry in canonical)
                display[entry.Key] = source.KnownPlayerNames.TryGetValue(entry.Value, out var name) ? name :
                    entry.Value == source.FraternityId ? "the fraternity" : entry.Value == source.PartyId ? "the party" : "that incident";
            string Alias(string id) => id != null && aliases.TryGetValue(id, out var alias) ? alias : id;
            // Replacing all host-issued IDs in a single pass avoids cascading replacements. The original
            // prompt, receipt, ledger and result are never modified. Arbitrary textual NPC IDs stay readable.
            string Render(string value)
            {
                if (value == null || aliases.Count == 0) return value;
                return Regex.Replace(value, string.Join("|", aliases.Keys.OrderByDescending(x => x.Length).Select(Regex.Escape)), m => aliases[m.Value]);
            }
            Context = new ModelContext {
                NpcId = source.NpcId, PlayerId = Alias(source.PlayerId), FraternityId = Alias(source.FraternityId),
                Voice = SocialVoice.Copy(source.Voice),
                GameSetting = source.GameSetting,
                StandingRevision = source.StandingRevision,
                PartyId = Alias(source.PartyId), PartyState = source.PartyState, ObservedEventId = Alias(source.ObservedEventId),
                Prompt = Render(source.CompactPrompt ?? source.Prompt),
                FactPrompt = Render(source.FactPrompt ?? source.CompactPrompt ?? source.Prompt),
                SystemPrompt = source.SystemPrompt +
                    "\nSpeak in first person as SELF, directly to the current PLAYER. Never speak as the player or narrator. " +
                    "Prior inference records are your interpretations, not player speech. Keep interpretation under 160 characters and reply under 300 characters; use one or two complete sentences.\n",
                RepairError = source.RepairError,
                EvidenceIds = new HashSet<string>(source.EvidenceIds.Select(Alias)),
                GrievanceIds = new HashSet<string>(source.GrievanceIds.Select(Alias)),
                GrievanceCandidateIds = new HashSet<string>(source.GrievanceCandidateIds.Select(Alias))
            };
            string grammar = SocialProposalGrammar.GrammarFor(Context);
            Grammar = grammar;
        }
        void Add(string id, string alias)
        {
            // Only host-generated GUIDs are shortened; never replace a name/word inside dialogue.
            if (id == null || !Regex.IsMatch(id, "^[0-9a-f]{32}$") || aliases.ContainsKey(id)) return;
            aliases.Add(id, alias); canonical.Add(alias, id);
        }
        public SocialProposal Decode(string json, IStateCodec codec)
        {
            var p = codec.Decode<SocialProposal>(json);
            if (p == null) throw new ArgumentException("Missing compact proposal");
            return new SocialProposal {
                schema = p.schema, decision = p.decision, evidence_ids = p.evidence_ids?.Select(RestoreId).ToArray(),
                relationship_effects = p.relationship_effects?.Select(x => x == null ? null : new RelationshipEffect { scope = x.scope, id = RestoreId(x.id), magnitude = x.magnitude }).ToArray(),
                condition = p.condition, grievance_event_id = RestoreId(p.grievance_event_id), resolve_grievance_event_id = RestoreId(p.resolve_grievance_event_id),
                interpretation = RestoreProse(p.interpretation, canonical), reply = RestoreProse(p.reply, display)
            };
        }
        public string RestoreId(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            if (canonical.TryGetValue(id, out var original)) return original;
            if (Context.EvidenceIds.Contains(id) || Context.GrievanceIds.Contains(id) || id == Context.PlayerId || id == Context.FraternityId) return id;
            throw new ArgumentException("Unknown inference ID alias");
        }
        public string RestoreReply(string text) => RestoreProse(text, display);
        public string RenderIds(string text) => aliases.Count == 0 ? text : Regex.Replace(text,
            string.Join("|", aliases.Keys.OrderByDescending(x => x.Length).Select(Regex.Escape)), m => aliases[m.Value]);
        static string RestoreProse(string text, Dictionary<string, string> values)
        {
            if (text == null || values.Count == 0) return text;
            // Aliases can be rebound on the next turn. Never persist them even if a model ignores
            // the display-name instruction. Do not cascade replacements through a player's name.
            return Regex.Replace(text, @"\b(?:" + string.Join("|", values.Keys.Select(Regex.Escape)) + @")\b", m => values[m.Value]);
        }
    }
}
