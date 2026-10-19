using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FratRow.Game;
using FratRow.Social;
using Systems.Campus;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Replicated whole-campaign HUD snapshot. Reliable NetworkVariable written by the host at most 4 Hz and only when changed.
/// <see cref="Version"/> lets older clients ignore fields they do not understand (network-code rule: versioned messages).
/// </summary>
public struct CampaignHudState : INetworkSerializable, IEquatable<CampaignHudState>
{
    /// <summary>Campaign replication schema. 2: <see cref="BrotherNetState"/> carries Drunk / DrunkTier.</summary>
    public const byte CurrentVersion = 2;
    public byte Version;
    public byte Phase, Day, PoliceAlert, ReadyCount, PlayerCount;
    public short Semester, Week, SemesterWeeks;
    /// <summary>Clock at <see cref="AnchorServerTime"/>; clients add (serverTime - anchor) × HoursPerSecond.</summary>
    public float ClockHour, HoursPerSecond, PhaseRemaining;
    public double AnchorServerTime;
    public bool PartyActive, Raided, CardCheck, PoliceDispatched;
    public short Guests, Capacity, UnderageDrinkers, OpenIncidents, Cover;
    public float Fun, Hype, Noise, Trouble, Attention;
    public int CashCollected;
    public FixedString32Bytes Theme;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Version); s.SerializeValue(ref Phase); s.SerializeValue(ref Day); s.SerializeValue(ref PoliceAlert);
        s.SerializeValue(ref ReadyCount); s.SerializeValue(ref PlayerCount); s.SerializeValue(ref Semester); s.SerializeValue(ref Week);
        s.SerializeValue(ref SemesterWeeks); s.SerializeValue(ref ClockHour); s.SerializeValue(ref HoursPerSecond); s.SerializeValue(ref PhaseRemaining);
        s.SerializeValue(ref AnchorServerTime); s.SerializeValue(ref PartyActive); s.SerializeValue(ref Raided); s.SerializeValue(ref CardCheck);
        s.SerializeValue(ref PoliceDispatched); s.SerializeValue(ref Guests); s.SerializeValue(ref Capacity); s.SerializeValue(ref UnderageDrinkers);
        s.SerializeValue(ref OpenIncidents); s.SerializeValue(ref Cover); s.SerializeValue(ref Fun); s.SerializeValue(ref Hype); s.SerializeValue(ref Noise);
        s.SerializeValue(ref Trouble); s.SerializeValue(ref Attention); s.SerializeValue(ref CashCollected); s.SerializeValue(ref Theme);
    }

    /// <summary>Equality ignores sub-1% float jitter so the variable is not re-sent every poll.</summary>
    public bool Equals(CampaignHudState o) =>
        Version == o.Version && Phase == o.Phase && Day == o.Day && PoliceAlert == o.PoliceAlert && ReadyCount == o.ReadyCount && PlayerCount == o.PlayerCount &&
        Semester == o.Semester && Week == o.Week && SemesterWeeks == o.SemesterWeeks && Math.Abs(ClockHour - o.ClockHour) < 0.25f &&
        HoursPerSecond == o.HoursPerSecond && Math.Abs(PhaseRemaining - o.PhaseRemaining) < 1f && PartyActive == o.PartyActive && Raided == o.Raided &&
        CardCheck == o.CardCheck && PoliceDispatched == o.PoliceDispatched && Guests == o.Guests && Capacity == o.Capacity && UnderageDrinkers == o.UnderageDrinkers &&
        OpenIncidents == o.OpenIncidents && Cover == o.Cover && Math.Abs(Fun - o.Fun) < 0.5f && Math.Abs(Hype - o.Hype) < 0.5f && Math.Abs(Noise - o.Noise) < 0.5f &&
        Math.Abs(Trouble - o.Trouble) < 0.5f && Math.Abs(Attention - o.Attention) < 0.5f && CashCollected == o.CashCollected && Theme.Equals(o.Theme);
}

/// <summary>One fraternity's public standing (players' house and rivals).</summary>
public struct HouseNetState : INetworkSerializable, IEquatable<HouseNetState>
{
    public FixedString32Bytes Id;
    public FixedString64Bytes Name;
    public FixedString32Bytes Nickname;
    public bool IsPlayer, Probation, Suspended, Trashed, Guarded;
    public float Popularity, Heat, Dean, Gpa;
    public int Cash;
    public short Members, CupPoints, Rank, Rating, LastRank;
    public FixedString32Bytes Grade;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Id); s.SerializeValue(ref Name); s.SerializeValue(ref Nickname); s.SerializeValue(ref IsPlayer); s.SerializeValue(ref Probation);
        s.SerializeValue(ref Suspended); s.SerializeValue(ref Trashed); s.SerializeValue(ref Guarded); s.SerializeValue(ref Popularity); s.SerializeValue(ref Heat);
        s.SerializeValue(ref Dean); s.SerializeValue(ref Gpa); s.SerializeValue(ref Cash); s.SerializeValue(ref Members); s.SerializeValue(ref CupPoints);
        s.SerializeValue(ref Rank); s.SerializeValue(ref Rating); s.SerializeValue(ref LastRank); s.SerializeValue(ref Grade);
    }

    public bool Equals(HouseNetState o) =>
        Id.Equals(o.Id) && Name.Equals(o.Name) && Nickname.Equals(o.Nickname) && IsPlayer == o.IsPlayer && Probation == o.Probation && Suspended == o.Suspended &&
        Trashed == o.Trashed && Guarded == o.Guarded && Math.Abs(Popularity - o.Popularity) < 0.05f && Math.Abs(Heat - o.Heat) < 0.5f && Math.Abs(Dean - o.Dean) < 0.5f &&
        Math.Abs(Gpa - o.Gpa) < 0.005f && Cash == o.Cash && Members == o.Members && CupPoints == o.CupPoints && Rank == o.Rank && Rating == o.Rating &&
        LastRank == o.LastRank && Grade.Equals(o.Grade);
}

/// <summary>A brother (player) as other clients see them.</summary>
public struct BrotherNetState : INetworkSerializable, IEquatable<BrotherNetState>
{
    public ulong ClientId;
    public FixedString64Bytes PlayerId, Name;
    public FixedString32Bytes Role, Location;
    public bool Ready, Connected;
    public float DetainedRemaining, Rep;
    public short Record, StudyThisWeek;
    /// <summary>Intoxication as a 0..1 share of the drunk cap (host-authoritative; design/gdd/drunk-mechanic.md).</summary>
    public float Drunk;
    /// <summary>Visible condition: 0 sober, 1 tipsy, 2 drunk, 3 wasted (<see cref="GameSession.ConditionTier"/>).</summary>
    public byte DrunkTier;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref ClientId); s.SerializeValue(ref PlayerId); s.SerializeValue(ref Name); s.SerializeValue(ref Role); s.SerializeValue(ref Location);
        s.SerializeValue(ref Ready); s.SerializeValue(ref Connected); s.SerializeValue(ref DetainedRemaining); s.SerializeValue(ref Rep);
        s.SerializeValue(ref Record); s.SerializeValue(ref StudyThisWeek); s.SerializeValue(ref Drunk); s.SerializeValue(ref DrunkTier);
    }

    /// <summary>Drunk sobers continuously; the 1% tolerance re-sends it about every 10 s per drunk brother, not every poll.</summary>
    public bool Equals(BrotherNetState o) =>
        ClientId == o.ClientId && PlayerId.Equals(o.PlayerId) && Name.Equals(o.Name) && Role.Equals(o.Role) && Location.Equals(o.Location) && Ready == o.Ready &&
        Connected == o.Connected && Math.Abs(DetainedRemaining - o.DetainedRemaining) < 1f && Math.Abs(Rep - o.Rep) < 0.05f && Record == o.Record && StudyThisWeek == o.StudyThisWeek &&
        Math.Abs(Drunk - o.Drunk) < 0.01f && DrunkTier == o.DrunkTier;
}

/// <summary>Police unit between two campus locations (clients interpolate positions).</summary>
public struct PatrolNetState : INetworkSerializable, IEquatable<PatrolNetState>
{
    public short Id;
    public FixedString32Bytes From, To;
    public float Progress, TravelSeconds;
    /// <summary>0 patrol, 1 respond, 2 parked.</summary>
    public byte Mode;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Id); s.SerializeValue(ref From); s.SerializeValue(ref To); s.SerializeValue(ref Progress); s.SerializeValue(ref TravelSeconds); s.SerializeValue(ref Mode);
    }

    public bool Equals(PatrolNetState o) => Id == o.Id && From.Equals(o.From) && To.Equals(o.To) && Math.Abs(Progress - o.Progress) < 0.1f && Mode == o.Mode && TravelSeconds == o.TravelSeconds;
}

public struct StashNetState : INetworkSerializable, IEquatable<StashNetState>
{
    public FixedString32Bytes ItemId;
    public short Count;
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref ItemId); s.SerializeValue(ref Count); }
    public bool Equals(StashNetState o) => ItemId.Equals(o.ItemId) && Count == o.Count;
}

public struct IncidentNetState : INetworkSerializable, IEquatable<IncidentNetState>
{
    public short Id, GuestId, OpenedTick;
    public FixedString32Bytes Kind;
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref Id); s.SerializeValue(ref GuestId); s.SerializeValue(ref OpenedTick); s.SerializeValue(ref Kind); }
    public bool Equals(IncidentNetState o) => Id == o.Id && GuestId == o.GuestId && OpenedTick == o.OpenedTick && Kind.Equals(o.Kind);
}

/// <summary>News feed line (house-wide; every player is a brother of the same house).</summary>
public struct NewsNet : INetworkSerializable
{
    public long Seq;
    public FixedString32Bytes Kind;
    public FixedString512Bytes Text;
    public bool IsPublic;
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref Seq); s.SerializeValue(ref Kind); s.SerializeValue(ref Text); s.SerializeValue(ref IsPublic); }
}

/// <summary>Result of the local player's last action (targeted RPC).</summary>
public struct ActionResultNet : INetworkSerializable
{
    public bool Ok;
    public FixedString32Bytes Action, Code;
    public FixedString512Bytes Message;
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref Ok); s.SerializeValue(ref Action); s.SerializeValue(ref Code); s.SerializeValue(ref Message); }
}

/// <summary>
/// Host adapter for the Unity-free game loop (<see cref="GameSession"/>, design/gdd/core-game-loop.md). Baked on GameManager.
/// Server: owns the session, maps NGO clients to durable player IDs (via <see cref="NetworkedSocialConversation"/>), resolves
/// player positions to campus locations, ticks the loop, applies core events to Unity systems, replicates HUD state.
/// Clients: read the replicated state and call <see cref="RequestAction"/> / <see cref="RequestTalk"/>.
/// Example (client UI): <c>NetworkedCampaign.Instance.RequestAction(ActionIds.Buy, item: "keg", amount: 2);</c>
/// </summary>
public sealed class NetworkedCampaign : NetworkBehaviour
{
    public static NetworkedCampaign Instance { get; private set; }

    [Header("Host settings")]
    [Tooltip("Players within this distance of a CampusLocation are 'at' it (metres).")]
    [SerializeField] float locationRadius = 14f;
    [Tooltip("Players within this distance of the party entrance count as 'at the door' (existing 4 m social rule).")]
    [SerializeField] float entranceRadius = 4f;
    [SerializeField] float replicateHz = 4f;
    [Tooltip("Max actions per client per second (excess are rejected).")]
    [SerializeField] int actionsPerSecond = 8;

    readonly NetworkVariable<CampaignHudState> hud = new NetworkVariable<CampaignHudState>();
    NetworkList<HouseNetState> houses;
    NetworkList<BrotherNetState> brothers;
    NetworkList<PatrolNetState> patrols;
    NetworkList<StashNetState> stash;
    NetworkList<IncidentNetState> incidents;

    /// <summary>Host only: the authoritative loop (null on clients or before the social backend is ready).</summary>
    public GameSession Session { get; private set; }
    public bool IsCampaignActive => IsServer ? Session != null : hud.Value.Version != 0;
    public CampaignHudState Hud => hud.Value;
    public NetworkList<HouseNetState> Houses => houses;
    public NetworkList<BrotherNetState> Brothers => brothers;
    public NetworkList<PatrolNetState> Patrols => patrols;
    public NetworkList<StashNetState> Stash => stash;
    public NetworkList<IncidentNetState> Incidents => incidents;
    public readonly List<NewsNet> RecentNews = new List<NewsNet>();

    public event Action<CampaignHudState> OnHudChanged;
    public event Action OnHousesChanged, OnBrothersChanged, OnPatrolsChanged, OnStashChanged, OnIncidentsChanged;
    public event Action<NewsNet> OnNews;
    public event Action<ActionResultNet> OnActionResult;
    /// <summary>NPC reply to the local player's talk/invite/rush (npcId, result).</summary>
    public event Action<string, ConversationResult> OnConversation;
    /// <summary>
    /// What the local player actually said to an NPC, sent as soon as the host accepts the turn (npcId, spoken line, speaker
    /// condition). When drunk this is the host's rewrite of what they typed; the NPC hears exactly this line.
    /// </summary>
    public event Action<string, string, string> OnSpeech;

    readonly List<GameEvent> drained = new List<GameEvent>();
    readonly Dictionary<ulong, string> clientPlayers = new Dictionary<ulong, string>();
    readonly Dictionary<ulong, (double second, int count)> rate = new Dictionary<ulong, (double, int)>();
    readonly UnitySocialCodec codec = new UnitySocialCodec();
    GameSaveStore saveStore;
    float replicateTimer, autosaveTimer;
    int lastDay = -1;
    Phase lastPhase = (Phase)255;

    void Awake()
    {
        Instance = this;
        houses = new NetworkList<HouseNetState>();
        brothers = new NetworkList<BrotherNetState>();
        patrols = new NetworkList<PatrolNetState>();
        stash = new NetworkList<StashNetState>();
        incidents = new NetworkList<IncidentNetState>();
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        hud.OnValueChanged += HudChanged;
        houses.OnListChanged += _ => OnHousesChanged?.Invoke();
        brothers.OnListChanged += _ => OnBrothersChanged?.Invoke();
        patrols.OnListChanged += _ => OnPatrolsChanged?.Invoke();
        stash.OnListChanged += _ => OnStashChanged?.Invoke();
        incidents.OnListChanged += _ => OnIncidentsChanged?.Invoke();
        if (IsServer) NetworkManager.OnClientDisconnectCallback += ClientDisconnected;
        if (!IsServer && GetComponent<PolicePatrolVisuals>() == null) gameObject.AddComponent<PolicePatrolVisuals>();
        if (IsServer && GetComponent<PolicePatrolVisuals>() == null) gameObject.AddComponent<PolicePatrolVisuals>();
    }

    public override void OnNetworkDespawn()
    {
        hud.OnValueChanged -= HudChanged;
        if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= ClientDisconnected;
        if (IsServer && Session != null) Save();
        if (Instance == this) Instance = null;
        base.OnNetworkDespawn();
    }

    void HudChanged(CampaignHudState previous, CampaignHudState next) => OnHudChanged?.Invoke(next);

    /// <summary>Client-side in-game clock, interpolated from the last host anchor.</summary>
    public float CurrentClockHour
    {
        get
        {
            var h = hud.Value;
            if (NetworkManager == null) return h.ClockHour;
            float hour = h.ClockHour + (float)(NetworkManager.ServerTime.Time - h.AnchorServerTime) * h.HoursPerSecond;
            return ((hour % 24f) + 24f) % 24f;
        }
    }

    public static string DayName(int day) => GameSession.DayName(day);

    // ---------------- Host: startup ----------------

    void Update()
    {
        if (!IsServer) return;
        // GameManager spawns with the host in the main menu and persists through the lobby. The semester only starts
        // (and the clock only runs) once LobbyManager has loaded the campus scene for everyone.
        if (!CampusScene.IsActive) return;
        if (Session == null) { TryStartCampaign(); return; }
        try
        {
            BindPlayers();
            ResolvePlayerLocations();
            Session.Tick(Time.deltaTime);
            ApplyEvents();
            replicateTimer += Time.unscaledDeltaTime;
            if (replicateTimer >= 1f / Mathf.Max(1f, replicateHz)) { replicateTimer = 0; Replicate(); }
            autosaveTimer += Time.unscaledDeltaTime;
            if (autosaveTimer >= Session.Config.autosaveSeconds) { autosaveTimer = 0; Save(); }
        }
        catch (Exception e)
        {
            // Never let one bad frame kill the session; the next frame retries from saved state in memory.
            Debug.LogError("[Campaign] Host tick failed: " + e);
        }
    }

    void TryStartCampaign()
    {
        var social = NetworkedSocialConversation.Instance;
        if (social == null || social.World == null || !social.IsSpawned) return;
        try
        {
            var config = LoadConfig();
            ApplyGuestArchetypes(config);
            var roster = LoadRoster();
            saveStore = new GameSaveStore(Path.Combine(social.CampaignDirectory, "game"), codec);
            var state = saveStore.Load() ?? GameSession.NewCampaign(config, roster, social.CampaignId, UnityEngine.Random.Range(1, int.MaxValue));
            Session = new GameSession(config, roster, state, new SocialWorldBridge(social.World));
            if (NetworkedGameManager.Instance != null && !string.IsNullOrWhiteSpace(NetworkedGameManager.Instance.FratName) && state.history.Count == 0)
                Session.PlayerHouse.name = NetworkedGameManager.Instance.FratName;
            foreach (var npc in Session.State.npcs)
                if (NetworkedContactRoster.Instance != null && NetworkedContactRoster.Instance.IsUnlocked(npc.npcId)) npc.contactUnlocked = true;
            Debug.Log("[Campaign] Started " + social.CampaignId + ": semester " + state.semester + " week " + (state.week + 1) + " " + state.phase +
                ", " + roster.npcs.Count + " NPCs, provider " + social.ProviderName + ".");
            RestorePartyBodies();
            Save();
            Replicate();
        }
        catch (Exception e)
        {
            Debug.LogError("[Campaign] Startup failed (falling back to legacy loop): " + e);
            enabled = false;
        }
    }

    /// <summary>Host reload mid-party (design §5): reopen the NetworkedParty and respawn bodies for guests still present.</summary>
    void RestorePartyBodies()
    {
        var party = Session.State.party;
        var manager = NetworkedPartyManager.Instance;
        if (Session.State.phase != Phase.Party || !party.active || manager == null) return;
        manager.BeginCampaignPartyServer(party.capacity, (float)Session.PhaseRemainingSeconds, Session.State.week + 1);
        foreach (var guest in Session.PresentGuests)
            manager.SpawnCampaignGuest(guest.guestId, (GuestClass)Mathf.Clamp(guest.archetype, 0, (int)GuestClass.Count), guest.npcId, party.socialPartyId);
    }

    /// <summary>Resources/Campus/GameLoopConfig.json, or code defaults.</summary>
    GameLoopConfig LoadConfig()
    {
        var asset = Resources.Load<TextAsset>("Campus/GameLoopConfig");
        return asset != null ? GameLoopConfig.Parse(asset.text, codec) : GameLoopConfig.CreateDefault();
    }

    public static CampusRoster LoadRoster()
    {
        var codec = new UnitySocialCodec();
        var asset = Resources.Load<TextAsset>("Campus/CampusRoster");
        if (asset == null) throw new FileNotFoundException("Resources/Campus/CampusRoster.json is missing");
        var roster = CampusRoster.Parse(asset.text, codec);
        var archetypes = Resources.Load<TextAsset>("Campus/PersonalityArchetypes");
        if (archetypes != null) roster.Expand(ArchetypeCatalog.Parse(archetypes.text, codec));
        return roster;
    }

    /// <summary>Filler archetypes mirror the GuestSO assets wired into GameManager (designers tune them there).</summary>
    static void ApplyGuestArchetypes(GameLoopConfig config)
    {
        var gm = NetworkedGameManager.Instance;
        if (gm == null || gm.guestClasses == null || gm.guestClasses.Count == 0) return;
        config.archetypes = gm.guestClasses.Where(g => g != null).Select(g => new GuestArchetypeDef
        {
            guestClass = (int)g.guestClass, name = g.guestClass.ToString(), popularity = g.popularity, trouble = g.troubleMaker, cash = g.cashvalue,
            baseArrivalChance = g.baseArrivalChance, popularityModifier = g.popularityModifier
        }).ToList();
    }

    void Save()
    {
        try { saveStore?.Save(Session.State); }
        catch (Exception e) { Debug.LogWarning("[Campaign] Save failed: " + e.Message); }
    }

    // ---------------- Host: players ----------------

    void BindPlayers()
    {
        var social = NetworkedSocialConversation.Instance;
        if (social == null) return;
        foreach (ulong client in NetworkManager.ConnectedClientsIds)
        {
            if (clientPlayers.ContainsKey(client) || !social.TryGetPlayerId(client, out string playerId, out string displayName)) continue;
            clientPlayers[client] = playerId;
            string lobbyClass = "";
            string name = displayName;
            if (LobbyManager.Instance != null && LobbyManager.Instance.Players.TryGetValue(client, out var lobby))
            {
                lobbyClass = lobby.SelectedClass.ToString();
                if (!string.IsNullOrWhiteSpace(lobby.PlayerName.ToString())) name = lobby.PlayerName.ToString();
            }
            Session.AddOrUpdatePlayer(playerId, name, lobbyClass);
        }
    }

    void ClientDisconnected(ulong client)
    {
        if (clientPlayers.TryGetValue(client, out string playerId))
        {
            Session?.SetPlayerConnected(playerId, false);
            clientPlayers.Remove(client);
        }
        rate.Remove(client);
    }

    void ResolvePlayerLocations()
    {
        var registry = CampusLocationRegistry.Instance;
        Vector3 entrance = NetworkedPartyManager.Instance != null ? NetworkedPartyManager.Instance.EntrancePosition : Vector3.positiveInfinity;
        foreach (var pair in clientPlayers)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(pair.Key, out var client) || client.PlayerObject == null) continue;
            Vector3 position = client.PlayerObject.transform.position;
            string best = "";
            float bestDistance = locationRadius;
            if (registry != null)
                foreach (var loc in registry.All)
                {
                    float d = Vector3.Distance(position, loc.Position);
                    if (d <= bestDistance) { bestDistance = d; best = loc.LocationId; }
                }
            bool atEntrance = Vector3.Distance(position, entrance) <= entranceRadius;
            if (atEntrance && best == "") best = Session.Config.playerHomeLocation;
            Session.SetPlayerLocation(pair.Value, best, atEntrance);
        }
    }

    /// <summary>Host: the client's durable campaign player id (null until the social backend binds them).</summary>
    public string PlayerIdFor(ulong clientId) => clientPlayers.TryGetValue(clientId, out var id) ? id : null;

    /// <summary>Replicated "no client" sentinel for brothers who are offline (0 is the host's client id).</summary>
    public const ulong NoClient = ulong.MaxValue;

    ulong ClientFor(string playerId)
    {
        foreach (var pair in clientPlayers) if (pair.Value == playerId) return pair.Key;
        return NoClient;
    }

    // ---------------- Host: events → Unity ----------------

    void ApplyEvents()
    {
        drained.Clear();
        Session.DrainEvents(drained);
        var party = NetworkedPartyManager.Instance;
        foreach (var e in drained)
        {
            switch (e.kind)
            {
                case GameEventKind.GuestArrived:
                    party?.SpawnCampaignGuest(e.guestId, (GuestClass)Mathf.Clamp(e.archetype, 0, (int)GuestClass.Count), e.npcId, Session.State.party.socialPartyId);
                    break;
                case GameEventKind.GuestLeft:
                    party?.DespawnCampaignGuest(e.guestId);
                    break;
                case GameEventKind.PartyStarted:
                    party?.BeginCampaignPartyServer(Session.State.party.capacity, Session.Config.partySeconds, Session.State.week + 1);
                    break;
                case GameEventKind.PartyEnded:
                    party?.EndCampaignPartyServer();
                    break;
                case GameEventKind.MemberJoined:
                    if (e.flag) NetworkedContactRoster.Instance?.SetRecruitedServer(e.npcId, true);
                    break;
                case GameEventKind.HouseChanged when e.text == "contact_unlocked":
                    NetworkedContactRoster.Instance?.SetUnlockedServer(e.npcId, true);
                    break;
                case GameEventKind.News:
                    var item = Session.State.news.LastOrDefault(n => n.text == e.text);
                    BroadcastNews(new NewsNet { Seq = item?.seq ?? 0, Kind = Fixed32(item?.kind ?? "news"), Text = Fixed512(e.text), IsPublic = e.flag });
                    break;
            }
        }
        var state = Session.State;
        if (state.phase != lastPhase || state.day != lastDay)
        {
            lastPhase = state.phase;
            lastDay = state.day;
            Save();
        }
        NetworkedCampusCalendar.Instance?.ApplyCampaignServer((GamePhase)state.phase, state.day, state.week, Session.ClockHour, Session.HoursPerSecond);
        MirrorLegacyState();
    }

    /// <summary>Keeps existing systems (HUD bindings, social standing, phone) consistent with the authoritative core.</summary>
    void MirrorLegacyState()
    {
        var h = Session.PlayerHouse;
        NetworkedGameManager.Instance?.ApplyCampaignServer(h.name, h.popularity, h.cash, h.heat, h.rating, h.grade, Session.TotalMembers(h));
        var p = Session.State.party;
        NetworkedPartyManager.Instance?.MirrorCampaignPartyServer(p.active, p.fun, p.trouble, p.cashCollected, p.capacity);
    }

    void BroadcastNews(NewsNet item)
    {
        NewsClientRpc(item);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void NewsClientRpc(NewsNet item)
    {
        RecentNews.Add(item);
        while (RecentNews.Count > 40) RecentNews.RemoveAt(0);
        OnNews?.Invoke(item);
    }

    // ---------------- Host: replication ----------------

    void Replicate()
    {
        var s = Session.State;
        var p = s.party;
        var readyMask = NetworkedWeekendReady.Instance;
        var next = new CampaignHudState
        {
            Version = CampaignHudState.CurrentVersion, Phase = (byte)s.phase, Day = (byte)s.day, Semester = (short)s.semester, Week = (short)s.week,
            SemesterWeeks = (short)Session.Config.semesterWeeks, ClockHour = Session.ClockHour, HoursPerSecond = Session.HoursPerSecond,
            PhaseRemaining = (float)Session.PhaseRemainingSeconds, AnchorServerTime = NetworkManager.ServerTime.Time,
            PoliceAlert = (byte)Session.PoliceAlertLevel, ReadyCount = (byte)(readyMask != null ? readyMask.ReadyCount : s.players.Count(x => x.ready)),
            PlayerCount = (byte)Session.ConnectedPlayers.Count(), PartyActive = p.active, Raided = p.raided, CardCheck = p.cardCheck || p.cardCheckCoverage > 0,
            PoliceDispatched = p.dispatchedPatrol >= 0, Guests = (short)Session.PresentGuests.Count(), Capacity = (short)p.capacity,
            UnderageDrinkers = (short)p.underageDrinkers, OpenIncidents = (short)Session.OpenIncidents.Count(), Cover = (short)Session.PlayerHouse.cover,
            Fun = p.fun, Hype = p.hype, Noise = p.noise, Trouble = p.trouble, Attention = p.attention, CashCollected = p.cashCollected,
            Theme = Fixed32(Session.Config.Theme(Session.PlayerHouse.theme)?.name ?? "")
        };
        if (!hud.Value.Equals(next)) hud.Value = next;

        var ranked = Session.Ranked();
        Sync(houses, s.houses.Select(h => new HouseNetState
        {
            Id = Fixed32(h.id), Name = Fixed64(h.name), Nickname = Fixed32(h.nickname), IsPlayer = h.isPlayer, Probation = h.probation, Suspended = h.suspended,
            Trashed = h.trashed, Guarded = h.guarded, Popularity = h.popularity, Heat = h.heat, Dean = h.dean, Gpa = h.gpa, Cash = h.cash,
            Members = (short)Session.TotalMembers(h), CupPoints = (short)h.cupPoints, Rank = (short)(ranked.IndexOf(h) + 1), Rating = (short)h.rating,
            LastRank = (short)h.lastRank, Grade = Fixed32(h.grade)
        }).ToList());
        Sync(brothers, s.players.Select(pl => new BrotherNetState
        {
            ClientId = ClientFor(pl.playerId), PlayerId = Fixed64(pl.playerId), Name = Fixed64(pl.displayName),
            Role = Fixed32(pl.role), Location = Fixed32(pl.location), Ready = pl.ready, Connected = pl.connected,
            DetainedRemaining = (float)Math.Max(0, pl.detainedUntil - s.time), Rep = pl.rep, Record = (short)pl.record, StudyThisWeek = (short)pl.studyThisWeek,
            Drunk = Session.Drunk01(pl), DrunkTier = GameSession.ConditionTier(Session.DrunkCondition(pl))
        }).ToList());
        Sync(patrols, s.patrols.Select(x => new PatrolNetState
        {
            Id = (short)x.id, From = Fixed32(x.from), To = Fixed32(x.to), Progress = x.progress, TravelSeconds = x.travelSeconds,
            Mode = (byte)(x.mode == "respond" ? 1 : x.mode == "parked" ? 2 : 0)
        }).ToList());
        Sync(stash, Session.PlayerHouse.stash.Select(x => new StashNetState { ItemId = Fixed32(x.itemId), Count = (short)x.count }).ToList());
        Sync(incidents, Session.OpenIncidents.Select(x => new IncidentNetState { Id = (short)x.id, GuestId = (short)x.guestId, OpenedTick = (short)x.openedTick, Kind = Fixed32(x.kind) }).ToList());
    }

    static void Sync<T>(NetworkList<T> list, List<T> next) where T : unmanaged, IEquatable<T>
    {
        while (list.Count > next.Count) list.RemoveAt(list.Count - 1);
        for (int i = 0; i < next.Count; i++)
        {
            if (i < list.Count) { if (!list[i].Equals(next[i])) list[i] = next[i]; }
            else list.Add(next[i]);
        }
    }

    static FixedString32Bytes Fixed32(string s) { var f = new FixedString32Bytes(); if (!string.IsNullOrEmpty(s)) f.CopyFromTruncated(s); return f; }
    static FixedString64Bytes Fixed64(string s) { var f = new FixedString64Bytes(); if (!string.IsNullOrEmpty(s)) f.CopyFromTruncated(s); return f; }
    static FixedString512Bytes Fixed512(string s) { var f = new FixedString512Bytes(); if (!string.IsNullOrEmpty(s)) f.CopyFromTruncated(s); return f; }

    // ---------------- Actions (client → host) ----------------

    /// <summary>Client: request any game action (see <see cref="ActionIds"/>). The result arrives via <see cref="OnActionResult"/>.</summary>
    public void RequestAction(string action, string target = "", string item = "", int amount = 0, string rival = "")
    {
        if (!IsSpawned) return;
        RequestActionServerRpc(Fixed32(action), Fixed64(target), Fixed32(item), amount, Fixed32(rival));
    }

    /// <summary>Client: say something to an NPC (free text, ≤ 800 chars). Reply arrives via <see cref="OnConversation"/>.</summary>
    public void RequestTalk(string npcId, string text)
    {
        if (!IsSpawned || string.IsNullOrWhiteSpace(text) || text.Length > SocialRules.InputCharacters) return;
        RequestTalkServerRpc(Fixed64(npcId), text);
    }

    bool Throttled(ulong client)
    {
        double now = Math.Floor(Time.unscaledTimeAsDouble);
        if (!rate.TryGetValue(client, out var r) || r.second != now) r = (now, 0);
        r.count++;
        rate[client] = r;
        return r.count > actionsPerSecond;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void RequestActionServerRpc(FixedString32Bytes action, FixedString64Bytes target, FixedString32Bytes item, int amount, FixedString32Bytes rival, RpcParams rpc = default)
    {
        ulong sender = rpc.Receive.SenderClientId;
        if (Session == null || Throttled(sender)) return;
        string playerId = PlayerIdFor(sender);
        string actionId = action.ToString();
        if (playerId == null) { Reply(sender, actionId, ActionResult.Fail("unknown_player", "Still connecting to the house...")); return; }
        if (actionId == ActionIds.Talk || actionId == ActionIds.Invite || actionId == ActionIds.RushTalk)
        {
            _ = ConversationAsync(sender, playerId, actionId, target.ToString(), "");
            return;
        }
        if (actionId == ActionIds.Ready) NetworkedWeekendReady.Instance?.SetReadyFromCampaignServer(sender, amount != 0);
        var result = Session.Apply(new PlayerAction
        {
            playerId = playerId, action = actionId, target = target.ToString(), item = item.ToString(), amount = Mathf.Clamp(amount, -1000, 1000), rival = rival.ToString()
        });
        Reply(sender, actionId, result);
        ApplyEvents();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void RequestTalkServerRpc(FixedString64Bytes npcId, string text, RpcParams rpc = default)
    {
        ulong sender = rpc.Receive.SenderClientId;
        if (Session == null || text == null || text.Length > SocialRules.InputCharacters || Throttled(sender)) return;
        string playerId = PlayerIdFor(sender);
        if (playerId == null) { Reply(sender, ActionIds.Talk, ActionResult.Fail("unknown_player", "Still connecting...")); return; }
        _ = ConversationAsync(sender, playerId, ActionIds.Talk, npcId.ToString(), text);
    }

    /// <summary>Validates through the core, submits to the social service as the player, applies mechanical effects, replies.</summary>
    async Task ConversationAsync(ulong sender, string playerId, string action, string npcId, string text)
    {
        var start = Session.Apply(new PlayerAction { playerId = playerId, action = action, target = npcId, text = text });
        if (!start.ok) { Reply(sender, action, start); return; }
        Reply(sender, action, start);
        // The rewrite happened in the core on the host; the speaker sees what actually came out before the NPC answers.
        if (NetworkManager.ConnectedClients.ContainsKey(sender))
            SpeechClientRpc(Fixed64(npcId), start.conversationText, Fixed32(start.speakerCondition), RpcTarget.Single(sender, RpcTargetUse.Temp));
        var social = NetworkedSocialConversation.Instance;
        ConversationResult result;
        try
        {
            result = await social.SubmitAsPlayerAsync(playerId, new ConversationRequest
            {
                requestId = SocialWorld.Id(), npcId = npcId, text = start.conversationText, channel = start.conversationChannel, partyId = start.socialPartyId,
                speakerCondition = start.speakerCondition
            }, start.conversationChannel == "in_person");
        }
        catch (Exception e) { result = new ConversationResult { status = "rejected", error = e.Message }; }
        if (!IsSpawned || Session == null) return;
        Session.OnConversationCommitted(playerId, npcId, action, result);
        ApplyEvents();
        if (NetworkManager.ConnectedClients.ContainsKey(sender))
            ConversationClientRpc(Fixed64(npcId), codec.Encode(result), RpcTarget.Single(sender, RpcTargetUse.Temp));
    }

    void Reply(ulong client, string action, ActionResult result)
    {
        if (!NetworkManager.ConnectedClients.ContainsKey(client)) return;
        ActionResultClientRpc(new ActionResultNet { Ok = result.ok, Action = Fixed32(action), Code = Fixed32(result.code), Message = Fixed512(result.message) },
            RpcTarget.Single(client, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void ActionResultClientRpc(ActionResultNet result, RpcParams rpc = default) => OnActionResult?.Invoke(result);

    [Rpc(SendTo.SpecifiedInParams)]
    void ConversationClientRpc(FixedString64Bytes npcId, string resultJson, RpcParams rpc = default) =>
        OnConversation?.Invoke(npcId.ToString(), codec.Decode<ConversationResult>(resultJson));

    /// <summary>Reliable, targeted at the speaker only, once per accepted talk turn (≤ 800 chars).</summary>
    [Rpc(SendTo.SpecifiedInParams)]
    void SpeechClientRpc(FixedString64Bytes npcId, string spoken, FixedString32Bytes condition, RpcParams rpc = default) =>
        OnSpeech?.Invoke(npcId.ToString(), spoken ?? "", condition.ToString());

    // ---------------- Host hooks used by existing systems ----------------

    /// <summary>Host: Start Weekend from the phone (existing ready flow) — runs remaining daily ticks then the party.</summary>
    public bool StartWeekendServer()
    {
        if (!IsServer || Session == null) return false;
        var result = Session.StartWeekendEarly();
        ApplyEvents();
        return result.ok;
    }

    /// <summary>Host: continue from debrief / start next semester (host-only shortcuts used by debug UI).</summary>
    public void AdvanceServer()
    {
        if (!IsServer || Session == null) return;
        if (Session.State.phase == Phase.Debrief) Session.ContinueDebrief();
        else if (Session.State.phase == Phase.SemesterEnd) Session.StartNextSemester();
        ApplyEvents();
    }

    /// <summary>Host: spend house cash for non-stash purchases (personal shop items). False when the treasury can't cover it.</summary>
    public bool TrySpendHouseCashServer(int amount)
    {
        if (!IsServer || Session == null) return false;
        if (amount <= 0) return true;
        var h = Session.PlayerHouse;
        if (h.cash < amount) return false;
        h.cash -= amount;
        return true;
    }

    /// <summary>Host: the contact roster unlocked an NPC through Yarn dialogue.</summary>
    public void NotifyContactUnlockedServer(string npcId)
    {
        if (IsServer) Session?.SetContactUnlocked(npcId, true);
    }

    /// <summary>Host: a Yarn &lt;&lt;recruit&gt;&gt; command from a client becomes a Bid Day bid in the campaign.</summary>
    public void RecruitFromDialogueServer(ulong client, string npcId)
    {
        if (!IsServer || Session == null) return;
        string playerId = PlayerIdFor(client);
        if (playerId == null) return;
        var result = Session.Apply(new PlayerAction { playerId = playerId, action = ActionIds.ExtendBid, target = npcId });
        Reply(client, ActionIds.ExtendBid, result);
    }

    /// <summary>Host: a phone Invite template for a named contact becomes a social invitation turn.</summary>
    public void InviteFromPhoneServer(ulong client, string npcId)
    {
        if (!IsServer || Session == null) return;
        string playerId = PlayerIdFor(client);
        if (playerId == null) return;
        _ = ConversationAsync(client, playerId, ActionIds.Invite, npcId, "");
    }

    /// <summary>Where a named NPC is right now per the authoritative loop (null = off campus / not visible).</summary>
    public string NpcLocationServer(string npcId) => Session?.NpcLocation(npcId);
}
