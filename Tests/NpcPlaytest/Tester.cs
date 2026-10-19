using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FratRow.Game;

/// <summary>
/// Calls a Claude model either through the Claude Code CLI in headless mode (default; uses the user's existing Claude
/// login, no API key) or the Messages API over HTTPS (needs ANTHROPIC_API_KEY in the environment). Each call is
/// stateless: the caller passes the whole transcript. CLI calls run in an empty temp directory so no project
/// CLAUDE.md, hooks or MCP servers load.
/// </summary>
sealed class ClaudeClient
{
    readonly string model, transport, executable;
    readonly string workDir = Path.Combine(Path.GetTempPath(), "FratRowNpcPlaytest", "claude-cwd");
    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
    readonly object costLock = new object();
    public double CostUsd { get; private set; }
    public int Calls { get; private set; }

    public ClaudeClient(string model, string transport, string executableOverride)
    {
        this.model = model; this.transport = transport;
        if (transport == "cli") executable = executableOverride ?? ResolveCli();
        Directory.CreateDirectory(workDir);
    }

    /// <summary>Finds a claude executable Process.Start can run directly (the npm .cmd/.ps1 shims re-parse arguments).</summary>
    static string ResolveCli()
    {
        var candidates = new List<string>();
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(dir.Trim(), OperatingSystem.IsWindows() ? "claude.exe" : "claude"));
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        candidates.Add(Path.Combine(appData, "npm", "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"));
        foreach (var c in candidates) if (File.Exists(c)) return c;
        string cmd = Path.Combine(appData, "npm", "claude.cmd");
        if (File.Exists(cmd)) return cmd;
        throw new FileNotFoundException("Could not find the claude CLI; pass --claude <path> or use --tester-transport api");
    }

    /// <summary>Plain-text completion. Low effort: tester lines are short, and thinking tokens dominate the cost.</summary>
    public Task<string> TextAsync(string system, string prompt, CancellationToken cancel = default) => WithRetry(() => Once(system, prompt, null, "low", cancel));

    /// <summary>Structured completion validated against a JSON schema; returns the JSON object.</summary>
    public async Task<JsonObject> JsonAsync(string system, string prompt, string schema, CancellationToken cancel = default)
    {
        string text = await WithRetry(() => Once(system, prompt, schema, null, cancel));
        int start = text.IndexOf('{'), end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidDataException("No JSON object in judge output: " + Trim(text));
        return JsonNode.Parse(text.Substring(start, end - start + 1))!.AsObject();
    }

    static async Task<string> WithRetry(Func<Task<string>> call)
    {
        try { return await call(); }
        catch (Exception first) when (!(first is OperationCanceledException))
        {
            await Task.Delay(1500);
            try { return await call(); }
            catch (Exception second) { throw new InvalidOperationException("Claude call failed twice: " + first.Message + " | " + second.Message); }
        }
    }

    Task<string> Once(string system, string prompt, string schema, string effort, CancellationToken cancel) =>
        transport == "api" ? ApiOnce(system, prompt, schema, cancel) : CliOnce(system, prompt, schema, effort, cancel);

    async Task<string> CliOnce(string system, string prompt, string schema, string effort, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable, WorkingDirectory = workDir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var a in new[] { "-p", "--model", model, "--system-prompt", system, "--tools", "", "--output-format", "json",
            "--no-session-persistence", "--safe-mode", "--strict-mcp-config" }) psi.ArgumentList.Add(a);
        if (schema != null) { psi.ArgumentList.Add("--json-schema"); psi.ArgumentList.Add(schema); }
        if (effort != null) { psi.ArgumentList.Add("--effort"); psi.ArgumentList.Add(effort); }
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + executable);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(240));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw new TimeoutException("claude CLI timed out"); }
        string output = await stdout, error = await stderr;
        JsonObject json;
        try { json = JsonNode.Parse(output)!.AsObject(); }
        catch { throw new InvalidDataException("claude CLI exit " + process.ExitCode + ": " + Trim(output + " " + error)); }
        if (json["total_cost_usd"] is JsonNode cost) lock (costLock) { CostUsd += cost.GetValue<double>(); }
        lock (costLock) Calls++;
        if (json["is_error"]?.GetValue<bool>() == true) throw new InvalidOperationException("claude CLI error: " + Trim(json["result"]?.ToString() ?? output));
        // --json-schema puts the validated object in structured_output and the same JSON as a string in result.
        if (schema != null && json["structured_output"] is JsonObject structured) return structured.ToJsonString();
        return json["result"]?.GetValue<string>() ?? throw new InvalidDataException("claude CLI returned no result");
    }

    async Task<string> ApiOnce(string system, string prompt, string schema, CancellationToken cancel)
    {
        string key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("--tester-transport api needs ANTHROPIC_API_KEY in the environment");
        if (schema != null) system += "\nReturn ONLY a JSON object matching this JSON schema, no prose: " + schema;
        var body = new JsonObject
        {
            ["model"] = model, ["max_tokens"] = schema != null ? 1200 : 400, ["system"] = system,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt })
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", "2023-06-01");
        using var response = await Http.SendAsync(request, cancel);
        string text = await response.Content.ReadAsStringAsync(cancel);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Messages API " + (int)response.StatusCode + ": " + Trim(text));
        var json = JsonNode.Parse(text)!.AsObject();
        lock (costLock) Calls++;
        return string.Concat(json["content"]!.AsArray().Where(b => b?["type"]?.GetValue<string>() == "text").Select(b => b!["text"]!.GetValue<string>()));
    }

    static string Trim(string s) => s.Length > 400 ? s.Substring(0, 400) + "…" : s;
}

sealed class TesterContext
{
    public RosterNpc Npc;
    public string House, Player;
    public int MaxTurns;
    public ConversationLog Log;
}

interface ITester
{
    Task<string> NextAsync(TesterContext c, int turn);
}

/// <summary>The same five lines to every NPC: the apples-to-apples "does everyone answer the same" check.</summary>
sealed class ScriptedTester : ITester
{
    public static readonly string[] Lines =
    {
        "hey, what's up?",
        "what are you into these days?",
        "we're throwing a party at {house} this Saturday, you should come",
        "lol you seem kinda hard to impress",
        "so are you coming or not?"
    };
    public Task<string> NextAsync(TesterContext c, int turn) =>
        Task.FromResult(turn < Lines.Length ? Lines[turn].Replace("{house}", c.House) : null);
}

/// <summary>Claude playing a QA game tester who only knows what a real player would know about the NPC.</summary>
sealed class HaikuTester : ITester
{
    readonly ClaudeClient claude;
    public HaikuTester(ClaudeClient claude) { this.claude = claude; }

    public async Task<string> NextAsync(TesterContext c, int turn)
    {
        string system =
            "You are a QA game tester playing FratRow, a college party-and-reputation game, to test how believable its NPC conversations are. " +
            "You play " + c.Player + ", a brother of " + c.House + ", a fraternity that is throwing a party this Saturday. You walked up to a student on campus and are talking in person. " +
            "Probe their personality the way a curious real player would: open naturally, ask at least one open question about them or what they're into, " +
            "react specifically to what they actually said, try a light tease or a follow-up to see how they handle it, and invite them to the Saturday party at " + c.House + " at some point. " +
            "Vary your wording like a real person; never sound like a script or a survey. You have at most " + c.MaxTurns + " messages in total. " +
            "Write ONLY your next message to them: plain text, no quotes, no narration or stage directions, no name prefix, at most 200 characters. " +
            "If the conversation is clearly over, output exactly [END].";
        var prompt = new StringBuilder();
        prompt.AppendLine("Student: " + c.Npc.displayName + " (" + c.Npc.gender + ", college student). What anyone on campus knows: " + c.Npc.summary + ".");
        prompt.AppendLine();
        if (c.Log.turns.Count == 0) prompt.AppendLine("You haven't said anything yet.");
        else
        {
            prompt.AppendLine("Conversation so far:");
            foreach (var t in c.Log.turns)
            {
                prompt.AppendLine("You: " + t.player);
                prompt.AppendLine(c.Npc.displayName + ": " + (t.reply ?? "(no reply: " + t.status + ")"));
            }
        }
        prompt.AppendLine();
        bool invited = c.Log.turns.Any(t => t.player.IndexOf("party", StringComparison.OrdinalIgnoreCase) >= 0);
        prompt.AppendLine("This is your message " + (turn + 1) + " of " + c.MaxTurns + "." +
            (!invited && turn >= c.MaxTurns - 2 ? " You haven't invited them to the Saturday party yet; do it in this message." : ""));
        prompt.AppendLine("Write your next message now.");
        string message = (await claude.TextAsync(system, prompt.ToString())).Trim();
        foreach (var prefix in new[] { c.Player + ":", "You:", "Me:" })
            if (message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) message = message.Substring(prefix.Length).Trim();
        if (message.Length >= 2 && message[0] == '"' && message[message.Length - 1] == '"') message = message.Substring(1, message.Length - 2).Trim();
        if (message.Contains("[END]")) return "[END]";
        return message.Length > 200 ? message.Substring(0, 200) : message;
    }
}

sealed class JudgeScores
{
    public int in_character, responsiveness, naturalness, variety, coherence;
    public bool rsvp_consistent;
    public string[] issues = Array.Empty<string>();
    public string verdict;
}

/// <summary>Scores a finished conversation against the NPC's hidden persona and voice.</summary>
sealed class HaikuJudge
{
    readonly ClaudeClient claude;
    public HaikuJudge(ClaudeClient claude) { this.claude = claude; }

    const string Schema = "{\"type\":\"object\",\"properties\":{" +
        "\"in_character\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5}," +
        "\"responsiveness\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5}," +
        "\"naturalness\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5}," +
        "\"variety\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5}," +
        "\"coherence\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5}," +
        "\"rsvp_consistent\":{\"type\":\"boolean\"}," +
        "\"issues\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
        "\"verdict\":{\"type\":\"string\"}}," +
        "\"required\":[\"in_character\",\"responsiveness\",\"naturalness\",\"variety\",\"coherence\",\"rsvp_consistent\",\"issues\",\"verdict\"]}";

    public async Task<JudgeScores> RateAsync(ConversationLog log, string house)
    {
        string system =
            "You are a strict QA judge for NPC dialogue in FratRow, a college party-and-reputation game. A tester (the player) talked to one NPC. " +
            "Score ONLY the NPC's replies, 1-5 each (1 = bad, 3 = acceptable, 5 = excellent):\n" +
            "- in_character: sounds unmistakably like THIS persona and voice (tone, slang level, example lines' style); 1 = generic, could be anyone.\n" +
            "- responsiveness: actually engages with what the player just said; 1 = canned or ignores the message.\n" +
            "- naturalness: sounds like a real college student talking, not a template, bot, customer support or therapist.\n" +
            "- variety: does not repeat itself across its own turns.\n" +
            "- coherence: consistent with earlier turns, no contradictions, no invented facts about the world.\n" +
            "- rsvp_consistent: true if the NPC's words agree with the recorded RSVP decision on each turn (accept = coming, decline = not coming, " +
            "defer = undecided, counteroffer = coming only if the inviter meets them at the entrance in the party's first minute, respond = no new RSVP announced).\n" +
            "List concrete issues (quote the reply) and give a one-line verdict. Be calibrated: a short reply can still be a 5 if it is perfectly in character and responsive.";
        var prompt = new StringBuilder();
        prompt.AppendLine("NPC: " + log.npc + " (" + log.gender + ")");
        prompt.AppendLine("Hidden persona: " + log.persona);
        prompt.AppendLine("Voice: tone=" + log.tone + ", slang=" + log.slang + (log.exampleLines.Length > 0 ? ", example lines: " + string.Join(" | ", log.exampleLines) : ""));
        prompt.AppendLine("Player's house: " + house + " (throwing a party this Saturday).");
        prompt.AppendLine();
        prompt.AppendLine("Transcript:");
        foreach (var t in log.turns)
        {
            prompt.AppendLine("Player: " + t.player);
            prompt.AppendLine(log.npc + ": " + (t.Failed ? "[turn failed: " + (t.error ?? t.status) + "] " : "") + t.reply + "   [recorded decision: " + (t.decision ?? "none") + "]");
        }
        prompt.AppendLine();
        prompt.AppendLine("Final saved RSVP: " + log.finalRsvp);
        var json = await claude.JsonAsync(system, prompt.ToString(), Schema);
        int Score(string name) => Math.Max(1, Math.Min(5, json[name]?.GetValue<int>() ?? 1));
        return new JudgeScores
        {
            in_character = Score("in_character"), responsiveness = Score("responsiveness"), naturalness = Score("naturalness"),
            variety = Score("variety"), coherence = Score("coherence"), rsvp_consistent = json["rsvp_consistent"]?.GetValue<bool>() ?? false,
            issues = json["issues"]?.AsArray().Select(i => i?.ToString() ?? "").Where(i => i.Length > 0).ToArray() ?? Array.Empty<string>(),
            verdict = json["verdict"]?.ToString() ?? ""
        };
    }
}
