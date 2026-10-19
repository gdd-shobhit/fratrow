using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FratRow.Game;
using FratRow.Social;

/// <summary>How the simulated brothers behave. Reckless crews skip ID checks and never turn the music down.</summary>
sealed class BotPolicy
{
    public bool CardCheck = true;
    public bool TurnDownWhenHot = true;
    public bool TalkToPolice = true;
    public bool Sabotage = true;
    public bool Rush = true;
    public int InvitesPerDay = 3;
    public int KegsPerWeek = 2;
    public static BotPolicy Responsible() => new BotPolicy();
    public static BotPolicy Reckless() => new BotPolicy { CardCheck = false, TurnDownWhenHot = false, TalkToPolice = false };
}

/// <summary>Plays the players' side of the loop: weekday routines by role, party-night hosting, debrief.</summary>
sealed class BotCrew
{
    readonly GameFixture f;
    readonly BotPolicy policy;
    readonly GameRng rng;
    public readonly List<string> Log = new List<string>();
    public int Invites, RushTalks, Crimes, Busts, Bids, ActionsFailed;
    readonly HashSet<string> invitedThisWeek = new HashSet<string>();

    public BotCrew(GameFixture fixture, BotPolicy policy, int seed)
    {
        f = fixture;
        this.policy = policy;
        rng = GameRng.For(seed, "bots");
    }

    GameSession S => f.Session;
    string RoleOf(int i) => f.PlayerState(i).role;

    ActionResult Do(int i, string action, string target = "", string item = "", int amount = 0, string rival = "")
    {
        var r = f.Do(i, action, target, item, amount, rival);
        if (!r.ok) ActionsFailed++;
        if (r.code == "busted") Busts++;
        return r;
    }

    /// <summary>Plays one whole week (weekday → party → debrief). Returns when the next week (or semester end) begins.</summary>
    public async Task PlayWeek()
    {
        invitedThisWeek.Clear();
        int startWeek = S.State.week;
        while (S.State.phase == Phase.Weekday)
        {
            int day = S.State.day;
            double daySeconds = S.Config.weekdaySecondsPerDay;
            f.Advance(daySeconds * 0.1 - S.State.phaseElapsed > 0 ? daySeconds * 0.1 - S.State.phaseElapsed : 0);
            for (int i = 0; i < f.Players.Count; i++) await Morning(i, day);
            f.Advance(Math.Max(0, daySeconds * 0.5 - S.State.phaseElapsed));
            for (int i = 0; i < f.Players.Count; i++) await Afternoon(i, day);
            f.Advance(Math.Max(0, daySeconds * 0.8 - S.State.phaseElapsed));
            for (int i = 0; i < f.Players.Count; i++) await Evening(i, day);
            if (day == 4) await Friday();
            // Finish the day (phase may flip to Party at the end of Friday).
            while (S.State.phase == Phase.Weekday && S.State.day == day) f.Advance(1);
        }
        if (S.State.phase == Phase.Party) await PartyNight();
        if (S.State.phase == Phase.Debrief)
        {
            f.Advance(5);
            if (S.State.phase == Phase.Debrief) Do(0, ActionIds.ContinueDebrief);
            if (S.State.phase == Phase.Debrief) f.AdvanceUntil(Phase.Weekday, 600);
        }
        _ = startWeek;
    }

    async Task TalkAround(int i, int budget)
    {
        // Meet whoever is here: get numbers, invite guests, pitch PNMs.
        var here = S.NpcsAt(f.PlayerState(i).location).OrderBy(_ => rng.NextULong()).ToList();
        foreach (var npcId in here)
        {
            if (budget <= 0) break;
            var npc = S.Npc(npcId);
            if (npc == null || !string.IsNullOrEmpty(npc.memberOf)) continue;
            if (!npc.contactUnlocked) Do(i, ActionIds.AskNumber, npcId);
            if (npc.isPnm && policy.Rush && (RoleOf(i) == "rush_chair" || RoleOf(i) == "president"))
            {
                var r = await f.Talk(i, ActionIds.RushTalk, npcId);
                if (r.status == "committed") RushTalks++;
                budget--;
                continue;
            }
            if (!invitedThisWeek.Contains(npcId) && S.State.week < S.Config.semesterWeeks)
            {
                var r = await f.Talk(i, ActionIds.Invite, npcId);
                if (r.status == "committed") { Invites++; invitedThisWeek.Add(npcId); }
                budget--;
            }
        }
    }

    async Task Morning(int i, int day)
    {
        switch (RoleOf(i))
        {
            case "treasurer":
                f.Goto(i, "Shop");
                Do(i, ActionIds.WorkShift);
                if (day == 0) Do(i, ActionIds.SetCover, amount: 5);
                break;
            case "social_chair":
                f.Goto(i, "Shop");
                if (S.StashCount(S.PlayerHouse, "speakers") == 0) Do(i, ActionIds.Buy, item: "speakers", amount: 1);
                if (day == 2) Do(i, ActionIds.Buy, item: "decorations", amount: 1);
                break;
            case "president":
                f.Goto(i, "Quad");
                Do(i, ActionIds.Volunteer);
                if (S.PlayerHouse.dean < 70) Do(i, ActionIds.MeetDean);
                break;
            case "rush_chair":
                f.Goto(i, day % 2 == 0 ? "Dorm" : "Gym");
                if (day % 2 == 1) Do(i, ActionIds.Workout);
                break;
            default:
                f.Goto(i, "Library");
                Do(i, ActionIds.Study);
                Do(i, ActionIds.Study);
                break;
        }
        await TalkAround(i, 2);
    }

    async Task Afternoon(int i, int day)
    {
        var house = S.PlayerHouse;
        switch (RoleOf(i))
        {
            case "social_chair":
                f.Goto(i, "PartyHouse");
                if (string.IsNullOrEmpty(house.theme)) Do(i, ActionIds.SetTheme, item: S.Config.themes[rng.Range(0, S.Config.themes.Count)].id);
                if (house.trashed) Do(i, ActionIds.CleanHouse);
                f.Goto(i, "Quad");
                break;
            case "treasurer":
                f.Goto(i, "Shop");
                int kegs = S.StashCount(house, "keg");
                if (kegs < policy.KegsPerWeek && house.cash > 200) Do(i, ActionIds.Buy, item: "keg", amount: policy.KegsPerWeek - kegs);
                if (S.StashCount(house, "soda_snacks") < 1) Do(i, ActionIds.Buy, item: "soda_snacks", amount: 1);
                if (policy.CardCheck && S.StashCount(house, "security") == 0 && house.cash > 300 && day == 3) Do(i, ActionIds.Buy, item: "security", amount: 1);
                if (policy.Sabotage && S.StashCount(house, "prank_kit") == 0 && house.cash > 150) Do(i, ActionIds.Buy, item: "prank_kit", amount: 1);
                break;
            case "rush_chair":
                f.Goto(i, "Quad");
                break;
            default:
                f.Goto(i, new[] { "Quad", "Gym", "Library", "Dorm" }[rng.Range(0, 4)]);
                break;
        }
        await TalkAround(i, policy.InvitesPerDay);
    }

    async Task Evening(int i, int day)
    {
        var house = S.PlayerHouse;
        if (RoleOf(i) == "president" || RoleOf(i) == "social_chair")
        {
            // Text-invite unlocked contacts who haven't been asked yet.
            foreach (var npc in S.State.npcs.Where(n => n.contactUnlocked && string.IsNullOrEmpty(n.memberOf) && !invitedThisWeek.Contains(n.npcId)).Take(2).ToList())
            {
                var r = await f.Talk(i, ActionIds.Invite, npc.npcId);
                if (r.status == "committed") { Invites++; invitedThisWeek.Add(npc.npcId); }
            }
        }
        if (policy.Sabotage && RoleOf(i) == "brother" || (policy.Sabotage && f.Players.Count < 5 && RoleOf(i) == "treasurer" && day == 3))
        {
            // Prank the leader among rivals when no patrol is parked there.
            var target = S.Ranked().FirstOrDefault(h => !h.isPlayer);
            if (target != null && S.StashCount(house, "prank_kit") > 0 && !S.PatrolAt(target.homeLocation) && !S.PatrolNear(target.homeLocation) && S.NpcsAt(target.homeLocation).Count <= 1)
            {
                f.Goto(i, target.homeLocation);
                var r = Do(i, ActionIds.Prank, target.id);
                Crimes++;
                Log.Add("W" + (S.State.week + 1) + " " + DayName(day) + ": prank on " + target.name + " -> " + r.code + " " + r.message);
            }
        }
        if (RoleOf(i) == "risk_manager" || (day == 4 && RoleOf(i) == "president"))
        {
            f.Goto(i, "PartyHouse");
            if (house.trashed) Do(i, ActionIds.CleanHouse);
            Do(i, ActionIds.GuardHouse);
        }
        await Task.CompletedTask;
    }

    async Task Friday()
    {
        // Rush Chair (or President) extends bids to PNMs who are leaning our way.
        int bidder = Enumerable.Range(0, f.Players.Count).FirstOrDefault(i => RoleOf(i) == "rush_chair");
        if (!S.RoleOf(f.PlayerState(bidder)).canExtendBid) bidder = 0;
        foreach (var npc in S.State.npcs.Where(n => n.isPnm && string.IsNullOrEmpty(n.memberOf)))
            if (S.RushInterest(npc.npcId, S.Config.playerHouseId) >= S.Config.rush.acceptBidInterest - 5 && Do(bidder, ActionIds.ExtendBid, npc.npcId).ok) Bids++;
        await Task.CompletedTask;
    }

    async Task PartyNight()
    {
        for (int i = 0; i < f.Players.Count; i++) f.Goto(i, "PartyHouse", atEntrance: true);
        var p = S.State.party;
        int rm = Enumerable.Range(0, f.Players.Count).FirstOrDefault(i => RoleOf(i) == "risk_manager");
        if (policy.CardCheck && p.active) Do(rm, ActionIds.CardCheck, amount: 1);
        bool calledCops = false;
        while (S.State.phase == Phase.Party)
        {
            f.Advance(S.Config.partyTickSeconds);
            if (S.State.phase != Phase.Party || !S.State.party.active) { if (S.State.phase == Phase.Party) f.Advance(5); continue; }
            p = S.State.party;
            for (int i = 0; i < f.Players.Count; i++)
            {
                var role = RoleOf(i);
                if (role == "social_chair" || (i == 0 && f.Players.Count == 1)) Do(i, ActionIds.Dj);
                if (role == "treasurer" || role == "brother") Do(i, ActionIds.ServeDrinks);
                foreach (var inc in S.OpenIncidents.ToList())
                {
                    string action = inc.kind == "fight" ? ActionIds.BreakUpFight : inc.kind == "passed_out" ? ActionIds.HelpGuest : inc.kind == "property_damage" ? ActionIds.FixDamage : null;
                    if (action != null && rng.Chance(0.6)) Do(i, action, inc.id.ToString());
                }
                if (inc_keg_dry() && role == "treasurer") Do(i, ActionIds.BeerRun);
                if (policy.TurnDownWhenHot && p.attention >= S.Config.police.warningThreshold - 8 && rng.Chance(0.7)) Do(i, ActionIds.TurnDownMusic);
                if (policy.TalkToPolice && p.dispatchedPatrol >= 0 && (role == "president" || role == "risk_manager")) Do(i, ActionIds.TalkToPolice);
            }
            if (policy.Sabotage && !calledCops && p.tick > p.ticksTotal / 3)
            {
                var leader = S.Ranked().First(h => !h.isPlayer);
                Do(f.Players.Count - 1, ActionIds.CallCops, leader.id);
                calledCops = true;
                Crimes++;
            }
        }
        bool inc_keg_dry() => S.OpenIncidents.Any(x => x.kind == "keg_dry");
        await Task.CompletedTask;
    }

    static string DayName(int d) => GameSession.DayName(d).Substring(0, 3);
}
