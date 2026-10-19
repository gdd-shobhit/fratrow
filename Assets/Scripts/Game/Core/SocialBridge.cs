using System;
using System.Collections.Generic;
using System.Linq;
using FratRow.Social;

namespace FratRow.Game
{
    /// <summary>Read-only view of one NPC's RSVP for a party.</summary>
    public struct CommitmentView
    {
        public string npcId, inviterId, state;
        public bool arrived;
    }

    /// <summary>
    /// The game core's only dependency on the social backend (design §6). Implemented over <see cref="SocialWorld"/>;
    /// tests may substitute a fake. All calls are host-only and synchronous.
    /// </summary>
    public interface ISocialBridge
    {
        /// <summary>Drop cached social reads; called once at the start of every core tick/action.</summary>
        void BeginTick();
        void RegisterRoster(CampusRoster roster);
        bool HasNpc(string npcId);
        string EnsurePlannedParty(int capacity);
        void StartParty(string partyId, double now);
        void EndParty(string partyId, bool cancelled);
        IReadOnlyList<CommitmentView> Commitments(string partyId);
        void ObserveEntrance(string partyId, ISet<string> playersAtEntrance, double now);
        void MarkArrived(string npcId, string partyId);
        /// <summary>NPC regard for the players' house (-100..100), owned by conversation.</summary>
        int RegardForPlayers(string npcId);
        bool KnowsPlayer(string npcId, string playerId);
        void SetStandings(HouseState players, IReadOnlyList<HouseState> ranked);
        void SetAffiliation(string npcId, string affiliation);
        /// <summary>World event with the given NPC witnesses; returns the social event id.</summary>
        string Observe(string kind, string actorId, string target, string text, string location, IReadOnlyCollection<string> witnesses);
        /// <summary>Public campus news every listed NPC learns (one memory each).</summary>
        void PublishNews(string text, IReadOnlyCollection<string> npcIds);
        void RegisterPlayer(string playerId, string displayName);
    }

    /// <summary><see cref="ISocialBridge"/> over the persisted social world.</summary>
    public sealed class SocialWorldBridge : ISocialBridge
    {
        readonly SocialWorld world;
        CampaignState cached;

        public SocialWorldBridge(SocialWorld world) { this.world = world ?? throw new ArgumentNullException(nameof(world)); }

        CampaignState Snapshot => cached ?? (cached = world.Store.Read());
        void Invalidate() => cached = null;

        public void BeginTick() => Invalidate();

        public void RegisterRoster(CampusRoster roster)
        {
            foreach (var npc in roster.npcs)
            {
                world.EnsureNpc(npc.id, npc.persona, npc.voice);
                // Public popularity is roster-owned; keep it in sync even for existing campaigns (no write when unchanged).
                world.SetNpcStanding(npc.id, npc.popularity, CampusRoster.Concern(npc));
            }
            Invalidate();
        }

        public bool HasNpc(string npcId) => SocialWorld.Npc(Snapshot, npcId) != null;

        public string EnsurePlannedParty(int capacity)
        {
            string id = world.EnsurePlannedParty(Math.Max(1, capacity));
            Invalidate();
            return id;
        }

        public void StartParty(string partyId, double now) { world.StartParty(partyId, now); Invalidate(); }

        public void EndParty(string partyId, bool cancelled)
        {
            if (string.IsNullOrEmpty(partyId) || SocialWorld.Party(Snapshot, partyId) == null) return;
            world.EndParty(partyId, cancelled);
            Invalidate();
        }

        public IReadOnlyList<CommitmentView> Commitments(string partyId)
        {
            var list = new List<CommitmentView>();
            foreach (var npc in Snapshot.npcs)
            {
                var c = npc.commitments.Find(x => x.partyId == partyId);
                if (c != null) list.Add(new CommitmentView { npcId = npc.npcId, inviterId = c.inviterId, state = c.state, arrived = c.arrived });
            }
            return list;
        }

        public void ObserveEntrance(string partyId, ISet<string> playersAtEntrance, double now)
        {
            long before = Snapshot.sequence;
            world.ObserveEntrance(partyId, playersAtEntrance, now);
            if (world.Store.Read().sequence != before) Invalidate();
        }

        public void MarkArrived(string npcId, string partyId) { world.MarkArrived(npcId, partyId); Invalidate(); }

        public int RegardForPlayers(string npcId) => SocialWorld.Npc(Snapshot, npcId)?.fraternityRegard ?? 0;

        public bool KnowsPlayer(string npcId, string playerId) =>
            Snapshot.events.Any(e => e.source == "speech" && e.actorId == playerId && e.targetId == npcId);

        public void SetStandings(HouseState players, IReadOnlyList<HouseState> ranked)
        {
            int rank = 0;
            for (int i = 0; i < ranked.Count; i++) if (ranked[i].id == players.id) rank = i + 1;
            world.SetFraternityStanding(players.popularity, Trim(players.name), players.grade ?? "", Math.Max(0, Math.Min(100, players.rating)));
            world.SetRivalStandings(ranked.Where(h => !h.isPlayer).Select(h => new RivalStanding
            {
                houseId = h.id, name = Trim(h.name), popularity = h.popularity, grade = h.grade ?? "", rank = ranked.ToList().IndexOf(h) + 1
            }), rank);
            Invalidate();
        }

        public void SetAffiliation(string npcId, string affiliation)
        {
            if (!HasNpc(npcId)) return;
            world.SetNpcAffiliation(npcId, affiliation ?? "");
            Invalidate();
        }

        public string Observe(string kind, string actorId, string target, string text, string location, IReadOnlyCollection<string> witnesses)
        {
            var known = witnesses.Where(HasNpc).Distinct().ToArray();
            string id = world.Observe(kind, actorId, target, Trim(text, 400), location, known);
            Invalidate();
            return id;
        }

        public void PublishNews(string text, IReadOnlyCollection<string> npcIds)
        {
            var known = npcIds.Where(HasNpc).Distinct().ToArray();
            if (known.Length == 0) return;
            world.Observe("campus_news", null, null, Trim(text, 600), "campus", known);
            Invalidate();
        }

        public void RegisterPlayer(string playerId, string displayName) { /* Unity binds through credentials; harness registers via SocialWorld.Register. */ }

        static string Trim(string s, int max = 64) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s.Substring(0, max);
    }

    /// <summary>No-op bridge for pure rule tests that do not need NPC memory.</summary>
    public sealed class NullSocialBridge : ISocialBridge
    {
        public readonly List<string> Observed = new List<string>();
        public readonly Dictionary<string, int> Regard = new Dictionary<string, int>();
        public readonly List<CommitmentView> FakeCommitments = new List<CommitmentView>();
        public void BeginTick() { }
        public void RegisterRoster(CampusRoster roster) { }
        public bool HasNpc(string npcId) => true;
        public string EnsurePlannedParty(int capacity) => "party";
        public void StartParty(string partyId, double now) { }
        public void EndParty(string partyId, bool cancelled) { }
        public IReadOnlyList<CommitmentView> Commitments(string partyId) => FakeCommitments;
        public void ObserveEntrance(string partyId, ISet<string> playersAtEntrance, double now) { }
        public void MarkArrived(string npcId, string partyId)
        {
            int i = FakeCommitments.FindIndex(c => c.npcId == npcId);
            if (i >= 0) { var c = FakeCommitments[i]; c.arrived = true; FakeCommitments[i] = c; }
        }
        public int RegardForPlayers(string npcId) => Regard.TryGetValue(npcId, out int r) ? r : 0;
        public bool KnowsPlayer(string npcId, string playerId) => false;
        public void SetStandings(HouseState players, IReadOnlyList<HouseState> ranked) { }
        public void SetAffiliation(string npcId, string affiliation) { }
        public string Observe(string kind, string actorId, string target, string text, string location, IReadOnlyCollection<string> witnesses)
        { Observed.Add(kind + ":" + string.Join(",", witnesses)); return Guid.NewGuid().ToString("N"); }
        public void PublishNews(string text, IReadOnlyCollection<string> npcIds) { Observed.Add("news:" + npcIds.Count); }
        public void RegisterPlayer(string playerId, string displayName) { }
    }
}
