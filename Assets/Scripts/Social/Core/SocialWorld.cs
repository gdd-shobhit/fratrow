using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FratRow.Social
{
    public sealed class SocialWorld
    {
        public readonly ISocialStore Store;
        public SocialWorld(ISocialStore store) { Store = store; }
        public static CampaignState NewCampaign(string campaignId) => new CampaignState
        { campaignId = campaignId, fraternityId = Guid.NewGuid().ToString("N") };
        public static NpcState Npc(CampaignState s, string id) => s.npcs.Find(n => n.npcId == id);
        public static PartyRecord Party(CampaignState s, string id) => s.parties.Find(p => p.partyId == id);
        /// <summary>Test hook only: replace with a seeded 32-hex generator for reproducible runs. Production uses random GUIDs.</summary>
        public static Func<string> IdSource;
        public static string Id() => IdSource?.Invoke() ?? Guid.NewGuid().ToString("N");

        public PlayerCredential Register(string displayName, PlayerCredential credential = null)
        {
            var s = Store.Read();
            if (credential != null)
            {
                if (credential.campaignId != s.campaignId || string.IsNullOrEmpty(credential.token))
                    throw new UnauthorizedAccessException("Wrong campaign credential");
                var player = s.players.Find(p => p.playerId == credential.playerId);
                if (player == null || player.credentialHash != HashToken(credential.token))
                    throw new UnauthorizedAccessException("Invalid reconnect credential");
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    string name = displayName.Substring(0, Math.Min(displayName.Length, 48));
                    if (player.displayName != name) Store.Transact(next => next.players.Find(p => p.playerId == credential.playerId).displayName = name);
                }
                return credential;
            }
            byte[] secret = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(secret);
            var issued = new PlayerCredential { campaignId = s.campaignId, playerId = Id(), token = Convert.ToBase64String(secret) };
            Store.Transact(next => next.players.Add(new PlayerIdentity
            {
                playerId = issued.playerId, credentialHash = HashToken(issued.token),
                displayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName.Substring(0, Math.Min(displayName.Length, 48))
            }));
            return issued;
        }
        static string HashToken(string token) => JournalSocialStore.Hash(Encoding.UTF8.GetBytes(token));
        public void EnsureNpc(string id, string persona, NpcVoiceProfile voice = null)
        {
            var existing = Npc(Store.Read(), id);
            if (existing != null)
            {
                // Fill the additive field in an older campaign without replacing its character/history.
                if (existing.voice == null && voice != null) SetNpcVoice(id, voice);
                return;
            }
            if (string.IsNullOrWhiteSpace(id) || id.Length > 48 || string.IsNullOrWhiteSpace(persona) || persona.Length > 900)
                throw new ArgumentException("Invalid NPC profile");
            var savedVoice = SocialVoice.Copy(voice);
            Store.Transact(s => { if (Npc(s, id) == null) s.npcs.Add(new NpcState { npcId = id, persona = persona, voice = savedVoice, voiceVersion = 1 }); });
        }
        /// <summary>Host authoring only; no client RPC. Changing voice invalidates an in-flight NPC context.</summary>
        public void SetNpcVoice(string id, NpcVoiceProfile voice)
        {
            SocialVoice.Validate(voice); var saved = SocialVoice.Copy(voice);
            Store.Transact(s => {
                var npc = Npc(s, id) ?? throw new ArgumentException("Unknown NPC");
                npc.voice = saved; npc.voiceVersion++; npc.revision++;
            });
        }
        public void SetFraternityStanding(float popularity, string name = "", string grade = "", int rating = 0)
        {
            SocialStanding.Validate(popularity);
            if (name == null || name.Length > 64 || grade == null ||
                (grade != "" && !new[] { "A", "B", "C", "D", "E", "F" }.Contains(grade)) || rating < 0 || rating > 100)
                throw new ArgumentException("Invalid fraternity standing");
            bool Same(FraternityStanding value) => value?.known == true && value.popularity == popularity && value.name == name && value.grade == grade && value.rating == rating;
            if (Same(Store.Read().standing)) return;
            Store.Transact(s => {
                if (Same(s.standing)) return;
                s.standing = new FraternityStanding { known = true, popularity = popularity, name = name, grade = grade, rating = rating,
                    revision = (s.standing?.revision ?? 0) + 1 };
            });
        }
        /// <summary>
        /// Host-only: public standings of the other fraternities plus the players' rank. No write when unchanged.
        /// Example: <c>world.SetRivalStandings(new[] { new RivalStanding { houseId = "kappa", name = "Kappa Sigma Psi", popularity = 60, rank = 1 } }, 4);</c>
        /// </summary>
        public void SetRivalStandings(IEnumerable<RivalStanding> rivals, int playersRank)
        {
            var next = (rivals ?? Enumerable.Empty<RivalStanding>()).Select(r => new RivalStanding { houseId = r.houseId, name = r.name ?? "",
                popularity = r.popularity, grade = r.grade ?? "", rank = r.rank }).OrderBy(r => r.houseId, StringComparer.Ordinal).ToList();
            foreach (var r in next)
            {
                SocialStanding.Validate(r.popularity);
                if (string.IsNullOrWhiteSpace(r.houseId) || r.name.Length > 64 || r.rank < 0 || (r.grade != "" && !new[] { "A", "B", "C", "D", "E", "F" }.Contains(r.grade)))
                    throw new ArgumentException("Invalid rival standing");
            }
            if (playersRank < 0) throw new ArgumentException("Invalid rank");
            bool Same(CampaignState s)
            {
                var current = s.rivals ?? new List<RivalStanding>();
                return s.playersRank == playersRank && current.Count == next.Count &&
                    current.Zip(next, (a, b) => a.houseId == b.houseId && a.name == b.name && a.popularity == b.popularity && a.grade == b.grade && a.rank == b.rank).All(x => x);
            }
            if (Same(Store.Read())) return;
            Store.Transact(s => { if (Same(s)) return; s.rivals = next; s.playersRank = playersRank; s.rivalsRevision++; });
        }
        /// <summary>Host-only: records that an NPC joined a house ("players", a rival display name, or "" for none).</summary>
        public void SetNpcAffiliation(string id, string affiliation)
        {
            if (affiliation == null || affiliation.Length > 64) throw new ArgumentException("Invalid affiliation");
            var existing = Npc(Store.Read(), id) ?? throw new ArgumentException("Unknown NPC");
            if ((existing.affiliation ?? "") == affiliation) return;
            Store.Transact(s => { var npc = Npc(s, id); npc.affiliation = affiliation; npc.revision++; });
        }
        public void SetNpcStanding(string id, float popularity, StatusConcern concern = StatusConcern.Moderate)
        {
            SocialStanding.Validate(popularity);
            if (!Enum.IsDefined(typeof(StatusConcern), concern)) throw new ArgumentException("Invalid status concern");
            var existing = Npc(Store.Read(), id) ?? throw new ArgumentException("Unknown NPC");
            bool Same(NpcStanding value) => value?.known == true && value.popularity == popularity && value.statusConcern == concern;
            if (Same(existing.standing)) return;
            Store.Transact(s => {
                var npc = Npc(s, id);
                if (Same(npc.standing)) return;
                npc.standing = new NpcStanding { known = true, popularity = popularity, statusConcern = concern }; npc.revision++;
            });
        }
        public void ImportLegacy(IDictionary<string, float> affinity)
        {
            if (Store.Read().legacyImported) return;
            Store.Transact(s =>
            {
                foreach (var entry in affinity)
                { var npc = Npc(s, entry.Key); if (npc != null) npc.fraternityRegard = Math.Max(-100, Math.Min(100, (int)entry.Value)); }
                s.legacyImported = true;
            });
        }
        public string EnsurePlannedParty(int capacity)
        {
            var existing = Store.Read().parties.Find(p => p.state == "active" || p.state == "planned");
            if (existing != null) return existing.partyId;
            string id = Id();
            Store.Transact(s => s.parties.Add(new PartyRecord { partyId = id, capacity = capacity }));
            return id;
        }
        public void StartParty(string id, double now)
        {
            Store.Transact(s =>
            {
                var p = Party(s, id) ?? throw new ArgumentException("Unknown party");
                if (p.state == "active") return;
                if (p.state != "planned") throw new InvalidOperationException("Party cannot start");
                p.state = "active"; p.startedAt = now; p.revision++;
                AddEvent(s, "party_started", "world", null, id, "PartyHouse", "Party started", new List<string>());
            });
        }
        public void EndParty(string id, bool cancelled = false)
        {
            Store.Transact(s =>
            {
                var p = Party(s, id) ?? throw new ArgumentException("Unknown party");
                if (p.state == "ended" || p.state == "cancelled") return;
                p.state = cancelled ? "cancelled" : "ended"; p.revision++;
                foreach (var npc in s.npcs)
                {
                    var c = npc.commitments.Find(x => x.partyId == id);
                    if (c == null) continue;
                    if (c.state == "conditional" && !c.conditionResolved)
                    { ResolveEntrance(s, npc, c, false); }
                    if (cancelled && (c.state == "accepted" || c.state == "conditional")) c.state = "cancelled";
                    npc.revision++;
                }
                AddEvent(s, cancelled ? "party_cancelled" : "party_ended", "world", null, id, "PartyHouse", p.state, new List<string>());
                if (!cancelled) s.day++;
            });
        }
        public void ObserveEntrance(string partyId, ISet<string> playersWithinFourMetres, double now)
        {
            var snapshot = Store.Read();
            var party = Party(snapshot, partyId);
            if (party == null || party.state != "active") return;
            bool due = snapshot.npcs.Any(n => n.commitments.Any(c => c.partyId == partyId && c.state == "conditional" &&
                !c.conditionResolved && (now - party.startedAt >= 60 || playersWithinFourMetres.Contains(c.inviterId))));
            if (!due) return;
            Store.Transact(s =>
            {
                var p = Party(s, partyId);
                if (p.state != "active") return;
                foreach (var n in s.npcs)
                {
                    var c = n.commitments.Find(x => x.partyId == partyId && x.state == "conditional" && !x.conditionResolved);
                    if (c == null) continue;
                    bool met = now - p.startedAt < 60 && playersWithinFourMetres.Contains(c.inviterId);
                    if (met || now - p.startedAt >= 60) ResolveEntrance(s, n, c, met);
                }
            });
        }
        static void ResolveEntrance(CampaignState s, NpcState n, Commitment c, bool met)
        {
            c.conditionResolved = true; c.state = met ? "accepted" : "missed"; n.revision++;
            var e = AddEvent(s, met ? "promise_fulfilled" : "promise_broken", "world", c.inviterId, n.npcId,
                "PartyHouse", "Entrance meeting " + (met ? "fulfilled" : "missed") + " for party " + c.partyId, new List<string> { n.npcId });
            e.relatedEventId = c.decisionEventId;
        }
        public void MarkArrived(string npcId, string partyId)
        {
            Store.Transact(s =>
            {
                var n = Npc(s, npcId); var c = n?.commitments.Find(x => x.partyId == partyId);
                if (c == null || c.state != "accepted" || Party(s, partyId)?.state != "active")
                    throw new InvalidOperationException("Attendance not committed");
                if (c.arrived) return;
                c.arrived = true; n.revision++;
                AddEvent(s, "arrived", "world", npcId, partyId, "PartyHouse", "Arrived at party", new List<string> { npcId });
            });
        }
        /// <summary>Only an authoritative observation adapter may call this. Never expose as a client RPC.</summary>
        public string Observe(string kind, string actor, string target, string text, string location, params string[] witnesses)
        {
            string id = null;
            Store.Transact(s =>
            {
                if (witnesses.Any(w => Npc(s, w) == null)) throw new ArgumentException("Unknown witness");
                id = AddEvent(s, kind, "world", actor, target, location, text, witnesses.ToList()).eventId;
                foreach (var w in witnesses.Distinct()) Npc(s, w).revision++;
            });
            return id;
        }
        public string TellRumor(string sourceNpc, string recipientNpc, string eventId)
        {
            string result = null;
            Store.Transact(s =>
            {
                var original = s.events.Find(e => e.eventId == eventId);
                if (Npc(s, sourceNpc) == null || Npc(s, recipientNpc) == null || original == null || !original.witnesses.Contains(sourceNpc))
                    throw new InvalidOperationException("Source does not know this event");
                var e = AddEvent(s, "rumor", "rumor", sourceNpc, recipientNpc, "gossip",
                    "Report about " + original.actorId + ": " + original.text, new List<string> { recipientNpc });
                e.relatedEventId = original.eventId; result = e.eventId; Npc(s, recipientNpc).revision++;
            });
            return result;
        }
        internal static SocialEvent AddEvent(CampaignState s, string kind, string source, string actor, string target,
            string location, string text, List<string> witnesses)
        {
            var e = new SocialEvent { eventId = Id(), kind = kind, source = source, actorId = actor,
                targetId = target, fraternityId = s.fraternityId, location = location, text = text,
                witnesses = witnesses, day = s.day, utcTicks = DateTime.UtcNow.Ticks };
            s.events.Add(e); return e;
        }
        // Unwrap only explicit gossip links. Decision/promise links have different meanings.
        internal static SocialEvent GossipOrigin(CampaignState s, SocialEvent receipt)
        {
            var visited = new HashSet<string>();
            while (receipt?.source == "rumor")
            {
                if (!visited.Add(receipt.eventId)) return null;
                receipt = s.events.Find(e => e.eventId == receipt.relatedEventId);
            }
            return receipt;
        }
    }
}
