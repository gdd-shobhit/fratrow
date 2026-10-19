using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>Model-free conversation metrics. Observations, not verdicts: a low duplicate rate is not proof of quality.</summary>
sealed class Metrics
{
    public int conversations, turns, committed, failed;
    public double meanReplyChars, withinNpcDistinctRatio, crossNpcDuplicateRate, meanPairwiseTrigramJaccard, distinct2;
    public double latencyP50Ms, latencyP95Ms, wallSeconds, claudeCostUsd, npcClaudeCostUsd;
    public int claudeCalls, rsvpOverrides, judged, notJudged, rsvpConsistent, testerErrors;
    public bool judgeEnabled;
    public Dictionary<string, int> decisions = new Dictionary<string, int>();
    public Dictionary<string, double> judgeMeans = new Dictionary<string, double>();
    /// <summary>Scenario judge checks: name → (conversations where true, conversations where the judge answered it).</summary>
    public Dictionary<string, int[]> judgeChecks = new Dictionary<string, int[]>();
    /// <summary>Blame scenario, from saved state: conversations where Player 2's trust went negative / a grievance names Player 2 / one names Player 1.</summary>
    public int blameStateChecked, player2TrustNegative, grievanceAgainstPlayer2, grievanceAgainstPlayer1;
    /// <summary>Adversarial scenario, from saved state: conversations with an RSVP recorded on an attack line.</summary>
    public int adversarialChecked, rsvpOnAttackLine;
    public List<NpcMetrics> perNpc = new List<NpcMetrics>();

    public sealed class NpcMetrics
    {
        public string npc, tone, finalRsvp;
        public int turns, failed;
        public double meanReplyChars, distinctRatio, duplicateRate;
        public double? judgeMean;
    }

    static readonly Regex Word = new Regex(@"[a-z0-9']+", RegexOptions.Compiled);
    static string Norm(string s) => string.Join(" ", Word.Matches((s ?? "").ToLowerInvariant()).Select(m => m.Value));
    static List<string> Tokens(string s) => Word.Matches((s ?? "").ToLowerInvariant()).Select(m => m.Value).ToList();

    /// <summary>Word trigrams padded with sentence boundaries so one-word replies like "Hey!" still count.</summary>
    static HashSet<string> Trigrams(IEnumerable<string> replies)
    {
        var set = new HashSet<string>();
        foreach (var r in replies)
        {
            var t = new List<string> { "<s>" }; t.AddRange(Tokens(r)); t.Add("</s>");
            for (int i = 0; i + 2 < t.Count; i++) set.Add(t[i] + " " + t[i + 1] + " " + t[i + 2]);
        }
        return set;
    }

    static double Percentile(List<long> values, double p)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(int)Math.Min(sorted.Count - 1, Math.Ceiling(p * sorted.Count) - 1)];
    }

    public static Metrics Compute(List<ConversationLog> logs, bool judgeEnabled, int rsvpOverrides, double cost, int calls, double wall)
    {
        var m = new Metrics { conversations = logs.Count, judgeEnabled = judgeEnabled, rsvpOverrides = rsvpOverrides, claudeCostUsd = cost, claudeCalls = calls, wallSeconds = wall };
        m.testerErrors = logs.Count(l => l.testerError != null);
        var allTurns = logs.SelectMany(l => l.turns).ToList();
        m.turns = allTurns.Count;
        m.failed = allTurns.Count(t => t.Failed);
        m.committed = allTurns.Count - m.failed;
        var good = logs.ToDictionary(l => l.npc, l => l.turns.Where(t => !t.Failed).Select(t => t.reply).ToList());
        var replies = good.SelectMany(g => g.Value).ToList();
        m.meanReplyChars = replies.Count == 0 ? 0 : replies.Average(r => r.Length);
        foreach (var t in allTurns) { string d = t.Failed ? "failed" : t.decision ?? "none"; m.decisions[d] = m.decisions.TryGetValue(d, out var n) ? n + 1 : 1; }
        m.latencyP50Ms = Percentile(allTurns.Select(t => t.ms).ToList(), 0.5);
        m.latencyP95Ms = Percentile(allTurns.Select(t => t.ms).ToList(), 0.95);

        // Exact duplicates across NPCs (normalized: case and punctuation ignored).
        var owners = new Dictionary<string, HashSet<string>>();
        foreach (var g in good) foreach (var r in g.Value) { var k = Norm(r); if (!owners.TryGetValue(k, out var s)) owners[k] = s = new HashSet<string>(); s.Add(g.Key); }
        int duplicated = good.Sum(g => g.Value.Count(r => owners[Norm(r)].Count > 1));
        m.crossNpcDuplicateRate = replies.Count == 0 ? 0 : (double)duplicated / replies.Count;

        var npcIds = good.Keys.Where(k => good[k].Count > 0).ToList();
        var grams = npcIds.ToDictionary(k => k, k => Trigrams(good[k]));
        var jaccards = new List<double>();
        for (int i = 0; i < npcIds.Count; i++)
            for (int j = i + 1; j < npcIds.Count; j++)
            {
                var a = grams[npcIds[i]]; var b = grams[npcIds[j]];
                int union = a.Union(b).Count();
                jaccards.Add(union == 0 ? 0 : (double)a.Intersect(b).Count() / union);
            }
        m.meanPairwiseTrigramJaccard = jaccards.Count == 0 ? 0 : jaccards.Average();

        var bigrams = new List<string>();
        foreach (var r in replies) { var t = Tokens(r); for (int i = 0; i + 1 < t.Count; i++) bigrams.Add(t[i] + " " + t[i + 1]); }
        m.distinct2 = bigrams.Count == 0 ? 0 : (double)bigrams.Distinct().Count() / bigrams.Count;

        var ratios = new List<double>();
        foreach (var l in logs)
        {
            var mine = good[l.npc];
            double distinct = mine.Count == 0 ? 0 : (double)mine.Select(Norm).Distinct().Count() / mine.Count;
            if (mine.Count > 0) ratios.Add(distinct);
            var j = l.judge;
            m.perNpc.Add(new NpcMetrics
            {
                npc = l.npc, tone = l.tone, finalRsvp = l.finalRsvp, turns = l.turns.Count, failed = l.turns.Count(t => t.Failed),
                meanReplyChars = mine.Count == 0 ? 0 : mine.Average(r => r.Length), distinctRatio = distinct,
                duplicateRate = mine.Count == 0 ? 0 : (double)mine.Count(r => owners[Norm(r)].Count > 1) / mine.Count,
                judgeMean = j == null ? (double?)null : (j.in_character + j.responsiveness + j.naturalness + j.variety + j.coherence) / 5.0
            });
        }
        m.withinNpcDistinctRatio = ratios.Count == 0 ? 0 : ratios.Average();

        var judged = logs.Where(l => l.judge != null).Select(l => l.judge).ToList();
        m.judged = judged.Count;
        m.notJudged = judgeEnabled ? logs.Count - judged.Count : 0;
        m.rsvpConsistent = judged.Count(j => j.rsvp_consistent);
        if (judged.Count > 0)
        {
            m.judgeMeans["in_character"] = judged.Average(j => j.in_character);
            m.judgeMeans["responsiveness"] = judged.Average(j => j.responsiveness);
            m.judgeMeans["naturalness"] = judged.Average(j => j.naturalness);
            m.judgeMeans["variety"] = judged.Average(j => j.variety);
            m.judgeMeans["coherence"] = judged.Average(j => j.coherence);
            m.judgeMeans["overall"] = m.judgeMeans.Values.Average();
        }
        foreach (var j in judged)
            foreach (var c in j.checks)
            {
                if (!m.judgeChecks.TryGetValue(c.Key, out var counts)) m.judgeChecks[c.Key] = counts = new int[2];
                if (c.Value) counts[0]++;
                counts[1]++;
            }
        foreach (var l in logs)
        {
            var h = l.hostChecks ?? new Dictionary<string, string>();
            if (h.TryGetValue("player2_trust", out var p2))
            {
                m.blameStateChecked++;
                if (int.TryParse(p2, out int v) && v < 0) m.player2TrustNegative++;
                if (h.TryGetValue("grievance_against_player2", out var g2) && g2 == "yes") m.grievanceAgainstPlayer2++;
                if (h.TryGetValue("grievance_against_player1", out var g1) && g1 == "yes") m.grievanceAgainstPlayer1++;
            }
            if (h.TryGetValue("rsvp_on_attack_line", out var hit)) { m.adversarialChecked++; if (hit != "none") m.rsvpOnAttackLine++; }
        }
        return m;
    }
}

static class Report
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    static readonly JsonSerializerOptions JsonLine = new JsonSerializerOptions { IncludeFields = true };
    static string F(double v, string fmt = "0.00") => v.ToString(fmt, CultureInfo.InvariantCulture);

    public static void Write(Options o, Backend backend, List<ConversationLog> logs, Metrics m)
    {
        File.WriteAllLines(Path.Combine(o.Out, "transcripts.jsonl"), logs.Select(l => JsonSerializer.Serialize(l, JsonLine)));
        File.WriteAllText(Path.Combine(o.Out, "summary.json"), JsonSerializer.Serialize(new
        {
            provider = backend.ProviderName, model = o.Model, tester = o.Tester == "haiku" ? o.TesterModel : "scripted",
            judge = o.Judge ? o.TesterModel : null, seed = o.Seed, turnsPerNpc = o.Turns, npcs = logs.Select(l => l.npc).ToArray(), metrics = m
        }, Json));
        var md = new StringBuilder();
        md.AppendLine("# NPC playtest — " + o.Provider + (o.Model != null && o.Provider != "heuristic" ? " · " + Options.ModelShort(o.Model) : ""));
        md.AppendLine();
        md.AppendLine("Provider: " + backend.ProviderName + "  ");
        md.AppendLine("Tester: " + (o.Tester == "haiku" ? o.TesterModel + " (adaptive game tester)" : "scripted (same " + ScriptedTester.Lines.Length + " lines to every NPC)") +
            " · judge: " + (o.Judge ? o.TesterModel : "off") + " · max " + o.Turns + " messages per NPC · seed " + o.Seed + "  ");
        md.AppendLine("Player house: " + backend.HouseName + " (planned Saturday party) · generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        md.AppendLine();
        md.AppendLine(Summary(m, markdown: true));
        md.AppendLine();
        md.AppendLine("## Per NPC");
        md.AppendLine();
        md.AppendLine("| NPC | Tone | Turns | Failed | Mean chars | Distinct | Dup w/ others | Judge mean | Final RSVP |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var n in m.perNpc)
            md.AppendLine("| " + n.npc + " | " + n.tone + " | " + n.turns + " | " + n.failed + " | " + F(n.meanReplyChars, "0") + " | " + F(n.distinctRatio) + " | " +
                F(n.duplicateRate) + " | " + (n.judgeMean.HasValue ? F(n.judgeMean.Value, "0.0") : "-") + " | " + n.finalRsvp + " |");
        md.AppendLine();
        md.AppendLine("## Transcripts");
        foreach (var l in logs)
        {
            md.AppendLine();
            md.AppendLine("### " + l.npc + " — " + l.summary + " (" + l.gender + ", " + l.tone + ", slang " + l.slang + ", " + (l.authored ? "authored" : "generated") + ")");
            md.AppendLine();
            md.AppendLine("> " + l.persona);
            md.AppendLine();
            foreach (var t in l.turns)
            {
                md.AppendLine("- **" + (t.speaker ?? "Player") + ":** " + t.player);
                md.AppendLine("  **" + l.npc + ":** " + (t.reply ?? "_(no reply)_") + " _(" + (t.Failed ? "FAILED " + (t.error ?? t.status) : t.decision) +
                    (t.commitment != null ? ", RSVP " + t.commitment : "") + ", " + F(t.ms / 1000.0, "0.0") + " s)_");
            }
            if (l.testerError != null) md.AppendLine("- _Tester error: " + l.testerError + "_");
            md.AppendLine();
            md.AppendLine("Final RSVP: " + l.finalRsvp);
            if (l.hostChecks != null && l.hostChecks.Count > 0)
                md.AppendLine("Saved state: " + string.Join(", ", l.hostChecks.Select(h => h.Key + "=" + h.Value)));
            if (l.judge != null)
            {
                var j = l.judge;
                md.AppendLine();
                md.AppendLine("Judge: in-character " + j.in_character + " · responsive " + j.responsiveness + " · natural " + j.naturalness + " · variety " + j.variety +
                    " · coherence " + j.coherence + " · RSVP consistent " + (j.rsvp_consistent ? "yes" : "NO") +
                    string.Concat(j.checks.Select(c => " · " + c.Key + " " + (c.Value ? "YES" : "no"))) + " — " + j.verdict);
                foreach (var issue in j.issues) md.AppendLine("  - " + issue);
            }
            else if (l.judgeError != null) md.AppendLine("Judge error: " + l.judgeError);
        }
        File.WriteAllText(Path.Combine(o.Out, "report.md"), md.ToString());
    }

    public static string Summary(Metrics m, bool markdown = false)
    {
        var rows = new List<(string, string)>
        {
            ("Conversations / turns", m.conversations + " / " + m.turns),
            ("Committed / failed turns", m.committed + " / " + m.failed),
            ("Mean reply length", F(m.meanReplyChars, "0") + " chars"),
            ("Distinct replies within an NPC", F(m.withinNpcDistinctRatio)),
            ("Replies duplicated verbatim by another NPC", F(m.crossNpcDuplicateRate * 100, "0") + "%"),
            ("Mean pairwise trigram Jaccard between NPCs (lower = more distinct)", F(m.meanPairwiseTrigramJaccard, "0.000")),
            ("Distinct-2 over all replies", F(m.distinct2)),
            ("Latency p50 / p95", F(m.latencyP50Ms / 1000, "0.0") + " / " + F(m.latencyP95Ms / 1000, "0.0") + " s"),
            ("Decisions", string.Join(", ", m.decisions.OrderByDescending(d => d.Value).Select(d => d.Key + "=" + d.Value))),
        };
        if (m.rsvpOverrides > 0) rows.Add(("Persona RSVP labels corrected to match wording", m.rsvpOverrides.ToString()));
        if (m.testerErrors > 0) rows.Add(("Tester errors (conversation cut short)", m.testerErrors.ToString()));
        if (!m.judgeEnabled) rows.Add(("Judge", "NOT ASSESSED (--no-judge)"));
        else if (m.notJudged > 0) rows.Add(("Judge NOT ASSESSED (judge call failed or no turns)", m.notJudged + " of " + m.conversations + " conversations; judge means cover the rest only"));
        if (m.judged > 0)
        {
            rows.Add(("Judge (1-5) in-character / responsive / natural / variety / coherence",
                string.Join(" / ", new[] { "in_character", "responsiveness", "naturalness", "variety", "coherence" }.Select(k => F(m.judgeMeans[k], "0.0")))));
            rows.Add(("Judge overall", F(m.judgeMeans["overall"], "0.0")));
            rows.Add(("RSVP words consistent with decision", m.rsvpConsistent + " / " + m.judged));
            foreach (var c in m.judgeChecks.OrderBy(c => c.Key))
                rows.Add(("Judge check: " + c.Key + " (true / answered)", c.Value[0] + " / " + c.Value[1] +
                    (c.Value[1] < m.judged ? " (NOT ASSESSED for " + (m.judged - c.Value[1]) + ")" : "")));
        }
        if (m.blameStateChecked > 0)
        {
            rows.Add(("Saved state: Player 2 personal trust negative (wrong blame)", m.player2TrustNegative + " / " + m.blameStateChecked));
            rows.Add(("Saved state: grievance names Player 2 (wrong blame)", m.grievanceAgainstPlayer2 + " / " + m.blameStateChecked));
            rows.Add(("Saved state: grievance names Player 1 (the insulter)", m.grievanceAgainstPlayer1 + " / " + m.blameStateChecked));
        }
        if (m.adversarialChecked > 0)
            rows.Add(("Saved state: RSVP recorded on an attack line", m.rsvpOnAttackLine + " / " + m.adversarialChecked));
        if (m.claudeCalls > 0) rows.Add(("Claude tester/judge calls / cost", m.claudeCalls + " / $" + F(m.claudeCostUsd, "0.000")));
        if (m.npcClaudeCostUsd > 0) rows.Add(("Claude NPC (persona-claude) cost", "$" + F(m.npcClaudeCostUsd, "0.000")));
        rows.Add(("Wall time", F(m.wallSeconds, "0") + " s"));
        if (!markdown) return string.Join("\n", rows.Select(r => r.Item1.PadRight(70) + " " + r.Item2));
        return "| Metric | Value |\n|---|---|\n" + string.Join("\n", rows.Select(r => "| " + r.Item1 + " | " + r.Item2 + " |"));
    }
}
