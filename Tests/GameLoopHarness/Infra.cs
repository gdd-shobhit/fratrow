using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FratRow.Game;
using FratRow.Social;

sealed class JsonCodec : IStateCodec
{
    static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
    public string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Options);
}

static class RepoPaths
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "Scripts", "Game", "Core"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Run from inside the FratRow repository");
        }
    }
    public static string Campus(string file) => Path.Combine(Root, "Assets", "Resources", "Campus", file);
}

static class Content
{
    public static GameLoopConfig Config(IStateCodec codec)
    {
        string path = RepoPaths.Campus("GameLoopConfig.json");
        return File.Exists(path) ? GameLoopConfig.Parse(File.ReadAllText(path), codec) : GameLoopConfig.CreateDefault();
    }
    public static CampusRoster Roster(IStateCodec codec, bool expand = true)
    {
        var roster = CampusRoster.Parse(File.ReadAllText(RepoPaths.Campus("CampusRoster.json")), codec);
        string archetypes = RepoPaths.Campus("PersonalityArchetypes.json");
        if (expand && File.Exists(archetypes)) roster.Expand(ArchetypeCatalog.Parse(File.ReadAllText(archetypes), codec));
        return roster;
    }
}

/// <summary>In person = same location as the NPC's current schedule/party; text = contact unlocked.</summary>
sealed class SessionAccess : IInteractionAccess
{
    public GameSession Session;
    public bool CanHear(string playerId, ConversationRequest request)
    {
        var p = Session?.Player(playerId);
        if (p == null) return false;
        if (request.channel == "in_person") return !string.IsNullOrEmpty(p.location) && Session.NpcLocation(request.npcId) == p.location;
        return Session.Npc(request.npcId)?.contactUnlocked == true;
    }
}

/// <summary>Forces a decision per NPC (e.g. accept/decline) while staying fully grounded and valid.</summary>
sealed class ScriptedProvider : ISocialModelProvider
{
    public readonly Dictionary<string, string> Decisions = new Dictionary<string, string>();
    public readonly List<ModelContext> Contexts = new List<ModelContext>();
    public Task<SocialProposal> GenerateAsync(ModelContext c, CancellationToken cancellation)
    {
        Contexts.Add(c);
        string decision = Decisions.TryGetValue(c.NpcId, out var d) && GroundedSocialTurn.Decisions(c).Contains(d) ? d : "respond";
        var p = new SocialProposal
        {
            schema = 1, decision = decision, evidence_ids = new[] { c.ObservedEventId },
            relationship_effects = new[] { new RelationshipEffect { scope = "player", id = c.PlayerId, magnitude = "unchanged" },
                new RelationshipEffect { scope = "house", id = c.FraternityId, magnitude = decision == "accept" ? "up_small" : "unchanged" } },
            condition = decision == "counteroffer" ? SocialRules.EntranceCondition : "none", grievance_event_id = "", resolve_grievance_event_id = "",
            grounded = new GroundedReply { attitude = "neutral", reason = "conversation", topic = "none", phrasing = 0 }
        };
        p.reply = GroundedSocialTurn.Compose(c, p);
        p.interpretation = GroundedSocialTurn.Interpretation(p);
        return Task.FromResult(p);
    }
    public void Dispose() { }
}

/// <summary>Everything the Unity host wires together, minus Unity: config, roster, social world, session, conversation service.</summary>
sealed class GameFixture : IDisposable
{
    public readonly string Dir = Path.Combine(Path.GetTempPath(), "FratRowGameTests", Guid.NewGuid().ToString("N"));
    public readonly JsonCodec Codec = new JsonCodec();
    public readonly GameLoopConfig Config;
    public readonly CampusRoster Roster;
    public readonly JournalSocialStore Store;
    public readonly SocialWorld World;
    public readonly SessionAccess Access = new SessionAccess();
    public readonly ISocialModelProvider Provider;
    public readonly SocialConversationService Service;
    public GameSession Session;
    public readonly List<GameEvent> Events = new List<GameEvent>();
    public readonly List<PlayerCredential> Players = new List<PlayerCredential>();
    public readonly List<(string player, string npc, string action, string text, ConversationResult result)> Conversations = new List<(string, string, string, string, ConversationResult)>();
    static readonly string[] LobbyClasses = { "President", "SocialChair", "Chad", "RichGuy", "", "" };

    public GameFixture(int seed = 7, int players = 2, ISocialModelProvider provider = null, Action<GameLoopConfig> tweak = null,
        GameState state = null, string reuseDir = null, bool expandArchetypes = true)
    {
        if (reuseDir != null) Dir = reuseDir;
        Config = Content.Config(Codec);
        tweak?.Invoke(Config);
        Config.Validate();
        Roster = Content.Roster(Codec, expandArchetypes);
        Store = new JournalSocialStore(Path.Combine(Dir, "social"), Codec, () => SocialWorld.NewCampaign("test"));
        World = new SocialWorld(Store);
        Session = new GameSession(Config, Roster, state ?? GameSession.NewCampaign(Config, Roster, "test", seed), new SocialWorldBridge(World));
        Access.Session = Session;
        if (state == null)
            for (int i = 0; i < players; i++)
            {
                var credential = World.Register("Player " + (i + 1));
                Players.Add(credential);
                Session.AddOrUpdatePlayer(credential.playerId, "Player " + (i + 1), LobbyClasses[i % LobbyClasses.Length]);
            }
        else foreach (var p in Session.State.players) Players.Add(new PlayerCredential { playerId = p.playerId });
        Provider = provider ?? new HeuristicSocialModelProvider(Store.Read, seed);
        Service = new SocialConversationService(World, Codec, Provider, Access, TimeSpan.FromSeconds(120), requireGroundedProposal: true);
        Session.DrainEvents(Events);
    }

    public string P(int i) => Players[i].playerId;
    public PlayerState PlayerState(int i) => Session.Player(P(i));

    public void Goto(int i, string location, bool atEntrance = true) => Session.SetPlayerLocation(P(i), location, atEntrance);

    public ActionResult Do(int i, string action, string target = "", string item = "", int amount = 0, string rival = "") =>
        Session.Apply(new PlayerAction { playerId = P(i), action = action, target = target, item = item, amount = amount, rival = rival });

    /// <summary>Runs a talk/invite/rush_talk through the core and the social service exactly like the Unity adapter.</summary>
    public async Task<ConversationResult> Talk(int i, string action, string npcId, string text = "")
    {
        var r = Session.Apply(new PlayerAction { playerId = P(i), action = action, target = npcId, text = text });
        if (!r.ok) return new ConversationResult { status = "rejected", error = r.code + ": " + r.message };
        var request = new ConversationRequest { requestId = SocialWorld.Id(), npcId = npcId, text = r.conversationText, channel = r.conversationChannel, partyId = r.socialPartyId,
            speakerCondition = r.speakerCondition };
        var result = await Service.SubmitAsync(P(i), request);
        Session.OnConversationCommitted(P(i), npcId, action, result);
        Conversations.Add((P(i), npcId, action, r.conversationText, result));
        Session.DrainEvents(Events);
        return result;
    }

    public void Advance(double seconds, double step = 1)
    {
        while (seconds > 1e-9)
        {
            double dt = Math.Min(step, seconds);
            Session.Tick(dt);
            seconds -= dt;
        }
        Session.DrainEvents(Events);
    }

    /// <summary>Advance until the phase changes to <paramref name="phase"/> (bounded).</summary>
    public void AdvanceUntil(Phase phase, double maxSeconds = 20000, double step = 1)
    {
        double spent = 0;
        while (Session.State.phase != phase && spent < maxSeconds) { Session.Tick(step); spent += step; }
        Session.DrainEvents(Events);
        if (Session.State.phase != phase) throw new Exception("Timed out waiting for phase " + phase + " (at " + Session.State.phase + ")");
    }

    public void Dispose() { Service.Dispose(); }
}

static class TestRunner
{
    public static int Passed, Failed;
    public static readonly List<string> Failures = new List<string>();
    public static void Assert(bool condition, string why) { if (!condition) throw new Exception(why); }
    public static async Task Test(string name, Func<Task> test)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { await test(); Passed++; Console.WriteLine("PASS " + name + " (" + watch.ElapsedMilliseconds + " ms)"); }
        catch (Exception e) { Failed++; Failures.Add(name); Console.WriteLine("FAIL " + name + " " + e); }
    }
    public static Task Test(string name, Action test) => Test(name, () => { test(); return Task.CompletedTask; });
}

/// <summary>Seeded 32-hex ids so social receipts (and therefore sampling seeds) repeat across runs.</summary>
sealed class DeterministicIds : IDisposable
{
    readonly GameRng rng;
    public DeterministicIds(int seed) { rng = GameRng.For(seed, "ids"); SocialWorld.IdSource = Next; }
    string Next() => rng.NextULong().ToString("x16") + rng.NextULong().ToString("x16");
    public void Dispose() { SocialWorld.IdSource = null; }
}
