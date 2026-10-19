using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FratRow.Social;
using Systems.Campus;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>Host-owned social backend. Baked on GameManager; inert unless the development feature is enabled.</summary>
public sealed class NetworkedSocialConversation : NetworkBehaviour, IInteractionAccess
{
    public static NetworkedSocialConversation Instance { get; private set; }
    readonly NetworkVariable<bool> featureEnabled = new NetworkVariable<bool>(false);
    readonly NetworkVariable<FixedString64Bytes> campaignId = new NetworkVariable<FixedString64Bytes>();
    readonly Dictionary<ulong, string> connections = new Dictionary<ulong, string>();
    readonly Dictionary<string, TaskCompletionSource<ConversationResult>> requests = new Dictionary<string, TaskCompletionSource<ConversationResult>>();
    readonly Dictionary<string, CancellationTokenSource> cancellations = new Dictionary<string, CancellationTokenSource>();
    readonly HashSet<string> delivered = new HashSet<string>();
    readonly UnitySocialCodec codec = new UnitySocialCodec();
    PlayerCredential localCredential;
    SocialConversationService service;
    string localParty;
    bool binding;
    bool restored;
    string lastPublicCommitments;
    float partyPoll;
    NetworkedGameManager standingSource;
    public SocialWorld World { get; private set; }
    public bool Enabled => IsSpawned && featureEnabled.Value;
    public bool LocalPlayerBound => localCredential != null;
    public string LocalPlayerId => localCredential?.playerId;
    public string CurrentPartyId => IsServer && World != null
        ? World.Store.Read().parties.LastOrDefault(p => p.state == "active" || p.state == "planned")?.partyId : localParty;
    public event Action<ConversationResult> OnResult;
    public event Action<ConversationHistoryEntry[]> OnHistory;
    public event Action<PublicCommitment[]> OnCommitments;
    [Serializable] public sealed class OwnHistory { public ConversationHistoryEntry[] turns; }
    [Serializable] public sealed class PublicCommitments { public PublicCommitment[] entries; }
    public static bool Manages(string npcId) => Instance != null && Instance.Enabled && npcId == "Ashley";
    public static string Option(string name, string fallback = null)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    static bool ShouldEnable()
    {
        bool requested = Environment.GetCommandLineArgs().Contains("--social-brains");
#if UNITY_EDITOR
        requested |= UnityEditor.EditorPrefs.GetBool("FratRow.Social.Enabled", false);
#endif
        return requested && (Application.isEditor || Debug.isDebugBuild) &&
            (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer);
    }
    void Awake() { Instance = this; }
    public override void OnNetworkSpawn()
    {
        Instance = this;
        featureEnabled.OnValueChanged += FeatureChanged;
        NetworkManager.OnClientDisconnectCallback += Disconnected;
        if (IsServer && ShouldEnable())
        {
            try
            {
                string id = Option("--social-campaign", "social-development");
                if (id.Length > 48 || id.Any(c => !char.IsLetterOrDigit(c) && c != '-')) throw new ArgumentException("Invalid campaign name");
                var store = new JournalSocialStore(Path.Combine(Application.persistentDataPath, "SocialCampaigns", id), codec, () => SocialWorld.NewCampaign(id));
                if (store.Read().campaignId != id) throw new InvalidDataException("Campaign ID does not match its save directory");
                World = new SocialWorld(store);
                var ashley = NamedNpcDatabase.GetById("Ashley");
                string defaultPersona = SocialCharacterCatalog.Get("Ashley").persona;
                World.EnsureNpc("Ashley", string.IsNullOrWhiteSpace(ashley?.socialPersona) ? defaultPersona : ashley.socialPersona,
                    ashley?.socialVoice ?? SocialVoice.Ashley());
                RefreshStanding();
                string path = Option("--social-model", Path.Combine(Application.persistentDataPath, "SocialModels", SocialModelCatalog.QwenFile));
#if UNITY_EDITOR
                path = UnityEditor.EditorPrefs.GetString("FratRow.Social.Model", path);
#endif
                string hash = SocialModelCatalog.HashFor(Path.GetFileName(path));
                string llamaServer = Option("--social-llama-server");
#if UNITY_EDITOR
                if (llamaServer == null) llamaServer = UnityEditor.EditorPrefs.GetString("FratRow.Social.LlamaServer", "");
#endif
                ISocialModelProvider provider = string.IsNullOrEmpty(llamaServer)
                    ? (ISocialModelProvider)new NativeSocialModelProvider(codec, path, hash)
                    : new LlamaServerModelProvider(codec, llamaServer, path, hash);
                bool compact = Environment.GetCommandLineArgs().Contains("--social-compact-inference");
                if (provider is NativeSocialModelProvider nativeProvider) nativeProvider.CompactFormat = compact;
                if (provider is LlamaServerModelProvider llamaProvider) llamaProvider.CompactFormat = compact;
                service = new SocialConversationService(World, codec, provider, this,
                    TimeSpan.FromSeconds(double.Parse(Option("--social-timeout", "15"), System.Globalization.CultureInfo.InvariantCulture)), requireGroundedProposal: true);
                campaignId.Value = id;
                localParty = World.EnsurePlannedParty(20);
                featureEnabled.Value = true;
                Debug.Log("[Social] Host campaign opened: " + id + ". Runtime: " + (string.IsNullOrEmpty(llamaServer) ? "RunAnywhere CPU" : "direct llama.cpp CUDA fallback") + ". Inference is loaded lazily.");
            }
            catch (Exception e) { Debug.LogError("[Social] Startup failed: " + e); featureEnabled.Value = false; }
        }
        if (Enabled) BeginBind();
    }
    void FeatureChanged(bool previous, bool next) { if (next) BeginBind(); }
    void BeginBind()
    {
        if (!IsClient || binding || localCredential != null) return;
        binding = true;
        try
        {
            string json = File.Exists(CredentialPath) ? File.ReadAllText(CredentialPath) : "";
            BindServerRpc(Option("--social-name", "Player"), json);
        }
        catch (Exception e) { binding = false; Debug.LogError("[Social] Credential load failed: " + e.Message); }
    }
    string CredentialPath => Path.Combine(Application.persistentDataPath, "SocialCredentials",
        campaignId.Value + "-" + SafeProfile() + ".json");
    static string SafeProfile()
    {
        string profile = Option("--social-profile", "default");
        if (profile.Length > 32 || profile.Any(c => !char.IsLetterOrDigit(c) && c != '-')) throw new ArgumentException("Invalid social profile");
        return profile;
    }
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void BindServerRpc(string displayName, string credentialJson, RpcParams rpc = default)
    {
        if (!Enabled || World == null || credentialJson == null || credentialJson.Length > 1024 || displayName == null || displayName.Length > 48) return;
        ulong sender = rpc.Receive.SenderClientId;
        try
        {
            var presented = string.IsNullOrEmpty(credentialJson) ? null : codec.Decode<PlayerCredential>(credentialJson);
            if (connections.ContainsKey(sender)) return;
            var issued = World.Register(displayName, presented);
            if (connections.Values.Contains(issued.playerId)) throw new UnauthorizedAccessException("Player identity already connected");
            connections.Add(sender, issued.playerId);
            BindResultRpc(codec.Encode(issued), CurrentPartyId, "", RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        catch (Exception e) { BindResultRpc("", "", e.Message, RpcTarget.Single(sender, RpcTargetUse.Temp)); }
    }
    [Rpc(SendTo.SpecifiedInParams)]
    void BindResultRpc(string credentialJson, string partyId, string error, RpcParams rpc = default)
    {
        binding = false;
        if (!string.IsNullOrEmpty(error)) { Debug.LogError("[Social] Bind rejected: " + error); return; }
        var credential = codec.Decode<PlayerCredential>(credentialJson);
        Directory.CreateDirectory(Path.GetDirectoryName(CredentialPath));
        string temporary = CredentialPath + ".tmp";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(credentialJson);
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        if (File.Exists(CredentialPath)) File.Replace(temporary, CredentialPath, null); else File.Move(temporary, CredentialPath);
        localCredential = credential; localParty = partyId;
        RequestPublicCommitments();
        Debug.Log("[Social] Local player bound. Campaign=" + campaignId.Value + " Player=" + localCredential.playerId);
    }
    public Task<ConversationResult> SubmitLocalAsync(ConversationRequest request)
    {
        if (!Enabled || localCredential == null) throw new InvalidOperationException("Social backend has not bound this player");
        if (request == null || requests.ContainsKey(request.requestId)) throw new InvalidOperationException("Request already outstanding");
        request.partyId = string.IsNullOrEmpty(request.partyId) ? localParty : request.partyId;
        var completion = new TaskCompletionSource<ConversationResult>(); requests.Add(request.requestId, completion);
        SubmitServerRpc(codec.Encode(request)); return completion.Task;
    }
    public void CancelQueuedLocal(string requestId) => CancelQueuedServerRpc(requestId);
    public void RequestOwnHistory(string npcId) => HistoryServerRpc(npcId);
    public void RequestPublicCommitments() => CommitmentsServerRpc();
    string EncodePublicCommitments()
    {
        var snapshot = World.Store.Read();
        return codec.Encode(new PublicCommitments { entries = snapshot.npcs.SelectMany(n => n.commitments
            .Where(c => c.partyId == CurrentPartyId).Select(c => new PublicCommitment {
                npcId = n.npcId, partyId = c.partyId, inviterId = c.inviterId, state = c.state, condition = c.condition, arrived = c.arrived
            })).ToArray() });
    }
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void CommitmentsServerRpc(RpcParams rpc = default)
    {
        if (Enabled && World != null && connections.ContainsKey(rpc.Receive.SenderClientId))
            CommitmentsResultRpc(EncodePublicCommitments(), RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
    }
    [Rpc(SendTo.SpecifiedInParams)]
    void CommitmentsResultRpc(string json, RpcParams rpc = default) => OnCommitments?.Invoke(codec.Decode<PublicCommitments>(json).entries);
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void HistoryServerRpc(string npcId, RpcParams rpc = default)
    {
        if (!Enabled || World == null || !Manages(npcId) || !connections.TryGetValue(rpc.Receive.SenderClientId, out string player)) return;
        var turns = World.Store.Read().turns.Where(t => t.playerId == player && t.request.npcId == npcId)
            .Reverse().Take(30).Reverse().Select(t => new ConversationHistoryEntry { request = t.request, result = t.result }).ToArray();
        string json = codec.Encode(new OwnHistory { turns = turns });
        while (json.Length > 12000 && turns.Length > 1)
        { turns = turns.Skip(1).ToArray(); json = codec.Encode(new OwnHistory { turns = turns }); }
        HistoryResultRpc(json, RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
    }
    [Rpc(SendTo.SpecifiedInParams)]
    void HistoryResultRpc(string historyJson, RpcParams rpc = default) => OnHistory?.Invoke(codec.Decode<OwnHistory>(historyJson).turns);
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void SubmitServerRpc(string requestJson, RpcParams rpc = default)
    {
        if (!Enabled || service == null || requestJson == null || requestJson.Length > 6000 || !connections.TryGetValue(rpc.Receive.SenderClientId, out string player)) return;
        _ = SubmitRequestAsync(requestJson, rpc.Receive.SenderClientId, player);
    }
    async Task SubmitRequestAsync(string requestJson, ulong sender, string player)
    {
        ConversationRequest request = null;
        CancellationTokenSource cancellation = null;
        string key = null;
        ConversationResult result;
        try
        {
            RefreshStanding();
            request = codec.Decode<ConversationRequest>(requestJson);
            key = player + "/" + request.requestId;
            if (cancellations.TryGetValue(key, out var existing)) result = await service.SubmitAsync(player, request, existing.Token);
            else
            {
                cancellation = new CancellationTokenSource(); cancellations.Add(key, cancellation);
                result = await service.SubmitAsync(player, request, cancellation.Token);
            }
        }
        catch (Exception e) { result = new ConversationResult { requestId = request?.requestId, status = "rejected", error = e.Message }; }
        finally { if (cancellation != null) { cancellations.Remove(key); cancellation.Dispose(); } }
        if (IsSpawned && connections.TryGetValue(sender, out string bound) && bound == player)
            ConversationResultRpc(codec.Encode(result), CurrentPartyId, RpcTarget.Single(sender, RpcTargetUse.Temp));
    }
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void CancelQueuedServerRpc(string requestId, RpcParams rpc = default)
    {
        if (requestId != null && requestId.Length <= 64 && connections.TryGetValue(rpc.Receive.SenderClientId, out string player)
            && cancellations.TryGetValue(player + "/" + requestId, out var cancellation)) cancellation.Cancel();
    }
    [Rpc(SendTo.SpecifiedInParams)]
    void ConversationResultRpc(string resultJson, string nextParty, RpcParams rpc = default)
    {
        var result = codec.Decode<ConversationResult>(resultJson); localParty = nextParty;
        if (result.requestId != null && requests.TryGetValue(result.requestId, out var completion))
        { requests.Remove(result.requestId); completion.TrySetResult(result); }
        if (result.status != "committed" || delivered.Add(result.requestId)) OnResult?.Invoke(result);
        Debug.Log("[Social] " + resultJson);
    }
    public bool CanHear(string playerId, ConversationRequest request)
    {
        if (!connections.Values.Contains(playerId) || !Manages(request.npcId)) return false;
        if (request.channel == "text") return NetworkedContactRoster.Instance != null && NetworkedContactRoster.Instance.IsUnlocked(request.npcId);
        ulong client = connections.First(p => p.Value == playerId).Key;
        if (!NetworkManager.ConnectedClients.TryGetValue(client, out var connected) || connected.PlayerObject == null) return false;
        var npc = FindObjectsByType<NamedNpcVisual>(FindObjectsSortMode.None).FirstOrDefault(n => n.NpcId == request.npcId);
        if (npc != null && npc.gameObject.activeInHierarchy && npc.GetComponent<Collider>()?.enabled == true && Vector3.Distance(npc.transform.position, connected.PlayerObject.transform.position) <= 4f) return true;
        var guest = FindObjectsByType<NetworkedNPCGuest>(FindObjectsSortMode.None).FirstOrDefault(n => n.NpcId == request.npcId);
        return guest != null && Vector3.Distance(guest.transform.position, connected.PlayerObject.transform.position) <= 4f;
    }
    void Disconnected(ulong client)
    {
        if (connections.TryGetValue(client, out string player))
        {
            connections.Remove(client);
            foreach (var pair in cancellations.Where(p => p.Key.StartsWith(player + "/", StringComparison.Ordinal)).ToArray()) pair.Value.Cancel();
        }
        if (client == NetworkManager.LocalClientId)
        { foreach (var completion in requests.Values) completion.TrySetException(new IOException("Disconnected; query same request after reconnect")); requests.Clear(); }
    }
    void Update()
    {
        if (!IsServer || !Enabled || World == null) return;
        try
        {
            if (!restored && NetworkedContactRoster.Instance != null && NetworkedContactRoster.Instance.IsSpawned)
            {
                RefreshStanding();
                World.ImportLegacy(new Dictionary<string, float> { { "Ashley", NetworkedContactRoster.Instance.GetAffinity("Ashley") } });
                NetworkedContactRoster.Instance.UnlockSocialScenarioServer("Ashley");
                NetworkedCampusCalendar.Instance?.RestoreSocialDayServer(World.Store.Read().day);
                restored = true; ResumeReceived();
            }
            partyPoll += Time.unscaledDeltaTime;
            if (partyPoll < 0.5f) return; partyPoll = 0;
            RefreshStanding();
            NetworkedContactRoster.Instance?.ProjectSocialRegardServer("Ashley", World.Store.Read().npcs.Find(n => n.npcId == "Ashley").fraternityRegard);
            string publicCommitments = EncodePublicCommitments();
            if (publicCommitments != lastPublicCommitments && connections.Count > 0)
            {
                lastPublicCommitments = publicCommitments;
                CommitmentsResultRpc(publicCommitments, RpcTarget.Group(connections.Keys.ToArray(), RpcTargetUse.Temp));
            }
            var manager = NetworkedPartyManager.Instance;
            if (manager == null || manager.NetworkedParty == null) return;
            var state = World.Store.Read(); var active = state.parties.Find(p => p.state == "active");
            if (active != null && !manager.IsPartyActive)
            {
                double remaining = manager.PartyDurationMinutes * 60 - (Now - active.startedAt);
                if (remaining > 0) manager.ResumeSocialPartyServer((float)remaining);
                else EndSocialPartyServer();
            }
            if (manager.IsPartyActive) manager.PollSocialAttendanceServer(this);
        }
        catch (Exception e) { Debug.LogError("[Social] Host state error; feature stopped: " + e); featureEnabled.Value = false; service?.Dispose(); }
    }
    void RefreshStanding()
    {
        if (!IsServer || World == null) return;
        // Read authoritative Unity state on the main thread; inference receives only saved DTOs.
        var source = FindFirstObjectByType<NetworkedGameManager>();
        if (source != null && source.IsSpawned)
        {
            if (standingSource != source)
            {
                DetachStanding(); standingSource = source;
                standingSource.OnPopularityChanged += PopularityChanged;
                standingSource.OnFraternityChanged += FraternityChanged;
            }
            var fraternity = source.myFraternity.Value;
            string name = fraternity.fratName ?? "";
            bool hasGrade = !FraternityGradeCalculator.IsUninitialized(fraternity);
            World.SetFraternityStanding(source.TotalPopularity, name.Substring(0, Math.Min(name.Length, 64)),
                hasGrade ? FraternityGradeCalculator.ToLetter(fraternity.grade) : "", hasGrade ? fraternity.rating : 0);
        }
        foreach (var npc in World.Store.Read().npcs)
        {
            var definition = NamedNpcDatabase.GetById(npc.npcId);
            if (definition != null) World.SetNpcStanding(npc.npcId, definition.basePopularity, definition.socialStatusConcern);
        }
    }
    void PopularityChanged(float value) => StandingChanged();
    void FraternityChanged(FraternityData value) => StandingChanged();
    void StandingChanged()
    {
        if (!IsServer || !Enabled) return;
        try { RefreshStanding(); }
        catch (Exception e) { Debug.LogError("[Social] Standing update failed: " + e); featureEnabled.Value = false; service?.Dispose(); }
    }
    void DetachStanding()
    {
        if (standingSource == null) return;
        standingSource.OnPopularityChanged -= PopularityChanged;
        standingSource.OnFraternityChanged -= FraternityChanged;
        standingSource = null;
    }
    async void ResumeReceived()
    { try { await service.ResumeReceivedAsync(); } catch (Exception e) { Debug.LogError("[Social] Recovery: " + e); } }
    public void StartSocialPartyServer()
    { if (IsServer && Enabled) { localParty = World.EnsurePlannedParty(20); World.StartParty(localParty, Now); } }
    public void EndSocialPartyServer()
    {
        if (!IsServer || !Enabled) return;
        if (!string.IsNullOrEmpty(CurrentPartyId)) World.EndParty(CurrentPartyId);
        localParty = World.EnsurePlannedParty(20);
        UpdatePartyIdRpc(localParty);
    }
    [Rpc(SendTo.ClientsAndHost)] void UpdatePartyIdRpc(string id) { localParty = id; }
    public ISet<string> PlayersAtEntrance(Vector3 entrance)
    {
        var present = new HashSet<string>();
        foreach (var binding in connections)
            if (NetworkManager.ConnectedClients.TryGetValue(binding.Key, out var player) && player.PlayerObject != null &&
                Vector3.Distance(player.PlayerObject.transform.position, entrance) <= 4f) present.Add(binding.Value);
        return present;
    }
    public override void OnNetworkDespawn()
    {
        DetachStanding();
        featureEnabled.OnValueChanged -= FeatureChanged;
        NetworkManager.OnClientDisconnectCallback -= Disconnected;
        service?.Dispose(); connections.Clear(); localCredential = null;
        foreach (var source in cancellations.Values.ToArray()) source.Cancel();
        foreach (var completion in requests.Values) completion.TrySetException(new IOException("Social endpoint despawned; received speech remains saved"));
        requests.Clear(); delivered.Clear();
        if (Instance == this) Instance = null;
        base.OnNetworkDespawn();
    }
}
public sealed class UnitySocialCodec : IStateCodec
{
    // Unity's JSON utility supports serializable field DTOs; wrap primitives/arrays for prompt serialization.
    [Serializable] sealed class StringValue { public string value; }
    [Serializable] sealed class EventArray { public Commitment[] values; }
    public string Encode<T>(T value)
    {
        if (value is string text) return JsonUtility.ToJson(new StringValue { value = text });
        if (value is Commitment[] array) return JsonUtility.ToJson(new EventArray { values = array });
        return JsonUtility.ToJson(value);
    }
    public T Decode<T>(string json) => JsonUtility.FromJson<T>(json);
}
