using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FratRow.Social
{
    /// <summary>Optional direct llama.cpp helper for GPU runs. Localhost only, one slot, private API key.
    /// The caller must supply the verified b11718 server from the bootstrap script.</summary>
    public sealed class LlamaServerModelProvider : ISocialModelProvider
    {
        readonly string executable, modelPath, hash;
        readonly IStateCodec codec;
        readonly int gpuLayers;
        public int Seed { get; set; }
        public bool CompactFormat { get; set; }
        public bool GroundedReplies { get; set; } = true;
        /// <summary>Free speech: the model speaks in character, then a constrained pass labels the social state of that reply
        /// (see <see cref="PersonaSocialTurn"/>). Takes precedence over <see cref="GroundedReplies"/>; the service must not require grounded proposals.</summary>
        public bool PersonaReplies { get; set; }
        public bool UseRequestSeed { get; set; } = true;
        /// <summary>Persona turns whose RSVP label was corrected to match the reply's explicit wording.</summary>
        public int RsvpOverrides { get; private set; }
        /// <summary>Persona replies regenerated because they copied an earlier line.</summary>
        public int VoiceRetries { get; private set; }
        readonly SemaphoreSlim worker = new SemaphoreSlim(1, 1);
        readonly HttpClient http = new HttpClient(new HttpClientHandler { UseProxy = false });
        readonly object lifecycle = new object();
        Process process;
        ChildProcessLifetime processLifetime;
        bool disposed;
        string baseUrl;
        readonly string key = Guid.NewGuid().ToString("N");
        public int ProcessId => process?.Id ?? 0;
        public long PeakWorkingSet => process != null && !process.HasExited ? process.PeakWorkingSet64 : 0;
        public long PrivateBytes => process != null && !process.HasExited ? process.PrivateMemorySize64 : 0;
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
        public readonly StringBuilder Diagnostics = new StringBuilder();
        public LlamaServerModelProvider(IStateCodec codec, string executable, string modelPath, string hash, int gpuLayers = 99, int seed = 42)
        {
            this.codec = codec; this.executable = Path.GetFullPath(executable); this.modelPath = Path.GetFullPath(modelPath); this.hash = hash; this.gpuLayers = gpuLayers; Seed = seed;
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
        async Task EnsureStarted(CancellationToken cancellation)
        {
            if (process != null) { if (process.HasExited) throw new InvalidOperationException("llama helper exited"); return; }
            var checksumWatch = Stopwatch.StartNew();
            await Task.Run(() =>
            {
                using (var stream = File.OpenRead(modelPath))
                using (var sha = SHA256.Create())
                    if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != hash)
                        throw new InvalidDataException("Pinned model checksum mismatch");
            }, cancellation).ConfigureAwait(false);
            ChecksumMilliseconds = checksumWatch.Elapsed.TotalMilliseconds;
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            baseUrl = "http://127.0.0.1:" + port;
            lock (lifecycle)
            {
                if (disposed) throw new ObjectDisposedException(nameof(LlamaServerModelProvider));
                process = new Process { StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--model " + Quote(modelPath) + " --host 127.0.0.1 --port " + port + " --api-key " + key +
                        " --ctx-size 4096 --parallel 1 --n-predict 384 --n-gpu-layers " + gpuLayers +
                        " --threads 6 --batch-size 128 --ubatch-size 128 --jinja --reasoning off --no-webui --offline --no-context-shift",
                    WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true
                } };
                process.ErrorDataReceived += Capture; process.OutputDataReceived += Capture;
                if (!process.Start()) throw new InvalidOperationException("Could not start llama helper");
                try { processLifetime = new ChildProcessLifetime(process); }
                catch { if (!process.HasExited) process.Kill(); throw; }
                process.BeginErrorReadLine(); process.BeginOutputReadLine();
            }
            for (;;)
            {
                cancellation.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("llama startup failed: " + Diagnostics);
                try
                {
                    using (var response = await http.GetAsync(baseUrl + "/health", cancellation).ConfigureAwait(false))
                        if (response.IsSuccessStatusCode) return;
                }
                catch (HttpRequestException) { }
                await Task.Delay(100, cancellation).ConfigureAwait(false);
            }
        }
        void Capture(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (Diagnostics) { if (Diagnostics.Length > 24000) Diagnostics.Remove(0, 12000); Diagnostics.AppendLine(e.Data); }
        }
        static string Quote(string value)
        { if (value.Contains("\"")) throw new ArgumentException("Invalid executable/model path"); return "\"" + value + "\""; }
        [Serializable] sealed class Message { public string role = "user"; public string content; }
        [Serializable] sealed class TemplateOptions { public bool enable_thinking = false; }
        [Serializable] sealed class CompletionRequest
        {
            public Message[] messages;
            public int max_tokens = 384;
            public float temperature = 0.4f;
            public float top_p = 0.9f;
            public float repeat_penalty = 1f;
            /// <summary>DRY ("don't repeat yourself") sampler; 0 disables it. Allowed length 2, window = the whole 4,096-token
            /// context (the pinned server rejects the -1 "context size" shorthand).</summary>
            public float dry_multiplier;
            public float dry_base = 1.75f;
            public int dry_allowed_length = 2;
            public int dry_penalty_last_n = 4096;
            public int seed;
            public bool cache_prompt = false;
            public TemplateOptions chat_template_kwargs = new TemplateOptions();
            public string grammar = NativeSocialModelProvider.Grammar;
        }
        [Serializable] sealed class Choice { public Message message; public string finish_reason; }
        [Serializable] sealed class Usage { public int prompt_tokens; public int completion_tokens; }
        [Serializable] sealed class CompletionResponse { public Choice[] choices; public Usage usage; }
        public async Task<SocialProposal> GenerateAsync(ModelContext context, CancellationToken cancellation)
        {
            await worker.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                ChecksumMilliseconds = LoadMilliseconds = GenerationMilliseconds = 0;
                PromptTokens = OutputTokens = ModelPasses = 0; LastRawOutput = LastRawDecision = LastRawReply = LastRawCheck = null;
                bool cold = process == null;
                var loadWatch = Stopwatch.StartNew();
                await EnsureStarted(cancellation).ConfigureAwait(false);
                if (cold) LoadMilliseconds = Math.Max(0, loadWatch.Elapsed.TotalMilliseconds - ChecksumMilliseconds);
                if (PersonaReplies)
                {
                    var persona = new PersonaSocialTurn(context);
                    int seed = UseRequestSeed ? Seed ^ context.SamplingSeed : Seed;
                    string reply = null, problem = null;
                    for (int attempt = 0; attempt < 2 && reply == null; attempt++)
                    {
                        var messages = new[] { new Message { role = "system", content = persona.VoiceSystem(retry: attempt > 0) } }
                            .Concat(persona.VoiceHistory().Select(m => new Message { role = m.Key, content = m.Value })).ToArray();
                        LastRawReply = await CompleteMessagesAsync(messages, PersonaSocialTurn.VoiceGrammar, cancellation, PersonaSocialTurn.VoiceTemperature,
                            unchecked(seed + 7919 * attempt), PersonaSocialTurn.VoiceTokens, allowTruncation: true, topP: PersonaSocialTurn.VoiceTopP,
                            repeatPenalty: PersonaSocialTurn.VoiceRepeatPenalty, dry: PersonaSocialTurn.VoiceDry).ConfigureAwait(false);
                        try
                        {
                            string cleaned = PersonaSocialTurn.Clean(LastRawReply, persona.NpcName, persona.SpeakerName);
                            if (attempt == 0 && persona.Repeats(cleaned)) { VoiceRetries++; problem = "Reply repeated an earlier line"; continue; }
                            reply = cleaned;
                        }
                        catch (ArgumentException e) { problem = e.Message; }
                    }
                    if (reply == null) throw new ArgumentException("Voiced reply unusable: " + problem);
                    LastRawDecision = await CompleteAsync(persona.StateSystem, persona.StatePrompt(reply), persona.StateGrammar, cancellation,
                        PersonaSocialTurn.StateTemperature, seed).ConfigureAwait(false);
                    var voiced = persona.Decode(LastRawDecision, reply, codec);
                    if (persona.Override != null) { RsvpOverrides++; LastRawCheck = "rsvp label " + persona.Override; }
                    LastRawOutput = codec.Encode(voiced);
                    return voiced;
                }
                if (GroundedReplies)
                {
                    var grounded = new GroundedSocialTurn(context);
                    LastRawDecision = await CompleteAsync(grounded.System, grounded.Prompt, grounded.Grammar, cancellation,
                        samplingSeed: UseRequestSeed ? Seed ^ context.SamplingSeed : Seed).ConfigureAwait(false);
                    var result = grounded.Decode(LastRawDecision, codec);
                    LastRawReply = codec.Encode(new SocialReply.Message { reply = result.reply });
                    LastRawOutput = codec.Encode(result);
                    return result;
                }
                var format = CompactFormat ? new SocialInferenceFormat(context) : null;
                var rendered = format?.Context ?? context;
                LastRawDecision = await CompleteAsync(format != null ? SocialReply.PlannerSystem(format) : rendered.SystemPrompt, rendered.Prompt,
                    format != null ? SocialReply.PlannerGrammar(format) : NativeSocialModelProvider.GrammarFor(context), cancellation).ConfigureAwait(false);
                SocialProposal proposal;
                try { proposal = format != null ? format.Decode(LastRawDecision, codec) : codec.Decode<SocialProposal>(LastRawDecision); }
                catch (Exception e) { throw new ArgumentException("Invalid model JSON: " + e.Message); }
                if (format != null)
                {
                    SocialConversationService.ValidateProposal(proposal, context, requireReply: false);
                    LastRawReply = await CompleteAsync(SocialReply.System(context, codec), SocialReply.Prompt(rendered, proposal, codec), SocialReply.Grammar, cancellation).ConfigureAwait(false);
                    proposal.reply = SocialReply.Decode(LastRawReply, format, codec);
                    SocialConversationService.ValidateProposal(proposal, context);
                    LastRawOutput = codec.Encode(proposal);
                    LastRawCheck = await CompleteAsync(SocialReply.CheckSystem, format.RenderIds(SocialReply.CheckPrompt(rendered, proposal, codec)), SocialReply.CheckGrammar, cancellation, temperature: 0f).ConfigureAwait(false);
                    SocialReply.ValidateCheck(LastRawCheck, codec);
                }
                LastRawOutput = codec.Encode(proposal);
                return proposal;
            }
            finally { worker.Release(); }
        }
        Task<string> CompleteAsync(string systemPrompt, string prompt, string grammar, CancellationToken cancellation, float temperature = 0.4f, int? samplingSeed = null) =>
            // A single system + user exchange; persona voice turns use CompleteMessagesAsync with the chat history.
            CompleteMessagesAsync(new[] { new Message { role = "system", content = systemPrompt }, new Message { content = prompt } },
                grammar, cancellation, temperature, samplingSeed ?? Seed);
        async Task<string> CompleteMessagesAsync(Message[] messages, string grammar, CancellationToken cancellation, float temperature, int samplingSeed,
            int maxTokens = 384, bool allowTruncation = false, float topP = 0.9f, float repeatPenalty = 1f, float dry = 0f)
        {
                ModelPasses++;
                var request = new CompletionRequest { seed = samplingSeed, temperature = temperature, grammar = grammar, messages = messages,
                    max_tokens = maxTokens, top_p = topP, repeat_penalty = repeatPenalty, dry_multiplier = dry };
                using (var body = new StringContent(codec.Encode(request), Encoding.UTF8, "application/json"))
                {
                    var generationWatch = Stopwatch.StartNew();
                    using (var response = await http.PostAsync(baseUrl + "/v1/chat/completions", body, cancellation).ConfigureAwait(false))
                    {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    GenerationMilliseconds += generationWatch.Elapsed.TotalMilliseconds;
                    if (!response.IsSuccessStatusCode) throw new ArgumentException("llama response " + (int)response.StatusCode + ": " + json);
                    var result = codec.Decode<CompletionResponse>(json);
                    if (result?.choices == null || result.choices.Length != 1) throw new ArgumentException("Missing completion");
                    int inputTokens = result.usage?.prompt_tokens ?? 0, outputTokens = result.usage?.completion_tokens ?? 0;
                    PromptTokens += inputTokens; OutputTokens += outputTokens;
                    if (result.choices[0].finish_reason != "stop" && !(allowTruncation && result.choices[0].finish_reason == "length")) throw new ArgumentException("Truncated model output");
                    if (inputTokens + outputTokens > 4096) throw new ArgumentException("Context exceeded");
                    return result.choices[0].message.content;
                    }
                }
        }
        internal async Task<string> ProbeTextAsync(string systemPrompt, string prompt, CancellationToken cancellation)
        {
            await worker.WaitAsync(cancellation).ConfigureAwait(false);
            try { await EnsureStarted(cancellation).ConfigureAwait(false); return await CompleteAsync(systemPrompt, prompt, "", cancellation).ConfigureAwait(false); }
            finally { worker.Release(); }
        }
        public void Dispose()
        {
            lock (lifecycle)
            {
                if (disposed) return;
                disposed = true;
                processLifetime?.Dispose();
                if (processLifetime == null && process != null && !process.HasExited) process.Kill();
                http.Dispose();
            }
        }
    }
}
