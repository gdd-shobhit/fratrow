using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FratRow.Game;
using FratRow.Social;

/// <summary>
/// <c>dotnet run --project Tests/GameLoopHarness -- --profile-gossip [--weeks 4] [--seed 51]</c>
/// Plays the same bot semester with gossip off and on and reports wall time, social-store transactions
/// (each rewrites the whole journal state), final social state size, journal size and where gossip's time goes.
/// </summary>
static class GossipProfile
{
    public static async Task Run(string[] args)
    {
        int weeks = int.Parse(Program.Arg(args, "--weeks") ?? "4");
        int seed = int.Parse(Program.Arg(args, "--seed") ?? "51");
        foreach (bool on in new[] { false, true })
        {
            using var ids = new DeterministicIds(seed);
            using var f = new GameFixture(seed: seed, players: 3, tweak: c =>
            {
                c.weekdaySecondsPerDay = 60; c.partySeconds = 240; c.debriefSeconds = 10;
                c.semesterWeeks = weeks; c.gossip.enabled = on;
            });
            var timed = new TimedBridge(new SocialWorldBridge(f.World));
            f.Session = new GameSession(f.Config, f.Roster, f.Session.State, timed);
            f.Access.Session = f.Session;
            var bots = new BotCrew(f, BotPolicy.Responsible(), seed);
            long startSeq = f.Store.Read().sequence;
            var watch = Stopwatch.StartNew();
            while (f.Session.State.phase != Phase.SemesterEnd) await bots.PlayWeek();
            watch.Stop();
            var social = f.Store.Read();
            int bytes = f.Codec.Encode(social).Length;
            long journal = new FileInfo(Path.Combine(f.Dir, "social", "social.journal")).Length;
            Console.WriteLine((on ? "gossip ON " : "gossip OFF") + ": " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s, social transactions " + (social.sequence - startSeq) +
                ", social events " + social.events.Count + " (gossip receipts " + social.events.Count(e => e.kind == "gossip") + ", gossip records " +
                social.events.Count(e => e.kind.StartsWith("gossip_", StringComparison.Ordinal) || e.kind == "denial") + "), social state " + (bytes / 1024) + " KB, journal " +
                (journal / (1024 * 1024)) + " MB, conversations " + f.Conversations.Count + ", stories " + f.Session.State.storySeq + ", knowledge rows " + f.Session.State.gossip.Count);
            Console.WriteLine("  time in RecordGossip " + timed.GossipTime.TotalSeconds.ToString("0.0") + " s over " + timed.GossipCalls + " writes; in Observe " +
                timed.ObserveTime.TotalSeconds.ToString("0.0") + " s over " + timed.ObserveCalls + "; social reads " + timed.ReadTime.TotalSeconds.ToString("0.0") + " s");
            if (!on) continue;
            // Host-frame cost of one round plus its flush on this late-semester save, written synchronously vs deferred.
            foreach (bool defer in new[] { false, true })
            {
                var bridge = new SocialWorldBridge(f.World, deferGossipWrites: defer);
                f.Session = new GameSession(f.Config, f.Roster, f.Session.State, bridge);
                f.Session.State.phase = Phase.Weekday;
                f.Session.Seed("party_raided", 90, f.Roster.npcs.Skip(defer ? 20 : 0).Take(20).Select(n => n.id), "player", npcId: defer ? "Megan" : "Maya", force: true);
                var round = Stopwatch.StartNew();
                f.Session.GossipRound("day", defer ? 98 : 99, 12f);
                f.Session.FlushGossipLedger();
                double frame = round.Elapsed.TotalMilliseconds;
                bridge.WaitForGossipWrites();
                Console.WriteLine("  one gossip round + flush with " + (bytes / 1024) + " KB social state, " + (defer ? "deferred" : "synchronous") + ": " +
                    frame.ToString("0.0") + " ms on the host frame (" + round.Elapsed.TotalMilliseconds.ToString("0") + " ms until written)");
            }
        }
    }

    /// <summary>Times the social calls the game core makes (gossip writes, observations, and reads that refresh the snapshot).</summary>
    sealed class TimedBridge : ISocialBridge
    {
        readonly SocialWorldBridge inner;
        readonly Stopwatch gossip = new Stopwatch(), observe = new Stopwatch(), reads = new Stopwatch();
        public int GossipCalls, ObserveCalls;
        public TimeSpan GossipTime => gossip.Elapsed;
        public TimeSpan ObserveTime => observe.Elapsed;
        public TimeSpan ReadTime => reads.Elapsed;
        public TimedBridge(SocialWorldBridge inner) { this.inner = inner; }
        T Read<T>(Func<T> f) { reads.Start(); try { return f(); } finally { reads.Stop(); } }
        public void BeginTick() => inner.BeginTick();
        public void RegisterRoster(CampusRoster roster) => inner.RegisterRoster(roster);
        public bool HasNpc(string npcId) => Read(() => inner.HasNpc(npcId));
        public string EnsurePlannedParty(int capacity) => inner.EnsurePlannedParty(capacity);
        public void StartParty(string partyId, double now) => inner.StartParty(partyId, now);
        public void EndParty(string partyId, bool cancelled) => inner.EndParty(partyId, cancelled);
        public IReadOnlyList<CommitmentView> Commitments(string partyId) => Read(() => inner.Commitments(partyId));
        public void ObserveEntrance(string partyId, ISet<string> playersAtEntrance, double now) => inner.ObserveEntrance(partyId, playersAtEntrance, now);
        public void MarkArrived(string npcId, string partyId) => inner.MarkArrived(npcId, partyId);
        public int RegardForPlayers(string npcId) => Read(() => inner.RegardForPlayers(npcId));
        public bool KnowsPlayer(string npcId, string playerId) => Read(() => inner.KnowsPlayer(npcId, playerId));
        public void SetStandings(HouseState players, IReadOnlyList<HouseState> ranked) => inner.SetStandings(players, ranked);
        public void SetAffiliation(string npcId, string affiliation) => inner.SetAffiliation(npcId, affiliation);
        public string Observe(string kind, string actorId, string target, string text, string location, IReadOnlyCollection<string> witnesses)
        { ObserveCalls++; observe.Start(); try { return inner.Observe(kind, actorId, target, text, location, witnesses); } finally { observe.Stop(); } }
        public void PublishNews(string text, IReadOnlyCollection<string> npcIds) => inner.PublishNews(text, npcIds);
        public void RegisterPlayer(string playerId, string displayName) => inner.RegisterPlayer(playerId, displayName);
        public TurnImpactView TurnImpact(string playerId, string requestId) => Read(() => inner.TurnImpact(playerId, requestId));
        public void RecordGossip(IReadOnlyList<GossipObservation> observations, IReadOnlyList<GossipShare> shares)
        { GossipCalls++; gossip.Start(); try { inner.RecordGossip(observations, shares); } finally { gossip.Stop(); } }
    }
}
