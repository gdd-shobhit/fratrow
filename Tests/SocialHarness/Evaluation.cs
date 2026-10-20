using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FratRow.Social;

sealed class Episode
{
    public string id;
    public string category;
    public Step[] steps;
    public string rubric;
    public string priorObservation;
    public string[] witnesses;
    public bool rumor;
    public float? fraternityPopularity, npcPopularity;
    public string statusConcern, personaOverride;
    public string profileId;
    public PublicGuestSetup[] confirmedGuests;
}
sealed class PublicGuestSetup { public string npcId; public float popularity; }
sealed class Step { public int player; public string text; public bool invitation; }
sealed class BorrowedProvider : ISocialModelProvider
{
    readonly ISocialModelProvider inner;
    public int Attempts { get; private set; }
    public int ModelPasses { get; private set; }
    public double Seconds { get; private set; }
    public void ResetMetrics() { Attempts = 0; Seconds = 0; ModelPasses = 0; }
    public BorrowedProvider(ISocialModelProvider inner) { this.inner = inner; }
    public async Task<SocialProposal> GenerateAsync(ModelContext c, System.Threading.CancellationToken token)
    {
        Attempts++; var timer = Stopwatch.StartNew();
        try { return await inner.GenerateAsync(c, token); }
        finally { Seconds += timer.Elapsed.TotalSeconds; ModelPasses += (inner as LlamaServerModelProvider)?.ModelPasses ?? 0; }
    }
    public void Dispose() { }
}

static class Evaluation
{
    public static async Task<int> Run(string[] args)
    {
        var codec = new JsonCodec();
        var fixtures = JsonSerializer.Deserialize<Episode[]>(File.ReadAllText(Program.Arg(args, "--fixtures", "Tests/SocialHarness/episodes.json")), new JsonSerializerOptions { IncludeFields = true });
        string path = Program.Arg(args, "--model"); string hash = SocialModelCatalog.HashFor(Path.GetFileName(path));
        var runtime = Program.Runtime(args);
        bool compact = args.Contains("--compact");
        bool legacyFreeform = args.Contains("--legacy-freeform");
        using var provider = new LlamaServerModelProvider(codec, runtime.ServerPath, path, hash, runtime.GpuLayers)
            { CompactFormat = compact, GroundedReplies = !legacyFreeform, UseRequestSeed = false };
        bool fixedSeed = args.Contains("--fixed-seed");
        string output = Path.GetFullPath(Program.Arg(args, "--output", "Library/SocialEvaluation")); Directory.CreateDirectory(output);
        string[] personas = {
            "Ashley values respect, dislikes public humiliation, enjoys parties, distinguishes people, and can rebuild trust after disrespect without automatically forgiving anyone.",
            "Ashley is cautious, values reliability over flattery, dislikes pressure, and will prefer a quiet evening over an untrustworthy party. She can be persuaded by credible accountability.",
            "Ashley is outgoing and enjoys playful teasing, values loyalty and accountability, dislikes cruelty, and is willing to attend a good party with a credible friend."
        };
        string defaultNpcId = Program.Arg(args, "--npc-id", "Ashley");
        string gender = Program.Arg(args, "--voice-gender", "girl");
        bool includeGameSetting = !args.Contains("--omit-game-setting");
        int limit = int.Parse(Program.Arg(args, "--limit", "20"));
        int repeats = int.Parse(Program.Arg(args, "--repeats", "3"));
        int personaCount = int.Parse(Program.Arg(args, "--personas", "3"));
        double deadline = double.Parse(Program.Arg(args, "--deadline", "15"), System.Globalization.CultureInfo.InvariantCulture);
        var latencies = new List<double>(); int completed = 0, rejected = 0, turns = 0; bool cold = true;
        using var results = new StreamWriter(Path.Combine(output, "episodes.jsonl"), false);
        foreach (var episode in fixtures.Take(limit))
        for (int persona = 0; persona < personaCount; persona++)
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            provider.Seed = 42 + repeat;
            using var f = new Fixture();
            var profile = string.IsNullOrEmpty(episode.profileId) ? null : SocialCharacterCatalog.Get(episode.profileId);
            string npcId = profile?.npcId ?? defaultNpcId;
            var voice = SocialVoice.Copy(profile?.voice);
            voice.gender = Program.Arg(args, "--voice-gender", profile?.voice.gender ?? gender);
            voice.tone = Program.Arg(args, "--voice-tone", profile?.voice.tone ?? new[] { "assertive", "reserved", "playful" }[persona]);
            voice.language = Program.Arg(args, "--voice-language", voice.language);
            voice.slang = Program.Arg(args, "--voice-slang", voice.slang);
            if (profile == null && voice.tone == "assertive") voice.exampleLines = SocialVoice.Ashley().exampleLines;
            string selectedPersona = (episode.personaOverride ?? profile?.persona ?? personas[persona]).Replace("Ashley", npcId);
            f.World.EnsureNpc(npcId, selectedPersona, voice);
            f.World.SetNpcVoice(npcId, voice);
            f.Store.Transact(s => SocialWorld.Npc(s, npcId).persona = selectedPersona);
            if (episode.fraternityPopularity.HasValue) f.World.SetFraternityStanding(episode.fraternityPopularity.Value, "Pine House");
            if (episode.npcPopularity.HasValue || profile != null)
            {
                var concern = string.IsNullOrEmpty(episode.statusConcern) ? profile?.statusConcern ?? StatusConcern.Moderate :
                    (StatusConcern)Enum.Parse(typeof(StatusConcern), episode.statusConcern, true);
                f.World.SetNpcStanding(npcId, episode.npcPopularity ?? profile.popularity, concern);
            }
            foreach (var guest in episode.confirmedGuests ?? Array.Empty<PublicGuestSetup>())
            {
                f.World.EnsureNpc(guest.npcId, "A publicly confirmed guest.");
                f.World.SetNpcStanding(guest.npcId, guest.popularity);
                f.Store.Transact(s => {
                    var receipt = SocialWorld.AddEvent(s, "public_rsvp", "world", guest.npcId, f.PartyId, "public party roster", "Accepted the party invitation; has not arrived.", new List<string> { npcId });
                    SocialWorld.Npc(s, guest.npcId).commitments.Add(new Commitment { partyId = f.PartyId, inviterId = f.P1.playerId,
                        state = "accepted", condition = "none", decisionEventId = receipt.eventId });
                    SocialWorld.Party(s, f.PartyId).revision++;
                });
            }
            if (!string.IsNullOrEmpty(episode.priorObservation))
            {
                string reporter = npcId == "Brad" ? "Morgan" : "Brad";
                f.World.EnsureNpc(reporter, "Reports only what he knows.");
                var witnesses = (episode.witnesses ?? Array.Empty<string>()).Select(n => n == "Ashley" ? npcId : n == "Brad" ? reporter : n).ToArray();
                var eventId = f.World.Observe("observed_action", f.P1.playerId, npcId, episode.priorObservation, "Quad", witnesses);
                if (episode.rumor) f.World.TellRumor(reporter, npcId, eventId);
            }
            var borrowed = new BorrowedProvider(provider);
            using var service = new SocialConversationService(f.World, codec, borrowed, f.Access, TimeSpan.FromSeconds(deadline), includeGameSetting, requireGroundedProposal: !legacyFreeform);
            int stepIndex = 0;
            foreach (var step in episode.steps)
            {
                int samplingSeed = 42 + repeat;
                if (!fixedSeed)
                {
                    // Match low/high draws for one NPC, without reusing a single draw across all characters.
                    foreach (char ch in npcId + ":" + stepIndex) samplingSeed = unchecked(samplingSeed * 31 + ch);
                    samplingSeed &= int.MaxValue;
                }
                provider.Seed = samplingSeed;
                var request = f.Request(step.text, npc: npcId); if (!step.invitation) request.partyId = null;
                string player = step.player == 1 ? f.P1.playerId : f.P2.playerId;
                borrowed.ResetMetrics();
                var watch = Stopwatch.StartNew();
                var result = await service.SubmitAsync(player, request); watch.Stop();
                if (!cold) latencies.Add(watch.Elapsed.TotalSeconds);
                var llama = provider;
                results.WriteLine(codec.Encode(new {
                    episode.id, episode.category, episode.rubric, episode.profileId, npcId, voice, includeGameSetting, persona, repeat, step = stepIndex++, player = step.player, step.text,
                    cold, seconds = watch.Elapsed.TotalSeconds, result, snapshot = f.Store.Read(),
                    generationAttempts = borrowed.Attempts, providerSeconds = borrowed.Seconds,
                    otherProcessingSeconds = Math.Max(0, watch.Elapsed.TotalSeconds - borrowed.Seconds),
                    promptTokens = llama.PromptTokens, outputTokens = llama.OutputTokens,
                    raw = llama.LastRawOutput,
                    rawDecision = llama.LastRawDecision, rawReply = llama.LastRawReply,
                    rawCheck = llama.LastRawCheck,
                    modelPasses = borrowed.ModelPasses,
                    replySource = legacyFreeform ? "model-freeform" : "host-grounded-composer",
                    samplingSeed,
                    validationSource = legacyFreeform ? "model-audit-and-host-schema" : "deterministic-host-acts-and-composition",
                    checksumMilliseconds = llama.ChecksumMilliseconds,
                    loadMilliseconds = llama.LoadMilliseconds,
                    generationMilliseconds = llama.GenerationMilliseconds,
                    helperPid = llama.ProcessId, helperPeakWorkingSet = llama.PeakWorkingSet, helperPrivateBytes = llama.PrivateBytes,
                    hostPeakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64,
                    attributionCorrect = (bool?)null, sociallyAcceptable = (bool?)null, dialogueStateAgreement = (bool?)null
                }));
                results.Flush(); cold = false; turns++;
                if (result.status != "committed") rejected++;
            }
            completed++;
            Console.WriteLine($"Episode {episode.id} persona {persona} repeat {repeat} complete");
        }
        var pairedFirst = new List<double>(); var pairedSecond = new List<double>();
        int pairedUncommitted = 0;
        using (var f = new Fixture())
        using (var service = new SocialConversationService(f.World, codec, new BorrowedProvider(provider), f.Access, TimeSpan.FromSeconds(deadline), includeGameSetting, requireGroundedProposal: !legacyFreeform))
        using (var pairs = new StreamWriter(Path.Combine(output, "paired-speakers.jsonl"), false))
        {
            string npcId = defaultNpcId;
            var pairedVoice = new NpcVoiceProfile { gender = gender, tone = Program.Arg(args, "--voice-tone", "assertive"),
                language = Program.Arg(args, "--voice-language", "English"), slang = Program.Arg(args, "--voice-slang", "light") };
            f.World.EnsureNpc(npcId, personas[0].Replace("Ashley", npcId), pairedVoice);
            f.World.SetNpcVoice(npcId, pairedVoice);
            int count = int.Parse(Program.Arg(args, "--pairs", "20"));
            for (int i = 0; i < count; i++)
            {
                var first = f.Request("How is your day going?", npc: npcId); first.partyId = null;
                var second = f.Request("Good to see you, " + npcId + ".", npc: npcId); second.partyId = null;
                var watch = Stopwatch.StartNew();
                async Task<(ConversationResult result, double seconds)> Measure(Task<ConversationResult> task)
                { var result = await task; return (result, watch.Elapsed.TotalSeconds); }
                var a = Measure(service.SubmitAsync(f.P1.playerId, first));
                var b = Measure(service.SubmitAsync(f.P2.playerId, second));
                var measured = await Task.WhenAll(a, b);
                pairedFirst.Add(measured[0].seconds); pairedSecond.Add(measured[1].seconds);
                pairedUncommitted += measured.Count(m => m.result.status != "committed");
                pairs.WriteLine(codec.Encode(new { pair = i, npcId, voice = pairedVoice, firstSeconds = measured[0].seconds, secondSeconds = measured[1].seconds,
                    firstResult = measured[0].result, secondResult = measured[1].result })); pairs.Flush();
            }
        }
        double Percentile(List<double> values) { values.Sort(); return values.Count == 0 ? 0 : values[(int)Math.Ceiling(values.Count * .95) - 1]; }
        double p95 = Percentile(latencies); double firstP95 = Percentile(pairedFirst); double secondP95 = Percentile(pairedSecond);
        File.WriteAllText(Path.Combine(output, "summary.json"), codec.Encode(new {
            model = Path.GetFileName(path), hash, runtime = runtime.Name,
            npcId = defaultNpcId, characterProfiles = fixtures.Take(limit).Select(e => e.profileId).Where(id => id != null).Distinct().ToArray(),
            includeGameSetting, voiceGender = gender, compactFormat = compact, inferenceFormat = legacyFreeform ? "legacy-freeform" : "grounded-acts-v2",
            replySource = legacyFreeform ? "model-freeform" : "host-grounded-composer",
            sampling = fixedSeed ? "fixed-seed-per-repeat" : "matched-npc-step-repeat-seed",
            episodes = completed, turns, uncommittedTurns = rejected, warmP95Seconds = p95,
            pairedSamples = pairedFirst.Count, pairedUncommitted, pairedFirstP95Seconds = firstP95, pairedSecondP95Seconds = secondP95,
            singleRequestLatencyTargetMet = latencies.Count > 0 && p95 <= 4,
            pairedLatencyTargetMet = pairedFirst.Count == 0 ? (bool?)null : firstP95 <= 4 && secondP95 <= 8,
            latencyTargetMet = latencies.Count > 0 && pairedFirst.Count > 0 && p95 <= 4 && firstP95 <= 4 && secondP95 <= 8,
            allTurnsCommitted = rejected == 0 && pairedUncommitted == 0,
            humanReviewRequired = true, attributionTarget = .95, socialQualityTarget = .85,
            memoryCeilingCertified = false, gpuProcessMemory = "Measure externally; WDDM can return unavailable. Process RAM alone is not combined inference memory.",
            graphicsFrameImpact = "Requires licensed Unity runtime; not measured by console harness"
        }));
        Console.WriteLine($"{completed} episodes, {rejected} uncommitted turns, warm p95 {p95:F3}s; human grading required.");
        return rejected == 0 ? 0 : 2;
    }
}
