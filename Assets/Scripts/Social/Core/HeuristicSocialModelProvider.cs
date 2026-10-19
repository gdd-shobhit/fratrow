using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FratRow.Social
{
    /// <summary>Coarse speech cues used by the model-free provider. English only, like the grounded composer.
    /// Cues match whole words or phrases ("you in" must not match "you into"), so inflections are listed explicitly.</summary>
    public static class SpeechCues
    {
        static readonly string[] Insults = { "loser", "ugly", "stupid", "pathetic", "shut up", "hate you", "idiot", "dumb", "trash", "gross",
            "nobody likes", "nobody wants", "worthless", "creep", "creepy", "suck", "sucks", "annoying", "clown", "embarrassing" };
        static readonly string[] Apologies = { "sorry", "apologize", "apologise", "apologies", "my bad", "didn't mean", "forgive", "out of line" };
        static readonly string[] Invites = { "party", "parties", "partying", "come", "coming", "invite", "invited", "invitation", "saturday", "tonight",
            "weekend", "pull up", "you in", "be there", "rsvp", "join us" };
        static readonly string[] Rush = { "pledge", "pledging", "rush", "rushing", "join our", "brother", "brothers", "bid", "recruit", "become a member",
            "frat life", "join the house" };
        static readonly string[] Compliments = { "love", "lovely", "awesome", "cool", "great", "nice", "cute", "funny", "smart", "amazing", "respect", "legend" };
        static readonly string[] Pressure = { "come on", "you have to", "you better", "don't be lame", "whatever", "just come", "stop being" };
        static readonly System.Collections.Generic.Dictionary<string[], System.Text.RegularExpressions.Regex> Patterns =
            new[] { Insults, Apologies, Invites, Rush, Compliments, Pressure }.ToDictionary(cues => cues, cues =>
                new System.Text.RegularExpressions.Regex(@"\b(?:" + string.Join("|", cues.Select(System.Text.RegularExpressions.Regex.Escape)) + @")\b"));
        /// <summary>A letter repeated 3+ times, as DrunkSpeech stretches a word's last letter ("partyyy", "grosssss").</summary>
        static readonly System.Text.RegularExpressions.Regex Stretched = new System.Text.RegularExpressions.Regex(@"(\p{L})\1{2,}");
        static bool Any(string text, string[] cues)
        {
            var pattern = Patterns[cues];
            // Try the stretched run collapsed to one letter ("party") and to two ("gross"); real words never triple a letter.
            return pattern.IsMatch(text) || pattern.IsMatch(Stretched.Replace(text, "$1")) || pattern.IsMatch(Stretched.Replace(text, "$1$1"));
        }
        public static bool IsInsult(string t) => Any(Norm(t), Insults);
        public static bool IsApology(string t) => Any(Norm(t), Apologies);
        public static bool IsInvite(string t) => Any(Norm(t), Invites);
        public static bool IsRush(string t) => Any(Norm(t), Rush);
        public static bool IsCompliment(string t) => Any(Norm(t), Compliments);
        public static bool IsPressure(string t) => Any(Norm(t), Pressure);
        static string Norm(string t) => (t ?? "").ToLowerInvariant();
    }

    /// <summary>
    /// Deterministic social provider used when no language model is configured. It reads the same saved social state the
    /// model would see and selects grounded acts; the host composer writes the reply, and the proposal passes exactly the same
    /// validation and commit path as a model proposal. It is a gameplay fallback, not a claim of model-quality judgment.
    /// Example: <c>new SocialConversationService(world, codec, new HeuristicSocialModelProvider(world.Store.Read), access, requireGroundedProposal: true)</c>
    /// </summary>
    public sealed class HeuristicSocialModelProvider : ISocialModelProvider
    {
        readonly Func<CampaignState> read;
        readonly int seed;
        public int Calls { get; private set; }

        public HeuristicSocialModelProvider(Func<CampaignState> read, int seed = 1)
        {
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            this.seed = seed;
        }

        public Task<SocialProposal> GenerateAsync(ModelContext context, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Decide(read(), context, seed));
        }

        /// <summary>Pure decision function; same state + context + seed gives the same proposal.</summary>
        public static SocialProposal Decide(CampaignState s, ModelContext c, int seed)
        {
            var npc = SocialWorld.Npc(s, c.NpcId);
            string text = s.events.Find(e => e.eventId == c.ObservedEventId)?.text ?? "";
            var rng = new Random(unchecked(c.SamplingSeed * 31 + seed));
            bool insult = SpeechCues.IsInsult(text), apology = SpeechCues.IsApology(text), invite = SpeechCues.IsInvite(text),
                rush = SpeechCues.IsRush(text), compliment = SpeechCues.IsCompliment(text), pressure = SpeechCues.IsPressure(text);
            int trust = npc?.trust.Find(t => t.playerId == c.PlayerId)?.value ?? 0;
            int regard = npc?.fraternityRegard ?? 0;
            float npcPop = npc?.standing?.known == true ? npc.standing.popularity : 15;
            float housePop = s.standing?.known == true ? s.standing.popularity : 15;
            var concern = npc?.standing?.statusConcern ?? StatusConcern.Moderate;
            string tone = c.Voice?.tone ?? "casual";
            var ownGrievance = c.GroundedMemories.FirstOrDefault(m => m.actorId == c.PlayerId);
            var commitment = npc?.commitments.Find(x => x.partyId == c.PartyId);

            double score = 0.5 + regard / 200.0 + trust / 150.0 + ToneBias(tone) + (rng.NextDouble() - 0.5) * 0.36;
            double gap = housePop - npcPop;
            // Status matters most to status-conscious students; low-concern students judge the people, not the letters.
            score += concern == StatusConcern.High ? gap / 90.0 : concern == StatusConcern.Moderate ? gap / 220.0 : 0.05;
            if (s.playersRank == 1) score += 0.05;
            if (compliment) score += 0.05;
            if (apology) score += 0.05;
            if (pressure) score -= 0.12;
            if (insult) score -= 0.5;
            if (ownGrievance != null && !apology) score -= 0.25;
            if (commitment?.state == "deferred") score += 0.05;
            score += DrunkBias(tone, c.SpeakerCondition);

            var allowedDecisions = GroundedSocialTurn.Decisions(c).ToList();
            var allowedTopics = GroundedSocialTurn.Topics(c).ToList();
            string decision = "respond";
            // A rush pitch is about joining the house, not an RSVP; only explicit invitations ask for attendance.
            bool attendanceAsked = invite;
            if (insult && commitment != null && (commitment.state == "accepted" || commitment.state == "conditional") && rng.NextDouble() < 0.6)
                decision = "decline";
            else if (attendanceAsked && !insult && commitment?.state != "accepted")
            {
                if (score >= 0.64) decision = "accept";
                else if (score >= 0.56) decision = rng.NextDouble() < 0.4 ? "counteroffer" : "defer";
                else if (score >= 0.38) decision = "defer";
                else decision = "decline";
            }
            else if (attendanceAsked && insult) decision = "decline";
            if (!allowedDecisions.Contains(decision)) decision = decision == "counteroffer" && allowedDecisions.Contains("accept") ? "accept" : "respond";

            string attitude = insult ? (tone == "reserved" ? "wary" : "annoyed") : pressure ? "wary" :
                apology || compliment ? "warm" : decision == "accept" ? (tone == "playful" ? "playful" : "warm") :
                decision == "decline" ? (concern == StatusConcern.High ? "dismissive" : "neutral") : "neutral";
            string reason = insult || apology || pressure ? "personal_treatment" : decision == "defer" ? "uncertainty" :
                attendanceAsked && Math.Abs(gap) >= 15 && concern != StatusConcern.Low ? "house_reputation" : attendanceAsked ? "party_appeal" : "conversation";

            string topic = "none";
            if (ownGrievance != null && (insult || apology || attendanceAsked)) topic = "memory:" + ownGrievance.eventId;
            else if (!c.PriorDirectContact && (concern == StatusConcern.High || rng.NextDouble() < 0.3)) topic = "identity";
            else if (rush) topic = score > 0.55 ? "house_interest" : "house_doubt";
            else if (decision == "defer") topic = new[] { "guests", "vibe", "details" }[rng.Next(3)];
            else if (decision == "accept") topic = new[] { "vibe", "details", "none" }[rng.Next(3)];
            else if (decision == "decline" && !insult) topic = concern == StatusConcern.High ? "house_doubt" : "none";
            if (!allowedTopics.Contains(topic)) topic = "none";

            string trustDelta = insult ? "down" : pressure ? "down_small" : apology || compliment ? "up_small" :
                decision == "accept" && rng.NextDouble() < 0.5 ? "up_small" : "unchanged";
            string regardDelta = insult ? "down_small" : rush && score > 0.6 ? "up_small" :
                decision == "accept" && rng.NextDouble() < 0.4 ? "up_small" : decision == "decline" && concern == StatusConcern.High ? "down_small" : "unchanged";
            string grievance = insult && c.GrievanceCandidateIds.Contains(c.ObservedEventId) ? c.ObservedEventId : "";
            string resolve = "";
            if (apology && ownGrievance != null && c.GrievanceIds.Contains(ownGrievance.eventId) && rng.NextDouble() < 0.5 + trust / 200.0)
                resolve = ownGrievance.eventId;

            int phrasing = rng.Next(2);
            try
            {
                var proposal = Build(c, decision, attitude, reason, topic, phrasing, trustDelta, regardDelta, grievance, resolve);
                Check(c, proposal);
                return proposal;
            }
            catch (ArgumentException)
            {
                // Never fail a turn because a heuristic combination is not allowed in this context.
                var safe = Build(c, "respond", "neutral", "conversation", "none", 0, "unchanged", "unchanged", "", "");
                Check(c, safe);
                return safe;
            }
        }

        static double ToneBias(string tone) =>
            tone == "bubbly" ? 0.08 : tone == "laid-back" ? 0.05 : tone == "playful" ? 0.03 : tone == "reserved" || tone == "dry" ? -0.05 : 0;

        /// <summary>How this voice takes a drunk speaker: party animals roll with "drunk", reserved students are put off, nobody loves "wasted".</summary>
        static double DrunkBias(string tone, string condition)
        {
            if (!SpeakerConditions.IsNoticeable(condition)) return 0;
            bool wasted = condition == SpeakerConditions.Wasted;
            if (tone == "bubbly" || tone == "playful" || tone == "laid-back") return wasted ? -0.04 : 0.03;
            if (tone == "reserved" || tone == "dry") return wasted ? -0.18 : -0.08;
            return wasted ? -0.1 : -0.03;
        }

        static SocialProposal Build(ModelContext c, string decision, string attitude, string reason, string topic, int phrasing,
            string trust, string regard, string grievance, string resolve)
        {
            var p = new SocialProposal
            {
                schema = SocialRules.Schema, decision = decision, evidence_ids = new[] { c.ObservedEventId },
                relationship_effects = new[] {
                    new RelationshipEffect { scope = "player", id = c.PlayerId, magnitude = trust },
                    new RelationshipEffect { scope = "house", id = c.FraternityId, magnitude = regard } },
                condition = decision == "counteroffer" ? SocialRules.EntranceCondition : "none",
                grievance_event_id = grievance, resolve_grievance_event_id = resolve,
                grounded = new GroundedReply { attitude = attitude, reason = reason, topic = topic, phrasing = phrasing }
            };
            p.reply = GroundedSocialTurn.Compose(c, p);
            p.interpretation = GroundedSocialTurn.Interpretation(p);
            return p;
        }

        static void Check(ModelContext c, SocialProposal p)
        {
            GroundedSocialTurn.Validate(c, p);
            SocialConversationService.ValidateProposal(p, c);
        }

        public void Dispose() { }
    }
}
