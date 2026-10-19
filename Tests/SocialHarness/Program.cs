using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FratRow.Social;

sealed class JsonCodec : IStateCodec
{
    static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
    public string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Options);
}
sealed class Access : IInteractionAccess
{
    public bool Allowed = true;
    public bool CanHear(string player, ConversationRequest request) => Allowed;
}
sealed class FakeProvider : ISocialModelProvider
{
    public int Calls, Active, MaxActive;
    public Func<ModelContext, SocialProposal> Decide = c => Proposal(c);
    public Func<ModelContext, Task> During;
    public async Task<SocialProposal> GenerateAsync(ModelContext c, CancellationToken cancellation)
    {
        Calls++; Active++; MaxActive = Math.Max(MaxActive, Active);
        try { if (During != null) await During(c); cancellation.ThrowIfCancellationRequested(); return Decide(c); }
        finally { Active--; }
    }
    public static SocialProposal Proposal(ModelContext c, string decision = "respond", string magnitude = "down") => new SocialProposal
    {
        schema = 1, decision = decision, evidence_ids = new[] { c.ObservedEventId },
        relationship_effects = new[] { new RelationshipEffect { scope = "player", id = c.PlayerId, magnitude = magnitude },
            new RelationshipEffect { scope = "house", id = c.FraternityId, magnitude = magnitude } },
        condition = decision == "counteroffer" ? SocialRules.EntranceCondition : "none",
        interpretation = "Test interpretation", reply = decision == "accept" ? "Yes, I will attend." : decision == "decline" ? "No, I am staying away." :
            decision == "counteroffer" ? "Meet me at the entrance or I won't come in." : "I remember who said that."
    };
    public void Dispose() { }
}
sealed class Fixture : IDisposable
{
    public readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "FratRowSocialTests", Guid.NewGuid().ToString("N"));
    public readonly JsonCodec Codec = new JsonCodec();
    public readonly FakeProvider Provider = new FakeProvider();
    public readonly Access Access = new Access();
    public JournalSocialStore Store;
    public SocialWorld World;
    public SocialConversationService Service;
    public PlayerCredential P1, P2;
    public string PartyId;
    public Fixture()
    {
        Store = new JournalSocialStore(DirectoryPath, Codec, () => SocialWorld.NewCampaign("campaign"));
        World = new SocialWorld(Store);
        World.EnsureNpc("Ashley", "Values respect, dislikes public humiliation, likes parties, willing to negotiate without automatic forgiveness.", SocialVoice.Ashley());
        P1 = World.Register("Player One"); P2 = World.Register("Player Two"); PartyId = World.EnsurePlannedParty(20);
        Service = new SocialConversationService(World, Codec, Provider, Access);
    }
    public ConversationRequest Request(string text = "Hello", string id = null, string npc = "Ashley") =>
        new ConversationRequest { requestId = id ?? SocialWorld.Id(), npcId = npc, text = text, partyId = PartyId };
    public void Dispose() { Service.Dispose(); } // Preserve failure artifacts in temp; no destructive cleanup.
}
static class Program
{
    static int passed, failed;
    static void Assert(bool condition, string why) { if (!condition) throw new Exception(why); }
    static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static async Task Test(string name, Func<Task> test)
    {
        try { await test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + " " + e); }
    }
    static SocialProposal Grounded(ModelContext c, IStateCodec codec, string decision = "respond", string attitude = "neutral", string topic = "none", string phrasing = "a", string trust = "unchanged", string regard = "unchanged", string grievance = "")
    {
        var ids = new SocialInferenceFormat(c);
        return new GroundedSocialTurn(c).Decode(codec.Encode(new { decision, attitude, reason = "conversation", topic = ids.RenderIds(topic), phrasing,
            evidence = new[] { ids.Context.ObservedEventId }, trust, regard, grievance = ids.RenderIds(grievance), resolve = "" }), codec);
    }
    static async Task<int> Main(string[] args)
    {
        if (args.Contains("--export-girls")) { Console.WriteLine(new JsonCodec().Encode(SocialCharacterCatalog.Girls())); return 0; }
        if (args.Contains("--native")) return await NativeSmoke(args);
        if (args.Contains("--probe"))
        {
            using var probe = new LlamaServerModelProvider(new JsonCodec(), Arg(args, "--llama-server"), Arg(args, "--model"), SocialModelCatalog.HashFor(Path.GetFileName(Arg(args, "--model"))));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            Console.WriteLine(await probe.ProbeTextAsync("You are Ashley, replying to Player Two. Write one or two sentences, no JSON. Use only the facts supplied. Do not invent events, dates or activities.",
                "Player One told you: 'You are pathetic. Nobody wants you at our fraternity house.' Player Two is a different person in the same fraternity. Player Two now says: 'That was cruel. I am sorry he treated you that way. Would you consider coming to our party?' Reply as Ashley to Player Two, remembering Player One said the insult. You may still be wary of their fraternity.", deadline.Token));
            Console.WriteLine(string.Join("\n", probe.Diagnostics.ToString().Split('\n').Where(x => x.Contains("thinking") || x.Contains("chat template"))));
            return 0;
        }
        if (args.Contains("--evaluate")) return await Evaluation.Run(args);
        await Test("sampling stays tied to the durable host receipt across retries and renamed reconnects", async () =>
        {
            using var f = new Fixture(); int? firstSeed = null;
            var request = f.Request(id: "client-chosen-seed");
            f.Provider.Decide = c => { firstSeed ??= c.SamplingSeed; Assert(c.SamplingSeed == firstSeed, "Repair changed the sampling seed"); throw new ArgumentException("Interrupted draft"); };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, request)).status == "pending", "Failed draft unexpectedly committed");
            f.World.Register("A new name", f.P1);
            var s = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception()).Read();
            var turn = s.turns.Single(); var a = SocialMemory.Build(s, turn, f.Codec);
            turn.request.requestId = "different-client-text"; var b = SocialMemory.Build(s, turn, f.Codec);
            Assert(a.SamplingSeed == firstSeed && a.SamplingSeed == b.SamplingSeed, "A rename or client request ID controlled the saved draw");
            f.Provider.Decide = c => { Assert(c.SamplingSeed == firstSeed, "Retry forgot the receipt draw"); return Grounded(c, f.Codec); };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, request)).status == "committed" && f.Store.Read().events.Count(e => e.source == "speech") == 1,
                "Retry created another draw/utterance");
        });
        await Test("grounded attendance remains consistent across every mood, tone and phrasing", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => {
                foreach (string tone in new[] { "casual", "assertive", "bubbly", "dry", "reserved", "playful", "laid-back" })
                foreach (string mood in new[] { "warm", "neutral", "dismissive", "playful", "annoyed", "wary" })
                foreach (string wording in new[] { "a", "b" })
                foreach (string decision in new[] { "respond", "accept", "decline", "defer", "counteroffer" })
                {
                    c.Voice.tone = tone; var p = Grounded(c, f.Codec, decision, mood, phrasing: wording);
                    SocialConversationService.ValidateProposal(p, c);
                    Assert(p.reply.Length <= 300, "Grounded wording overflowed");
                    if (decision == "accept") Assert(p.reply.Contains("I'll come") || p.reply.Contains("I'm in."), "Acceptance not expressed");
                    if (decision == "decline") Assert(p.reply.Contains("not coming") || p.reply.Contains("pass on the party"), "Refusal not expressed");
                    if (decision == "defer") Assert(p.reply.Contains("haven't decided") || p.reply.Contains("Let me think"), "Pending decision not expressed");
                    if (decision == "counteroffer") Assert(p.reply.Contains("first minute") && (p.reply.Contains("you meet me") || p.reply.Contains("Meet me")), "Entrance promise reversed actor/deadline");
                }
                c.Voice.tone = "assertive";
                return Grounded(c, f.Codec, "decline", "annoyed");
            };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request("Hello"))).decision == "decline", "Rudeness after a neutral invitation was prohibited");
        });
        await Test("invented facts or changed RSVP prose cannot pass grounded commit validation", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => { var p = Grounded(c, f.Codec, "accept"); p.reply += " I have a dorm curfew at 6 AM."; return p; };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request())).status == "pending", "Invented schedule committed");
            f.Provider.Decide = c => { var p = Grounded(c, f.Codec, "decline"); p.reply = "Yeah, I'll come."; return p; };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request())).status == "pending", "Contradictory attendance wording committed");
            var s = f.Store.Read(); Assert(s.npcs[0].commitments.Count == 0 && !s.events.Any(e => e.source == "inference"), "Rejected drafts applied consequences");
        });
        await Test("host can require grounded acts and reject a freeform provider", async () =>
        {
            using var f = new Fixture(); using var service = new SocialConversationService(f.World, f.Codec, f.Provider, f.Access, requireGroundedProposal: true);
            Assert((await service.SubmitAsync(f.P1.playerId, f.Request())).status == "pending" && f.Store.Read().npcs[0].trust.Count == 0, "Ungrounded proposal bypassed required grounding");
        });
        await Test("heuristic speech cues match whole words, not fragments inside other words", () =>
        {
            Assert(!SpeechCues.IsInvite("what are you into these days?"), "'you into' read as the invite 'you in'");
            Assert(!SpeechCues.IsInvite("welcome to campus, how's it going?"), "'welcome' read as the invite 'come'");
            Assert(!SpeechCues.IsCompliment("that's a cooler take than I expected") || SpeechCues.IsCompliment("cool"), "Compliment cue missing");
            Assert(SpeechCues.IsInvite("we're partying Saturday, you in?") && SpeechCues.IsInvite("you're coming tonight right?") && SpeechCues.IsInvite("Party at our house!"),
                "Real invitations were missed");
            Assert(SpeechCues.IsRush("ever thought about rushing?") && SpeechCues.IsInsult("you suck") && SpeechCues.IsApology("I'm sorry about that"), "Inflected cues were missed");
            // DrunkSpeech.Stretch repeats a word's last letter 3-5 times, even on keyword-safe invite lines.
            Assert(SpeechCues.IsInvite("partyyy at our houssse saturdayyy") && SpeechCues.IsInvite("comeee thru") && SpeechCues.IsApology("sorryyyy"),
                "Drunk-stretched cues were missed");
            Assert(SpeechCues.IsInsult("that's grosssss"), "Stretched double-letter words were missed");
            return Task.CompletedTask;
        });
        await Test("persona cleanup keeps one short in-character line", () =>
        {
            Assert(PersonaSocialTurn.Clean("Brad: Bro, say less! Who else is coming? Player One: cool", "Brad", "Player One") == "Bro, say less! Who else is coming?", "Name label or continued dialogue survived");
            Assert(PersonaSocialTurn.Clean("*shrugs* “Maybe, I guess.”", "Brad", "Player One") == "Maybe, I guess.", "Stage direction or quotes survived");
            Assert(PersonaSocialTurn.Clean("One. Two. Three. Four. Five.", "Brad", "Player One") == "One. Two. Three.", "More than three sentences survived");
            Assert(PersonaSocialTurn.Clean("Tell me about you: what's the vibe?", "Brad", "Player One") == "Tell me about you: what's the vibe?", "A colon inside speech was treated as a label");
            Assert(PersonaSocialTurn.Clean(new string('a', 400), "Brad", "Player One").Length <= 281, "Overlong reply survived");
            try { PersonaSocialTurn.Clean(" *waves* ", "Brad", "Player One"); throw new Exception("Empty reply accepted"); } catch (ArgumentException) { }
            return Task.CompletedTask;
        });
        await Test("persona RSVP check blocks contradictions, not ordinary chat", () =>
        {
            Assert(PersonaSocialTurn.Contradiction("accept", "Bro, say less, I'll be there.") == null, "Plain acceptance rejected");
            Assert(PersonaSocialTurn.Contradiction("accept", "Nah, I'll pass this time.") != null, "Refusal passed as acceptance");
            Assert(PersonaSocialTurn.Contradiction("decline", "Okay fine, I'm in!") != null, "Acceptance passed as refusal");
            Assert(PersonaSocialTurn.Contradiction("decline", "Not my scene, sorry. Have fun though.") == null, "Soft refusal rejected");
            Assert(PersonaSocialTurn.Contradiction("defer", "Maybe, let me think about it.") == null, "Noncommittal reply rejected");
            Assert(PersonaSocialTurn.Contradiction("defer", "See you there!") != null, "Firm yes passed as undecided");
            Assert(PersonaSocialTurn.Contradiction("respond", "Mostly lifting and bad movies, honestly. You?") == null, "Small talk rejected");
            Assert(PersonaSocialTurn.Contradiction("respond", "Sure, I'll come.") != null, "New RSVP announced without a decision");
            Assert(PersonaSocialTurn.Contradiction("respond", "Yeah, I'll come, I said that already.", "accepted") == null, "Reaffirmed RSVP rejected");
            Assert(PersonaSocialTurn.Contradiction("counteroffer", "Meet me at the door in the first minute and I'm there.") == null, "Entrance condition rejected");
            Assert(PersonaSocialTurn.Contradiction("counteroffer", "I'll come if you bring snacks.") != null, "Invented condition passed");
            return Task.CompletedTask;
        });
        await Test("persona context carries this player's chat history and plain facts without host IDs", async () =>
        {
            using var f = new Fixture(); ModelContext seen = null;
            f.Provider.Decide = c => Grounded(c, f.Codec);
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("hey Ashley"));
            await f.Service.SubmitAsync(f.P2.playerId, f.Request("Player Two private line"));
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("what are you into?"));
            f.Provider.Decide = c => { seen = c; return Grounded(c, f.Codec); };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("party Saturday, you in?"));
            Assert(seen.Conversation.Count == 4 && seen.Conversation[0].speaker == "player" && seen.Conversation[0].text == "hey Ashley" &&
                seen.Conversation[1].speaker == "npc" && seen.Conversation[3].speaker == "npc", "Chat history missing, reordered or mislabeled");
            Assert(!seen.Conversation.Any(l => l.text.Contains("Player Two")), "Another player's conversation leaked into this chat");
            Assert(seen.CurrentText == "party Saturday, you in?" && seen.SpeakerName == "Player One", "Current speech or speaker missing");
            Assert(!System.Text.RegularExpressions.Regex.IsMatch(seen.Situation, "[0-9a-f]{32}") && seen.Situation.Contains("party"), "Situation leaked host IDs or omitted the party");
            var history = new PersonaSocialTurn(seen).VoiceHistory();
            Assert(history.Count == 5 && history[4].Key == "user" && history[4].Value == "party Saturday, you in?" && history[3].Key == "assistant", "Voice history does not end with the current message");
        });
        await Test("voiced persona replies commit through validation; forged state and required grounding still reject them", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => new PersonaSocialTurn(c).Build("accept", "unchanged", "up_small", "", "", "Bro, say less. I'm there.");
            var result = await f.Service.SubmitAsync(f.P1.playerId, f.Request("party Saturday?"));
            Assert(result.status == "committed" && result.commitment == "accepted" && result.reply == "Bro, say less. I'm there.", "Voiced reply did not commit with its decision");
            f.Provider.Decide = c => { var p = new PersonaSocialTurn(c).Build("accept", "up", "unchanged", "", "", "Sure."); p.relationship_effects[0].id = f.P2.playerId; return p; };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request("again?"))).status == "pending", "Voiced reply laundered an effect on another player");
            using var strict = new SocialConversationService(f.World, f.Codec, new FakeProvider { Decide = c => new PersonaSocialTurn(c).Build("respond", "unchanged", "unchanged", "", "", "Hey.") }, f.Access, requireGroundedProposal: true);
            Assert((await strict.SubmitAsync(f.P1.playerId, f.Request("hi"))).status == "pending", "Grounding-required service accepted a voiced reply");
        });
        await Test("persona state labels follow explicit wording and stay inside allowed decisions", async () =>
        {
            using var f = new Fixture(); var codec = f.Codec; string failure = null;
            f.Provider.Decide = c => { try { return Checks(c); } catch (Exception e) { failure = e.Message; throw; } };
            SocialProposal Checks(ModelContext c)
            {
                var t = new PersonaSocialTurn(c); string current = new SocialInferenceFormat(c).RenderIds(c.ObservedEventId);
                string Label(string decision, string grievance = "") => codec.Encode(new { decision, trust = "unchanged", regard = "unchanged", grievance, resolve = "" });
                Assert(t.Decode(Label("accept"), "Nah, I'll pass this time.", codec).decision == "decline", "Explicit refusal kept an accept label");
                Assert(t.Decode(Label("respond"), "Okay fine, count me in.", codec).decision == "accept", "Explicit yes stayed an ordinary reply");
                Assert(t.Decode(Label("accept"), "Maybe, I'll think about it.", codec).decision == "defer", "Explicit maybe stayed an acceptance");
                Assert(t.Decode(Label("accept"), "Bro, say less.", codec).decision == "accept" && t.Override == null, "Slang acceptance without explicit wording was overridden");
                Assert(t.Decode(Label("respond"), "Mostly lifting, you?", codec).decision == "respond", "Small talk gained an RSVP");
                Assert(t.Decode(Label("counteroffer"), "I'll come if you bring snacks.", codec).decision == "defer", "Counteroffer with an invented condition was not treated as undecided");
                var cruel = t.Decode(Label("respond", current), "Wow. Rude.", codec);
                Assert(cruel.grievance_event_id == c.ObservedEventId, "Grievance label did not restore the received speech ID");
                try { t.Decode(codec.Encode(new { decision = "respond", trust = "furious", regard = "unchanged", grievance = "", resolve = "" }), "Hey.", codec); throw new Exception("Invalid magnitude accepted"); }
                catch (ArgumentException) { }
                Assert(t.StateGrammar.Contains("\"\\\"accept\\\"\"") && !t.StateGrammar.Contains(c.ObservedEventId), "State grammar omits live decisions or leaks a raw host ID");
                return t.Build("respond", "unchanged", "unchanged", "", "", "Hey.");
            }
            var labeled = await f.Service.SubmitAsync(f.P1.playerId, f.Request("party Saturday, you in?"));
            Assert(failure == null, failure ?? ""); Assert(labeled.status == "committed", "Label checks did not run to completion");
            using var h = new Fixture(); string playtestFailure = null;
            // Regressions from the 18 Oct persona playtest (Tessa, Benji, Malik, Priya).
            h.Provider.Decide = c => { try { return Playtest(c); } catch (Exception e) { playtestFailure = e.Message; throw; } };
            SocialProposal Playtest(ModelContext c)
            {
                var t = new PersonaSocialTurn(c);
                string L(string decision) => codec.Encode(new { decision, trust = "unchanged", regard = "unchanged", grievance = "", resolve = "" });
                if (c.CurrentText == "what are you into?")
                    Assert(t.Decode(L("accept"), "Details, or I won't attend.", codec).decision == "respond", "RSVP saved before any invitation");
                if (c.CurrentText == "party Saturday, you should come")
                {
                    Assert(t.Decode(L("accept"), "I'll come, but only if they give me a list of the guests.", codec).decision == "defer", "Conditional yes saved as a firm accept");
                    Assert(t.Decode(L("accept"), "Maybe. But if the playlist is good, yeah, I'll be there.", codec).decision == "defer", "Hedged yes saved as a firm accept");
                    Assert(t.Decode(L("respond"), "Nope, not tonight. Too busy.", codec).decision == "decline", "Plain refusal stayed an ordinary reply");
                    Assert(t.Decode(L("accept"), "If the music works and you bring a decent crowd, yeah.", codec).decision == "defer", "Conditional informal yes saved as a firm accept");
                    Assert(t.Decode(L("accept"), "I'll be there. Let me know if you need anything.", codec).decision == "accept", "An unrelated 'if' sentence downgraded a plain yes");
                    return t.Build("accept", "unchanged", "unchanged", "", "", "Bro, say less.");
                }
                if (c.CurrentText == "you seem hard to impress")
                {
                    Assert(t.ExistingCommitment == "accepted", "Fixture did not record the acceptance");
                    Assert(t.Decode(L("defer"), "Eww, yeah, but hey, I'm just as happy coming!", codec).decision == "respond", "Accepted RSVP silently downgraded to undecided");
                    Assert(t.Decode(L("decline"), "Actually no, I'm not coming.", codec).decision == "decline", "Explicit withdrawal was blocked");
                }
                return t.Build("respond", "unchanged", "unchanged", "", "", "Hey.");
            }
            foreach (var line in new[] { "what are you into?", "party Saturday, you should come", "you seem hard to impress" })
            {
                var r = await h.Service.SubmitAsync(h.P1.playerId, h.Request(line));
                Assert(playtestFailure == null, playtestFailure ?? ""); Assert(r.status == "committed", "Playtest regression turn did not commit: " + line);
            }
            using var g = new Fixture(); var noParty = g.Request("hey"); noParty.partyId = null;
            g.Provider.Decide = c => { var t = new PersonaSocialTurn(c); Assert(t.Reconcile("accept", "I'm in!") == "respond", "An RSVP survived without a live party"); return t.Build("respond", "unchanged", "unchanged", "", "", "Hey."); };
            Assert((await g.Service.SubmitAsync(g.P1.playerId, noParty)).status == "committed", "No-party label check did not run");
        });
        await Test("persona repeat check catches a copied earlier line, not a new one", async () =>
        {
            using var f = new Fixture(); ModelContext seen = null;
            f.Provider.Decide = c => { seen = c; return new PersonaSocialTurn(c).Build("respond", "unchanged", "unchanged", "", "", "Bro, say less. Who else is coming through? If it's packed I'm here."); };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("party Saturday"));
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("you seem boring"));
            var t = new PersonaSocialTurn(seen);
            Assert(t.Repeats("bro, say less. who else is coming through? if it's packed, i'm here."), "Copied line passed as new");
            Assert(!t.Repeats("Boring? I deadlift on Tuesdays, that's elite behavior."), "New line flagged as a repeat");
        });
        await Test("a host-observed drunk speaker reaches every prompt and the grounded reply within budget", async () =>
        {
            using var f = new Fixture(); ModelContext seen = null;
            f.Provider.Decide = c => {
                seen = c;
                foreach (string tone in new[] { "casual", "assertive", "bubbly", "dry", "reserved", "playful", "laid-back" })
                foreach (string mood in new[] { "warm", "neutral", "dismissive", "playful", "annoyed", "wary" })
                foreach (string decision in new[] { "respond", "accept", "decline", "defer", "counteroffer" })
                {
                    c.Voice.tone = tone; var p = Grounded(c, f.Codec, decision, mood);
                    SocialConversationService.ValidateProposal(p, c);
                    Assert(p.reply.Length <= 300 && (p.reply.Contains("wasted") || p.reply.Contains("hammered")), "Drunk aside missing or over budget: " + p.reply);
                }
                c.Voice.tone = "casual";
                return Grounded(c, f.Codec);
            };
            var request = f.Request("heyyy ashley shaturday party at oursh *hic*"); request.speakerCondition = SpeakerConditions.Wasted;
            var result = await f.Service.SubmitAsync(f.P1.playerId, request);
            Assert(result.status == "committed" && (result.reply.Contains("wasted") || result.reply.Contains("hammered")), "Committed reply should remark on it: " + result.reply);
            Assert(seen.SpeakerCondition == SpeakerConditions.Wasted && seen.Prompt.Contains("SPEAKER_CONDITION completely wasted") && seen.CompactPrompt.Contains("SPEAKER_CONDITION:") &&
                   seen.FactPrompt.Contains("CURRENT_SPEAKER_CONDITION") && seen.DecisionFacts.Contains("Speaker condition="), "Condition missing from a prompt projection");
            var reloaded = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception()).Read();
            Assert(reloaded.turns.Single().request.speakerCondition == SpeakerConditions.Wasted, "Condition is journaled with the turn");
        });
        await Test("persona replies see a host-observed drunk speaker in plain English, and sober speakers get no remark", async () =>
        {
            using var f = new Fixture(); ModelContext seen = null;
            f.Provider.Decide = c => { seen = c; return new PersonaSocialTurn(c).Build("respond", "unchanged", "unchanged", "", "", "Hey."); };
            var drunk = f.Request("heyyy ashley shaturday party at oursh *hic*"); drunk.speakerCondition = SpeakerConditions.Wasted;
            await f.Service.SubmitAsync(f.P1.playerId, drunk);
            var turn = new PersonaSocialTurn(seen);
            Assert(seen.Situation.Contains("completely wasted") && turn.VoiceSystem().Contains("completely wasted") && turn.StatePrompt("Hey.").Contains("speaker is wasted"),
                "Persona voice or state pass did not see the drunk speaker");
            await f.Service.SubmitAsync(f.P2.playerId, f.Request("hey ashley"));
            Assert(!seen.Situation.Contains("drunk") && !seen.Situation.Contains("wasted") && !seen.Situation.Contains("tipsy"), "Sober speaker described as drunk");
        });
        await Test("an invented speaker condition is rejected and old journal requests replay as sober", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => Grounded(c, f.Codec);
            var request = f.Request(); request.speakerCondition = "sober-and-charming";
            await Throws<ArgumentException>(() => f.Service.SubmitAsync(f.P1.playerId, request));
            var legacy = f.Codec.Decode<ConversationRequest>("{\"requestId\":\"r1\",\"npcId\":\"Ashley\",\"text\":\"Hi\",\"channel\":\"text\",\"partyId\":null}");
            Assert(legacy.speakerCondition == SpeakerConditions.Sober, "A pre-drunk journal request must decode as sober");
            var sober = await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            Assert(sober.status == "committed" && !sober.reply.Contains("wasted") && !sober.reply.Contains("had a few"), "No aside for a sober speaker: " + sober.reply);
        });
        await Test("grounded memory clauses preserve the original speaker for a second fraternity member", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => Grounded(c, f.Codec, "respond", "annoyed", trust: "down", regard: "down_small", grievance: c.ObservedEventId);
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("You're pathetic"));
            string cause = f.Store.Read().npcs[0].grievances.Single().eventId;
            f.Provider.Decide = c => Grounded(c, f.Codec, "accept", "wary", "memory:" + cause);
            var result = await f.Service.SubmitAsync(f.P2.playerId, f.Request("He was cruel, would you come with me?"));
            Assert(result.reply.Contains("Player One") && result.reply.Contains("That wasn't you") && result.commitment == "accepted", "Memory wording blamed the current speaker");
            f.Provider.Decide = c => {
                foreach (string tone in new[] { "casual", "assertive", "bubbly", "dry", "reserved", "playful", "laid-back" })
                foreach (string decision in new[] { "respond", "accept", "decline", "defer", "counteroffer" })
                {
                    c.Voice.tone = tone;
                    var candidate = Grounded(c, f.Codec, decision, "annoyed", "guests");
                    Assert(candidate.reply.Contains("Player One") && candidate.reply.Contains("That wasn't you") && candidate.reply.Length <= 300,
                        "Choosing another topic lost attribution or overflowed");
                }
                return Grounded(c, f.Codec, "decline", "annoyed", "guests");
            };
            Assert((await f.Service.SubmitAsync(f.P2.playerId, f.Request("Who else is coming?"))).reply.Contains("That wasn't you"), "Non-memory topic lost the original actor");
            var n = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception()).Read().npcs[0];
            Assert(n.trust.Single(t => t.playerId == f.P1.playerId).value == -5 && n.fraternityRegard == -2 && n.grievances.Single().actorId == f.P1.playerId, "Grounded attribution did not survive reload");
        });
        await Test("grounded dialogue keeps gossip reported and rejects unheard memory references", async () =>
        {
            using var f = new Fixture(); f.World.EnsureNpc("Brad", "Witness");
            string unseen = f.World.Observe("insult", f.P1.playerId, "Ashley", "An insult", "Quad", "Brad");
            string report = f.World.TellRumor("Brad", "Ashley", unseen);
            f.Provider.Decide = c => Grounded(c, f.Codec, grievance: report);
            await f.Service.SubmitAsync(f.P2.playerId, f.Request());
            f.Provider.Decide = c => Grounded(c, f.Codec, topic: "memory:" + report);
            Assert((await f.Service.SubmitAsync(f.P2.playerId, f.Request())).reply.Contains("I didn't witness it"), "Report became firsthand dialogue");
            f.Provider.Decide = c => Grounded(c, f.Codec, topic: "memory:" + unseen);
            Assert((await f.Service.SubmitAsync(f.P2.playerId, f.Request())).status == "pending", "Unheard evidence reached dialogue");
        });
        await Test("grounded grammar excludes nonexistent parties and remembers introductions", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => Grounded(c, f.Codec, topic: "identity");
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("I'm Player One"));
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request("Hi again"))).status == "pending", "Direct introduction was forgotten");
            f.Provider.Decide = c => Grounded(c, f.Codec, "accept");
            var request = f.Request(); request.partyId = null;
            Assert((await f.Service.SubmitAsync(f.P2.playerId, request)).status == "pending", "No-party attendance was invented");
        });
        await Test("eleven distinct girl profiles register and persist without shared mutable defaults", () =>
        {
            using var f = new Fixture(); var profiles = SocialCharacterCatalog.Girls();
            Assert(profiles.Length == 11 && profiles.Select(p => p.npcId).Distinct().Count() == 11, "Character IDs collided or a profile is missing");
            foreach (var p in profiles) { SocialVoice.Validate(p.voice); Assert(p.voice.gender == "girl" && p.persona.Length <= 900, "Invalid authored girl profile"); SocialCharacterCatalog.Register(f.World, p.npcId); }
            profiles[1].voice.tone = "changed outside the catalog";
            var recovered = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception()).Read();
            Assert(recovered.npcs.Count == 11 && SocialWorld.Npc(recovered, "Maya").voice.tone == "bubbly" &&
                SocialCharacterCatalog.Get("Maya").voice.tone == "bubbly", "Profiles shared mutable defaults or did not persist");
            return Task.CompletedTask;
        });
        await Test("catalog registration preserves an existing character's relationships and authoring", async () =>
        {
            using var f = new Fixture(); await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            f.World.SetNpcVoice("Ashley", new NpcVoiceProfile { gender = "girl", tone = "reserved" });
            f.World.SetNpcStanding("Ashley", 22, StatusConcern.High);
            f.Store.Transact(s => s.npcs[0].persona = "Custom Ashley");
            SocialCharacterCatalog.Register(f.World, "Ashley"); var n = f.Store.Read().npcs[0];
            Assert(n.persona == "Custom Ashley" && n.voice.tone == "reserved" && n.standing.popularity == 22 &&
                n.trust.Single().value == -5 && n.fraternityRegard == -5, "Catalog reset an existing campaign profile");
        });
        await Test("game-setting ablation changes framing without changing evidence or world facts", async () =>
        {
            using var f = new Fixture(); await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            var s = f.Store.Read(); long sequence = s.sequence; var turn = s.turns[0];
            var enabled = SocialMemory.Build(s, turn, f.Codec);
            var omitted = SocialMemory.Build(s, turn, f.Codec, includeGameSetting: false);
            Assert(enabled.SystemPrompt.Contains(SocialGameSetting.Prompt) && SocialReply.System(enabled, f.Codec).Contains(SocialGameSetting.Prompt) &&
                !omitted.SystemPrompt.Contains(SocialGameSetting.Prompt) && !SocialReply.System(omitted, f.Codec).Contains(SocialGameSetting.Prompt), "Framing ablation missed an inference pass");
            Assert(new GroundedSocialTurn(enabled).System.Contains("FratRow") && !new GroundedSocialTurn(omitted).System.Contains("FratRow"), "Grounded setting switch was ignored");
            Assert(enabled.EvidenceIds.SetEquals(omitted.EvidenceIds) && enabled.FactPrompt == omitted.FactPrompt &&
                enabled.NpcRevision == omitted.NpcRevision && f.Store.Read().sequence == sequence, "Game framing changed facts, evidence or saved state");
        });
        await Test("recognition distinguishes direct introductions, overheard speech and a different fraternity member", async () =>
        {
            using var f = new Fixture(); f.World.EnsureNpc("Brad", "Student");
            f.Store.Transact(s => SocialWorld.AddEvent(s, "utterance", "speech", f.P1.playerId, "Brad", "inperson", "Hi Brad", new List<string> { "Brad", "Ashley" }));
            f.Provider.Decide = c => {
                bool priorDirect = f.Provider.Calls == 2;
                Assert(c.FactPrompt.Contains(priorDirect ? "FAMILIARITY = prior direct conversation recorded" : "FAMILIARITY = no prior direct conversation recorded"),
                    "Overhearing or shared fraternity membership became a personal introduction");
                return FakeProvider.Proposal(c);
            };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("Hi, I'm Player One"));
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("Hello again"));
            await f.Service.SubmitAsync(f.P2.playerId, f.Request("I'm another member"));
        });
        await Test("public popularity stays separate from personal regard and survives reload", async () =>
        {
            using var f = new Fixture();
            await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            f.World.SetFraternityStanding(5, "Our House", "C", 50);
            f.World.SetNpcStanding("Ashley", 80, StatusConcern.High);
            long sequence = f.Store.Read().sequence;
            f.World.SetFraternityStanding(5, "Our House", "C", 50);
            f.World.SetNpcStanding("Ashley", 80, StatusConcern.High);
            Assert(f.Store.Read().sequence == sequence, "Unchanged game metrics generated journal writes");
            f.Provider.Decide = c => {
                Assert(c.Prompt.Contains("popularity=5") && c.CompactPrompt.Contains("higher than") && c.FactPrompt.Contains("popularity=80"), "Popularity did not reach every context projection");
                Assert(c.FactPrompt.Contains("no prior direct conversation") && c.FactPrompt.Contains("NPC_REGARD_FOR_FRATERNITY = -5"), "Fame became familiarity or erased house opinion");
                return FakeProvider.Proposal(c, "accept", "unchanged");
            };
            var result = await f.Service.SubmitAsync(f.P2.playerId, f.Request("Would you come?"));
            var s = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception()).Read();
            Assert(result.decision == "accept" && s.standing.popularity == 5 && s.npcs[0].standing.popularity == 80 &&
                s.npcs[0].fraternityRegard == -5 && s.npcs[0].trust.Find(t => t.playerId == f.P1.playerId).value == -5,
                "Popularity forced an outcome or changed personal relationships");
        });
        await Test("a fraternity popularity change during generation repairs once", async () =>
        {
            using var f = new Fixture(); f.World.SetFraternityStanding(5);
            f.Provider.During = c => { if (f.Provider.Calls == 1) f.World.SetFraternityStanding(80); return Task.CompletedTask; };
            var result = await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            var s = f.Store.Read();
            Assert(result.status == "committed" && f.Provider.Calls == 2 && s.npcs[0].trust.Single().value == -5 &&
                s.events.Count(e => e.source == "speech") == 1 && s.events.Count(e => e.source == "inference") == 1, "Stale fraternity context committed or duplicated influence");
        });
        await Test("an NPC popularity change during generation invalidates its context", async () =>
        {
            using var f = new Fixture(); f.World.SetNpcStanding("Ashley", 10);
            f.Provider.During = c => { if (f.Provider.Calls == 1) f.World.SetNpcStanding("Ashley", 80, StatusConcern.High); return Task.CompletedTask; };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request())).status == "committed" && f.Provider.Calls == 2 &&
                f.Store.Read().npcs[0].trust.Single().value == -5, "Stale NPC standing committed");
        });
        await Test("party social draw lists confirmed guests without turning conditional RSVPs into arrivals", async () =>
        {
            using var f = new Fixture(); f.World.EnsureNpc("Famous", "Popular, values kindness"); f.World.EnsureNpc("Tentative", "Selective");
            f.World.SetNpcStanding("Famous", 80); f.World.SetNpcStanding("Tentative", 99);
            f.Provider.Decide = c => FakeProvider.Proposal(c, c.NpcId == "Tentative" ? "counteroffer" : "accept", "unchanged");
            await f.Service.SubmitAsync(f.P1.playerId, f.Request(npc: "Famous"));
            await f.Service.SubmitAsync(f.P2.playerId, f.Request(npc: "Tentative"));
            f.Provider.Decide = c => {
                Assert(c.FactPrompt.Contains("GUEST \"Famous\"; popularity=80; RSVP accepted; not arrived") &&
                    !c.FactPrompt.Contains("GUEST \"Tentative\""), "Party appeal invented a confirmed/arrived guest");
                return FakeProvider.Proposal(c);
            };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request());
        });
        await Test("a large public guest list keeps prompts bounded", async () =>
        {
            using var f = new Fixture();
            f.Store.Transact(s => { for (int i = 0; i < 20; i++) s.npcs.Add(new NpcState { npcId = "VIP" + i, persona = "Guest", standing = new NpcStanding { known = true, popularity = i },
                commitments = new List<Commitment> { new Commitment { partyId = f.PartyId, state = "accepted", inviterId = f.P1.playerId } } }); });
            f.Provider.Decide = c => {
                Assert(c.FactPrompt.Split('\n').Count(line => line.StartsWith("GUEST ")) == 6 && c.FactPrompt.Contains("GUEST \"VIP19\"") && !c.FactPrompt.Contains("GUEST \"VIP0\""), "Public guest retrieval grew without bound");
                Assert(c.Prompt.Length + c.SystemPrompt.Length <= SocialMemory.MaxPromptCharacters, "Guest list exceeded context budget");
                return FakeProvider.Proposal(c);
            };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request());
        });
        await Test("invalid popularity and missing legacy standing never fabricate status", async () =>
        {
            using var f = new Fixture(); long sequence = f.Store.Read().sequence;
            await Throws<ArgumentException>(() => { f.World.SetFraternityStanding(float.NaN); return Task.CompletedTask; });
            await Throws<ArgumentException>(() => { f.World.SetNpcStanding("Ashley", float.PositiveInfinity); return Task.CompletedTask; });
            Assert(f.Store.Read().sequence == sequence, "Invalid status mutated canonical state");
            f.Provider.Decide = c => { Assert(c.FactPrompt.Contains("FRATERNITY popularity unknown") && c.FactPrompt.Contains("NPC popularity unknown"), "Missing legacy fields became zero popularity"); return FakeProvider.Proposal(c); };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request());
        });
        await Test("NPC voices stay distinct in both prompt paths and survive campaign reload", async () =>
        {
            using var f = new Fixture();
            var brad = new NpcVoiceProfile { gender = "boy", tone = "reserved", slang = "none" };
            f.World.EnsureNpc("Brad", "Cautious and reliable", brad);
            brad.gender = "changed outside the store";
            f.Provider.Decide = c => {
                var identity = c.NpcId == "Ashley" ? "Gen Z college girl" : "Gen Z college boy";
                Assert(c.SystemPrompt.Contains(identity) && SocialReply.System(c, f.Codec).Contains(identity), "One inference path lost the NPC's own voice");
                Assert(c.Voice.tone == (c.NpcId == "Ashley" ? "assertive" : "reserved"), "Another NPC's tone leaked");
                return FakeProvider.Proposal(c);
            };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            await f.Service.SubmitAsync(f.P2.playerId, f.Request(npc: "Brad"));
            f.World.EnsureNpc("Brad", "Do not replace existing persona", SocialVoice.Ashley());
            var s = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception()).Read();
            Assert(SocialWorld.Npc(s, "Brad").voice.gender == "boy" && SocialWorld.Npc(s, "Brad").persona == "Cautious and reliable" &&
                SocialWorld.Npc(s, "Ashley").voice.exampleLines.Contains("I'm not here to be your punching bag."), "Voice did not persist or was overwritten");
        });
        await Test("invalid voice authoring cannot mutate the campaign", async () =>
        {
            using var f = new Fixture(); long sequence = f.Store.Read().sequence;
            await Throws<ArgumentException>(() => { f.World.SetNpcVoice("Ashley", new NpcVoiceProfile { tone = "unknown" }); return Task.CompletedTask; });
            await Throws<ArgumentException>(() => { f.World.SetNpcVoice("Ashley", new NpcVoiceProfile { exampleLines = new[] { new string('x', 161) } }); return Task.CompletedTask; });
            Assert(f.Store.Read().sequence == sequence && f.Store.Read().npcs[0].voice.tone == "assertive", "Rejected authoring changed the voice");
        });
        await Test("a voice change during generation invalidates the draft without duplicating consequences", async () =>
        {
            using var f = new Fixture();
            f.Provider.During = c => { if (f.Provider.Calls == 1) f.World.SetNpcVoice("Ashley", new NpcVoiceProfile { gender = "girl", tone = "reserved" }); return Task.CompletedTask; };
            var result = await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            var s = f.Store.Read();
            Assert(result.status == "committed" && f.Provider.Calls == 2 && s.events.Count(e => e.source == "speech") == 1 &&
                s.events.Count(e => e.source == "inference") == 1 && s.npcs[0].trust.Single().value == -5 && s.npcs[0].voice.tone == "reserved",
                "Stale voice draft committed or effects repeated");
        });
        await Test("grounding audit rejects missing fields and cannot publish effects on repeated failure", async () =>
        {
            using var f = new Fixture();
            foreach (var json in new[] { "{}", "{\"valid\":true}", "{\"valid\":false,\"error\":\"wrong actor\"}", "{\"valid\":true,\"error\":\"wrong actor\"}" })
                await Throws<ArgumentException>(() => { SocialReply.ValidateCheck(json, f.Codec); return Task.CompletedTask; });
            SocialReply.ValidateCheck("{\"valid\":true,\"error\":\"\"}", f.Codec);
            f.Provider.Decide = c => { SocialReply.ValidateCheck("{\"valid\":false,\"error\":\"invented detail\"}", f.Codec); return FakeProvider.Proposal(c); };
            var result = await f.Service.SubmitAsync(f.P1.playerId, f.Request("Received speech must survive a rejected draft"));
            var s = f.Store.Read();
            Assert(result.status == "pending" && f.Provider.Calls == 2 && s.events.Count(e => e.source == "speech") == 1 &&
                !s.events.Any(e => e.source == "inference") && s.npcs[0].trust.Count == 0 && s.npcs[0].commitments.Count == 0,
                "A rejected draft published a reply/effect or erased received speech");
        });
        await Test("grievances persist the receipt actor rather than the next speaker", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => { var p = FakeProvider.Proposal(c); p.grievance_event_id = c.ObservedEventId; return p; };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("An insult"));
            f.Provider.Decide = c => FakeProvider.Proposal(c, "accept", "unchanged");
            await f.Service.SubmitAsync(f.P2.playerId, f.Request("I disagree with what he said. Come to our party?"));
            f.World.Register("A changed name", f.P1);
            var recovered = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception()).Read();
            var g = recovered.npcs[0].grievances.Single();
            Assert(g.actorId == f.P1.playerId && g.actorId != f.P2.playerId && g.fraternityId == recovered.fraternityId && g.knowledge == "witnessed", "Grievance actor/provenance changed across speakers or reload");
        });
        await Test("an NPC's own interpretation cannot become someone else's grievance", async () =>
        {
            using var f = new Fixture(); await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            string self = f.Store.Read().events.Single(e => e.source == "inference").eventId;
            f.Provider.Decide = c => { Assert(!c.GrievanceCandidateIds.Contains(self), "Self-inference became an external cause"); var p = FakeProvider.Proposal(c); p.grievance_event_id = self; return p; };
            Assert((await f.Service.SubmitAsync(f.P2.playerId, f.Request())).status == "pending" && f.Store.Read().npcs[0].grievances.Count == 0, "Self-inference grievance committed");
        });
        await Test("gossip grievance keeps alleged actor and reporter distinct", async () =>
        {
            using var f = new Fixture(); f.World.EnsureNpc("Brad", "Reports what he knows.");
            string incident = f.World.Observe("insult", f.P1.playerId, "Ashley", "Player One said rude words", "Quad", "Brad");
            string report = f.World.TellRumor("Brad", "Ashley", incident);
            f.Provider.Decide = c => {
                Assert(!c.EvidenceIds.Contains(incident) && c.KnownPlayerIds.Contains(f.P1.playerId), "Rumor lost actor or exposed firsthand evidence");
                var p = FakeProvider.Proposal(c); p.grievance_event_id = report; return p;
            };
            await f.Service.SubmitAsync(f.P2.playerId, f.Request());
            var g = f.Store.Read().npcs[0].grievances.Single();
            Assert(g.actorId == f.P1.playerId && g.reporterId == "Brad" && g.knowledge == "reported", "Rumor became firsthand knowledge or blamed reporter");
        });
        await Test("voice facts exclude unsupported previous NPC interpretations", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => { var p = FakeProvider.Proposal(c); p.interpretation = "INVENTED_SINK_TASK"; return p; };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            f.Provider.Decide = c => { Assert(c.CompactPrompt.Contains("INVENTED_SINK_TASK") && !c.FactPrompt.Contains("INVENTED_SINK_TASK"), "Prior NPC belief promoted to voice fact"); return FakeProvider.Proposal(c); };
            await f.Service.SubmitAsync(f.P2.playerId, f.Request());
        });
        await Test("forwarded gossip never blames an intermediate reporter or grants firsthand knowledge", async () =>
        {
            using var f = new Fixture(); f.World.EnsureNpc("Brad", "Witness"); f.World.EnsureNpc("Casey", "Reporter");
            string original = f.World.Observe("insult", f.P1.playerId, "Ashley", "Original insult", "Quad", "Brad");
            string first = f.World.TellRumor("Brad", "Casey", original);
            string forwarded = f.World.TellRumor("Casey", "Ashley", first);
            f.Provider.Decide = c => {
                Assert(!c.EvidenceIds.Contains(original) && !c.EvidenceIds.Contains(first) && c.KnownPlayerIds.Contains(f.P1.playerId), "Forwarding exposed unheard receipts or lost alleged actor");
                var p = FakeProvider.Proposal(c); p.grievance_event_id = forwarded; return p;
            };
            await f.Service.SubmitAsync(f.P2.playerId, f.Request());
            var g = f.Store.Read().npcs[0].grievances.Single();
            Assert(g.actorId == f.P1.playerId && g.reporterId == "Casey" && g.knowledge == "reported", "Forwarded rumor blamed a reporter or became firsthand knowledge");
        });
        await Test("an internal empty-reply plan can never commit before voicing", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => { var p = FakeProvider.Proposal(c); p.reply = ""; SocialConversationService.ValidateProposal(p, c, requireReply: false); return p; };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request())).status == "pending" && f.Store.Read().npcs[0].trust.Count == 0, "Unvoiced draft committed");
        });
        await Test("descriptive compact contract restores canonical references", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => {
                var wire = new SocialInferenceFormat(c);
                Assert(wire.Grammar.Contains("\\\"decision\\\"") && wire.Grammar.Contains("ws ::="), "Descriptive contract missing");
                Assert(wire.Context.SystemPrompt.Contains("first person as SELF"), "Speaker role missing");
                return wire.Decode(f.Codec.Encode(FakeProvider.Proposal(wire.Context)), f.Codec);
            };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request())).status == "committed", "Descriptive compact decode failed");
        });
        await Test("compact inference aliases round trip to canonical actor and evidence", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => {
                var wire = new SocialInferenceFormat(c);
                Assert(wire.Context.PlayerId == "player_2" && wire.Context.FraternityId == "h1", "Identity aliases missing");
                Assert(!wire.Context.Prompt.Contains(c.PlayerId) && c.Prompt.Contains(c.PlayerId), "Canonical context mutated or not shortened");
                Assert(wire.Grammar.Contains("\\\"schema\\\""), "Descriptive grammar missing");
                var proposal = new SocialProposal {
                    schema = 1, decision = "accept", evidence_ids = new[] { wire.Context.ObservedEventId },
                    relationship_effects = new[] { new RelationshipEffect { scope = "player", id = "player_2", magnitude = "up_small" } },
                    condition = "none", grievance_event_id = "", resolve_grievance_event_id = "", interpretation = "player_2 offers a friendly invitation.", reply = "Sure player_2, see you there."
                };
                var decoded = wire.Decode(f.Codec.Encode(proposal), f.Codec);
                SocialConversationService.ValidateProposal(decoded, c);
                Assert(decoded.evidence_ids[0] == c.ObservedEventId && decoded.relationship_effects[0].id == c.PlayerId, "Alias changed actor/evidence");
                Assert(decoded.interpretation.Contains(c.PlayerId) && decoded.reply == "Sure Player Two, see you there.", "Transient aliases leaked into persistent prose");
                return decoded;
            };
            var result = await f.Service.SubmitAsync(f.P2.playerId, f.Request("Come to our party?"));
            Assert(result.status == "committed" && f.Store.Read().npcs[0].trust[0].playerId == f.P2.playerId, "Canonical transaction failed");
        });
        await Test("compact inference rejects invented aliases and another player's effects", async () =>
        {
            using var f = new Fixture(); await f.Service.SubmitAsync(f.P1.playerId, f.Request("A witnessed greeting"));
            f.Provider.Decide = c => {
                var wire = new SocialInferenceFormat(c);
                var p = new SocialProposal { schema = 1, decision = "respond", evidence_ids = new[] { "e999" },
                    relationship_effects = Array.Empty<RelationshipEffect>(), condition = "none", grievance_event_id = "", resolve_grievance_event_id = "", interpretation = "Test", reply = "Hi." };
                try { wire.Decode(f.Codec.Encode(p), f.Codec); throw new Exception("Invented alias accepted"); }
                catch (ArgumentException) { }
                p.evidence_ids = new[] { wire.Context.ObservedEventId };
                p.relationship_effects = new[] { new RelationshipEffect { scope = "player", id = "player_1", magnitude = "down" } };
                return wire.Decode(f.Codec.Encode(p), f.Codec);
            };
            var result = await f.Service.SubmitAsync(f.P2.playerId, f.Request("Hello"));
            Assert(result.status == "pending" && f.Store.Read().npcs[0].trust.Count == 1, "Wrong player's effect committed");
        });
        await Test("compact prose cannot reassign prior blame when speaker aliases change", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => {
                var wire = new SocialInferenceFormat(c);
                return wire.Decode(f.Codec.Encode(new SocialProposal {
                    schema = 1, decision = "respond", evidence_ids = new[] { wire.Context.ObservedEventId }, relationship_effects = Array.Empty<RelationshipEffect>(),
                    condition = "none", grievance_event_id = "", resolve_grievance_event_id = "", interpretation = "player_1 was cruel.", reply = "That was hurtful."
                }), f.Codec);
            };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("An insult"));
            f.Provider.Decide = c => {
                var wire = new SocialInferenceFormat(c);
                Assert(wire.Context.PlayerId == "player_2" && wire.Context.Prompt.Contains("player_1 was cruel.") && !wire.Context.Prompt.Contains("player_2 was cruel."), "Prior blame rebound to the current player");
                return FakeProvider.Proposal(c, "respond", "unchanged");
            };
            Assert((await f.Service.SubmitAsync(f.P2.playerId, f.Request("I am his frat mate."))).status == "committed", "Second actor failed");
        });
        await Test("compact rendering retains witnessed history and unresolved grievances", async () =>
        {
            using var f = new Fixture(); string old = f.World.Observe("insult", f.P1.playerId, "Ashley", "WITNESSED_INSULT", "Quad", "Ashley");
            f.Store.Transact(s => s.npcs[0].grievances.Add(new Grievance { eventId = old, interpretation = "Unresolved disrespect" }));
            f.Provider.Decide = c => {
                var wire = new SocialInferenceFormat(c);
                Assert(wire.Context.Prompt.Contains("WITNESSED_INSULT") && wire.Context.Prompt.Contains("Player One") && wire.Context.Prompt.Contains("player_1"), "Actor or memory omitted");
                var p = new SocialProposal { schema = 1, decision = "respond", evidence_ids = new[] { wire.Context.ObservedEventId },
                    relationship_effects = Array.Empty<RelationshipEffect>(), condition = "none", grievance_event_id = "", resolve_grievance_event_id = wire.Context.GrievanceIds.Single(), interpretation = "Repair", reply = "I appreciate the apology." };
                var decoded = wire.Decode(f.Codec.Encode(p), f.Codec);
                Assert(decoded.resolve_grievance_event_id == old, "Grievance alias lost");
                return decoded;
            };
            Assert((await f.Service.SubmitAsync(f.P2.playerId, f.Request("I am sorry about what he said."))).status == "committed", "Grievance decode failed");
        });
        await Test("salient evidence is budgeted first but presented in time order", async () =>
        {
            using var f = new Fixture();
            string old = f.World.Observe("insult", f.P1.playerId, "Ashley", "OLD_RECEIPT " + new string('x', 790), "Quad", "Ashley");
            f.Store.Transact(s => s.npcs[0].grievances.Add(new Grievance { eventId = old, interpretation = "Unresolved incident" }));
            for (int i = 0; i < 12; i++) f.World.Observe("greeting", f.P2.playerId, "Ashley", "NEW_RECEIPT " + new string('y', 790), "Quad", "Ashley");
            f.Provider.Decide = c => {
                Assert(c.EvidenceIds.Contains(old), "Important receipt lost to context budget");
                Assert(c.CompactPrompt.IndexOf("OLD_RECEIPT", StringComparison.Ordinal) < c.CompactPrompt.IndexOf("NEW_RECEIPT", StringComparison.Ordinal), "Salience changed chronology");
                return FakeProvider.Proposal(c, "respond", "unchanged");
            };
            Assert((await f.Service.SubmitAsync(f.P2.playerId, f.Request())).status == "committed", "Priority retrieval failed");
        });
        await Test("two players share house regard but keep personal blame", async () =>
        {
            using var f = new Fixture();
            await f.Service.SubmitAsync(f.P1.playerId, f.Request("Insult"));
            f.Provider.Decide = c =>
            {
                Assert(c.Prompt.Contains(f.P1.playerId) && c.Prompt.Contains("Insult"), "Prior actor must be retrieved");
                Assert(c.PlayerId == f.P2.playerId, "Current actor changed");
                return FakeProvider.Proposal(c, "decline", "unchanged");
            };
            var result = await f.Service.SubmitAsync(f.P2.playerId, f.Request("Will you come?"));
            var npc = f.Store.Read().npcs[0];
            Assert(result.commitment == "declined" && npc.fraternityRegard == -5, "House consequence lost");
            Assert(npc.trust.Single(t => t.playerId == f.P1.playerId).value == -5 && npc.trust.Single(t => t.playerId == f.P2.playerId).value == 0, "Blame leaked between players");
        });
        await Test("duplicate delivery is exactly once and preserves accepted text", async () =>
        {
            using var f = new Fixture(); var request = f.Request();
            var a = await f.Service.SubmitAsync(f.P1.playerId, request);
            var b = await f.Service.SubmitAsync(f.P1.playerId, request);
            Assert(f.Provider.Calls == 1 && f.Codec.Encode(a) == f.Codec.Encode(b), "Duplicate regenerated");
            Assert(f.Store.Read().events.Count(e => e.kind == "utterance") == 1, "Duplicate receipt");
            request.text = "Changed";
            await Throws<InvalidOperationException>(() => f.Service.SubmitAsync(f.P1.playerId, request));
        });
        await Test("duplicate delivery while generating shares one decision", async () =>
        {
            using var f = new Fixture(); var release = new TaskCompletionSource<bool>(); var entered = new TaskCompletionSource<bool>();
            f.Provider.During = _ => { entered.TrySetResult(true); return release.Task; };
            var request = f.Request(); var first = f.Service.SubmitAsync(f.P1.playerId, request); await entered.Task;
            var duplicate = f.Service.SubmitAsync(f.P1.playerId, request);
            release.SetResult(true); var results = await Task.WhenAll(first, duplicate);
            Assert(f.Provider.Calls == 1 && f.Codec.Encode(results[0]) == f.Codec.Encode(results[1]), "In-flight request generated twice");
        });
        await Test("bad identities and reconnect credentials cannot impersonate", async () =>
        {
            using var f = new Fixture();
            Assert(f.World.Register("Renamed", f.P1).playerId == f.P1.playerId, "Name reset identity");
            await Throws<UnauthorizedAccessException>(() => f.Service.SubmitAsync("forged", f.Request()));
            await Throws<UnauthorizedAccessException>(() => Task.Run(() => f.World.Register("X", new PlayerCredential { campaignId = "other", playerId = f.P1.playerId, token = f.P1.token })));
            await Throws<UnauthorizedAccessException>(() => Task.Run(() => f.World.Register("X", new PlayerCredential { campaignId = "campaign", playerId = f.P2.playerId, token = f.P1.token })));
        });
        await Test("unheard speech has no receipt or influence", async () =>
        {
            using var f = new Fixture(); f.Access.Allowed = false;
            var r = await f.Service.SubmitAsync(f.P1.playerId, f.Request("Unheard insult"));
            Assert(r.status == "rejected" && f.Provider.Calls == 0 && !f.Store.Read().events.Any(), "Unheard insult leaked");
        });
        await Test("gossip is explicit and retains report provenance", () =>
        {
            using var f = new Fixture(); f.World.EnsureNpc("Brad", "Calm");
            string id = f.World.Observe("insult", f.P1.playerId, "Ashley", "Observed insult", "Quad", "Ashley");
            var s = f.Store.Read(); Assert(!s.events.Any(e => e.witnesses.Contains("Brad")), "Omniscient NPC");
            string rumor = f.World.TellRumor("Ashley", "Brad", id);
            var e = f.Store.Read().events.Single(e => e.eventId == rumor);
            Assert(e.source == "rumor" && e.relatedEventId == id && e.witnesses.SequenceEqual(new[] { "Brad" }), "Rumor became fact");
            return Task.CompletedTask;
        });
        await Test("invented evidence rejected with bounded repair", async () =>
        {
            using var f = new Fixture(); f.Provider.Decide = c => { var p = FakeProvider.Proposal(c, "accept"); p.evidence_ids = new[] { "invented" }; return p; };
            var r = await f.Service.SubmitAsync(f.P1.playerId, f.Request()); var s = f.Store.Read();
            Assert(r.status == "pending" && f.Provider.Calls == 2 && s.npcs[0].trust.Count == 0 && s.npcs[0].commitments.Count == 0, "Invalid proposal mutated world");
        });
        await Test("repair succeeds without duplicate observation", async () =>
        {
            using var f = new Fixture(); f.Provider.Decide = c => { var p = FakeProvider.Proposal(c); if (c.RepairError == null) p.schema = 9; return p; };
            var r = await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            Assert(r.status == "committed" && f.Provider.Calls == 2 && f.Store.Read().events.Count(e => e.kind == "utterance") == 1, "Repair receipt duplicated");
        });
        await Test("retry after unavailable model reuses received speech", async () =>
        {
            using var f = new Fixture(); var request = f.Request(); f.Provider.Decide = _ => throw new Exception("Unavailable");
            Assert((await f.Service.SubmitAsync(f.P1.playerId, request)).status == "pending", "Failure hidden");
            f.Provider.Decide = c => FakeProvider.Proposal(c);
            Assert((await f.Service.SubmitAsync(f.P1.playerId, request)).status == "committed" && f.Store.Read().events.Count(e => e.kind == "utterance") == 1, "Retry duplicated speech");
        });
        await Test("missing model file returns a recoverable interruption", async () =>
        {
            using var f = new Fixture(); f.Provider.Decide = _ => throw new FileNotFoundException("Model not installed");
            var r = await f.Service.SubmitAsync(f.P1.playerId, f.Request());
            Assert(r.status == "pending" && f.Store.Read().turns.Count == 1 && f.Store.Read().npcs[0].trust.Count == 0, "Model I/O failure corrupted turn lifecycle");
        });
        await Test("party cancellation during generation rejects stale acceptance", async () =>
        {
            using var f = new Fixture(); f.Provider.During = c => { if (f.Provider.Calls == 1) f.World.EndParty(f.PartyId, true); return Task.CompletedTask; };
            f.Provider.Decide = c => FakeProvider.Proposal(c, "accept");
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request())).status == "pending" && f.Store.Read().npcs[0].commitments.Count == 0, "Cancelled party accepted");
        });
        await Test("same NPC ordering and shared runtime serialize concurrent speakers", async () =>
        {
            using var f = new Fixture(); f.Provider.During = async _ => await Task.Delay(25);
            var a = f.Service.SubmitAsync(f.P1.playerId, f.Request("First"));
            var b = f.Service.SubmitAsync(f.P2.playerId, f.Request("Second"));
            await Task.WhenAll(a, b);
            Assert(f.Provider.MaxActive == 1 && f.Store.Read().events.Where(e => e.kind == "utterance").Select(e => e.text).SequenceEqual(new[] { "First", "Second" }), "Mailbox ordering broken");
        });
        await Test("cancelling queued input never records it", async () =>
        {
            using var f = new Fixture(); var entered = new TaskCompletionSource<bool>(); var release = new TaskCompletionSource<bool>();
            f.Provider.During = _ => { entered.TrySetResult(true); return release.Task; };
            var first = f.Service.SubmitAsync(f.P1.playerId, f.Request()); await entered.Task;
            using var cancel = new CancellationTokenSource(); var second = f.Service.SubmitAsync(f.P2.playerId, f.Request("cancelled"), cancel.Token);
            cancel.Cancel(); Assert((await second).status == "cancelled", "Queued cancel ignored"); release.SetResult(true); await first;
            Assert(!f.Store.Read().events.Any(e => e.text == "cancelled"), "Queued input was heard");
        });
        await Test("cancelling after receipt keeps committed consequence", async () =>
        {
            using var f = new Fixture(); using var cancel = new CancellationTokenSource();
            f.Provider.During = _ => { cancel.Cancel(); return Task.CompletedTask; };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request(), cancel.Token)).status == "committed", "Received speech rolled back");
        });
        await Test("party accept is authoritative and arrival idempotent", async () =>
        {
            using var f = new Fixture(); f.Provider.Decide = c => FakeProvider.Proposal(c, "accept");
            await f.Service.SubmitAsync(f.P1.playerId, f.Request()); f.World.StartParty(f.PartyId, 100);
            f.World.MarkArrived("Ashley", f.PartyId); f.World.MarkArrived("Ashley", f.PartyId);
            Assert(f.Store.Read().events.Count(e => e.kind == "arrived") == 1, "Duplicate guest attendance");
        });
        await Test("entrance condition resolves once for correct inviter", async () =>
        {
            using var f = new Fixture(); f.Provider.Decide = c => FakeProvider.Proposal(c, "counteroffer");
            await f.Service.SubmitAsync(f.P2.playerId, f.Request()); f.World.StartParty(f.PartyId, 100);
            f.World.ObserveEntrance(f.PartyId, new HashSet<string> { f.P1.playerId }, 110);
            Assert(f.Store.Read().npcs[0].commitments[0].state == "conditional", "Wrong player fulfilled promise");
            f.World.ObserveEntrance(f.PartyId, new HashSet<string> { f.P2.playerId }, 159);
            f.World.ObserveEntrance(f.PartyId, new HashSet<string> { f.P2.playerId }, 160);
            Assert(f.Store.Read().events.Count(e => e.kind == "promise_fulfilled") == 1, "Promise credited twice");
        });
        await Test("missed entrance remains missed after deadline", async () =>
        {
            using var f = new Fixture(); f.Provider.Decide = c => FakeProvider.Proposal(c, "counteroffer");
            await f.Service.SubmitAsync(f.P1.playerId, f.Request()); f.World.StartParty(f.PartyId, 100);
            f.World.ObserveEntrance(f.PartyId, new HashSet<string> { f.P1.playerId }, 160);
            Assert(f.Store.Read().npcs[0].commitments[0].state == "missed", "Late promise fulfilled");
        });
        await Test("compliment farming is bounded across reload", async () =>
        {
            using var f = new Fixture(); f.Provider.Decide = c => FakeProvider.Proposal(c, "respond", "up");
            for (int i = 0; i < 20; i++) await f.Service.SubmitAsync(f.P1.playerId, f.Request("Compliment"));
            var reload = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception("Must reload"));
            Assert(reload.Read().npcs[0].fraternityRegard == 10 && reload.Read().influence.Any(c => c.used == 10), "Influence farmed or reset");
        });
        await Test("100 NPCs do not expand current prompt or share private memories", async () =>
        {
            using var f = new Fixture();
            for (int i = 0; i < 99; i++) f.World.EnsureNpc("Npc" + i, "Private profile " + i);
            f.World.Observe("secret", f.P1.playerId, "Npc98", "SECRET_98", "Dorm", "Npc98");
            f.Provider.Decide = c => { Assert(c.Prompt.Length < SocialMemory.MaxPromptCharacters && !c.Prompt.Contains("SECRET_98") && !c.Prompt.Contains("Private profile"), "Cross-NPC leakage"); return FakeProvider.Proposal(c); };
            await f.Service.SubmitAsync(f.P1.playerId, f.Request()); Assert(f.Provider.Calls == 1, "Extra model calls for idle NPCs");
        });
        await Test("torn journal tail is ignored; accepted state and text survive reload", async () =>
        {
            using var f = new Fixture(); var request = f.Request(); var result = await f.Service.SubmitAsync(f.P1.playerId, request);
            File.AppendAllText(Path.Combine(f.DirectoryPath, "social.journal"), "torn");
            var reload = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception());
            Assert(reload.Read().turns[0].result.reply == result.reply, "Accepted reply lost");
            reload.Transact(s => s.day++);
            Assert(new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception()).Read().day == 1, "Tail not truncated safely");
        });
        await Test("failure after journal flush recovers the committed transaction", () =>
        {
            using var f = new Fixture(); bool inject = true;
            var store = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception(), stage => { if (inject && stage == "after_append") throw new IOException("simulated crash"); });
            try { store.Transact(s => s.day = 42); } catch (IOException) { }
            inject = false;
            Assert(new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception()).Read().day == 42, "Durable transaction lost");
            return Task.CompletedTask;
        });
        await Test("invalid transaction is atomic", () =>
        {
            using var f = new Fixture(); var before = f.Codec.Encode(f.Store.Read());
            try { f.Store.Transact(s => { s.day = 88; throw new ArgumentException(); }); } catch (ArgumentException) { }
            Assert(f.Codec.Encode(f.Store.Read()) == before, "Partial transaction leaked"); return Task.CompletedTask;
        });
        await Test("forged relationship targets and invented conditions cannot mutate", async () =>
        {
            using var f = new Fixture();
            f.Provider.Decide = c => { var p = FakeProvider.Proposal(c, "accept"); p.relationship_effects[0].id = f.P2.playerId; return p; };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request())).status == "pending", "Forged target accepted");
            f.Provider.Decide = c => { var p = FakeProvider.Proposal(c, "counteroffer"); p.condition = "player_is_banned"; return p; };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request())).status == "pending", "Invented condition accepted");
            Assert(f.Store.Read().npcs[0].trust.Count == 0, "Invalid target applied");
        });
        await Test("typed grievance citation is grounded without redundant general citation", async () =>
        {
            using var f = new Fixture(); var original = f.World.Observe("insult", f.P1.playerId, "Ashley", "Old insult", "Quad", "Ashley");
            f.Provider.Decide = c => { var p = FakeProvider.Proposal(c); p.grievance_event_id = original; return p; };
            Assert((await f.Service.SubmitAsync(f.P2.playerId, f.Request())).status == "committed", "Known typed evidence rejected");
            f.Provider.Decide = c => { var p = FakeProvider.Proposal(c); p.grievance_event_id = "invented"; return p; };
            Assert((await f.Service.SubmitAsync(f.P2.playerId, f.Request())).status == "pending", "Invented grievance accepted");
        });
        await Test("received unfinished turn resumes after reload while player is disconnected", async () =>
        {
            using var f = new Fixture(); var request = f.Request(); f.Provider.Decide = _ => throw new Exception("offline");
            await f.Service.SubmitAsync(f.P1.playerId, request);
            var store = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception());
            using var recovery = new SocialConversationService(new SocialWorld(store), f.Codec, new FakeProvider(), new Access { Allowed = false });
            await recovery.ResumeReceivedAsync();
            Assert(store.Read().turns[0].result.status == "committed" && store.Read().events.Count(e => e.kind == "utterance") == 1, "Pending receipt was lost or duplicated");
        });
        await Test("unresolved old incident survives recent greetings in bounded retrieval", async () =>
        {
            using var f = new Fixture(); string old = f.World.Observe("insult", f.P1.playerId, "Ashley", "OLD_IMPORTANT_INSULT", "Quad", "Ashley");
            f.Store.Transact(s => s.npcs[0].grievances.Add(new Grievance { eventId = old, interpretation = "Unresolved insult" }));
            for (int i = 0; i < 20; i++) f.World.Observe("greeting", f.P2.playerId, "Ashley", "Hi " + i, "Quad", "Ashley");
            f.Provider.Decide = c => { Assert(c.Prompt.Contains("OLD_IMPORTANT_INSULT"), "Old grievance evicted by greetings"); return FakeProvider.Proposal(c); };
            await f.Service.SubmitAsync(f.P2.playerId, f.Request());
        });
        await Test("stale NPC interpretation regenerates before applying effects once", async () =>
        {
            using var f = new Fixture(); f.Provider.During = _ => { if (f.Provider.Calls == 1) f.World.Observe("new_fact", f.P2.playerId, "Ashley", "Something changed", "Quad", "Ashley"); return Task.CompletedTask; };
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request())).status == "committed", "Fresh retry failed");
            Assert(f.Provider.Calls == 2 && f.Store.Read().npcs[0].fraternityRegard == -5, "Stale proposal applied");
        });
        await Test("party capacity cannot be bypassed by model acceptance", async () =>
        {
            using var f = new Fixture(); f.Store.Transact(s => s.parties[0].capacity = 0); f.Provider.Decide = c => FakeProvider.Proposal(c, "accept");
            Assert((await f.Service.SubmitAsync(f.P1.playerId, f.Request())).status == "pending" && f.Store.Read().npcs[0].commitments.Count == 0, "Overcapacity acceptance applied");
        });
        await Test("second pending request from same player is bounded before receipt", async () =>
        {
            using var f = new Fixture(); var release = new TaskCompletionSource<bool>();
            f.Provider.During = _ => release.Task;
            var first = f.Service.SubmitAsync(f.P1.playerId, f.Request("First"));
            var next = await f.Service.SubmitAsync(f.P1.playerId, f.Request("Flood"));
            Assert(next.status == "busy", "Per-player queue not bounded"); release.SetResult(true); await first;
            Assert(!f.Store.Read().events.Any(e => e.text == "Flood"), "Rejected queued input was observed");
        });
        await Test("complete corrupt journal record fails closed", async () =>
        {
            using var f = new Fixture();
            File.AppendAllText(Path.Combine(f.DirectoryPath, "social.journal"), "corrupt\n");
            await Throws<InvalidDataException>(() => Task.Run(() => new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception())));
        });
        await Test("snapshot failure preserves journal commit", () =>
        {
            using var f = new Fixture();
            var store = new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception(), stage => { if (stage == "before_snapshot") throw new IOException("snapshot crash"); });
            for (int i = 0; i < 40; i++) store.Transact(s => s.day++);
            Assert(new JournalSocialStore(f.DirectoryPath, f.Codec, () => throw new Exception()).Read().day == 40, "Snapshot failure lost commits");
            return Task.CompletedTask;
        });
        await Test("new campaigns start without previous grudges", () =>
        {
            using var f = new Fixture();
            var store = new JournalSocialStore(Path.Combine(f.DirectoryPath, "new-campaign"), f.Codec, () => SocialWorld.NewCampaign("new-campaign"));
            Assert(store.Read().events.Count == 0 && store.Read().players.Count == 0 && store.Read().fraternityId != f.Store.Read().fraternityId, "Campaign isolation failed");
            return Task.CompletedTask;
        });
        Console.WriteLine($"{passed} passed, {failed} failed"); return failed == 0 ? 0 : 1;
    }
    internal static string Arg(string[] args, string name, string fallback = null)
    { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
    static async Task<int> NativeSmoke(string[] args)
    {
        using var f = new Fixture();
        using ISocialModelProvider model = Arg(args, "--llama-server") != null
            ? new LlamaServerModelProvider(f.Codec, Arg(args, "--llama-server"), Arg(args, "--model"), Arg(args, "--hash"), int.Parse(Arg(args, "--gpu", "99")))
            : new NativeSocialModelProvider(f.Codec, Arg(args, "--model"), Arg(args, "--hash"), int.Parse(Arg(args, "--gpu", "0")));
        if (model is NativeSocialModelProvider nativeFormat) nativeFormat.CompactFormat = args.Contains("--compact");
        if (model is LlamaServerModelProvider llamaFormat) llamaFormat.CompactFormat = args.Contains("--compact");
        var r = f.Request("Hi Ashley, I am Player Two. Would you like to come to our party?");
        f.Store.Transact(s => { var e = SocialWorld.AddEvent(s, "utterance", "speech", f.P2.playerId, "Ashley", "text", r.text, new List<string> { "Ashley" }); s.turns.Add(new TurnRecord { playerId = f.P2.playerId, request = r, observedEventId = e.eventId }); });
        var context = SocialMemory.Build(f.Store.Read(), f.Store.Read().turns[0], f.Codec);
        int runs = int.Parse(Arg(args, "--runs", "1"));
        if (runs < 1 || runs > 10) throw new ArgumentException("Smoke runs must be between 1 and 10");
        for (int run = 0; run < runs; run++)
        {
        var timer = Stopwatch.StartNew(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            var result = await model.GenerateAsync(context, timeout.Token); SocialConversationService.ValidateProposal(result, context);
            var native = model as NativeSocialModelProvider; var llama = model as LlamaServerModelProvider;
            Console.WriteLine(f.Codec.Encode(new { status = "valid", run, cold = run == 0, seconds = timer.Elapsed.TotalSeconds,
                checksumMilliseconds = native?.ChecksumMilliseconds ?? llama?.ChecksumMilliseconds,
                loadMilliseconds = native?.LoadMilliseconds ?? llama?.LoadMilliseconds,
                generationMilliseconds = native?.GenerationMilliseconds ?? llama?.GenerationMilliseconds,
                promptTokens = native?.PromptTokens ?? llama?.PromptTokens, outputTokens = native?.OutputTokens ?? llama?.OutputTokens,
                proposal = result, hostPeakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64, helperPeakWorkingSet = llama?.PeakWorkingSet, helperPrivateBytes = llama?.PrivateBytes }));
        }
        catch (Exception e) { Console.WriteLine(e); Console.WriteLine("RAW " + ((model as NativeSocialModelProvider)?.LastRawOutput ?? (model as LlamaServerModelProvider)?.LastRawOutput)); Console.WriteLine((model as LlamaServerModelProvider)?.Diagnostics); return 1; }
        }
        return 0;
    }
}

