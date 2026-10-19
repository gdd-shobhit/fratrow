using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FratRow.Social;

/// <summary>
/// Reference ceiling, not a game provider: the exact <see cref="PersonaSocialTurn"/> prompts (character card, situation,
/// chat history, state labels) voiced and labeled by a Claude model instead of the local 2B/4B. Same host cleanup,
/// repeat check, wording reconciliation and validation. Latency includes the CLI process start (~4 s per call), so
/// only compare its quality, not its speed.
/// </summary>
sealed class ClaudePersonaProvider : ISocialModelProvider
{
    readonly ClaudeClient claude;
    readonly IStateCodec codec;
    public int RsvpOverrides { get; private set; }
    public double CostUsd => claude.CostUsd;

    public ClaudePersonaProvider(ClaudeClient claude, IStateCodec codec) { this.claude = claude; this.codec = codec; }

    public async Task<SocialProposal> GenerateAsync(ModelContext context, CancellationToken cancellation)
    {
        var persona = new PersonaSocialTurn(context);
        string reply = null;
        for (int attempt = 0; attempt < 2 && reply == null; attempt++)
        {
            string raw = await claude.TextAsync(persona.VoiceSystem(retry: attempt > 0), persona.FlattenedPrompt(), cancellation);
            string cleaned = PersonaSocialTurn.Clean(raw, persona.NpcName, persona.SpeakerName);
            if (attempt == 0 && persona.Repeats(cleaned)) continue;
            reply = cleaned;
        }
        if (reply == null) throw new ArgumentException("Voiced reply unusable");
        string Enum(System.Collections.Generic.IEnumerable<string> values) => "{\"type\":\"string\",\"enum\":[" + string.Join(",", values.Select(v => "\"" + v + "\"")) + "]}";
        string schema = "{\"type\":\"object\",\"properties\":{\"decision\":" + Enum(persona.AllowedDecisions) + ",\"trust\":" + Enum(persona.AllowedMagnitudes) +
            ",\"regard\":" + Enum(persona.AllowedMagnitudes) + ",\"grievance\":" + Enum(persona.AllowedGrievances) + ",\"resolve\":" + Enum(persona.AllowedResolves) +
            "},\"required\":[\"decision\",\"trust\",\"regard\",\"grievance\",\"resolve\"]}";
        JsonObject label = await claude.JsonAsync(persona.StateSystem, persona.StatePrompt(reply), schema, cancellation, effort: "low");
        var proposal = persona.Decode(label.ToJsonString(), reply, codec);
        if (persona.Override != null) RsvpOverrides++;
        return proposal;
    }

    public void Dispose() { }
}
