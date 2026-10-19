using System;
using System.Linq;
using System.Threading.Tasks;
using FratRow.Game;
using FratRow.Social;
using static TestRunner;

/// <summary>Drunk mechanic (design/gdd/drunk-mechanic.md): speech rewriting, drinking, sobering, NPC reactions.</summary>
static class DrunkTests
{
    const string Typed = "Hey, are you going to the party this Saturday? It's going to be so sick, everyone is coming.";

    public static async Task Run()
    {
        await Test("DrunkSpeech_BelowThreshold_NoRewrite", () =>
        {
            var t = GameLoopConfig.CreateDefault().drunk;
            Assert(DrunkSpeech.Intensity(t.threshold - 0.01f, t) == 0, "No intensity below the threshold");
            Assert(DrunkSpeech.Intensity(t.threshold, t) >= t.speech.startIntensity, "Crossing the threshold is noticeable at once");
            Assert(DrunkSpeech.Intensity(t.max, t) == 1f && DrunkSpeech.Intensity(t.max + 5, t) == 1f, "Intensity caps at 1");
            Assert(DrunkSpeech.Rewrite(Typed, 0f, t.speech, GameRng.For(1, "x")) == Typed, "Zero intensity returns the text untouched");
        });

        await Test("DrunkSpeech_SameSeed_SameOutput", () =>
        {
            var s = GameLoopConfig.CreateDefault().drunk.speech;
            for (int seed = 0; seed < 50; seed++)
                Assert(DrunkSpeech.Rewrite(Typed, 0.8f, s, GameRng.For(seed, "drunk-speech", 3)) == DrunkSpeech.Rewrite(Typed, 0.8f, s, GameRng.For(seed, "drunk-speech", 3)),
                    "Rewrite must be deterministic for a seed (" + seed + ")");
        });

        await Test("DrunkSpeech_MoreDrunk_RewritesMore", () =>
        {
            var t = GameLoopConfig.CreateDefault().drunk;
            double Damage(float drunk)
            {
                double sum = 0;
                for (int seed = 0; seed < 300; seed++)
                {
                    string said = DrunkSpeech.Rewrite(Typed, DrunkSpeech.Intensity(drunk, t), t.speech, GameRng.For(seed, "drunk-speech"));
                    sum += Distance(Typed.ToLowerInvariant(), said.ToLowerInvariant()) / (double)Typed.Length;
                }
                return sum / 300;
            }
            double tipsy = Damage(t.threshold - 0.01f), drunk = Damage(0.6f), wasted = Damage(1.0f), blackout = Damage(t.max);
            Assert(tipsy == 0, "Tipsy speech is untouched, got " + tipsy.ToString("0.000"));
            Assert(drunk > 0 && drunk < wasted && wasted < blackout, "Damage must grow with drunkenness: " + drunk.ToString("0.000") + " < " + wasted.ToString("0.000") + " < " + blackout.ToString("0.000"));
            Assert(blackout > 0.25, "At the cap the line should be heavily mangled, got " + blackout.ToString("0.000"));
        });

        await Test("DrunkSpeech_LongOrUnicodeInput_StaysValidSocialSpeech", () =>
        {
            var s = GameLoopConfig.CreateDefault().drunk.speech;
            string longText = string.Join(" ", Enumerable.Repeat("seriously this is the best party", 40)).Substring(0, SocialRules.InputCharacters);
            string emoji = "yo 🎉🍺 party at ours 🔥 you coming?";
            for (int seed = 0; seed < 200; seed++)
            {
                string a = DrunkSpeech.Rewrite(longText, 1f, s, GameRng.For(seed, "long"));
                Assert(a.Length > 0 && a.Length <= SocialRules.InputCharacters, "Rewrite must fit the social input limit, got " + a.Length);
                string b = DrunkSpeech.Rewrite(emoji, 1f, s, GameRng.For(seed, "emoji"));
                for (int i = 0; i < b.Length; i++)
                {
                    if (char.IsHighSurrogate(b[i])) { Assert(i + 1 < b.Length && char.IsLowSurrogate(b[i + 1]), "Split surrogate pair: " + b); i++; }
                    else Assert(!char.IsLowSurrogate(b[i]), "Lone low surrogate: " + b);
                }
            }
        });

        await Test("DrunkSpeech_ButtonLines_KeepInviteAndRushIntent", () =>
        {
            var s = GameLoopConfig.CreateDefault().drunk.speech;
            string[] invites = { "Hey Ashley, we're throwing a party at Alpha Beta Rho this Saturday. Want to come?", "We'd love to have you at our party this weekend, Ashley. You in?" };
            string[] rush = { "Have you thought about rushing Alpha Beta Rho? We'd love to have you as a brother.", "Rush Alpha Beta Rho this week. We want you as a brother, Ashley." };
            for (int seed = 0; seed < 300; seed++)
            {
                foreach (var line in invites)
                    Assert(SpeechCues.IsInvite(DrunkSpeech.Rewrite(line, 1f, s, GameRng.For(seed, "i"), cosmeticOnly: true)), "A wasted invite must still read as an invite");
                foreach (var line in rush)
                    Assert(SpeechCues.IsRush(DrunkSpeech.Rewrite(line, 1f, s, GameRng.For(seed, "r"), cosmeticOnly: true)), "A wasted rush pitch must still read as a rush pitch");
            }
        });

        await Test("Drink_AtHouse_RaisesDrunkFromHouseStashAndAnnouncesIt", () =>
        {
            using var f = new GameFixture();
            var h = f.Session.PlayerHouse;
            f.Session.AddStash(h, "liquor", 1);
            f.Goto(0, "PartyHouse");
            var first = f.Do(0, ActionIds.Drink);
            Assert(first.ok, "Drink at the house: " + first.message);
            Assert(Math.Abs(f.PlayerState(0).drunk - f.Config.Item("liquor").strength) < 1e-4, "One liquor serving adds its strength");
            Assert(f.Session.StashCount(h, "liquor") == 0 && f.Session.State.party.openServings.Any(o => o.itemId == "liquor" && o.count == f.Config.Item("liquor").servings - 1),
                "Drinking opens a container from the stash, shared with the guests");
            Assert(f.Session.DrunkCondition(f.PlayerState(0)) == SpeakerConditions.Tipsy, "One shot is tipsy");
            for (int i = 0; i < 4; i++) { f.Advance(f.Config.drunk.drinkCooldownSeconds); Assert(f.Do(0, ActionIds.Drink).ok, "Drink " + (i + 2)); }
            Assert(f.Session.DrunkCondition(f.PlayerState(0)) == SpeakerConditions.Wasted, "Five shots is wasted, drunk = " + f.PlayerState(0).drunk);
            Assert(f.Session.State.news.Count(n => n.kind == "drunk" && n.playerId == f.P(0)) == 2, "The house hears once at drunk and once at wasted");
        });

        await Test("Drink_WrongPlaceTooFastAtCapOrDry_IsRefused", () =>
        {
            using var f = new GameFixture(tweak: c => c.drunk.drinkCooldownSeconds = 5);
            var h = f.Session.PlayerHouse;
            f.Goto(0, "Quad");
            Assert(f.Do(0, ActionIds.Drink).code == "wrong_location", "Drinks are at the house");
            f.Goto(0, "PartyHouse");
            Assert(f.Do(0, ActionIds.Drink).code == "missing_item", "No drinks in the stash");
            Assert(f.Do(0, ActionIds.Drink, item: "speakers").code == "invalid_target", "Speakers aren't a drink");
            f.Session.AddStash(h, "keg", 1);
            Assert(f.Do(0, ActionIds.Drink, item: "keg").ok, "Keg");
            Assert(f.Do(0, ActionIds.Drink, item: "keg").code == "cap_reached", "Cooldown between drinks");
            f.PlayerState(0).drunk = f.Config.drunk.max;
            f.Advance(f.Config.drunk.drinkCooldownSeconds);
            f.PlayerState(0).drunk = f.Config.drunk.max;
            Assert(f.Do(0, ActionIds.Drink, item: "keg").code == "cap_reached", "No drinking past the cap");
            f.Session.AddStash(h, "soda_snacks", 1);
            var soda = f.Do(0, ActionIds.Drink, item: "soda_snacks");
            Assert(soda.ok && f.PlayerState(0).drunk == f.Config.drunk.max, "Soda is allowed at the cap and adds nothing");
        });

        await Test("Drunk_OverTime_SobersUp", () =>
        {
            using var f = new GameFixture();
            f.PlayerState(0).drunk = 1.0f;
            double seconds = 0.5 / f.Config.drunk.soberPerSecond;
            f.Advance(seconds, step: 5);
            Assert(Math.Abs(f.PlayerState(0).drunk - 0.5f) < 0.01f, "Sobers at soberPerSecond, got " + f.PlayerState(0).drunk);
            f.Advance(seconds * 2, step: 5);
            Assert(f.PlayerState(0).drunk == 0 && f.Session.DrunkCondition(f.PlayerState(0)) == SpeakerConditions.Sober, "Never below zero");
        });

        await Test("Talk_WhileWasted_NpcHearsSlurredSpeechAndReacts", async () =>
        {
            var provider = new ScriptedProvider();
            using var f = new GameFixture(provider: provider);
            f.Session.SetContactUnlocked("Ashley", true);
            f.PlayerState(0).drunk = 1.2f;
            var result = await f.Talk(0, ActionIds.Talk, "Ashley", Typed);
            Assert(result.status == "committed", "Turn committed: " + result.error);
            string said = f.Conversations.Last().text;
            Assert(said != Typed && said.Length <= SocialRules.InputCharacters, "The NPC hears the rewritten line, not what was typed");
            var heard = f.Store.Read().events.Last(e => e.kind == "utterance" && e.actorId == f.P(0));
            Assert(heard.text == said, "The social journal records the slurred line");
            var context = provider.Contexts.Last();
            Assert(context.SpeakerCondition == SpeakerConditions.Wasted, "The NPC context carries the host-observed condition");
            Assert(context.Prompt.Contains("SPEAKER_CONDITION") && context.CompactPrompt.Contains("SPEAKER_CONDITION") &&
                   context.FactPrompt.Contains("CURRENT_SPEAKER_CONDITION") && context.DecisionFacts.Contains("Speaker condition="), "Every prompt projection states the condition");
            Assert(result.reply.Contains("wasted") || result.reply.Contains("hammered"), "The NPC remarks on it: " + result.reply);
        });

        await Test("Talk_WhileSober_TextUntouchedAndNoExtraRandomness", async () =>
        {
            var provider = new ScriptedProvider();
            using var f = new GameFixture(provider: provider);
            f.Session.SetContactUnlocked("Ashley", true);
            long seq = f.Session.State.eventSeq;
            var result = await f.Talk(0, ActionIds.Talk, "Ashley", Typed);
            Assert(result.status == "committed" && f.Conversations.Last().text == Typed, "Sober speech passes through");
            Assert(f.Session.State.eventSeq == seq, "Sober talk draws no randomness (existing determinism is unchanged)");
            Assert(provider.Contexts.Last().SpeakerCondition == SpeakerConditions.Sober && !provider.Contexts.Last().Prompt.Contains("SPEAKER_CONDITION"), "No condition line when sober");
            Assert(!result.reply.Contains("wasted") && !result.reply.Contains("drunk, huh"), "No drunk aside when sober");
        });

        await Test("Invite_WhileWasted_StillAnInviteForTheHeuristicNpc", async () =>
        {
            using var f = new GameFixture();
            f.Session.SetContactUnlocked("Ashley", true);
            f.PlayerState(0).drunk = f.Config.drunk.max;
            var result = await f.Talk(0, ActionIds.Invite, "Ashley");
            Assert(result.status == "committed", "Committed: " + result.error);
            Assert(result.decision != "respond", "A slurred invite is still answered as an RSVP, got " + result.decision);
        });

        await Test("DrunkPenalties_CrimeDetectionAndPoliceTalk_ScaleWithDrunk", () =>
        {
            using var f = new GameFixture();
            var crime = f.Config.Crime("prank");
            float sober = f.Session.DetectionChance(crime, "House_Theta", 0, 0f), wasted = f.Session.DetectionChance(crime, "House_Theta", 0, 1f);
            Assert(Math.Abs(wasted - sober - f.Config.drunk.crimeDetect) < 1e-4, "Detection rises by crimeDetect at the cap");
            Assert(f.Session.Drunk01(f.PlayerState(0)) == 0, "Sober share is 0");
            f.PlayerState(0).drunk = f.Config.drunk.max;
            Assert(f.Session.Drunk01(f.PlayerState(0)) == 1, "Cap share is 1");
        });

        await Test("Drunk_PreGameKeg_StaysOpenForTheParty", () =>
        {
            using var f = new GameFixture();
            f.Session.AddStash(f.Session.PlayerHouse, "keg", 1);
            f.Goto(0, "PartyHouse");
            Assert(f.Do(0, ActionIds.Drink, item: "keg").ok, "Weekday drink");
            int left = f.Session.State.party.openServings.First(o => o.itemId == "keg").count;
            f.Session.StartWeekendEarly();
            var open = f.Session.State.party.openServings.FirstOrDefault(o => o.itemId == "keg");
            Assert(f.Session.State.phase == Phase.Party && open != null && open.count <= left && open.count > 0, "The opened keg is still there on party night");
        });

        await Test("DrunkState_SaveRoundTrip_Persists", () =>
        {
            using var f = new GameFixture();
            f.PlayerState(0).drunk = 0.77f;
            f.PlayerState(0).nextDrinkAt = 123.5;
            var copy = f.Codec.Decode<GameState>(f.Codec.Encode(f.Session.State));
            var p = copy.players.First(x => x.playerId == f.P(0));
            Assert(Math.Abs(p.drunk - 0.77f) < 1e-6 && p.nextDrinkAt == 123.5, "Drunk state survives save/load");
        });

        await Test("Config_DrunkSection_LoadsFromContentAndRejectsBadTuning", () =>
        {
            var config = Content.Config(new JsonCodec());
            var d = config.drunk;
            Assert(d != null && d.speech != null && d.speech.interjections.Count > 0 && d.speech.tangents.Count > 0 && d.speech.trailOffs.Count > 0, "Content config has a drunk section");
            bool threw = false;
            try { var bad = GameLoopConfig.CreateDefault(); bad.drunk.threshold = bad.drunk.max + 1; bad.Validate(); } catch (FormatException) { threw = true; }
            Assert(threw, "threshold above max is rejected");
            threw = false;
            try { var bad = GameLoopConfig.CreateDefault(); bad.drunk.speech.typo = 2; bad.Validate(); } catch (FormatException) { threw = true; }
            Assert(threw, "Rates above 1 are rejected");
        });
    }

    /// <summary>Levenshtein distance (rewrite damage metric).</summary>
    static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
