using System;
using System.Linq;
using FratRow.Game;

/// <summary><c>--debug-party [--seed N] [--heat H]</c>: prints one loud party tick by tick (attention, police, incidents) for tuning.</summary>
static class DebugParty
{
    public static void Run(string[] args)
    {
        int seed = int.Parse(Program.Arg(args, "--seed") ?? "3");
        float heat = float.Parse(Program.Arg(args, "--heat") ?? "95", System.Globalization.CultureInfo.InvariantCulture);
        using var f = new GameFixture(seed: seed, tweak: c => { c.weekdaySecondsPerDay = 60; c.partySeconds = 240; c.debriefSeconds = 10; });
        var h = f.Session.PlayerHouse;
        h.heat = heat;
        h.popularity = float.Parse(Program.Arg(args, "--pop") ?? "5", System.Globalization.CultureInfo.InvariantCulture);
        f.Session.AddStash(h, "speakers", 1);
        f.Session.AddStash(h, "keg", 5);
        f.Session.StartWeekendEarly();
        f.Goto(0, "PartyHouse");
        f.Goto(1, "PartyHouse");
        var p = f.Session.State.party;
        while (f.Session.State.phase == Phase.Party)
        {
            f.Do(1, ActionIds.Dj);
            f.Advance(f.Config.partyTickSeconds);
            Console.WriteLine("tick " + p.tick + " guests " + f.Session.PresentGuests.Count() + " hype " + p.hype.ToString("0") + " noise " + p.noise.ToString("0") +
                " trouble " + p.trouble.ToString("0") + " att " + p.attention.ToString("0") + " open " + string.Join("/", f.Session.OpenIncidents.Select(i => i.kind)) +
                " underage " + f.Session.PresentGuests.Count(g => g.drankUnderage) + " disp " + p.dispatchedPatrol + " heat " + h.heat.ToString("0") +
                " fun " + p.fun.ToString("0") + " incidents " + p.incidentsTotal);
        }
        Console.WriteLine("incidents by kind: " + string.Join(", ", p.incidents.GroupBy(i => i.kind).Select(g => g.Key + "=" + g.Count())));
        Console.WriteLine("raided=" + p.raided + " warnings=" + p.warnings + " score=" + f.Session.State.history.LastOrDefault()?.entries.First(e => e.houseId == "player").score);
    }
}
