using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace FratRow.Social
{
    /// <summary>Owns one model and an asynchronous worker lifetime. No Unity APIs on worker threads.</summary>
    public sealed class NativeSocialModelProvider : ISocialModelProvider
    {
        readonly SemaphoreSlim worker = new SemaphoreSlim(1, 1);
        readonly IStateCodec codec;
        readonly string modelPath, expectedHash;
        readonly int gpuLayers;
        public int Seed { get; set; }
        public bool CompactFormat { get; set; }
        public bool GroundedReplies { get; set; } = true;
        /// <summary>Free speech; see <see cref="LlamaServerModelProvider.PersonaReplies"/>. The native shim has no chat
        /// history API, so the conversation is flattened into the user message.</summary>
        public bool PersonaReplies { get; set; }
        public bool UseRequestSeed { get; set; } = true;
        public int RsvpOverrides { get; private set; }
        public int VoiceRetries { get; private set; }
        IntPtr handle;
        volatile bool disposed;
        public int PromptTokens { get; private set; }
        public int OutputTokens { get; private set; }
        public string LastRawOutput { get; private set; }
        public string LastRawDecision { get; private set; }
        public string LastRawReply { get; private set; }
        public string LastRawCheck { get; private set; }
        public int ModelPasses { get; private set; }
        public double ChecksumMilliseconds { get; private set; }
        public double LoadMilliseconds { get; private set; }
        public double GenerationMilliseconds { get; private set; }
        public NativeSocialModelProvider(IStateCodec codec, string modelPath, string expectedHash, int gpuLayers = 0, int seed = 42)
        { this.codec = codec; this.modelPath = modelPath; this.expectedHash = expectedHash; this.gpuLayers = gpuLayers; Seed = seed; }
        public async Task<SocialProposal> GenerateAsync(ModelContext context, CancellationToken cancellation)
        {
            if (disposed) throw new ObjectDisposedException(nameof(NativeSocialModelProvider));
            await worker.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    if (disposed) throw new ObjectDisposedException(nameof(NativeSocialModelProvider));
                    cancellation.ThrowIfCancellationRequested();
                    ChecksumMilliseconds = LoadMilliseconds = GenerationMilliseconds = 0;
                    PromptTokens = OutputTokens = ModelPasses = 0; LastRawOutput = LastRawDecision = LastRawReply = LastRawCheck = null;
                    if (handle == IntPtr.Zero)
                    {
                        var loadWatch = Stopwatch.StartNew();
                        using (var file = File.OpenRead(modelPath))
                        using (var sha = SHA256.Create())
                        {
                            string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                            if (actual != expectedHash) throw new InvalidDataException("Pinned GGUF checksum mismatch");
                        }
                        ChecksumMilliseconds = loadWatch.Elapsed.TotalMilliseconds;
                        loadWatch.Restart();
                        int code = Native.Create(modelPath, gpuLayers, out handle, out var error);
                        LoadMilliseconds = loadWatch.Elapsed.TotalMilliseconds;
                        string message = Take(error);
                        if (code != 0) throw new InvalidOperationException("Native model load: " + message);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    using (cancellation.Register(() => Native.Cancel(handle)))
                    {
                        if (PersonaReplies)
                        {
                            var persona = new PersonaSocialTurn(context);
                            int seed = UseRequestSeed ? Seed ^ context.SamplingSeed : Seed;
                            var watch = Stopwatch.StartNew();
                            string reply = null, problem = null;
                            for (int attempt = 0; attempt < 2 && reply == null; attempt++)
                            {
                                ModelPasses++;
                                int voiceStatus = Native.Generate(handle, persona.FlattenedPrompt(), persona.VoiceSystem(retry: attempt > 0), PersonaSocialTurn.VoiceGrammar,
                                    unchecked(seed + 7919 * attempt), PersonaSocialTurn.VoiceTemperature, out var voice, out var voiceError, out int voiceIn, out int voiceOut);
                                LastRawReply = Take(voice); string voiceDetail = Take(voiceError);
                                PromptTokens += voiceIn; OutputTokens += voiceOut;
                                cancellation.ThrowIfCancellationRequested();
                                if (voiceStatus != 0) throw new InvalidOperationException("Native persona voice: " + voiceDetail);
                                try
                                {
                                    string cleaned = PersonaSocialTurn.Clean(LastRawReply, persona.NpcName, persona.SpeakerName);
                                    if (attempt == 0 && persona.Repeats(cleaned)) { VoiceRetries++; problem = "Reply repeated an earlier line"; continue; }
                                    reply = cleaned;
                                }
                                catch (ArgumentException e) { problem = e.Message; }
                            }
                            if (reply == null) throw new ArgumentException("Voiced reply unusable: " + problem);
                            ModelPasses++;
                            int status = Native.Generate(handle, persona.StatePrompt(reply), persona.StateSystem, persona.StateGrammar, seed, PersonaSocialTurn.StateTemperature,
                                out var data, out var failure, out int promptTokens, out int completionTokens);
                            LastRawDecision = Take(data); string detail = Take(failure);
                            PromptTokens += promptTokens; OutputTokens += completionTokens;
                            GenerationMilliseconds = watch.Elapsed.TotalMilliseconds;
                            cancellation.ThrowIfCancellationRequested();
                            if (status != 0) throw new InvalidOperationException("Native persona state: " + detail);
                            var voiced = persona.Decode(LastRawDecision, reply, codec);
                            if (persona.Override != null) { RsvpOverrides++; LastRawCheck = "rsvp label " + persona.Override; }
                            LastRawOutput = codec.Encode(voiced); return voiced;
                        }
                        if (GroundedReplies)
                        {
                            var grounded = new GroundedSocialTurn(context);
                            var watch = Stopwatch.StartNew(); ModelPasses++;
                            int status = Native.Generate(handle, grounded.Prompt, grounded.System, grounded.Grammar, UseRequestSeed ? Seed ^ context.SamplingSeed : Seed, 0.4f,
                                out var data, out var failure, out int promptTokens, out int completionTokens);
                            GenerationMilliseconds = watch.Elapsed.TotalMilliseconds;
                            LastRawDecision = Take(data); string detail = Take(failure);
                            PromptTokens = promptTokens; OutputTokens = completionTokens;
                            cancellation.ThrowIfCancellationRequested();
                            if (status != 0) throw new InvalidOperationException("Native social acts: " + detail);
                            if (promptTokens + completionTokens > 4096) throw new ArgumentException("Grounded context exceeded");
                            var result = grounded.Decode(LastRawDecision, codec);
                            LastRawReply = codec.Encode(new SocialReply.Message { reply = result.reply });
                            LastRawOutput = codec.Encode(result); return result;
                        }
                        var format = CompactFormat ? new SocialInferenceFormat(context) : null;
                        var rendered = format?.Context ?? context;
                        var generationWatch = Stopwatch.StartNew();
                        ModelPasses++;
                        int code = Native.Generate(handle, rendered.Prompt, format != null ? SocialReply.PlannerSystem(format) : rendered.SystemPrompt,
                            format != null ? SocialReply.PlannerGrammar(format) : GrammarFor(context), Seed, 0.4f, out var output, out var error, out int inputTokens, out int outputTokens);
                        GenerationMilliseconds = generationWatch.Elapsed.TotalMilliseconds;
                        LastRawOutput = Take(output); string message = Take(error);
                        PromptTokens = inputTokens; OutputTokens = outputTokens;
                        cancellation.ThrowIfCancellationRequested();
                        if (code != 0) throw new InvalidOperationException("Native generation: " + message);
                        if (inputTokens + outputTokens > 4096) throw new InvalidOperationException("Actual model token budget exceeded");
                        LastRawDecision = LastRawOutput;
                        SocialProposal proposal;
                        try { proposal = format != null ? format.Decode(LastRawDecision, codec) : codec.Decode<SocialProposal>(LastRawDecision); }
                        catch (Exception e) { throw new ArgumentException("Invalid model JSON: " + e.Message); }
                        if (format != null)
                        {
                            SocialConversationService.ValidateProposal(proposal, context, requireReply: false);
                            cancellation.ThrowIfCancellationRequested();
                            generationWatch.Restart(); ModelPasses++;
                            code = Native.Generate(handle, SocialReply.Prompt(rendered, proposal, codec), SocialReply.System(context, codec), SocialReply.Grammar, Seed, 0.4f,
                                out output, out error, out inputTokens, out outputTokens);
                            GenerationMilliseconds += generationWatch.Elapsed.TotalMilliseconds;
                            LastRawReply = Take(output); message = Take(error);
                            PromptTokens += inputTokens; OutputTokens += outputTokens;
                            cancellation.ThrowIfCancellationRequested();
                            if (code != 0) throw new InvalidOperationException("Native voice generation: " + message);
                            if (inputTokens + outputTokens > 4096) throw new ArgumentException("Voice context exceeded");
                            proposal.reply = SocialReply.Decode(LastRawReply, format, codec);
                            SocialConversationService.ValidateProposal(proposal, context);
                            LastRawOutput = codec.Encode(proposal);
                            generationWatch.Restart(); ModelPasses++;
                            code = Native.Generate(handle, format.RenderIds(SocialReply.CheckPrompt(rendered, proposal, codec)), SocialReply.CheckSystem, SocialReply.CheckGrammar, Seed, 0f,
                                out output, out error, out inputTokens, out outputTokens);
                            GenerationMilliseconds += generationWatch.Elapsed.TotalMilliseconds;
                            LastRawCheck = Take(output); message = Take(error);
                            PromptTokens += inputTokens; OutputTokens += outputTokens;
                            cancellation.ThrowIfCancellationRequested();
                            if (code != 0) throw new InvalidOperationException("Native grounding audit: " + message);
                            if (inputTokens + outputTokens > 4096) throw new ArgumentException("Audit context exceeded");
                            SocialReply.ValidateCheck(LastRawCheck, codec);
                        }
                        LastRawOutput = codec.Encode(proposal);
                        return proposal;
                    }
                }, CancellationToken.None).ConfigureAwait(false);
            }
            finally { worker.Release(); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Do not block Unity's main thread on a running native generation or unload.
            _ = Task.Run(ReleaseAsync);
        }
        async Task ReleaseAsync()
        {
            await worker.WaitAsync().ConfigureAwait(false);
            try { if (handle != IntPtr.Zero) { Native.Destroy(handle); handle = IntPtr.Zero; } }
            finally { worker.Release(); }
        }
        static string Take(IntPtr text)
        {
            if (text == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(text); }
            finally { Native.Free(text); }
        }
        // Field ordering and bounded arrays keep decoding compact. Semantic checks remain in host code.
        public const string Grammar = @"
root ::= ""{"" ws ""\""schema\"""" ws "":"" ws ""1"" ws "","" ws ""\""evidence_ids\"""" ws "":"" ws ids ws "","" ws ""\""interpretation\"""" ws "":"" ws str ws "","" ws ""\""decision\"""" ws "":"" ws decision ws "","" ws ""\""relationship_effects\"""" ws "":"" ws effects ws "","" ws ""\""condition\"""" ws "":"" ws condition ws "","" ws ""\""grievance_event_id\"""" ws "":"" ws str ws "","" ws ""\""resolve_grievance_event_id\"""" ws "":"" ws str ws "","" ws ""\""reply\"""" ws "":"" ws str ws ""}""
decision ::= ""\""respond\"""" | ""\""accept\"""" | ""\""decline\"""" | ""\""defer\"""" | ""\""counteroffer\""""
condition ::= ""\""none\"""" | ""\""inviter_at_entrance\""""
ids ::= ""["" ws str (ws "","" ws str){0,3} ws ""]""
effects ::= ""["" ws (effect (ws "","" ws effect)?)? ws ""]""
effect ::= ""{"" ws ""\""scope\"""" ws "":"" ws (""\""player\"""" | ""\""house\"""") ws "","" ws ""\""id\"""" ws "":"" ws str ws "","" ws ""\""magnitude\"""" ws "":"" ws magnitude ws ""}""
magnitude ::= ""\""down\"""" | ""\""down_small\"""" | ""\""unchanged\"""" | ""\""up_small\"""" | ""\""up\""""
str ::= ""\"""" char{0,300} ""\""""
char ::= [^""\\\x00-\x1f] | ""\\"" ([""\\/bfnrt] | ""u"" [0-9a-fA-F]{4})
ws ::= [ \t\n\r]{0,2}
";
        public static string GrammarFor(ModelContext context)
        {
            if (context == null || context.EvidenceIds.Count == 0) throw new ArgumentException("A model turn requires observed evidence");
            string grammar = Regex.Replace(Grammar, @"(?m)^ids ::= .*", "ids ::= \"[\" ws evidence (ws \",\" ws evidence){0,3} ws \"]\"");
            grammar += "\nevidence ::= " + string.Join(" | ", context.EvidenceIds.OrderBy(x => x).Select(JsonIdTerminal)) + "\n";
            string player = "\"{\" ws \"\\\"scope\\\"\" ws \":\" ws \"\\\"player\\\"\" ws \",\" ws \"\\\"id\\\"\" ws \":\" ws " + JsonIdTerminal(context.PlayerId);
            string house = "\"{\" ws \"\\\"scope\\\"\" ws \":\" ws \"\\\"house\\\"\" ws \",\" ws \"\\\"id\\\"\" ws \":\" ws " + JsonIdTerminal(context.FraternityId);
            string tail = " ws \",\" ws \"\\\"magnitude\\\"\" ws \":\" ws magnitude ws \"}\"";
            grammar += "player-effect ::= " + player + tail + "\nhouse-effect ::= " + house + tail + "\n";
            grammar = Regex.Replace(grammar, @"(?m)^effects ::= .*", "effects ::= \"[\" ws (player-effect (ws \",\" ws house-effect)? | house-effect)? ws \"]\"");
            grammar = grammar.Replace("\"\\\"grievance_event_id\\\"\" ws \":\" ws str", "\"\\\"grievance_event_id\\\"\" ws \":\" ws grievance-id")
                .Replace("\"\\\"resolve_grievance_event_id\\\"\" ws \":\" ws str", "\"\\\"resolve_grievance_event_id\\\"\" ws \":\" ws resolved-id")
                .Replace("\"\\\"interpretation\\\"\" ws \":\" ws str", "\"\\\"interpretation\\\"\" ws \":\" ws short-str")
                .Replace("\"\\\"reply\\\"\" ws \":\" ws str", "\"\\\"reply\\\"\" ws \":\" ws reply-str");
            grammar += "grievance-id ::= \"\\\"\\\"\"" + (context.GrievanceCandidateIds.Count == 0 ? "" : " | " + string.Join(" | ", context.GrievanceCandidateIds.OrderBy(x => x).Select(JsonIdTerminal))) + "\nresolved-id ::= \"\\\"\\\"\"" +
                (context.GrievanceIds.Count == 0 ? "" : " | " + string.Join(" | ", context.GrievanceIds.OrderBy(x => x).Select(JsonIdTerminal))) + "\n";
            grammar += "short-str ::= \"\\\"\" char{1,160} \"\\\"\"\nreply-str ::= \"\\\"\" char{1,300} \"\\\"\"\n";
            string root = Regex.Match(grammar, @"(?m)^root ::= .*").Value;
            string ordinary = root.Replace("root ::=", "ordinary ::=").Replace("ws condition ws", "ws \"\\\"none\\\"\" ws");
            string conditional = root.Replace("root ::=", "conditional ::=").Replace("ws decision ws", "ws \"\\\"counteroffer\\\"\" ws")
                .Replace("ws condition ws", "ws \"\\\"inviter_at_entrance\\\"\" ws");
            grammar = Regex.Replace(grammar, @"(?m)^root ::= .*", "root ::= ordinary" + (context.PartyState == "planned" ? " | conditional" : ""));
            grammar += ordinary + "\n" + conditional + "\n";
            grammar = Regex.Replace(grammar, @"(?m)^decision ::= .*", "decision ::= \"\\\"respond\\\"\" | \"\\\"accept\\\"\" | \"\\\"decline\\\"\" | \"\\\"defer\\\"\"");
            if (context.PartyId == null || (context.PartyState != "planned" && context.PartyState != "active"))
                grammar = Regex.Replace(grammar, @"(?m)^decision ::= .*", "decision ::= \"\\\"respond\\\"\"");
            return grammar;
        }
        static string JsonIdTerminal(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new ArgumentException("Host ID is not grammar-safe");
            return "\"\\\"" + id + "\\\"\"";
        }
        static class Native
        {
            const string Library = "FratRowSocialNative";
            [DllImport(Library, EntryPoint = "fr_social_create", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int Create([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int gpuLayers, out IntPtr handle, out IntPtr error);
            [DllImport(Library, EntryPoint = "fr_social_generate_configured", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int Generate(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string prompt,
                [MarshalAs(UnmanagedType.LPUTF8Str)] string systemPrompt,
                [MarshalAs(UnmanagedType.LPUTF8Str)] string grammar, int seed, float temperature, out IntPtr output, out IntPtr error, out int promptTokens, out int outputTokens);
            [DllImport(Library, EntryPoint = "fr_social_cancel", CallingConvention = CallingConvention.Cdecl)] internal static extern void Cancel(IntPtr handle);
            [DllImport(Library, EntryPoint = "fr_social_free", CallingConvention = CallingConvention.Cdecl)] internal static extern void Free(IntPtr buffer);
            [DllImport(Library, EntryPoint = "fr_social_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern void Destroy(IntPtr handle);
        }
    }
}
