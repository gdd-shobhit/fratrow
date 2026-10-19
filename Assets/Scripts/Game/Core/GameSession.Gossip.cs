using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FratRow.Social;

namespace FratRow.Game
{
    /// <summary>
    /// Campus rumor mill (design/gdd/campus-gossip.md). Only significant, real events (or deliberate smears) become stories.
    /// Stories move one hop per round between NPCs who are actually together, distort and lose names as they travel,
    /// stop when tellers keep meeting people who already know, and change opinions only once believed.
    /// Deterministic: every roll uses the campaign seed; iteration follows roster and story-id order.
    /// </summary>
    public sealed partial class GameSession
    {
        GossipTuning G => Config.gossip;

        /// <summary>Absolute in-game day (7 per week, Saturday = 5, Sunday = 6), used for story age.</summary>
        public int DayIndex => ((State.semester - 1) * Math.Max(1, Config.semesterWeeks) + State.week) * 7 + State.day;

        int gossipVersion;
        int opinionVersion = -1, opinionDay = -1;
        // Social ledger writes are queued and flushed as ONE transaction per tick/action/round: the social journal rewrites its
        // whole state per transaction, so a write per story would multiply its cost (design §6, measured in GossipProfile).
        // Ids are assigned here, so the core never waits for the write and the bridge may defer it.
        readonly List<GossipObservation> ledgerObservations = new List<GossipObservation>();
        readonly List<GossipShare> ledgerShares = new List<GossipShare>();
        readonly Dictionary<string, float> opinions = new Dictionary<string, float>();
        readonly Dictionary<string, float> gossipiness = new Dictionary<string, float>();
        Dictionary<string, GossipKnowledge> knowIndex;

        /// <summary>A chosen retelling. The teller's version is captured when chosen, so a teller who learns a sharper version
        /// later in the same round still passes on what they knew at its start (one hop per round, consistent ledger links).</summary>
        struct Tell
        {
            public string listener;
            public GossipKnowledge teller;
            public float tie, belief;
            public int detail, spice;
            public string ledger;
        }

        // ---------- Queries ----------

        /// <summary>0..1 talkativeness about other people: the roster value, else derived from clique, status concern and popularity.</summary>
        public float Gossipiness(string npcId)
        {
            if (npcId == null) return 0;
            if (gossipiness.TryGetValue(npcId, out float cached)) return cached;
            var def = RosterNpc(npcId);
            float g = 0;
            if (def != null && def.gossip >= 0) g = Clamp(def.gossip, 0, 1);
            else if (def != null)
            {
                g = def.guestClass == "MeanGirl" ? 0.7f : def.guestClass == "Jock" ? 0.4f : def.guestClass == "Nerd" ? 0.25f : 0.45f;
                var concern = CampusRoster.Concern(def);
                g += concern == StatusConcern.High ? 0.1f : concern == StatusConcern.Low ? -0.05f : 0f;
                g = Clamp(g + def.popularity / 200f, 0.05f, 0.95f);
            }
            gossipiness[npcId] = g;
            return g;
        }

        /// <summary>The campus hubs: the most gossipy NPCs, who hear institutional news (police blotter, Dean letters) first.</summary>
        public List<string> GossipHubs()
        {
            var ranked = Roster.npcs.Where(n => Npc(n.id) != null).OrderByDescending(n => Gossipiness(n.id)).ThenBy(n => n.id, StringComparer.Ordinal).ToList();
            var hubs = ranked.Where(n => Gossipiness(n.id) >= G.hubGossip).Take(Math.Max(1, G.hubCount)).Select(n => n.id).ToList();
            if (hubs.Count == 0 && ranked.Count > 0) hubs.Add(ranked[0].id);
            return hubs;
        }

        /// <summary>
        /// Hearsay opinion (−opinionCap..opinionCap) an NPC holds about a house from stories they believe. Members ignore gossip about
        /// their own house. Example: <c>session.Opinion("Chloe", "kappa")</c>.
        /// </summary>
        public float Opinion(string npcId, string houseId)
        {
            BuildOpinions();
            return opinions.TryGetValue(npcId + "|" + houseId, out float v) ? Clamp(v, -G.opinionCap, G.opinionCap) : 0f;
        }

        /// <summary>Hearsay opinion of one player (only stories that still name them count).</summary>
        public float PlayerOpinion(string npcId, string playerId) => Opinion(npcId, "player:" + playerId);

        /// <summary>Campus word of mouth about a house: mean hearsay opinion of every named NPC who is not a member.</summary>
        public float WordOfMouth(string houseId)
        {
            int n = 0;
            float sum = 0;
            foreach (var npc in State.npcs)
            {
                if (RosterNpc(npc.npcId) == null || npc.memberOf == houseId) continue;
                n++;
                sum += Opinion(npc.npcId, houseId);
            }
            return n == 0 ? 0 : sum / n;
        }

        /// <summary>Every story still spreading (or every remembered one), hottest first.</summary>
        public List<StoryView> Stories(bool activeOnly = true)
        {
            var list = new List<StoryView>();
            foreach (var s in State.stories.Where(x => !activeOnly || !x.retired).OrderByDescending(x => x.heat).ThenBy(x => x.id))
            {
                var def = Config.StoryKind(s.kind);
                var knowers = KnowersOf(s.id);
                list.Add(new StoryView
                {
                    id = s.id, kind = s.kind, topic = def?.topic ?? "", text = StoryText(s, 0, 0), houseId = s.houseId, otherHouseId = s.otherHouseId,
                    playerId = s.playerId, npcId = s.npcId, valence = def?.valence ?? 0, severity = s.severity, heat = s.heat,
                    knowers = knowers.Count, believers = knowers.Count(k => Believes(k)), exposed = s.exposed, retired = s.retired
                });
            }
            return list;
        }

        /// <summary>What one NPC has heard, in the version they would tell it (detail and exaggeration included).</summary>
        public List<HeardView> WhatNpcHeard(string npcId)
        {
            var list = new List<HeardView>();
            foreach (var k in State.gossip.Where(x => x.npcId == npcId).OrderByDescending(x => x.storyId))
            {
                var s = Story(k.storyId);
                if (s == null) continue;
                list.Add(new HeardView { storyId = s.id, kind = s.kind, text = StoryText(s, k.detail, k.spice), heardFrom = SourceName(k.heardFrom),
                    detail = k.detail, spice = k.spice, belief = k.belief });
            }
            return list;
        }

        /// <summary>
        /// Renders a story at a detail level (0 full, 1 no quote, 2 vague) and spice level (0 accurate .. 2 wild).
        /// Example: <c>StoryText(story, 0, 0)</c> → "Tyler from Alpha Beta Rho went off on Ashley: "...""
        /// </summary>
        public string StoryText(GossipStory s, int detail, int spice)
        {
            var def = Config.StoryKind(s.kind);
            string t = def == null || def.texts.Count == 0 ? s.kind : def.texts[Math.Max(0, Math.Min(spice, def.texts.Count - 1))];
            string house = HouseName(s.houseId);
            string other = string.IsNullOrEmpty(s.otherHouseId) ? "someone" : HouseName(s.otherHouseId);
            string player = Player(s.playerId)?.displayName ?? "someone";
            string npc = RosterNpc(s.npcId)?.displayName ?? (string.IsNullOrEmpty(s.npcId) ? "someone" : s.npcId);
            if (detail >= 2)
            {
                // Leveling: who exactly fades to "one of the X guys"; a victim's name fades too, a celebrity's never does.
                t = t.Replace("{player} from {house}", "one of the " + house + " guys");
                player = "one of the " + house + " guys";
                if (def?.topic == "said") npc = "someone";
            }
            string quote = detail == 0 && !string.IsNullOrEmpty(s.quote) ? ": \"" + s.quote + "\"" : "";
            t = t.Replace("{house}", house).Replace("{other}", other).Replace("{player}", player).Replace("{npc}", npc)
                .Replace("{quote}", quote).Replace("{detail}", s.detail ?? "").Trim();
            if (t.Length == 0) return t;
            t = char.ToUpperInvariant(t[0]) + t.Substring(1);
            if (".!?\"".IndexOf(t[t.Length - 1]) < 0) t += ".";
            return t;
        }

        internal GossipStory Story(int id) => State.stories.Find(s => s.id == id);
        /// <summary>Call after editing gossip state directly (dev tools/tests) so cached opinions rebuild.</summary>
        internal void GossipChanged() => gossipVersion++;
        internal List<GossipKnowledge> KnowersOf(int storyId) => State.gossip.Where(k => k.storyId == storyId).ToList();

        internal GossipKnowledge Know(string npcId, int storyId)
        {
            if (knowIndex == null || knowIndex.Count != State.gossip.Count)
            {
                knowIndex = new Dictionary<string, GossipKnowledge>();
                foreach (var k in State.gossip) knowIndex[k.npcId + "#" + k.storyId] = k;
            }
            return knowIndex.TryGetValue(npcId + "#" + storyId, out var hit) ? hit : null;
        }

        void AddKnowledge(GossipKnowledge k)
        {
            State.gossip.Add(k);
            if (knowIndex != null) knowIndex[k.npcId + "#" + k.storyId] = k;
            gossipVersion++;
        }

        bool Believes(GossipKnowledge k) => k.belief >= G.beliefThreshold || (k.tellers.Count >= 2 && k.belief >= G.multiSourceBelief);

        float Spice(int spice) => G.spiceMultiplier.Length == 0 ? 1f : G.spiceMultiplier[Math.Max(0, Math.Min(spice, G.spiceMultiplier.Length - 1))];

        /// <summary>Opinion points this knowledge contributes before house/player weights (§4.3).</summary>
        float EffectWeight(GossipStory s, GossipKnowledge k)
        {
            if (!Believes(k)) return 0;
            float fade = 1f - (DayIndex - s.createdDay) / (7f * Math.Max(1, G.memoryWeeks));
            if (fade <= 0) return 0;
            return s.severity / 100f * Spice(k.spice) * k.belief * Math.Min(1f, fade) * G.opinionScale;
        }

        void BuildOpinions()
        {
            if (opinionVersion == gossipVersion && opinionDay == DayIndex) return;
            opinions.Clear();
            var stories = new Dictionary<int, GossipStory>();
            foreach (var s in State.stories) stories[s.id] = s;
            void Add(string key, float v) { opinions.TryGetValue(key, out float cur); opinions[key] = cur + v; }
            float Biased(float v) => v > 0 ? v * G.positiveOpinionFactor : v;
            foreach (var k in State.gossip)
            {
                if (!stories.TryGetValue(k.storyId, out var s)) continue;
                var def = Config.StoryKind(s.kind);
                var npc = Npc(k.npcId);
                if (def == null || npc == null) continue;
                float w = EffectWeight(s, k);
                if (w <= 0) continue;
                if (s.houseId != "" && npc.memberOf != s.houseId) Add(k.npcId + "|" + s.houseId, Biased(def.valence * def.houseWeight * w));
                if (s.otherHouseId != "" && npc.memberOf != s.otherHouseId) Add(k.npcId + "|" + s.otherHouseId, Biased(def.otherHouseWeight * w));
                if (s.playerId != "" && k.detail <= 1) Add(k.npcId + "|player:" + s.playerId, Biased(def.valence * def.playerWeight * w));
            }
            opinionVersion = gossipVersion;
            opinionDay = DayIndex;
        }

        string HouseName(string houseId) => string.IsNullOrEmpty(houseId) ? "someone" : House(houseId)?.name ?? houseId;

        string SourceName(string heardFrom)
        {
            if (heardFrom == "witness") return "saw it";
            if (heardFrom.StartsWith("player:", StringComparison.Ordinal)) return Player(heardFrom.Substring(7))?.displayName ?? "a brother";
            if (heardFrom.StartsWith("house:", StringComparison.Ordinal)) return "a " + HouseName(heardFrom.Substring(6)) + " brother";
            return RosterNpc(heardFrom)?.displayName ?? heardFrom;
        }

        bool AboutPlayers(GossipStory s) => s.houseId == Config.playerHouseId || s.otherHouseId == Config.playerHouseId || Player(s.playerId) != null;

        /// <summary>One capped line of player speech: control and line/paragraph separators become spaces, invisible format characters
        /// (bidi overrides, zero-width) are dropped, double quotes become single quotes.</summary>
        string CleanQuote(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                var category = char.GetUnicodeCategory(c);
                if (category == UnicodeCategory.Format) continue;
                bool breaks = char.IsControl(c) || category == UnicodeCategory.LineSeparator || category == UnicodeCategory.ParagraphSeparator;
                sb.Append(breaks ? ' ' : c == '"' ? '\'' : c);
            }
            string q = string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            int max = Math.Max(10, G.quoteChars);
            if (q.Length <= max) return q;
            int cut = max - 1;
            if (char.IsHighSurrogate(q[cut - 1])) cut--; // never split an emoji
            return q.Substring(0, cut).TrimEnd() + "…";
        }

        // ---------- Seeding (design §3.3) ----------

        /// <summary>
        /// Creates or reinforces a story if it passes the significance gate (<c>severity ≥ seedThreshold</c>) and the caps.
        /// <paramref name="knowers"/> start out knowing it. With a null <paramref name="socialEventId"/> the host records one ledger
        /// observation they all witness; otherwise every knower must already be a witness of that event.
        /// Example: <c>Seed("party_raided", 72, guests, houseId: "player", location: "PartyHouse")</c>
        /// </summary>
        internal GossipStory Seed(string kind, float severity, IEnumerable<string> knowers, string houseId = "", string playerId = "",
            string npcId = "", string otherHouseId = "", string quote = "", string detail = "", string origin = "witness", bool fabricated = false,
            string socialEventId = null, string location = "campus", string heardFrom = "witness", Func<string, float> beliefFor = null, bool force = false)
        {
            if (!G.enabled || Config.StoryKind(kind) == null) return null;
            severity = Clamp(severity, 0, 100);
            if (!force && severity < G.seedThreshold) return null;
            var who = ValidKnowers(knowers);
            if (who.Count == 0) return null;
            houseId = houseId ?? ""; playerId = playerId ?? ""; npcId = npcId ?? ""; otherHouseId = otherHouseId ?? "";
            origin = origin ?? "witness"; detail = detail ?? "";
            // The same story while it's still going around is reinforced and refreshed, not told as new ("Kappa throws good parties").
            // Same source and claim too: two houses' smears are two stories, each traceable to its own liar.
            var same = State.stories.Find(s => !s.retired && s.kind == kind && s.houseId == houseId && s.playerId == playerId && s.npcId == npcId &&
                s.otherHouseId == otherHouseId && s.fabricated == fabricated && s.origin == origin && s.detail == detail);
            if (same != null)
            {
                same.severity = Math.Max(same.severity, severity);
                same.heat = Math.Min(100, Math.Max(same.heat, severity));
                same.createdDay = DayIndex;
                AddKnowers(same, who, socialEventId, location, heardFrom, beliefFor);
                gossipVersion++;
                return same;
            }
            // A deliberate player act (planting a rumor) always gets its story; the volume caps are for ambient campus chatter.
            bool playerMade = origin.StartsWith("player:", StringComparison.Ordinal);
            bool scandal = force || playerMade || severity >= G.capBypassSeverity;
            if (!scandal && State.storiesThisWeek >= G.maxNewStoriesPerWeek) return null;
            if (!scandal && State.stories.Count(s => s.kind == kind && s.createdDay >= State.gossipWeekStart) >= G.maxPerKindPerWeek) return null;
            var active = State.stories.Where(s => !s.retired).ToList();
            if (active.Count >= G.maxActiveStories)
            {
                var coldest = active.OrderBy(s => s.heat).ThenBy(s => s.id).First();
                if (coldest.heat >= severity && !force && !playerMade) return null;
                coldest.retired = true;
            }
            var story = new GossipStory
            {
                id = ++State.storySeq, kind = kind, houseId = houseId, playerId = playerId, npcId = npcId, otherHouseId = otherHouseId,
                quote = CleanQuote(quote), detail = detail, origin = origin, fabricated = fabricated, severity = severity,
                heat = severity, createdDay = DayIndex, semester = State.semester, week = State.week
            };
            State.stories.Add(story);
            State.storiesThisWeek++;
            AddKnowers(story, who, socialEventId, location, heardFrom, beliefFor);
            Emit(GameEventKind.Gossip, StoryText(story, 0, 0), houseId, playerId, npcId, value: story.id, flag: AboutPlayers(story));
            return story;
        }

        List<string> ValidKnowers(IEnumerable<string> ids)
        {
            var list = new List<string>();
            foreach (var id in ids ?? Enumerable.Empty<string>())
                if (!string.IsNullOrEmpty(id) && !list.Contains(id) && Npc(id) != null && RosterNpc(id) != null) list.Add(id);
            return list;
        }

        /// <summary>Gives NPCs firsthand (or source-level) knowledge of a story, recording one ledger observation unless one is supplied.</summary>
        internal void AddKnowers(GossipStory story, IEnumerable<string> ids, string socialEventId, string location, string heardFrom = "witness",
            Func<string, float> beliefFor = null)
        {
            var fresh = ValidKnowers(ids).Where(id => Know(id, story.id) == null).ToList();
            if (fresh.Count == 0) return;
            string ledger = socialEventId ?? "";
            if (ledger == "" && Recorded(story))
            {
                // One origin record they all witnessed. The actor is whoever did it: for a trashed house that's the culprit (or
                // nobody known), never the victim.
                string text = StoryText(story, 0, 0);
                ledger = NewLedgerId("gossip_" + story.kind, story.id.ToString(), text, string.Join(",", fresh));
                string actor = story.playerId != "" ? story.playerId : story.kind == "house_trashed" ? (story.otherHouseId != "" ? HouseName(story.otherHouseId) : null) :
                    story.houseId != "" ? HouseName(story.houseId) : null;
                string target = story.npcId != "" ? story.npcId : story.kind == "house_trashed" ? HouseName(story.houseId) :
                    story.otherHouseId != "" ? HouseName(story.otherHouseId) : null;
                // The shared record only colours memory if every one of them believes it (witnesses do; a smear's listeners may not).
                bool allBelieve = fresh.All(id => Clamp(beliefFor?.Invoke(id) ?? 1f, 0, 1) >= G.beliefThreshold);
                QueueObservation(ledger, "gossip_" + story.kind, actor, target, text, location, fresh, story, allBelieve ? Config.StoryKind(story.kind)?.valence ?? 0 : 0);
            }
            if (story.socialEventId == "") story.socialEventId = ledger;
            bool sourced = heardFrom != "witness";
            foreach (var id in fresh)
            {
                var k = new GossipKnowledge { storyId = story.id, npcId = id, heardFrom = heardFrom, detail = 0, spice = 0,
                    belief = Clamp(beliefFor?.Invoke(id) ?? 1f, 0, 1), learnedDay = DayIndex, ledger = ledger };
                if (sourced) k.tellers.Add(heardFrom);
                AddKnowledge(k);
            }
        }

        /// <summary>Stories that reach NPC conversation memory: those involving the players, or all with <c>ledgerAllStories</c>.</summary>
        bool Recorded(GossipStory s) => G.ledgerAllStories || AboutPlayers(s);

        /// <summary>
        /// Deterministic 32-hex social event id for a gossip record: a hash of campaign, counter and content. An identical replay after a
        /// reload reproduces the id (recorded once); a replay that diverged gets new ids instead of silently colliding.
        /// </summary>
        string NewLedgerId(params string[] content)
        {
            string key = State.campaignId + "|" + State.seed + "|" + (++State.ledgerSeq) + "|" + string.Join("|", content);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var hex = new StringBuilder(32);
                for (int i = 0; i < 16; i++) hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
            }
        }

        static string Topic(GossipStory s) => "story:" + s.id;

        /// <summary>Queues a world record about a story; subject/valence/topic let NPC memory know what each witness now holds.</summary>
        void QueueObservation(string eventId, string kind, string actor, string target, string text, string location, List<string> witnesses,
            GossipStory story, int valence) =>
            ledgerObservations.Add(new GossipObservation { eventId = eventId, kind = kind, actorId = actor, targetId = target, text = text,
                location = location ?? "campus", witnesses = new List<string>(witnesses), subjectId = story == null ? "" : Subject(story, 0),
                topicId = story == null ? "" : Topic(story), valence = valence });

        /// <summary>Last failure writing gossip to NPC memory ("" if none) and the count. NPC memory is best-effort (ADR-0002).</summary>
        public string LastGossipLedgerError { get; private set; } = "";
        public int GossipLedgerFailures { get; private set; }

        /// <summary>
        /// Hands every queued gossip record to the social backend as one transaction (ids were assigned when queued). Never throws:
        /// a failing journal costs NPC memories, not the game loop. The queues are emptied either way, so they stay bounded.
        /// </summary>
        internal void FlushGossipLedger()
        {
            if (ledgerObservations.Count == 0 && ledgerShares.Count == 0) return;
            var seen = ledgerObservations.ToList();
            var told = ledgerShares.ToList();
            ledgerObservations.Clear();
            ledgerShares.Clear();
            try { social.RecordGossip(seen, told); }
            catch (Exception e)
            {
                GossipLedgerFailures++;
                LastGossipLedgerError = e.GetType().Name + ": " + e.Message;
            }
        }

        // ---------- Spreading (design §3.4) ----------

        /// <summary>Weekday clock: each configured round runs once its in-game hour is reached.</summary>
        void GossipClock()
        {
            if (!G.enabled || State.phase != Phase.Weekday) return;
            while (State.gossipRound < G.roundHours.Length && ClockHour >= G.roundHours[State.gossipRound])
            {
                int round = State.gossipRound++;
                GossipRound("day", round, G.roundHours[round]);
            }
        }

        /// <summary>End of an in-game day: run rounds the clock skipped (early weekend), then cool, retire and forget stories.</summary>
        internal void FinishGossipDay()
        {
            if (G.enabled && State.phase == Phase.Weekday)
                while (State.gossipRound < G.roundHours.Length)
                {
                    int round = State.gossipRound++;
                    GossipRound("day", round, G.roundHours[round]);
                }
            State.gossipRound = 0;
            CoolStories();
        }

        /// <summary>
        /// One gossip round: every spreader may tell up to 1–3 contacts their juiciest story. Tells are chosen first and applied after,
        /// so a story moves at most one hop per round. Weekday rounds use schedule co-location; "brunch" uses who partied together.
        /// </summary>
        internal void GossipRound(string label, int round, float hour)
        {
            var live = new Dictionary<int, GossipStory>();
            foreach (var s in State.stories) if (!s.retired && s.heat > 0) live[s.id] = s;
            if (live.Count == 0) return;
            FlushGossipLedger(); // tellers' records must exist before retellings link to them
            var ids = Roster.npcs.Where(n => Npc(n.id) != null).Select(n => n.id).ToList();
            var place = new Dictionary<string, string>();
            foreach (var id in ids)
            {
                string at = label == "brunch" ? BrunchSpot(id) : CampusRoster.LocationAt(RosterNpc(id), State.day, hour);
                if (!string.IsNullOrEmpty(at)) place[id] = at;
            }
            var rng = Rng("gossip-" + label, round);
            var order = new Dictionary<string, ulong>();
            foreach (var id in ids) order[id] = rng.NextULong();
            var held = new Dictionary<string, List<GossipKnowledge>>();
            foreach (var k in State.gossip.OrderBy(k => k.storyId))
            {
                if (!live.ContainsKey(k.storyId) || !k.spreader || k.belief < G.spreadMinBelief) continue;
                if (!held.TryGetValue(k.npcId, out var mine)) held[k.npcId] = mine = new List<GossipKnowledge>();
                mine.Add(k);
            }
            var tells = new List<Tell>();
            foreach (var speaker in ids)
            {
                if (!held.TryGetValue(speaker, out var mine)) continue;
                float g = Gossipiness(speaker);
                int budget = 1 + (g >= 0.6f ? 1 : 0) + (g >= 0.85f ? 1 : 0);
                var contacts = ids.Where(o => o != speaker).Select(o => new KeyValuePair<string, float>(o, Tie(speaker, o, place)))
                    .Where(c => c.Value > 0).OrderByDescending(c => c.Value).ThenBy(c => order[c.Key]).ToList();
                foreach (var c in contacts)
                {
                    if (budget == 0) break;
                    GossipKnowledge best = null;
                    float bestJuice = 0;
                    foreach (var k in mine)
                    {
                        float j = Juice(live[k.storyId], k, speaker, c.Key, c.Value);
                        if (j > bestJuice) { best = k; bestJuice = j; }
                    }
                    if (best == null) continue;
                    double p = Clamp(G.tellBase * (0.4f + g) * bestJuice / 100f * c.Value, 0, 0.95f);
                    if (!rng.Chance(p)) continue;
                    tells.Add(new Tell { listener = c.Key, teller = best, tie = c.Value, belief = best.belief, detail = best.detail, spice = best.spice, ledger = best.ledger });
                    budget--;
                }
            }
            foreach (var t in tells) Hear(live[t.teller.storyId], t, rng);
            gossipVersion++;
        }

        string BrunchSpot(string npcId)
        {
            var npc = Npc(npcId);
            string went = npc == null ? "" : WeekendDestination(npc);
            return went == "" || went == "home" ? null : "party:" + went;
        }

        // Where each NPC partied Saturday, captured before Bid Day (joining a house clears an NPC's destination).
        Dictionary<string, string> weekendDestinations;

        void SnapshotWeekendDestinations()
        {
            weekendDestinations = new Dictionary<string, string>();
            foreach (var n in State.npcs) weekendDestinations[n.npcId] = n.destination;
        }

        string WeekendDestination(NpcGameState n) =>
            weekendDestinations != null && weekendDestinations.TryGetValue(n.npcId, out var went) ? went : n.destination;

        /// <summary>Tie strength between two NPCs this round (design §4.2). 0 = they don't talk.</summary>
        float Tie(string a, string b, Dictionary<string, string> place)
        {
            var na = Npc(a);
            var nb = Npc(b);
            var da = RosterNpc(a);
            var db = RosterNpc(b);
            if (na == null || nb == null || da == null || db == null) return 0;
            bool together = place.TryGetValue(a, out var pa) && place.TryGetValue(b, out var pb) && pa == pb;
            bool clique = da.guestClass != "None" && da.guestClass == db.guestClass;
            float hours = Familiarity(a, b);
            float t = 0;
            if (!string.IsNullOrEmpty(na.memberOf) && na.memberOf == nb.memberOf) t = G.tieBrother;
            if (together && clique) t = Math.Max(t, G.tieCliqueTogether);
            if (together && pa.StartsWith("party:", StringComparison.Ordinal)) t = Math.Max(t, G.tieSameParty);
            if (clique) t = Math.Max(t, G.tieClique);
            if (hours >= G.friendHours) t = Math.Max(t, G.tieFriend);
            if (together) t = Math.Max(t, G.tieTogether + Math.Min(G.familiarityCap, hours * G.familiarityPerHour));
            return t;
        }

        Dictionary<string, float> familiarity;

        /// <summary>Scheduled weekday hours two NPCs spend at the same place per week (static per roster; built once).</summary>
        internal float Familiarity(string a, string b)
        {
            if (familiarity == null)
            {
                familiarity = new Dictionary<string, float>();
                var slots = Roster.npcs.SelectMany(n => n.schedule.Where(e => e.day <= 4).Select(e => new KeyValuePair<string, RosterScheduleEntry>(n.id, e)))
                    .GroupBy(x => x.Value.day + "|" + x.Value.location);
                foreach (var slot in slots)
                {
                    var people = slot.ToList();
                    for (int i = 0; i < people.Count; i++)
                        for (int j = i + 1; j < people.Count; j++)
                        {
                            if (people[i].Key == people[j].Key) continue;
                            float shared = Overlap(people[i].Value, people[j].Value);
                            if (shared <= 0) continue;
                            string key = Pair(people[i].Key, people[j].Key);
                            familiarity.TryGetValue(key, out float sum);
                            familiarity[key] = sum + shared;
                        }
                }
            }
            return familiarity.TryGetValue(Pair(a, b), out float hours) ? hours : 0f;
        }

        static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;

        static float Overlap(RosterScheduleEntry x, RosterScheduleEntry y)
        {
            float total = 0;
            foreach (var p in Spans(x))
                foreach (var q in Spans(y))
                    total += Math.Max(0, Math.Min(p.Value, q.Value) - Math.Max(p.Key, q.Key));
            return total;
        }

        static IEnumerable<KeyValuePair<float, float>> Spans(RosterScheduleEntry e)
        {
            if (e.start <= e.end) { yield return new KeyValuePair<float, float>(e.start, e.end); yield break; }
            yield return new KeyValuePair<float, float>(e.start, 24);
            yield return new KeyValuePair<float, float>(0, e.end);
        }

        /// <summary>How worth telling a story is to this listener (0 = won't tell). Nasty stories need a close tie.</summary>
        internal float Juice(GossipStory s, GossipKnowledge k, string speaker, string listener, float tie)
        {
            var def = Config.StoryKind(s.kind);
            if (def == null || listener == s.npcId) return 0;
            if (def.valence < 0 && tie < G.negativeTieMin) return 0;
            var sp = Npc(speaker);
            var ln = Npc(listener);
            var ldef = RosterNpc(listener);
            float j = s.heat * (1 + 0.15f * k.spice) * (def.valence < 0 ? G.negativityBias : 1f);
            // Brothers bury their own house's dirt and brag about its wins.
            if (!string.IsNullOrEmpty(sp.memberOf) && sp.memberOf == s.houseId) j *= def.valence < 0 ? 0.2f : 1.5f;
            if (!string.IsNullOrEmpty(ln.memberOf) && (ln.memberOf == s.houseId || ln.memberOf == s.otherHouseId)) j *= 1.3f;
            if (ldef.guestClass == "MeanGirl" && (def.topic == "said" || def.topic == "status" || def.topic == "party")) j *= 1.15f;
            if (CampusRoster.Concern(ldef) == StatusConcern.High && (def.topic == "status" || def.topic == "rush")) j *= 1.15f;
            return j;
        }

        /// <summary>Belief a listener gives a story told by someone they have this tie with (design §4.3).</summary>
        float HearsayBelief(string kind, string houseId, bool exposed, float tellerBelief, string listener, float tie)
        {
            var def = Config.StoryKind(kind);
            var ln = Npc(listener);
            if (def == null || ln == null) return 0;
            float b = G.beliefBase + G.beliefTie * tie + (tellerBelief - 0.5f) * 0.3f + (Gossipiness(listener) - 0.5f) * 0.2f;
            if (!string.IsNullOrEmpty(ln.memberOf) && ln.memberOf == houseId) b = def.valence < 0 ? b * G.loyaltyDiscount : b + 0.15f;
            if (!string.IsNullOrEmpty(houseId)) b += def.valence * HouseRegard(listener, houseId) / 100f * 0.2f;
            if (exposed) b *= 0.3f;
            return Clamp(b, 0.05f, 0.95f);
        }

        float HouseRegard(string npcId, string houseId)
        {
            var h = House(houseId);
            if (h == null) return 0;
            return h.isPlayer ? social.RegardForPlayers(npcId) : RivalRegard(Npc(npcId), houseId);
        }

        void Hear(GossipStory s, Tell t, GameRng rng)
        {
            var teller = t.teller;
            var mine = Know(t.listener, s.id);
            if (mine != null)
            {
                // Maki–Thompson stifling: telling people who already know burns the teller out.
                if (++teller.stifles >= G.stifleAfter) teller.spreader = false;
                if (mine.heardFrom == "witness" || mine.heardFrom == teller.npcId || mine.tellers.Contains(teller.npcId)) return;
                bool believed = Believes(mine);
                if (mine.tellers.Count < 4) mine.tellers.Add(teller.npcId);
                mine.belief = Clamp(mine.belief + G.beliefRepeat * t.tie * (1 - mine.belief), 0, 0.97f);
                // Converging reports keep names alive (the more detailed version wins). A sharper version, or hearing it from a
                // second person until they believe it, replaces what this NPC holds in memory.
                int detail = Math.Min(2, t.detail + (rng.Chance(G.levelingChance) ? 1 : 0));
                bool sharper = detail < mine.detail;
                if (sharper) mine.detail = detail;
                if (sharper || (!believed && Believes(mine))) QueueShare(s, teller.npcId, t.ledger, mine);
                gossipVersion++;
                return;
            }
            var k = new GossipKnowledge
            {
                storyId = s.id, npcId = t.listener, heardFrom = teller.npcId,
                detail = Math.Min(2, t.detail + (rng.Chance(G.levelingChance) ? 1 : 0)),
                spice = Math.Min(G.maxSpice, t.spice + (rng.Chance(G.sharpenChance * Gossipiness(teller.npcId)) ? 1 : 0)),
                belief = HearsayBelief(s.kind, s.houseId, s.exposed, t.belief, t.listener, t.tie), learnedDay = DayIndex
            };
            k.tellers.Add(teller.npcId);
            AddKnowledge(k);
            QueueShare(s, teller.npcId, t.ledger, k);
        }

        /// <summary>
        /// Queues the ledger receipt for one retelling. Only a full-detail retelling links to the teller's record, so the listener's memory
        /// carries the alleged actor and original words; vaguer retellings are unlinked paraphrases ("one of the Kappa guys..."). A receipt
        /// the listener doesn't believe carries valence 0: they heard it, but it doesn't colour how they treat anyone.
        /// </summary>
        void QueueShare(GossipStory s, string tellerId, string tellerLedger, GossipKnowledge listener)
        {
            if (!Recorded(s)) return;
            string text = StoryText(s, listener.detail, listener.spice);
            string via = listener.detail == 0 ? tellerLedger ?? "" : "";
            listener.ledger = NewLedgerId("gossip", tellerId, listener.npcId, text, via);
            ledgerShares.Add(new GossipShare
            {
                eventId = listener.ledger, tellerId = tellerId, listenerId = listener.npcId, viaEventId = via, text = text,
                subjectId = Subject(s, listener.detail), topicId = Topic(s), valence = Believes(listener) ? Config.StoryKind(s.kind)?.valence ?? 0 : 0
            });
        }

        /// <summary>Social-side subject token: a player id when named, "players" for the players' house, else the house display name.</summary>
        string Subject(GossipStory s, int detail)
        {
            if (s.playerId != "" && detail <= 1) return s.playerId;
            if (s.houseId == Config.playerHouseId) return "players";
            return s.houseId == "" ? "" : HouseName(s.houseId);
        }

        /// <summary>Daily cooling: heat × (base + severity × perSeverity); retire cold or burnt-out stories; forget old ones.</summary>
        void CoolStories()
        {
            if (State.stories.Count == 0) return;
            var spreading = new HashSet<int>(State.gossip.Where(k => k.spreader && k.belief >= G.spreadMinBelief).Select(k => k.storyId));
            foreach (var s in State.stories)
            {
                if (s.retired) continue;
                s.heat *= Clamp(G.heatRetentionBase + s.severity / 100f * G.heatRetentionPerSeverity, 0, 0.98f);
                if (s.heat < G.retireHeat || !spreading.Contains(s.id)) s.retired = true;
            }
            int horizon = 7 * Math.Max(1, G.memoryWeeks);
            var forgotten = new HashSet<int>(State.stories.Where(s => s.retired && DayIndex - s.createdDay >= horizon).Select(s => s.id));
            if (forgotten.Count > 0)
            {
                State.stories.RemoveAll(s => forgotten.Contains(s.id));
                State.gossip.RemoveAll(k => forgotten.Contains(k.storyId));
                knowIndex = null;
            }
            ViralCheck();
            gossipVersion++;
        }

        void ViralCheck()
        {
            int campus = Math.Max(1, State.npcs.Count(n => RosterNpc(n.npcId) != null));
            foreach (var s in State.stories.Where(x => !x.retired && !x.viralReported).OrderBy(x => x.id).ToList())
            {
                int reach = State.gossip.Count(k => k.storyId == s.id);
                if (reach < G.viralShare * campus) continue;
                s.viralReported = true;
                News("gossip", "Everyone's talking about it: \"" + StoryText(s, 0, 0) + "\" (" + reach + " people have heard).", s.houseId, s.playerId, s.npcId);
            }
        }

        // ---------- Weekly hooks (design §3.5) ----------

        /// <summary>Sunday: seed the weekend's stories, run brunch rounds, publish the rumor-mill digest.</summary>
        void GossipWeekend(List<string> standingBefore, WeekResult week, List<CommitmentView> missed)
        {
            if (!G.enabled) return;
            SeedPlayersParty();
            foreach (var c in missed)
            {
                var def = RosterNpc(c.npcId);
                if (def == null || Player(c.inviterId) == null) continue;
                Seed("stood_up", Config.StoryKind("stood_up").severity + def.popularity, new[] { c.npcId }, Config.playerHouseId, c.inviterId, c.npcId,
                    origin: "npc:" + c.npcId, location: Config.playerHomeLocation);
            }
            SeedRivalParties(week);
            SeedStatusSwings(standingBefore, week);
            for (int r = 0; r < G.brunchRounds; r++) GossipRound("brunch", r, 0);
            RumorMillDigest();
        }

        void SeedPlayersParty()
        {
            var p = State.party;
            var h = PlayerHouse;
            // Not h.suspended: that is next week's sanction. A party suspended tonight never had guests.
            if (!p.ended || p.guests.Count == 0) return;
            var guests = p.guests.Where(g => !string.IsNullOrEmpty(g.npcId)).Select(g => g.npcId).Distinct().ToList();
            var candidates = new List<KeyValuePair<string, float>>();
            string starId = "";
            if (p.raided)
            {
                bool legend = p.fun >= Config.police.infamyFun;
                candidates.Add(new KeyValuePair<string, float>(legend ? "raid_legend" : "party_raided",
                    Config.StoryKind(legend ? "raid_legend" : "party_raided").severity + (legend ? p.fun - Config.police.infamyFun : 2 * Math.Min(10, p.underageDrinkers))));
            }
            if (p.hospitalized > 0) candidates.Add(new KeyValuePair<string, float>("party_hospital", Config.StoryKind("party_hospital").severity + 5 * (p.hospitalized - 1)));
            if (p.injuries > 0) candidates.Add(new KeyValuePair<string, float>("party_fight", Config.StoryKind("party_fight").severity + 5 * (p.injuries - 1)));
            if (!p.raided && p.fun >= G.legendaryFun && p.peakGuests >= p.capacity * 0.5f)
                candidates.Add(new KeyValuePair<string, float>("party_legendary", 30 + (p.fun - 60) * 1.5f + p.notablePopularity / 5f));
            else if (!p.raided && (p.fun < G.flopFun || p.peakGuests < p.capacity * 0.25f))
                candidates.Add(new KeyValuePair<string, float>("party_flop", 30 + Math.Max(0, 50 - p.fun) * 0.8f + (p.peakGuests < p.capacity * 0.25f ? 15 : 0)));
            var star = guests.Select(RosterNpc).Where(d => d != null && d.popularity >= Config.scoring.notablePopularity)
                .OrderByDescending(d => d.popularity).ThenBy(d => d.id, StringComparer.Ordinal).FirstOrDefault();
            if (star != null) { candidates.Add(new KeyValuePair<string, float>("celebrity_sighting", star.popularity * G.notableSightingFactor)); starId = star.id; }
            foreach (var c in candidates.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).Take(G.partyStoriesMax))
                Seed(c.Key, c.Value, guests, h.id, npcId: c.Key == "celebrity_sighting" ? starId : "", location: Config.playerHomeLocation);
        }

        void SeedRivalParties(WeekResult week)
        {
            float best = week.entries.Count == 0 ? 0 : week.entries.Max(e => e.score);
            foreach (var rp in State.rivalParties.OrderBy(r => r.houseId, StringComparer.Ordinal))
            {
                var h = House(rp.houseId);
                var entry = week.entries.Find(e => e.houseId == rp.houseId);
                // entry.suspended is party-time; h.suspended may already be next week's sanction.
                if (h == null || entry == null || entry.suspended) continue;
                var attendees = State.npcs.Where(n => WeekendDestination(n) == h.id).Select(n => n.npcId).ToList();
                var candidates = new List<KeyValuePair<string, float>>();
                string starId = "";
                if (rp.raided) candidates.Add(new KeyValuePair<string, float>("party_raided", Config.StoryKind("party_raided").severity - 5));
                else if (entry.rank == 1) candidates.Add(new KeyValuePair<string, float>("party_legendary", 40 + rp.notablePopularity / 5f));
                else if (entry.rank == week.entries.Count && entry.score < best * 0.4f) candidates.Add(new KeyValuePair<string, float>("party_flop", 40));
                var star = attendees.Select(RosterNpc).Where(d => d != null && d.popularity >= Config.scoring.notablePopularity)
                    .OrderByDescending(d => d.popularity).ThenBy(d => d.id, StringComparer.Ordinal).FirstOrDefault();
                if (star != null) { candidates.Add(new KeyValuePair<string, float>("celebrity_sighting", star.popularity * G.notableSightingFactor)); starId = star.id; }
                foreach (var c in candidates.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).Take(2))
                    Seed(c.Key, c.Value, attendees, h.id, npcId: c.Key == "celebrity_sighting" ? starId : "", location: h.homeLocation);
            }
        }

        /// <summary>Big moves in the Greek Cup table or popularity become "on the rise" / "falling off" stories (§3.3 status).</summary>
        void SeedStatusSwings(List<string> standingBefore, WeekResult week)
        {
            var after = Ranked().Select(h => h.id).ToList();
            foreach (var h in State.houses.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                int before = standingBefore.IndexOf(h.id), now = after.IndexOf(h.id);
                if (before < 0 || now < 0) continue;
                int moved = before - now;
                float swing = week.entries.Find(e => e.houseId == h.id)?.popDelta ?? 0;
                string kind = null;
                float severity = 0;
                if ((now == 0 && before != 0) || moved >= G.rankSwingPlaces) { kind = "on_the_rise"; severity = 40 + 10 * Math.Max(1, moved); }
                else if ((before == 0 && now != 0) || moved <= -G.rankSwingPlaces) { kind = "falling_off"; severity = 40 + 10 * Math.Max(1, -moved); }
                if (Math.Abs(swing) >= G.popularitySwing)
                {
                    string byPop = swing > 0 ? "on_the_rise" : "falling_off";
                    float popSeverity = 25 + Math.Abs(swing) * 4;
                    if (kind == null || kind == byPop) { kind = byPop; severity = Math.Max(severity, popSeverity); }
                }
                if (kind == null) continue;
                var knowers = GossipHubs().Concat(h.npcMembers).Concat(State.npcs.Where(n => WeekendDestination(n) == h.id).Select(n => n.npcId));
                Seed(kind, severity, knowers, h.id, location: "campus");
            }
        }

        void RumorMillDigest()
        {
            var top = Stories().Take(3).ToList();
            if (top.Count == 0) return;
            float wom = WordOfMouth(Config.playerHouseId);
            string mood = wom >= 2 ? "people are saying good things" : wom <= -2 ? "the talk is mostly bad" : "the talk is mixed";
            News("gossip", "Rumor mill: " + string.Join(" | ", top.Select(s => s.text + " (" + s.knowers + " heard)")) + " About " + PlayerHouse.name + ", " + mood + ".",
                PlayerHouse.id);
        }

        /// <summary>The hottest story people are passing around this morning, preferring ones about the players.</summary>
        string MorningGossipLine()
        {
            if (!G.enabled) return null;
            var stories = Stories();
            if (stories.Count == 0) return null;
            var pick = stories.FirstOrDefault(s => s.houseId == Config.playerHouseId || Player(s.playerId) != null);
            if (pick.text == null) pick = stories[0];
            return "Word on campus: \"" + pick.text + "\" (" + pick.knowers + " heard).";
        }

        // ---------- Smears and exposure (design §3.6) ----------

        SmearClaimDef PickClaim(string requested, string purpose)
        {
            var claims = Config.smearClaims;
            if (claims.Count == 0) return new SmearClaimDef { id = "lame", text = "throws the lamest parties on Greek Row" };
            return claims.Find(c => c.id == requested) ?? claims[NextRng(purpose).Range(0, claims.Count)];
        }

        bool ClaimTrue(SmearClaimDef claim, HouseState h)
        {
            switch (claim.truth ?? "")
            {
                case "probation": return h.probation || h.suspended;
                case "suspended": return h.suspended;
                case "trashed": return h.trashed;
                case "raided": return State.history.Count > 0 && State.history[State.history.Count - 1].entries.Any(e => e.houseId == h.id && e.raided);
                default: return false;
            }
        }

        /// <summary>Rival AI: a house whose members (or enough of campus) hear a smear the players started may trace and expose it.</summary>
        void RivalTraceSmears()
        {
            if (!G.enabled) return;
            foreach (var s in State.stories.Where(x => x.fabricated && !x.exposed && x.origin.StartsWith("player:", StringComparison.Ordinal)).OrderBy(x => x.id).ToList())
            {
                var victim = House(s.houseId);
                if (victim == null || victim.isPlayer) continue;
                var knowers = KnowersOf(s.id);
                bool pressure = knowers.Count >= G.rivalTraceReach || knowers.Any(k => Npc(k.npcId)?.memberOf == victim.id);
                if (!pressure) continue;
                if (Rng("gossip-trace-" + victim.id, s.id).Chance(G.rivalTraceBase + G.rivalTraceCaution * victim.caution))
                    Expose(s, victim.id, victim.homeLocation);
            }
        }

        /// <summary>
        /// Debunks a smear: every knower's belief collapses and stops spreading; a new "smear_exposed" story about whoever started it
        /// spreads from the exposing house's people and the campus hubs. Returns the exposure story, or null if it wasn't a smear.
        /// </summary>
        internal GossipStory Expose(GossipStory s, string byHouseId, string location)
        {
            if (s.exposed || !s.fabricated) return null;
            string culpritHouse = "", culpritPlayer = "";
            if (s.origin.StartsWith("player:", StringComparison.Ordinal)) { culpritHouse = Config.playerHouseId; culpritPlayer = s.origin.Substring(7); }
            else if (s.origin.StartsWith("house:", StringComparison.Ordinal)) culpritHouse = s.origin.Substring(6);
            else return null;
            s.exposed = true;
            s.retired = true;
            s.heat = 0;
            var knowers = KnowersOf(s.id);
            foreach (var k in knowers) { k.belief *= G.exposeBeliefFactor; k.spreader = false; }
            gossipVersion++;
            // Everyone who heard the lie learns it was one: a valence-0 record on the same topic replaces it in NPC memory.
            var told = knowers.Where(k => k.ledger != "").Select(k => k.npcId).ToList();
            if (told.Count > 0)
            {
                string debunk = "That rumor that " + HouseName(s.houseId) + " " + s.detail + " turned out to be made up.";
                QueueObservation(NewLedgerId("gossip_debunked", s.id.ToString(), string.Join(",", told)), "gossip_debunked", null, null, debunk,
                    location, told, s, 0);
            }
            var by = House(byHouseId);
            var witnesses = GossipHubs().Concat(by?.npcMembers ?? new List<string>()).Concat(NpcsAt(location)).ToList();
            var story = Seed("smear_exposed", G.exposedSeverity, witnesses, culpritHouse, culpritPlayer, otherHouseId: s.houseId, detail: s.detail,
                location: location, force: true);
            if (culpritHouse == Config.playerHouseId)
                News("gossip", (by?.name ?? "Someone") + " traced the rumor that " + HouseName(s.houseId) + " " + s.detail + " back to " +
                    (Player(culpritPlayer)?.displayName ?? "one of us") + ". Now everyone knows " + PlayerHouse.name + " made it up.", PlayerHouse.id, culpritPlayer, isPublic: true);
            else
                News("gossip", HouseName(culpritHouse) + " got exposed: they made up the rumor that " + HouseName(s.houseId) + " " + s.detail + ".",
                    culpritHouse, isPublic: true);
            return story;
        }
    }
}
