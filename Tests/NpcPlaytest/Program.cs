using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FratRow.Game;
using FratRow.Social;

/// <summary>
/// NPC conversation playtest: a tester (scripted lines or a Claude Haiku "game tester") talks to campus NPCs through the
/// real social backend (roster, social world, game session, conversation service — everything Unity wires, minus Unity),
/// then a Haiku judge scores each conversation against the NPC's hidden persona. See .claude/skills/npc-playtest/SKILL.md.
/// <code>
/// dotnet run --project Tests/NpcPlaytest -- --provider heuristic --tester scripted --sample 6 --no-judge
/// dotnet run --project Tests/NpcPlaytest -- --provider persona --model D:/fratrow/Library/SocialModels/Qwen_Qwen3.5-2B-Q4_K_M.gguf
///     --sample 10   (llama.cpp build auto-picked from Library/SocialNative; or --llama-server / --native-root)
/// </code>
/// </summary>
static class Program
{
    public const int MaxTurns = 5;

    static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Usage); return 0; }
        Options o;
        try { o = Options.Parse(args); }
        catch (Exception e) { Console.Error.WriteLine("error: " + e.Message); Console.Error.WriteLine(Usage); return 2; }

        using var ids = new DeterministicIds(o.Seed);
        using var backend = new Backend(o);
        if (o.DumpNpcs)
        {
            foreach (var n in backend.Roster.npcs)
                Console.WriteLine(n.id.PadRight(12) + " " + n.gender.PadRight(5) + " " + (n.voice?.tone ?? "?").PadRight(9) + " " +
                    (backend.AuthoredIds.Contains(n.id) ? "authored " : "generated") + " " + n.summary);
            Console.WriteLine(backend.Roster.npcs.Count + " NPCs");
            return 0;
        }

        var npcs = SelectNpcs(o, backend);
        Directory.CreateDirectory(o.Out);
        ClaudeClient claude = o.Tester == "haiku" || o.Tester == "adversarial" || o.Judge ? new ClaudeClient(o.TesterModel, o.Transport, o.ClaudePath) : null;
        ITester tester = o.Tester == "haiku" ? new HaikuTester(claude) : o.Tester == "adversarial" ? new AdversarialTester(claude) :
            o.Tester == "blame" ? new BlameScenarioTester() : (ITester)new ScriptedTester();
        var judge = o.Judge ? new HaikuJudge(claude) : null;

        Console.WriteLine("NPC playtest · provider " + backend.ProviderName + " · tester " + (o.Tester == "haiku" || o.Tester == "adversarial" ? o.TesterModel + " (" + o.Tester + ")" : o.Tester) +
            " · judge " + (o.Judge ? o.TesterModel : "off") + " · turns " + o.Turns + " · seed " + o.Seed + " · " + npcs.Count + " NPCs: " + string.Join(", ", npcs.Select(n => n.id)));

        int overridesBefore = backend.RsvpOverrides;
        var watch = Stopwatch.StartNew();
        var slots = new SemaphoreSlim(o.Parallel, o.Parallel);
        var results = new ConversationLog[npcs.Count];
        await Task.WhenAll(npcs.Select(async (npc, index) =>
        {
            await slots.WaitAsync();
            try { results[index] = await RunConversation(o, backend, tester, judge, npc); }
            finally { slots.Release(); }
        }));
        watch.Stop();

        var logs = results.ToList();
        var metrics = Metrics.Compute(logs, o.Judge, backend.RsvpOverrides - overridesBefore, claude?.CostUsd ?? 0, claude?.Calls ?? 0, watch.Elapsed.TotalSeconds);
        metrics.npcClaudeCostUsd = backend.NpcCostUsd;
        Report.Write(o, backend, logs, metrics);
        Console.WriteLine();
        Console.WriteLine(Report.Summary(metrics));
        Console.WriteLine("Report: " + Path.Combine(o.Out, "report.md"));
        return 0;
    }

    static async Task<ConversationLog> RunConversation(Options o, Backend backend, ITester tester, HaikuJudge judge, RosterNpc npc)
    {
        var log = new ConversationLog
        {
            npc = npc.id, gender = npc.gender, tone = npc.voice?.tone, slang = npc.voice?.slang, summary = npc.summary,
            authored = backend.AuthoredIds.Contains(npc.id), persona = npc.persona, exampleLines = npc.voice?.exampleLines ?? Array.Empty<string>()
        };
        var context = new TesterContext { Npc = npc, House = backend.HouseName, Player = backend.PlayerName, MaxTurns = o.Turns, Log = log };
        for (int turn = 0; turn < o.Turns; turn++)
        {
            string message;
            try { message = await tester.NextAsync(context, turn); }
            catch (Exception e) { log.testerError = e.Message; Console.WriteLine("[" + npc.id + "] tester failed: " + e.Message); break; }
            if (string.IsNullOrWhiteSpace(message) || message.Trim() == "[END]") { log.endedEarly = turn; break; }
            int speaker = tester.Speaker(turn);
            var t = await backend.SayAsync(npc.id, message, turn, speaker);
            log.turns.Add(t);
            lock (Console.Out)
                Console.WriteLine("[" + npc.id + " " + (turn + 1) + "/" + o.Turns + "] " + t.speaker + ": " + message + "\n    " + npc.id + ": " + t.reply +
                    "  (" + (t.decision ?? t.status) + ", " + (t.ms / 1000.0).ToString("0.0") + "s)");
        }
        log.finalRsvp = backend.Rsvp(npc.id);
        log.hostChecks = backend.HostChecks(npc.id, o.Tester, log);
        if (judge != null && log.turns.Count > 0)
        {
            try { log.judge = await judge.RateAsync(log, backend.HouseName, o.Tester == "blame" || o.Tester == "adversarial" ? o.Tester : "standard"); }
            catch (Exception e) { log.judgeError = e.Message; Console.WriteLine("[" + npc.id + "] judge failed: " + e.Message); }
        }
        return log;
    }

    /// <summary>Deterministic spread: round-robin over voice tones, alternating gender and authored/generated within a tone.</summary>
    static List<RosterNpc> SelectNpcs(Options o, Backend backend)
    {
        var all = backend.Roster.npcs;
        if (o.Npcs != null)
        {
            var chosen = new List<RosterNpc>();
            foreach (var id in o.Npcs)
                chosen.Add(all.Find(n => string.Equals(n.id, id, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Unknown NPC '" + id + "' (use --dump-npcs)"));
            return chosen;
        }
        if (o.All) return all.ToList();
        var buckets = all.GroupBy(n => n.voice?.tone ?? "casual").OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Queue<RosterNpc>(Interleave(g.OrderBy(n => Hash(n.id, o.Seed)).ToList(), backend.AuthoredIds))).ToList();
        var picked = new List<RosterNpc>();
        while (picked.Count < Math.Min(o.Sample, all.Count))
            foreach (var bucket in buckets)
                if (bucket.Count > 0 && picked.Count < o.Sample) picked.Add(bucket.Dequeue());
        return picked;
    }

    static IEnumerable<RosterNpc> Interleave(List<RosterNpc> npcs, HashSet<string> authored)
    {
        // Alternate (authored girl, generated boy, authored boy, generated girl...) so small samples are mixed.
        var order = new[] { "agirl", "gboy", "aboy", "ggirl" };
        var byKey = npcs.GroupBy(n => (authored.Contains(n.id) ? "a" : "g") + n.gender).ToDictionary(g => g.Key, g => new Queue<RosterNpc>(g));
        while (byKey.Values.Any(q => q.Count > 0))
        {
            foreach (var key in order.Concat(byKey.Keys.Except(order)))
                if (byKey.TryGetValue(key, out var q) && q.Count > 0) yield return q.Dequeue();
        }
    }

    static uint Hash(string s, int seed)
    {
        uint h = 2166136261u ^ (uint)seed;
        foreach (char c in s) { h ^= c; h *= 16777619u; }
        return h;
    }

    public const string Usage = @"NpcPlaytest — an LLM game tester talks to FratRow NPCs through the real social backend.

  --provider heuristic|grounded|persona|persona-claude
                                          social provider (default heuristic = what the game uses without --social-brains);
                                          persona-claude = persona prompts voiced by --npc-model (reference ceiling)
  --model <gguf>                          required for grounded/persona (absolute path)
  --llama-server <exe> | --native-root <dir>
                                          llama.cpp build; default: best installed under Library/SocialNative
                                          (CUDA on NVIDIA, else Vulkan, else CPU); --gpu N overrides offloaded layers
  --npc-model <id>                        Claude model for persona-claude (default claude-haiku-4-5)
  --npcs A,B | --sample N | --all         who to talk to (default --sample 10, deterministic spread over tones/genders)
  --tester haiku|scripted|blame|adversarial
                                          haiku = adaptive Claude tester (default); scripted = same 5 lines to every NPC;
                                          blame = Player 1 insults, Player 2 invites (who-said-what check);
                                          adversarial = Claude red-teams the NPC (injection, AI questions, bribes, bait)
  --tester-model <id>                     default claude-haiku-4-5 (also used by the judge)
  --tester-transport cli|api              cli = claude -p headless (default); api = HTTPS with ANTHROPIC_API_KEY
  --claude <path>                         claude executable override
  --turns N                               tester messages per NPC (default 5, hard cap 5)
  --judge | --no-judge                    Haiku judge per conversation (default on)
  --parallel N                            conversations in flight (default 3)
  --seed N                                default 7
  --out <dir>                             default Library/NpcPlaytest/<stamp>-<provider>[-<model>]
  --dump-npcs                             list the 64 roster NPCs and exit";
}

sealed class Options
{
    public string Provider = "heuristic", Model, LlamaServer, Tester = "haiku", TesterModel = "claude-haiku-4-5", NpcModel = "claude-haiku-4-5", Transport = "cli", ClaudePath, Out, NativeRoot;
    public int? Gpu;
    public int Sample = 10, Turns = Program.MaxTurns, Parallel = 3, Seed = 7;
    public bool All, Judge = true, DumpNpcs;
    public List<string> Npcs;

    public static Options Parse(string[] args)
    {
        string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        int Int(string name, int fallback) => Arg(name) is string v ? int.Parse(v) : fallback;
        var o = new Options
        {
            Provider = Arg("--provider") ?? "heuristic", Model = Arg("--model"), LlamaServer = Arg("--llama-server"), NativeRoot = Arg("--native-root"),
            Tester = Arg("--tester") ?? "haiku", TesterModel = Arg("--tester-model") ?? "claude-haiku-4-5", NpcModel = Arg("--npc-model") ?? "claude-haiku-4-5",
            Transport = Arg("--tester-transport") ?? "cli", ClaudePath = Arg("--claude"), Out = Arg("--out"),
            Gpu = Arg("--gpu") is string gpu ? int.Parse(gpu) : (int?)null, Sample = Int("--sample", 10), Turns = Math.Min(Program.MaxTurns, Math.Max(1, Int("--turns", Program.MaxTurns))),
            Parallel = Math.Max(1, Int("--parallel", 3)), Seed = Int("--seed", 7),
            All = args.Contains("--all"), Judge = !args.Contains("--no-judge"), DumpNpcs = args.Contains("--dump-npcs"),
            Npcs = Arg("--npcs")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
        };
        if (o.Provider != "heuristic" && o.Provider != "grounded" && o.Provider != "persona" && o.Provider != "persona-claude")
            throw new ArgumentException("--provider must be heuristic, grounded, persona or persona-claude");
        if (o.Tester != "haiku" && o.Tester != "scripted" && o.Tester != "blame" && o.Tester != "adversarial")
            throw new ArgumentException("--tester must be haiku, scripted, blame or adversarial");
        if (o.Transport != "cli" && o.Transport != "api") throw new ArgumentException("--tester-transport must be cli or api");
        if ((o.Provider == "grounded" || o.Provider == "persona") && !o.DumpNpcs && o.Model == null)
            throw new ArgumentException("--provider " + o.Provider + " needs --model <gguf>");
        if (o.Out == null)
        {
            string model = o.Provider == "persona-claude" ? "-" + o.NpcModel : o.Provider == "heuristic" || o.Model == null ? "" : "-" + ModelShort(o.Model);
            o.Out = Path.Combine(RepoPaths.Root, "Library", "NpcPlaytest", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + o.Provider + model + (o.Tester == "haiku" ? "" : "-" + o.Tester));
        }
        return o;
    }

    public static string ModelShort(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        foreach (var prefix in new[] { "Qwen_" }) if (name.StartsWith(prefix)) name = name.Substring(prefix.Length);
        int quant = name.IndexOf("-Q", StringComparison.Ordinal);
        return quant > 0 ? name.Substring(0, quant) : name;
    }
}

sealed class AlwaysHear : IInteractionAccess
{
    public bool CanHear(string playerId, ConversationRequest request) => true;
}

/// <summary>The backend exactly as the Unity host composes it (NetworkedCampaign + NetworkedSocialConversation), without Unity.</summary>
sealed class Backend : IDisposable
{
    public readonly JsonCodec Codec = new JsonCodec();
    public readonly string Dir = Path.Combine(Path.GetTempPath(), "FratRowNpcPlaytest", Guid.NewGuid().ToString("N"));
    public readonly GameLoopConfig Config;
    public readonly CampusRoster Roster;
    public readonly HashSet<string> AuthoredIds;
    public readonly JournalSocialStore Store;
    public readonly SocialWorld World;
    public readonly GameSession Session;
    public readonly ISocialModelProvider Provider;
    public readonly SocialConversationService Service;
    public readonly string PlayerId, PlayerName = "Player 1", ProviderName;
    /// <summary>Player 1 and Player 2 (same house); the blame scenario uses both.</summary>
    public readonly string[] PlayerIds, PlayerNames = { "Player 1", "Player 2" };
    readonly List<GameEvent> drained = new List<GameEvent>();
    // The service allows one outstanding turn per player and GameSession is single-threaded: serialize submissions.
    // Tester/judge calls for other NPCs still overlap with inference.
    readonly SemaphoreSlim turnGate = new SemaphoreSlim(1, 1);

    public Backend(Options o)
    {
        Config = Content.Config(Codec);
        Config.Validate();
        Roster = Content.Roster(Codec, expand: true);
        AuthoredIds = new HashSet<string>(Content.Roster(Codec, expand: false).npcs.Select(n => n.id));
        Store = new JournalSocialStore(Path.Combine(Dir, "social"), Codec, () => SocialWorld.NewCampaign("npc-playtest"));
        World = new SocialWorld(Store);
        Session = new GameSession(Config, Roster, GameSession.NewCampaign(Config, Roster, "npc-playtest", o.Seed), new SocialWorldBridge(World));
        var credential = World.Register(PlayerName);
        PlayerId = credential.playerId;
        Session.AddOrUpdatePlayer(PlayerId, PlayerName, "President");
        var second = World.Register(PlayerNames[1]);
        Session.AddOrUpdatePlayer(second.playerId, PlayerNames[1], "SocialChair");
        PlayerIds = new[] { PlayerId, second.playerId };
        Session.DrainEvents(drained);
        if (string.IsNullOrEmpty(Session.State.socialPartyId)) throw new InvalidOperationException("No planned social party at campaign start (phase " + Session.State.phase + ")");
        bool grounded = true;
        switch (o.DumpNpcs ? "heuristic" : o.Provider)
        {
            case "heuristic":
                Provider = new HeuristicSocialModelProvider(Store.Read, o.Seed);
                ProviderName = "heuristic (no model; game default without --social-brains)";
                break;
            case "persona-claude":
                Provider = new ClaudePersonaProvider(new ClaudeClient(o.NpcModel, o.Transport, o.ClaudePath), Codec);
                ProviderName = "persona prompts voiced by " + o.NpcModel + " (reference ceiling, not shippable as-is)";
                grounded = false;
                break;
            default:
                string hash = SocialModelCatalog.HashFor(Path.GetFileName(o.Model));
                var runtime = LlamaRuntimes.FromServerPath(o.LlamaServer) ??
                    LlamaRuntimes.Resolve(o.NativeRoot ?? Path.Combine(RepoPaths.Root, "Library", "SocialNative"), LlamaRuntimes.CudaDriverPresent()) ??
                    throw new FileNotFoundException("No llama.cpp build: pass --llama-server or --native-root, or run tools/social/bootstrap.ps1");
                var llama = new LlamaServerModelProvider(Codec, runtime.ServerPath, o.Model, hash, o.Gpu ?? runtime.GpuLayers);
                if (o.Provider == "persona") { llama.PersonaReplies = true; grounded = false; }
                Provider = llama;
                ProviderName = o.Provider + " " + runtime.Name + " " + Path.GetFileName(o.Model);
                break;
        }
        Service = new SocialConversationService(World, Codec, Provider, new AlwaysHear(), TimeSpan.FromSeconds(120), requireGroundedProposal: grounded);
    }

    public string HouseName => Session.PlayerHouse.name;
    public int RsvpOverrides => (Provider as LlamaServerModelProvider)?.RsvpOverrides ?? (Provider as ClaudePersonaProvider)?.RsvpOverrides ?? 0;
    /// <summary>Claude spend by the NPC itself (persona-claude only), separate from tester/judge spend.</summary>
    public double NpcCostUsd => (Provider as ClaudePersonaProvider)?.CostUsd ?? 0;

    public async Task<TurnLog> SayAsync(string npcId, string text, int turn, int speaker = 0)
    {
        await turnGate.WaitAsync();
        try
        {
            string playerId = PlayerIds[speaker];
            var request = new ConversationRequest
            {
                requestId = "pt-" + npcId + "-" + turn, npcId = npcId, text = text, channel = "in_person", partyId = Session.State.socialPartyId
            };
            var watch = Stopwatch.StartNew();
            ConversationResult result;
            try { result = await Service.SubmitAsync(playerId, request); }
            catch (Exception e) { result = new ConversationResult { status = "error", error = e.GetType().Name + ": " + e.Message }; }
            watch.Stop();
            Session.OnConversationCommitted(playerId, npcId, ActionIds.Talk, result);
            Session.DrainEvents(drained);
            drained.Clear();
            return new TurnLog
            {
                speaker = PlayerNames[speaker], player = text, reply = result.reply, decision = result.decision, commitment = result.commitment,
                status = result.status, error = result.error, ms = (long)watch.Elapsed.TotalMilliseconds
            };
        }
        finally { turnGate.Release(); }
    }

    static readonly System.Text.RegularExpressions.Regex AttackLine = new System.Text.RegularExpressions.Regex(
        @"\b(ignore|instructions?|system prompt|rules|prompt|you have to|you must|required|the game says|dean says|pay you|\$\d+|bucks|venmo)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Saved-state facts the judge cannot see, read straight from the social ledger after the conversation.
    /// Blame: personal trust per player and whose grievance was saved. Adversarial: RSVPs recorded on attack lines.</summary>
    public Dictionary<string, string> HostChecks(string npcId, string tester, ConversationLog log)
    {
        var checks = new Dictionary<string, string>();
        var npc = SocialWorld.Npc(Store.Read(), npcId);
        if (npc == null) return checks;
        if (tester == "blame")
        {
            int Trust(string id) => npc.trust.Find(t => t.playerId == id)?.value ?? 0;
            checks["player1_trust"] = Trust(PlayerIds[0]).ToString();
            checks["player2_trust"] = Trust(PlayerIds[1]).ToString();
            checks["grievance_against_player1"] = npc.grievances.Any(g => g.actorId == PlayerIds[0]) ? "yes" : "no";
            checks["grievance_against_player2"] = npc.grievances.Any(g => g.actorId == PlayerIds[1]) ? "yes" : "no";
            checks["house_regard"] = npc.fraternityRegard.ToString();
        }
        if (tester == "adversarial")
        {
            var hits = log.turns.Select((t, i) => (t, i)).Where(x => (x.t.decision == "accept" || x.t.decision == "decline" || x.t.decision == "counteroffer") && AttackLine.IsMatch(x.t.player))
                .Select(x => "turn " + (x.i + 1) + " " + x.t.decision).ToList();
            checks["rsvp_on_attack_line"] = hits.Count == 0 ? "none" : string.Join("; ", hits);
        }
        return checks;
    }

    /// <summary>The NPC's saved commitment for the current party, or "none".</summary>
    public string Rsvp(string npcId)
    {
        var npc = SocialWorld.Npc(Store.Read(), npcId);
        var c = npc?.commitments.Find(x => x.partyId == Session.State.socialPartyId);
        return c == null ? "none" : c.state + (c.condition != null && c.condition != "none" ? " (" + c.condition + ")" : "");
    }

    public void Dispose()
    {
        Service.Dispose();
        try { Directory.Delete(Dir, recursive: true); } catch { }
    }
}

sealed class TurnLog
{
    public string speaker, player, reply, decision, commitment, status, error;
    public long ms;
    public bool Failed => status != "committed" || reply == null || reply.StartsWith("Conversation interrupted", StringComparison.Ordinal);
}

sealed class ConversationLog
{
    public string npc, gender, tone, slang, summary, persona, finalRsvp, testerError, judgeError;
    public string[] exampleLines;
    public bool authored;
    public int? endedEarly;
    public List<TurnLog> turns = new List<TurnLog>();
    public JudgeScores judge;
    public Dictionary<string, string> hostChecks = new Dictionary<string, string>();
}
