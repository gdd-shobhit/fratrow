using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FratRow.Game;
using FratRow.Social;
using static TestRunner;

static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Contains("--export-config"))
        {
            string path = Arg(args, "--export-config") ?? RepoPaths.Campus("GameLoopConfig.json");
            var json = JsonSerializer.Serialize(GameLoopConfig.CreateDefault(), new JsonSerializerOptions { IncludeFields = true, WriteIndented = true });
            File.WriteAllText(path, json + Environment.NewLine);
            Console.WriteLine("Wrote " + path);
            return 0;
        }
        if (args.Contains("--simulate")) return await Simulation.Run(args);
        if (args.Contains("--debug-party")) { DebugParty.Run(args); return 0; }
        await RunTests();
        Console.WriteLine(Passed + " passed, " + Failed + " failed");
        if (Failed > 0) Console.WriteLine("Failed: " + string.Join("; ", Failures));
        return Failed == 0 ? 0 : 1;
    }

    public static string Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
    }

    /// <summary>Short phases keep integration tests fast without changing any rule.</summary>
    static void Fast(GameLoopConfig c)
    {
        c.weekdaySecondsPerDay = 60;
        c.partySeconds = 240;
        c.debriefSeconds = 10;
    }

    static double SecondsUntil(GameSession s, int day, float hour) =>
        (day - s.State.day) * s.Config.weekdaySecondsPerDay + (hour - s.Config.weekdayStartHour) / (s.Config.weekdayEndHour - s.Config.weekdayStartHour) * s.Config.weekdaySecondsPerDay - s.State.phaseElapsed;

    static string Projection(GameSession s) => string.Join("\n",
        s.State.houses.Select(h => h.id + " cup=" + h.cupPoints + " pop=" + h.popularity.ToString("0.###") + " cash=" + h.cash + " heat=" + h.heat.ToString("0.###") +
            " dean=" + h.dean.ToString("0.###") + " members=" + h.memberCount + " grade=" + h.grade)
        .Concat(s.State.history.Select(w => "W" + w.week + ": " + string.Join(",", w.entries.Select(e => e.houseId + "#" + e.rank + ":" + e.score.ToString("0.###") + (e.raided ? "R" : ""))))));

    static async Task RunTests()
    {
        await Test("ConfigAndRoster_Load_ValidateAndCrossReference", () =>
        {
            var codec = new JsonCodec();
            var config = Content.Config(codec);
            var roster = Content.Roster(codec);
            Assert(config.houses.Count == 4 && config.houses.Count(h => h.isPlayer) == 1, "Expected 1 player house + 3 rivals");
            Assert(roster.npcs.Count >= 32, "Roster should contain at least the 32 hand-written NPCs, got " + roster.npcs.Count);
            Assert(roster.npcs.Count(n => n.isPnm) >= 10, "Expected at least 10 PNMs");
            foreach (var n in roster.npcs)
                foreach (var e in n.schedule)
                    Assert(config.Location(e.location) != null, n.id + " schedule uses unknown location " + e.location);
            var again = Content.Roster(codec);
            Assert(string.Join("|", roster.npcs.Select(n => n.id + n.persona + n.popularity)) == string.Join("|", again.npcs.Select(n => n.id + n.persona + n.popularity)),
                "Archetype expansion must be deterministic");
            Assert(roster.npcs.All(n => !n.isPnm || n.gender == "boy"), "PNMs are fraternity pledges (boys)");
        });

        await Test("NewCampaign_Start_WeekdayMondayWithPlannedPartyAndPatrols", () =>
        {
            using var f = new GameFixture(players: 2);
            var s = f.Session.State;
            Assert(s.phase == Phase.Weekday && s.day == 0 && s.week == 0, "Starts on Monday of week 1");
            Assert(!string.IsNullOrEmpty(s.socialPartyId), "A planned social party exists for invitations");
            Assert(s.patrols.Count >= 1, "At least one patrol unit");
            Assert(f.Session.PlayerHouse.cash > 0, "The house starts with money for supplies");
            Assert(f.PlayerState(0).role == "president" && f.PlayerState(1).role == "social_chair", "Lobby classes map to roles");
            Assert(Math.Abs(f.Session.ClockHour - 8f) < 0.01f, "Clock starts at 08:00");
        });

        await Test("Weekday_FiveCompressedDays_ThenPartyThenDebrief", () =>
        {
            using var f = new GameFixture(tweak: Fast);
            var days = new List<int>();
            f.Session.State.npcs.ForEach(_ => { });
            for (int i = 0; i < 4; i++) { f.Advance(f.Config.weekdaySecondsPerDay); days.Add(f.Session.State.day); }
            Assert(string.Join(",", days) == "1,2,3,4", "Days advance Mon..Fri: " + string.Join(",", days));
            f.Advance(f.Config.weekdaySecondsPerDay);
            Assert(f.Session.State.phase == Phase.Party && f.Session.State.day == 5, "Friday ends into Saturday party night");
            Assert(Math.Abs(f.Session.ClockHour - 21f) < 0.1f, "Party starts at 21:00");
            f.AdvanceUntil(Phase.Debrief, 1000);
            Assert(f.Session.State.history.Count == 1, "Debrief settled the week");
            f.AdvanceUntil(Phase.Weekday, 100);
            Assert(f.Session.State.week == 1 && f.Session.State.day == 0, "Next week starts Monday");
        });

        await Test("FullSemester_TwoPlayersHeuristic_ReachesSemesterEndWithConsistentTables", async () =>
        {
            using var f = new GameFixture(seed: 11, players: 2, tweak: c => { Fast(c); c.semesterWeeks = 4; });
            var bots = new BotCrew(f, BotPolicy.Responsible(), 11);
            int guard = 0;
            while (f.Session.State.phase != Phase.SemesterEnd && guard++ < 10) await bots.PlayWeek();
            var s = f.Session.State;
            Assert(s.phase == Phase.SemesterEnd, "Reached SemesterEnd (at " + s.phase + ")");
            Assert(s.history.Count == 4, "One result per week, got " + s.history.Count);
            foreach (var w in s.history)
            {
                Assert(w.entries.Count == 4 && w.entries.Select(e => e.rank).OrderBy(r => r).SequenceEqual(new[] { 1, 2, 3, 4 }), "Four ranked houses");
                Assert(w.entries.Sum(e => e.cupPoints) <= 20, "Cup points per week never exceed 20");
            }
            foreach (var h in s.houses) Assert(h.cupPoints == s.history.Sum(w => w.entries.First(e => e.houseId == h.id).cupPoints), "Cup totals match history for " + h.id);
            Assert(bots.Invites > 0 && f.Conversations.Any(c => c.result.status == "committed"), "Conversations happened");
            Assert(f.Conversations.All(c => c.result.status != "pending"), "No conversation left pending");
            Assert(!string.IsNullOrEmpty(s.semesterWinner), "A winner was crowned");
        });

        await Test("SameSeedAndIds_FullSemester_IdenticalTables", async () =>
        {
            string Run()
            {
                using var ids = new DeterministicIds(99);
                using var f = new GameFixture(seed: 5, players: 2, tweak: c => { Fast(c); c.semesterWeeks = 2; });
                var bots = new BotCrew(f, BotPolicy.Responsible(), 5);
                for (int i = 0; i < 3 && f.Session.State.phase != Phase.SemesterEnd; i++) bots.PlayWeek().GetAwaiter().GetResult();
                return Projection(f.Session);
            }
            string a = Run(), b = Run();
            Assert(a == b, "Same seed diverged:\n" + a + "\n---\n" + b);
            await Task.CompletedTask;
        });

        await Test("Invite_AcceptedArrivesInWindow_DeclinedNeverArrives", async () =>
        {
            var provider = new ScriptedProvider();
            provider.Decisions["Ashley"] = "accept";
            provider.Decisions["Maya"] = "decline";
            using var f = new GameFixture(provider: provider, tweak: Fast);
            f.Session.SetContactUnlocked("Ashley", true);
            f.Session.SetContactUnlocked("Maya", true);
            Assert((await f.Talk(0, ActionIds.Invite, "Ashley")).commitment == "accepted", "Ashley accepted");
            Assert((await f.Talk(0, ActionIds.Invite, "Maya")).commitment == "declined", "Maya declined");
            f.Session.StartWeekendEarly();
            f.Goto(0, "PartyHouse");
            var p = f.Session.State.party;
            int window = (int)(p.ticksTotal * f.Config.party.committedArrivalWindow) + 1;
            f.Advance(window * f.Config.partyTickSeconds);
            var ashley = p.guests.FirstOrDefault(g => g.npcId == "Ashley");
            Assert(ashley != null && ashley.arrivedTick <= window, "Ashley arrived within the committed window");
            var c = f.Store.Read().npcs.First(n => n.npcId == "Ashley").commitments.First(x => x.partyId == p.socialPartyId);
            Assert(c.arrived, "Social backend recorded the arrival");
            f.AdvanceUntil(Phase.Debrief, 1000);
            Assert(p.guests.All(g => g.npcId != "Maya"), "Maya declined and never arrived");
        });

        await Test("CounterOffer_InviterAtEntrance_ConvertsToArrival", async () =>
        {
            var provider = new ScriptedProvider();
            provider.Decisions["Chloe"] = "counteroffer";
            using var f = new GameFixture(provider: provider, tweak: Fast);
            f.Session.SetContactUnlocked("Chloe", true);
            Assert((await f.Talk(0, ActionIds.Invite, "Chloe")).commitment == "conditional", "Chloe made it conditional");
            f.Goto(0, "PartyHouse", atEntrance: true);
            f.Session.StartWeekendEarly();
            f.Advance(10 * f.Config.partyTickSeconds);
            Assert(f.Session.State.party.guests.Any(g => g.npcId == "Chloe"), "Meeting Chloe at the door brought her in");
        });

        await Test("CrimeAtRivalHouse_ScheduledNpcWitnesses_AndRemembersInConversation", async () =>
        {
            var provider = new ScriptedProvider();
            using var f = new GameFixture(provider: provider, tweak: Fast);
            // Find a weekday slot where someone hangs out at a rival house.
            var slot = f.Roster.npcs.SelectMany(n => n.schedule.Select(e => (n, e)))
                .Where(x => x.e.day <= 4 && x.e.location.StartsWith("House_") && x.e.end - x.e.start >= 1)
                .OrderBy(x => x.e.day).ThenBy(x => x.e.start).First();
            f.Advance(SecondsUntil(f.Session, slot.e.day, slot.e.start + 0.5f));
            Assert(f.Session.NpcLocation(slot.n.id) == slot.e.location, "Witness is at " + slot.e.location + " (was " + f.Session.NpcLocation(slot.n.id) + ")");
            f.Session.AddStash(f.Session.PlayerHouse, "prank_kit", 1);
            f.Goto(0, slot.e.location);
            var r = f.Do(0, ActionIds.Prank);
            Assert(r.ok, "Prank attempted: " + r.code + " " + r.message);
            var evt = f.Store.Read().events.LastOrDefault(e => e.kind == "prank");
            Assert(evt != null && evt.witnesses.Contains(slot.n.id) && evt.actorId == f.P(0), "The scheduled NPC witnessed the prank by this player");
            f.Session.SetContactUnlocked(slot.n.id, true);
            var talk = await f.Talk(0, ActionIds.Talk, slot.n.id, "Hey, what's up?");
            Assert(talk.status == "committed", "Talk committed: " + talk.error);
            var ctx = provider.Contexts.Last();
            Assert(ctx.EvidenceIds.Contains(evt.eventId) && ctx.GrievanceCandidateIds.Contains(evt.eventId), "The witnessed crime is in the NPC's conversation memory");
        });

        await Test("HighAttentionParty_Raided_FinesHeatAndEarlyEnd", () =>
        {
            using var f = new GameFixture(tweak: Fast);
            var h = f.Session.PlayerHouse;
            h.heat = 95;
            h.popularity = 70; // a famous, already-hot house draws a big, loud crowd
            f.Session.AddStash(h, "speakers", 1);
            f.Session.AddStash(h, "keg", 5);
            f.Session.AddStash(h, "liquor", 3);
            int cashBefore = h.cash;
            f.Session.StartWeekendEarly();
            f.Goto(0, "PartyHouse");
            f.Goto(1, "PartyHouse");
            double spent = 0;
            while (f.Session.State.phase == Phase.Party && spent < 240) { f.Do(1, ActionIds.Dj); f.Advance(5); spent += 5; }
            Assert(f.Session.State.party.raided, "A loud party at heat 95 gets raided");
            Assert(f.Session.State.phase == Phase.Debrief, "Raid ends the night early");
            Assert(f.Events.Any(e => e.kind == GameEventKind.Raid), "Raid event emitted");
            Assert(h.cash < cashBefore + f.Session.State.party.cashCollected + f.Config.economy.dues * f.Session.TotalMembers(h) + 1, "Fines were paid");
        });

        await Test("PartyMitigation_CardCheckAndMusicDown_FewerRaidsOver30Seeds", () =>
        {
            int Raids(BotPolicy policy)
            {
                int raids = 0;
                for (int seed = 1; seed <= 30; seed++)
                {
                    using var f = new GameFixture(seed: seed, players: 2, tweak: Fast, expandArchetypes: false);
                    var h = f.Session.PlayerHouse;
                    h.heat = 60;
                    h.popularity = 45; // enough of a draw that noise and underage drinking build up
                    f.Session.AddStash(h, "speakers", 1);
                    f.Session.AddStash(h, "keg", 4);
                    f.Session.AddStash(h, "liquor", 2);
                    f.Session.StartWeekendEarly();
                    var bots = new BotCrew(f, policy, seed);
                    bots.PlayWeek().GetAwaiter().GetResult();
                    if (f.Session.State.history.Last().entries.First(e => e.houseId == "player").raided) raids++;
                }
                return raids;
            }
            int reckless = Raids(BotPolicy.Reckless()), careful = Raids(BotPolicy.Responsible());
            Console.WriteLine("    raids reckless=" + reckless + "/30 responsible=" + careful + "/30");
            Assert(reckless > careful, "Mitigation should reduce raids (reckless " + reckless + ", responsible " + careful + ")");
        });

        await Test("BidDay_InterestedPnmWithBid_JoinsAndNeverSpawnsAsGuest", async () =>
        {
            using var f = new GameFixture(tweak: Fast, players: 3);
            int rushChair = Enumerable.Range(0, 3).First(i => f.PlayerState(i).role == "rush_chair");
            var pnm = f.Session.State.npcs.First(n => n.isPnm);
            pnm.rushBonus.Add(new HouseValue { houseId = "player", value = 60 });
            Assert(f.Do(rushChair, ActionIds.ExtendBid, pnm.npcId).ok, "Rush Chair can extend a bid");
            f.Session.StartWeekendEarly();
            f.AdvanceUntil(Phase.Debrief, 1000);
            Assert(pnm.memberOf == "" && pnm.bids.Contains("player"), "Week 1 is rush only; the bid stays queued");
            f.AdvanceUntil(Phase.Weekday, 100);
            f.Session.StartWeekendEarly();
            f.AdvanceUntil(Phase.Debrief, 1000);
            Assert(pnm.memberOf == "player", pnm.npcId + " joined on the first Bid Day (interest " + f.Session.RushInterest(pnm.npcId, "player") + ")");
            Assert(f.Store.Read().npcs.First(n => n.npcId == pnm.npcId).affiliation == "players", "Social context knows they are a brother now");
            f.AdvanceUntil(Phase.Weekday, 100);
            f.Session.SetContactUnlocked(pnm.npcId, true);
            var invite = f.Do(0, ActionIds.Invite, pnm.npcId);
            Assert(!invite.ok && invite.code == "invalid_target", "Members can't be invited as guests");
            f.Session.StartWeekendEarly();
            f.AdvanceUntil(Phase.Debrief, 1000);
            Assert(f.Session.State.party.guests.All(g => g.npcId != pnm.npcId), "Members never spawn as guests");
            await Task.CompletedTask;
        });

        await Test("Probation_HalvesCapacityAndPoints_SuspensionSkipsParty", () =>
        {
            using var f = new GameFixture(tweak: Fast);
            var h = f.Session.PlayerHouse;
            h.probation = true;
            f.Session.StartWeekendEarly();
            int expected = (int)Math.Round((f.Config.party.baseCapacity + f.Config.party.capacityPerMember * h.memberCount) * f.Config.dean.probationCapacityFactor);
            Assert(f.Session.State.party.capacity == expected, "Probation halves capacity (" + f.Session.State.party.capacity + " vs " + expected + ")");
            f.AdvanceUntil(Phase.Debrief, 1000);
            var e = f.Session.State.history.Last().entries.First(x => x.houseId == "player");
            Assert(e.cupPoints == (int)Math.Round(f.Config.scoring.cupPoints[e.rank - 1] * f.Config.dean.probationPointsFactor), "Probation halves points");
            using var g = new GameFixture(tweak: Fast);
            g.Session.PlayerHouse.suspended = true;
            g.Session.StartWeekendEarly();
            Assert(!g.Session.State.party.active, "Suspended house throws no party");
            g.AdvanceUntil(Phase.Debrief, 1000);
            var se = g.Session.State.history.Last().entries.First(x => x.houseId == "player");
            Assert(se.cupPoints == 0 && se.suspended, "Suspension scores nothing");
        });

        await Test("Roles_PerksAndExclusivity_Enforced", async () =>
        {
            using var f = new GameFixture(players: 4, tweak: Fast);
            int treasurer = Enumerable.Range(0, 4).First(i => f.PlayerState(i).role == "treasurer");
            int social = Enumerable.Range(0, 4).First(i => f.PlayerState(i).role == "social_chair");
            f.Goto(treasurer, "Shop");
            int before = f.Session.PlayerHouse.cash;
            Assert(f.Do(treasurer, ActionIds.Buy, item: "keg", amount: 1).ok, "Treasurer buys");
            Assert(before - f.Session.PlayerHouse.cash == (int)Math.Round(60 * 0.85), "Treasurer gets 15% off");
            f.Goto(social, "Quad");
            Assert(f.Do(social, ActionIds.MeetDean).code == "not_allowed_role", "Only the President meets the Dean");
            f.Goto(0, "Quad");
            Assert(f.Do(0, ActionIds.MeetDean).ok, "President meets the Dean");
            Assert(f.Do(0, ActionIds.MeetDean).code == "cap_reached", "Once per week");
            Assert(f.Do(social, ActionIds.ClaimRole, "treasurer").code == "already_done", "Unique roles can't be double-claimed");
            Assert(f.Do(social, ActionIds.ClaimRole, "risk_manager").ok, "Free roles can be claimed");
            f.Goto(treasurer, "Library");
            Assert(f.Do(treasurer, ActionIds.Buy, item: "keg").code == "wrong_location", "Buying requires the shop");
            await Task.CompletedTask;
        });

        await Test("RushChair_RushTalk_AddsBoostedInterest", async () =>
        {
            using var f = new GameFixture(players: 3, tweak: Fast);
            int rush = Enumerable.Range(0, 3).First(i => f.PlayerState(i).role == "rush_chair");
            // Find a PNM and teleport to them.
            for (int step = 0; step < 300; step++)
            {
                var target = f.Session.State.npcs.Where(n => n.isPnm).Select(n => (n, loc: f.Session.NpcLocation(n.npcId))).FirstOrDefault(x => x.loc != null);
                if (target.loc != null)
                {
                    f.Goto(rush, target.loc);
                    float before = f.Session.RushInterest(target.n.npcId, "player");
                    var result = await f.Talk(rush, ActionIds.RushTalk, target.n.npcId);
                    Assert(result.status == "committed", "Rush talk committed: " + result.error);
                    float after = f.Session.RushInterest(target.n.npcId, "player");
                    if (result.decision != "decline") Assert(after - before >= f.Config.rush.rushTalkInterest * 1.5f - 6f, "Rush Chair multiplier applied (" + before + " -> " + after + ")");
                    Assert(f.Do(rush, ActionIds.RushTalk, target.n.npcId).code == "cap_reached", "One pitch per PNM per day");
                    return;
                }
                f.Advance(5);
            }
            throw new Exception("No PNM found on campus during the week");
        });

        await Test("CrimeCaughtByPatrol_PlayerBustedAndDetained_TalkStillAllowed", async () =>
        {
            using var f = new GameFixture(tweak: c => { Fast(c); c.crimes.First(x => x.id == "prank").baseDetect = 1f; });
            f.Session.AddStash(f.Session.PlayerHouse, "prank_kit", 2);
            f.Goto(0, "House_Theta");
            var r = f.Do(0, ActionIds.Prank);
            Assert(r.code == "busted", "Certain detection busts the player");
            var p = f.PlayerState(0);
            Assert(p.record == 1 && p.detainedUntil > f.Session.State.time, "Player has a record and is detained");
            Assert(f.Do(0, ActionIds.Prank).code == "detained", "Detained players can't act");
            f.Session.SetContactUnlocked("Ashley", true);
            Assert((await f.Talk(0, ActionIds.Talk, "Ashley", "I got arrested lol")).status == "committed", "Texting still works while detained");
            f.Advance(f.Config.police.detainSeconds + 1);
            Assert(f.Do(0, ActionIds.Prank).code != "detained", "Detention expires");
        });

        await Test("HighHeatWeekday_CopCarParksOutsideHouse", () =>
        {
            using var f = new GameFixture(tweak: Fast);
            f.Session.PlayerHouse.heat = 95;
            f.Advance(200, 1);
            Assert(f.Session.State.patrols.Any(x => x.mode == "parked" && x.to == "PartyHouse"), "A unit parked at the house");
            Assert(f.Session.PoliceAlertLevel == 3, "HUD alert level 3 while parked");
            int expectedUnits = Math.Min(f.Config.police.maxPatrols, 1 + (int)Math.Floor(f.Session.PlayerHouse.heat / f.Config.police.patrolHeatStep));
            Assert(f.Session.State.patrols.Count == expectedUnits && expectedUnits >= 3, "High heat puts more units on campus (" + f.Session.State.patrols.Count + ")");
        });

        await Test("SaveLoad_MidSemester_ContinuesIdenticallyWithoutSocialRandomness", () =>
        {
            var codec = new JsonCodec();
            var config = Content.Config(codec);
            Fast(config);
            config.semesterWeeks = 3;
            var roster = Content.Roster(codec, expand: false);
            GameSession Make(GameState state)
            {
                var session = new GameSession(config, roster, state, new NullSocialBridge());
                return session;
            }
            void Script(GameSession s, double until)
            {
                while (s.State.time < until && s.State.phase != Phase.SemesterEnd)
                {
                    if (s.State.phase == Phase.Weekday && s.State.day == 1 && s.Player("a").location != "Shop")
                    { s.SetPlayerLocation("a", "Shop", false); s.Apply(new PlayerAction { playerId = "a", action = ActionIds.Buy, item = "keg", amount = 1 }); }
                    if (s.State.phase == Phase.Party) { s.SetPlayerLocation("a", "PartyHouse", true); s.Apply(new PlayerAction { playerId = "a", action = ActionIds.ServeDrinks }); }
                    if (s.State.phase == Phase.Debrief) s.SetPlayerLocation("a", "", false);
                    s.Tick(1);
                }
            }
            var a = Make(GameSession.NewCampaign(config, roster, "save", 3));
            a.AddOrUpdatePlayer("a", "A", "President");
            Script(a, 1e9);
            var b = Make(GameSession.NewCampaign(config, roster, "save", 3));
            b.AddOrUpdatePlayer("a", "A", "President");
            Script(b, 700);
            var restored = codec.Decode<GameState>(codec.Encode(b.State));
            var c = Make(restored);
            Script(c, 1e9);
            Assert(Projection(a) == Projection(c), "Save/load changed the outcome:\n" + Projection(a) + "\n---\n" + Projection(c));
            var store = new GameSaveStore(Path.Combine(Path.GetTempPath(), "FratRowGameTests", Guid.NewGuid().ToString("N")), codec);
            store.Save(c.State);
            Assert(Projection(Make(store.Load())) == Projection(c), "Checksummed save round-trips");
        });

        await Test("HouseNames_Sanitize_TrimsTurnsControlsIntoSpacesAndCapsLength", () =>
        {
            Assert(HouseNames.Sanitize(null) == "" && HouseNames.Sanitize(" \t\n ") == "", "Blank names sanitize to empty");
            Assert(HouseNames.Sanitize("  Alpha\tBeta\nGamma  ") == "Alpha Beta Gamma", "Trimmed, control characters become spaces");
            string longName = new string('x', HouseNames.MaxLength + 8);
            Assert(HouseNames.Sanitize(longName).Length == HouseNames.MaxLength, "Capped at " + HouseNames.MaxLength + " characters");
            string emojiAtCap = new string('a', HouseNames.MaxLength - 1) + "\U0001F37A";
            Assert(HouseNames.Sanitize(emojiAtCap) == new string('a', HouseNames.MaxLength - 1), "A surrogate pair is never split at the cap");
        });

        await Test("RenamePlayerHouse_LobbyName_RenamesOnlyPlayersHouseAndBlankKeepsIt", () =>
        {
            var codec = new JsonCodec();
            var config = Content.Config(codec);
            var roster = Content.Roster(codec, expand: false);
            var session = new GameSession(config, roster, GameSession.NewCampaign(config, roster, "rename", 5), new NullSocialBridge());
            string id = session.PlayerHouse.id;
            string rivals = string.Join("|", session.Rivals.Select(h => h.name));

            session.RenamePlayerHouse("  Alpha Beta Gamma ");
            Assert(session.PlayerHouse.name == "Alpha Beta Gamma" && session.PlayerHouse.id == id, "Lobby name applied, id unchanged");
            Assert(string.Join("|", session.Rivals.Select(h => h.name)) == rivals, "Rival names untouched");

            session.RenamePlayerHouse("   ");
            Assert(session.PlayerHouse.name == "Alpha Beta Gamma", "A blank lobby name keeps the current name");

            var restored = codec.Decode<GameState>(codec.Encode(session.State));
            Assert(restored.houses.First(h => h.isPlayer).name == "Alpha Beta Gamma", "The name is saved with the campaign");
        });

        await Test("HeuristicProvider_EveryNpcInvited_AllCommittedWithMixedRsvps", async () =>
        {
            using var f = new GameFixture(seed: 21, tweak: Fast);
            foreach (var n in f.Session.State.npcs) n.contactUnlocked = true;
            var results = new List<ConversationResult>();
            foreach (var n in f.Roster.npcs) results.Add(await f.Talk(0, ActionIds.Invite, n.id));
            Assert(results.All(r => r.status == "committed"), "All invites committed: " + string.Join(",", results.Where(r => r.status != "committed").Select(r => r.status + ":" + r.error)));
            var commitments = results.Select(r => r.commitment ?? "none").GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine("    RSVPs " + string.Join(", ", commitments.Select(kv => kv.Key + "=" + kv.Value)));
            Assert(commitments.Keys.Count >= 2, "NPCs don't all answer the same way");
            Assert(results.All(r => !string.IsNullOrWhiteSpace(r.reply) && r.reply.Length <= 300), "Every reply is host-composed and bounded");
        });

        await Test("HeuristicProvider_InsultAfterRsvp_GrievanceAgainstSpeakerAndUsuallyDeclines", async () =>
        {
            using var ids = new DeterministicIds(17);
            using var f = new GameFixture(seed: 17, tweak: Fast);
            int declines = 0, tried = 0;
            foreach (var npcId in f.Roster.npcs.Take(24).Select(n => n.id))
            {
                f.Session.SetContactUnlocked(npcId, true);
                var invite = await f.Talk(0, ActionIds.Invite, npcId);
                if (invite.commitment != "accepted" && invite.commitment != "conditional") continue;
                tried++;
                var insult = await f.Talk(0, ActionIds.Talk, npcId, "Honestly you're a loser and nobody wants you there.");
                Assert(insult.status == "committed", "Insult turn committed");
                var npc = f.Store.Read().npcs.First(n => n.npcId == npcId);
                Assert(npc.grievances.Any(g => !g.resolved && g.actorId == f.P(0)), npcId + " holds a grievance against the insulting player");
                Assert(npc.grievances.All(g => g.actorId != f.P(1)), "The other brother is not blamed");
                if (insult.commitment == "declined") declines++;
            }
            Console.WriteLine("    insults after RSVP: " + declines + "/" + tried + " withdrew");
            Assert(tried >= 3, "Enough RSVPs to judge (" + tried + ")");
            Assert(declines * 2 >= tried, "Most insulted guests withdraw");
        });

        await Test("RivalStandings_InNpcConversationContext", async () =>
        {
            var provider = new ScriptedProvider();
            using var f = new GameFixture(provider: provider, tweak: Fast);
            f.Session.SetContactUnlocked("Ivy", true);
            await f.Talk(0, ActionIds.Talk, "Ivy", "What do you think of the houses on campus?");
            var ctx = provider.Contexts.Last();
            foreach (var rival in f.Session.Rivals) Assert(ctx.Prompt.Contains(rival.name), "Context mentions " + rival.name);
            Assert(ctx.Prompt.Contains("campus rank"), "Context includes the players' rank");
        });

        await Test("RivalSabotage_GuardHouse_BlocksAndReports", () =>
        {
            using var f = new GameFixture(tweak: c => { Fast(c); c.rivals.dailySabotage = 10; foreach (var h in c.houses) h.aggression = 1; });
            f.Goto(0, "PartyHouse");
            f.Advance(f.Config.weekdaySecondsPerDay * 0.8);
            Assert(f.Do(0, ActionIds.GuardHouse).ok, "Guard duty in the evening");
            f.Advance(f.Config.weekdaySecondsPerDay * 0.3);
            Assert(f.Session.State.news.Any(n => n.kind == "defense"), "A sneak attack was stopped");
        });

        await Test("SemesterEnd_StartNextSemester_ResetsCupPointsKeepsMembers", () =>
        {
            using var f = new GameFixture(tweak: c => { Fast(c); c.semesterWeeks = 1; });
            f.Session.PlayerHouse.memberCount = 3;
            f.Session.StartWeekendEarly();
            f.AdvanceUntil(Phase.SemesterEnd, 2000);
            Assert(f.Events.Any(e => e.kind == GameEventKind.SemesterEnded), "Semester ended event");
            Assert(f.Do(1, ActionIds.StartSemester).code == "not_allowed_role", "Only the President starts the semester");
            Assert(f.Do(0, ActionIds.StartSemester).ok, "President starts semester 2");
            Assert(f.Session.State.semester == 2 && f.Session.State.houses.All(h => h.cupPoints == 0), "Cup points reset");
            Assert(f.Session.PlayerHouse.memberCount >= 3, "Members persist");
        });

        await Test("StartWeekend_RequiresEveryoneReady_PresidentOnly", () =>
        {
            using var f = new GameFixture(tweak: Fast);
            Assert(f.Do(0, ActionIds.StartWeekend).code == "not_ready", "Others must be ready");
            Assert(f.Do(1, ActionIds.Ready, amount: 1).ok, "Ready up");
            Assert(f.Do(1, ActionIds.StartWeekend).code == "not_allowed_role", "Social Chair can't start it");
            Assert(f.Do(0, ActionIds.StartWeekend).ok && f.Session.State.phase == Phase.Party, "President starts the weekend");
        });
    }
}
