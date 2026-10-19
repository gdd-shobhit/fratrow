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

/// <summary>
/// Host-owned social backend for every campus NPC (memory, relationships, RSVPs). Baked on GameManager.
/// The social world is always on; the local language model is an opt-in development provider, otherwise the deterministic
/// <see cref="HeuristicSocialModelProvider"/> answers. When <see cref="NetworkedCampaign"/> is running it owns the party
/// lifecycle and public standings; this component keeps identity binding, conversation RPCs and history.
/// </summary>
public sealed class NetworkedSocialConversation : NetworkBehaviour, IInteractionAccess
{
    public static NetworkedSocialConversation Instance { get; private set; }
    readonly NetworkVariable<bool> featureEnabled = new NetworkVariable<bool>(false);
    readonly NetworkVariable<FixedString64Bytes> campaignId = new NetworkVariable<FixedString64Bytes>();
    readonly Dictionary<ulong, string> connections = new Dictionary<ulong, string>();
    readonly Dictionary<string, string> displayNames = new Dictionary<string, string>();
    readonly Dictionary<string, TaskCompletionSource<ConversationResult>> requests = new Dictionary<string, TaskCompletionSource<ConversationResult>>();
    readonly Dictionary<string, CancellationTokenSource> cancellations = new Dictionary<string, CancellationTokenSource>();
    readonly HashSet<string> delivered = new HashSet<string>();
    readonly HashSet<string> campaignAuthorized = new HashSet<string>();
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
    /// <summary>Host: campaign id and its save directory (social journal + game save live here).</summary>
    public string CampaignId { get; private set; }
    public string CampaignDirectory { get; private set; }
    /// <summary>Host: human-readable provider in use (model path or heuristic).</summary>
    public string ProviderName { get; private set; } = "none";
    static bool CampaignDriven => NetworkedCampaign.Instance != null && NetworkedCampaign.Instance.IsServer && NetworkedCampaign.Instance.Session != null;
    public string CurrentPartyId => IsServer && World != null
        ? (CampaignDriven ? NetworkedCampaign.Instance.Session.State.socialPartyId :
            World.Store.Read().parties.LastOrDefault(p => p.state == "active" || p.state == "planned")?.partyId) : localParty;
    public event Action<ConversationResult> OnResult;
    public event Action<ConversationHistoryEntry[]> OnHistory;
    public event Action<PublicCommitment[]> OnCommitments;
    [Serializable] public sealed class OwnHistory { public ConversationHistoryEntry[] turns; }
    [Serializable] public sealed class PublicCommitments { public PublicCommitment[] entries; }

    /// <summary>True when the social backend owns this NPC's relationships (every registered campus NPC).</summary>
    public static bool Manages(string npcId) =>
        Instance != null && Instance.Enabled && Instance.World != null && !string.IsNullOrEmpty(npcId) && SocialWorld.Npc(Instance.CachedState, npcId) != null;

    CampaignState cachedState;
    float cacheAge;
    /// <summary>Host snapshot reused for cheap reads within ~0.25 s (Store.Read deep-copies the campaign).</summary>
    CampaignState CachedState
    {
        get
        {
            if (cachedState == null || Time.unscaledTime - cacheAge > 0.25f) { cachedState = World.Store.Read(); cacheAge = Time.unscaledTime; }
            return cachedState;
        }
    }

    public static string Option(string name, string fallback = null)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    /// <summary>Opt-in local model (development builds on Windows only; RunAnywhere/llama licensing not cleared for release).</summary>
    static bool ShouldUseModel()
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
        if (IsServer)
        {
            try
            {
                string id = Option("--social-campaign", "fratrow-main");
                if (id.Length > 48 || id.Any(c => !char.IsLetterOrDigit(c) && c != '-')) throw new ArgumentException("Invalid campaign name");
                CampaignId = id;
                CampaignDirectory = Path.Combine(Application.persistentDataPath, "SocialCampaigns", id);
                var store = new JournalSocialStore(CampaignDirectory, codec, () => SocialWorld.NewCampaign(id));
                if (store.Read().campaignId != id) throw new InvalidDataException("Campaign ID does not match its save directory");
                World = new SocialWorld(store);
                RegisterRoster();
                service = new SocialConversationService(World, codec, CreateProvider(), this,
                    TimeSpan.FromSeconds(double.Parse(Option("--social-timeout", "15"), System.Globalization.CultureInfo.InvariantCulture)), requireGroundedProposal: true);
                campaignId.Value = id;
                localParty = World.EnsurePlannedParty(20);
                featureEnabled.Value = true;
                Debug.Log("[Social] Host campaign opened: " + id + ". Provider: " + ProviderName + ".");
            }
            catch (Exception e) { Debug.LogError("[Social] Startup failed: " + e); featureEnabled.Value = false; }
        }
        if (Enabled) BeginBind();
    }

    /// <summary>Every roster NPC (hand-written + generated tier) gets a persona, voice and public standing.</summary>
    void RegisterRoster()
    {
        try
        {
            var roster = NetworkedCampaign.LoadRoster();
            foreach (var npc in roster.npcs)
            {
                World.EnsureNpc(npc.id, npc.persona, npc.voice);
                World.SetNpcStanding(npc.id, npc.popularity, FratRow.Game.CampusRoster.Concern(npc));
            }
        }
        catch (Exception e)
        {
            // Fallback to the authored ScriptableObject data when the roster file is missing.
            Debug.LogWarning("[Social] Roster unavailable (" + e.Message + "); registering NamedNpcDatabase entries.");
            foreach (var npc in NamedNpcDatabase.All)
            {
                string persona = string.IsNullOrWhiteSpace(npc.socialPersona) ? npc.displayName + " is a college student who weighs who is inviting them and how they are treated." : npc.socialPersona;
                World.EnsureNpc(npc.npcId, persona, npc.socialVoice ?? new NpcVoiceProfile());
            }
        }
    }

    ISocialModelProvider CreateProvider()
    {
        if (ShouldUseModel())
        {
            try
            {
                string path = Option("--social-model", Path.Combine(Application.persistentDataPath, "SocialModels", SocialModelCatalog.QwenFile));
#if UNITY_EDITOR
                path = UnityEditor.EditorPrefs.GetString("FratRow.Social.Model", path);
#endif
                string hash = SocialModelCatalog.HashFor(Path.GetFileName(path));
                string llamaServer = Option("--social-llama-server");
#if UNITY_EDITOR
                if (llamaServer == null) llamaServer = UnityEditor.EditorPrefs.GetString("FratRow.Social.LlamaServer", "");
#endif
                bool compact = Environment.GetCommandLineArgs().Contains("--social-compact-inference");
                if (string.IsNullOrEmpty(llamaServer))
                {
                    ProviderName = "RunAnywhere CPU (" + Path.GetFileName(path) + ")";
                    return new NativeSocialModelProvider(codec, path, hash) { CompactFormat = compact };
                }
                ProviderName = "llama.cpp CUDA (" + Path.GetFileName(path) + ")";
                return new LlamaServerModelProvider(codec, llamaServer, path, hash) { CompactFormat = compact };
            }
            catch (Exception e) { Debug.LogWarning("[Social] Model provider unavailable, using heuristic replies: " + e.Message); }
        }
        ProviderName = "heuristic (no model)";
        return new HeuristicSocialModelProvider(World.Store.Read, UnityEngine.Random.Range(1, int.MaxValue));
    }

    void FeatureChanged(bool previous, bool next) { if (next) BeginBind(); }
    void BeginBind()
    {
        if (!IsClient || binding || localCredential != null) return;
        binding = true;
        try
        {
            string json = File.Exists(CredentialPath) ? File.ReadAllText(CredentialPath) : "";
            string name = Option("--social-name");
            if (string.IsNullOrEmpty(name) && LobbyManager.Instance != null && LobbyManager.Instance.Players.TryGetValue(NetworkManager.LocalClientId, out var lobby))
                name = lobby.PlayerName.ToString();
            BindServerRpc(string.IsNullOrWhiteSpace(name) ? "Player" : name, json);
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
            displayNames[issued.playerId] = displayName;
            BindResultRpc(codec.Encode(issued), CurrentPartyId ?? "", "", RpcTarget.Single(sender, RpcTargetUse.Temp));
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

    /// <summary>Host: durable campaign player id bound to an NGO client.</summary>
    public bool TryGetPlayerId(ulong clientId, out string playerId, out string displayName)
    {
        displayName = null;
        if (!connections.TryGetValue(clientId, out playerId)) return false;
        displayNames.TryGetValue(playerId, out displayName);
        return true;
    }

    /// <summary>
    /// Host: submit a turn the campaign already validated (location/contact/phase) on the player's behalf.
    /// Pre-authorizes the request so Unity's 4 m proximity check does not second-guess the campaign's location rules.
    /// </summary>
    public async Task<ConversationResult> SubmitAsPlayerAsync(string playerId, ConversationRequest request, bool inPerson)
    {
        if (!IsServer || service == null) throw new InvalidOperationException("Social backend not running");
        string key = playerId + "/" + request.requestId;
        campaignAuthorized.Add(key);
        try { return await service.SubmitAsync(playerId, request); }
        finally { campaignAuthorized.Remove(key); }
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
        var snapshot = CachedState;
        string party = CurrentPartyId;
        return codec.Encode(new PublicCommitments { entries = snapshot.npcs.SelectMany(n => n.commitments
            .Where(c => c.partyId == party).Select(c => new PublicCommitment {
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
            if (!CampaignDriven) RefreshStanding();
            request = codec.Decode<ConversationRequest>(requestJson);
            // Speaker condition is host-observed (NetworkedCampaign); a client may not claim one.
            if (request != null) request.speakerCondition = SpeakerConditions.Sober;
            if (CampaignDriven) request.partyId = CurrentPartyId ?? "";
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
        // Free-text phone/command turns still feed the game loop (e.g. a mid-party RSVP schedules an arrival).
        if (CampaignDriven && request != null) NetworkedCampaign.Instance.Session.OnConversationCommitted(player, request.npcId, FratRow.Game.ActionIds.Talk, result);
        if (IsSpawned && connections.TryGetValue(sender, out string bound) && bound == player)
            ConversationResultRpc(codec.Encode(result), CurrentPartyId ?? "", RpcTarget.Single(sender, RpcTargetUse.Temp));
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
        if (!connections.Values.Contains(playerId) || SocialWorld.Npc(CachedState, request.npcId) == null) return false;
        if (campaignAuthorized.Contains(playerId + "/" + request.requestId)) return true;
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
                if (!CampaignDriven) RefreshStanding();
                // One-time import of the legacy shared affinity for every registered NPC.
                World.ImportLegacy(CachedState.npcs.ToDictionary(n => n.npcId, n => NetworkedContactRoster.Instance.GetAffinity(n.npcId)));
                restored = true; ResumeReceived();
            }
            partyPoll += Time.unscaledDeltaTime;
            if (partyPoll < 0.5f) return; partyPoll = 0;
            var state = World.Store.Read();
            cachedState = state; cacheAge = Time.unscaledTime;
            // Phone affinity shows each NPC's regard for the players' house (read projection; never written back).
            foreach (var npc in state.npcs) NetworkedContactRoster.Instance?.ProjectSocialRegardServer(npc.npcId, npc.fraternityRegard);
            string publicCommitments = EncodePublicCommitments();
            if (publicCommitments != lastPublicCommitments && connections.Count > 0)
            {
                lastPublicCommitments = publicCommitments;
                CommitmentsResultRpc(publicCommitments, RpcTarget.Group(connections.Keys.ToArray(), RpcTargetUse.Temp));
            }
            if (CampaignDriven) return; // the campaign owns parties and standings
            RefreshStanding();
            var manager = NetworkedPartyManager.Instance;
            if (manager == null || manager.NetworkedParty == null) return;
            var active = state.parties.Find(p => p.state == "active");
            if (active != null && !manager.IsPartyActive)
            {
                double remaining = manager.PartyDurationMinutes * 60 - (Now - active.startedAt);
                if (remaining > 0) manager.ResumeSocialPartyServer((float)remaining);
                else EndSocialPartyServer();
            }
            if (manager.IsPartyActive) manager.PollSocialAttendanceServer(this);
        }
        catch (Exception e) { Debug.LogError("[Social] Host state error: " + e); }
    }
    void RefreshStanding()
    {
        if (!IsServer || World == null || CampaignDriven) return;
        // Legacy (no campaign): read authoritative Unity state on the main thread; inference receives only saved DTOs.
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
            World.SetFraternityStanding(Mathf.Clamp(source.TotalPopularity, 0, 100), name.Substring(0, Math.Min(name.Length, 64)),
                hasGrade ? FraternityGradeCalculator.ToLetter(fraternity.grade) : "", hasGrade ? fraternity.rating : 0);
        }
    }
    void PopularityChanged(float value) => StandingChanged();
    void FraternityChanged(FraternityData value) => StandingChanged();
    void StandingChanged()
    {
        if (!IsServer || !Enabled || CampaignDriven) return;
        try { RefreshStanding(); }
        catch (Exception e) { Debug.LogError("[Social] Standing update failed: " + e); }
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
    /// <summary>Legacy party path (no campaign). The campaign drives the social party lifecycle itself.</summary>
    public void StartSocialPartyServer()
    { if (IsServer && Enabled && !CampaignDriven) { localParty = World.EnsurePlannedParty(20); World.StartParty(localParty, Now); } }
    public void EndSocialPartyServer()
    {
        if (!IsServer || !Enabled || CampaignDriven) return;
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
