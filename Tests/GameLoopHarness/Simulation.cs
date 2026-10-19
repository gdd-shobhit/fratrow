using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FratRow.Game;
using FratRow.Social;

/// <summary>
/// <c>dotnet run --project Tests/GameLoopHarness -- --simulate [--weeks 6] [--players 4] [--seed 7] [--reckless]
/// [--model path.gguf --llama-server llama-server.exe] [--report Library/GameLoopHarness/report.md]</c>
/// Plays a whole semester with bot brothers and prints/writes a week-by-week report.
/// </summary>
static class Simulation
{
    public static async Task<int> Run(string[] args)
    {
        int weeks = int.Parse(Program.Arg(args, "--weeks") ?? "6");
        int players = int.Parse(Program.Arg(args, "--players") ?? "4");
        int seed = int.Parse(Program.Arg(args, "--seed") ?? "7");
        bool reckless = args.Contains("--reckless");
        bool realTime = args.Contains("--full-length");
        string model = Program.Arg(args, "--model");
        string server = Program.Arg(args, "--llama-server");
        string reportPath = Program.Arg(args, "--report") ?? Path.Combine(RepoPaths.Root, "Library", "GameLoopHarness", "report.md");

        ISocialModelProvider provider = null;
        string providerName = "heuristic (no model)";
        if (model != null && server != null)
        {
            provider = new LlamaServerModelProvider(new JsonCodec(), server, model, SocialModelCatalog.HashFor(Path.GetFileName(model)));
            providerName = "llama.cpp " + Path.GetFileName(model);
        }
        var watch = Stopwatch.StartNew();
        using var f = new GameFixture(seed: seed, players: players, provider: provider, tweak: c =>
        {
            c.semesterWeeks = weeks;
            if (!realTime) { c.weekdaySecondsPerDay = 60; c.partySeconds = 240; c.debriefSeconds = 10; }
        });
        var bots = new BotCrew(f, reckless ? BotPolicy.Reckless() : BotPolicy.Responsible(), seed);
        var report = new StringBuilder();
        report.AppendLine("# FratRow headless semester");
        report.AppendLine();
        report.AppendLine("Seed " + seed + " · " + players + " players · " + weeks + " weeks · provider: " + providerName + " · policy: " + (reckless ? "reckless" : "responsible"));
        report.AppendLine("Roster: " + f.Roster.npcs.Count + " NPCs (" + f.Roster.npcs.Count(n => n.isPnm) + " PNMs). Roles: " +
            string.Join(", ", f.Session.State.players.Select(p => p.displayName + "=" + p.role)));
        report.AppendLine();
        int lastNews = 0, lastConversation = 0;
        while (f.Session.State.phase != Phase.SemesterEnd)
        {
            int week = f.Session.State.week;
            await bots.PlayWeek();
            var result = f.Session.State.history.Last(w => w.week == week);
            var party = f.Session.State.party;
            report.AppendLine("## Week " + (week + 1));
            report.AppendLine();
            report.AppendLine("| Rank | House | Score | Cup | Δpop | Raided |");
            report.AppendLine("|---|---|---|---|---|---|");
            foreach (var e in result.entries)
                report.AppendLine("| " + e.rank + " | " + f.Session.House(e.houseId).name + " | " + e.score.ToString("0.0") + " | " + e.cupPoints + " | " +
                    e.popDelta.ToString("+0.0;-0.0") + " | " + (e.raided ? "yes" : e.suspended ? "suspended" : "") + " |");
            report.AppendLine();
            report.AppendLine("Our party: " + result.guests + " guests (peak " + result.peakGuests + "; " + result.namedGuests.Count + " named: " +
                string.Join(", ", result.namedGuests) + "), fun " + result.fun.ToString("0") + ", hype " + result.hype.ToString("0") +
                ", incidents " + result.incidents + " (" + result.escalated + " escalated), underage drinkers " + result.underageDrinkers +
                ", cash in $" + result.cashCollected + ", peak attention " + result.peakAttention.ToString("0") + ", police warnings " + result.warnings +
                (result.raided ? ", RAIDED" : "") + ".");
            _ = party;
            var h = f.Session.PlayerHouse;
            report.AppendLine("House after debrief: pop " + h.popularity.ToString("0.0") + ", cash $" + h.cash + ", heat " + h.heat.ToString("0") + ", dean " + h.dean.ToString("0") +
                ", GPA " + h.gpa.ToString("0.00") + ", members " + f.Session.TotalMembers(h) + ", grade " + h.grade + (h.probation ? " (PROBATION)" : h.suspended ? " (SUSPENDED)" : ""));
            report.AppendLine();
            report.AppendLine("Conversations this week (sample):");
            foreach (var c in f.Conversations.Skip(lastConversation).Where(c => c.result.status == "committed").Take(6))
                report.AppendLine("- " + Name(f, c.player) + " → " + c.npc + " [" + c.action + "]: \"" + c.text + "\" → \"" + c.result.reply + "\" (" + c.result.decision + "/" + (c.result.commitment ?? "-") + ")");
            var rsvps = f.Conversations.Skip(lastConversation).Where(c => c.action == ActionIds.Invite && c.result.status == "committed")
                .GroupBy(c => c.result.commitment ?? "none").Select(g => g.Key + "=" + g.Count());
            report.AppendLine("RSVPs: " + string.Join(", ", rsvps));
            lastConversation = f.Conversations.Count;
            report.AppendLine();
            report.AppendLine("News:");
            foreach (var n in f.Session.State.news.Where(n => n.seq > lastNews)) report.AppendLine("- " + n.text);
            lastNews = (int)f.Session.State.newsSeq;
            report.AppendLine();
        }
        report.AppendLine("## Semester result");
        report.AppendLine();
        foreach (var hh in f.Session.Ranked()) report.AppendLine("- " + hh.name + ": " + hh.cupPoints + " cup points, popularity " + hh.popularity.ToString("0") + ", grade " + hh.grade + ", members " + f.Session.TotalMembers(hh));
        report.AppendLine();
        var commits = f.Conversations.Count(c => c.result.status == "committed");
        report.AppendLine("Totals: " + f.Conversations.Count + " conversations (" + commits + " committed), invites " + bots.Invites + ", rush talks " + bots.RushTalks +
            ", bids " + bots.Bids + ", crimes " + bots.Crimes + ", busts " + bots.Busts + ", failed action attempts " + bots.ActionsFailed +
            ". Wall time " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s.");
        foreach (var line in bots.Log) report.AppendLine("- " + line);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllText(reportPath, report.ToString());
        Console.WriteLine(report.ToString());
        Console.WriteLine("Report written to " + reportPath);
        return 0;
    }

    static string Name(GameFixture f, string playerId) => f.Session.Player(playerId)?.displayName ?? playerId;
}
