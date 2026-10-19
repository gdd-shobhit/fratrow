using System.Collections.Generic;
using Systems.Campus;
using Systems.Phone;
using Unity.Netcode;
using UnityEngine;
using System.Linq;
using FratRow.Social;

/// <summary>
/// NetworkBehaviour singleton for PartyManager.
/// Server-only: guest spawning logic, party timing.
/// Syncs party state changes across the network.
/// </summary>
public class NetworkedPartyManager : NetworkBehaviour
{

    private static NetworkedPartyManager _instance;
    public static NetworkedPartyManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // Try to find existing instance in scene
                NetworkedPartyManager found = FindFirstObjectByType<NetworkedPartyManager>();
                if (found != null)
                {
                    _instance = found;
                }
                else
                {
                    Debug.Log("PartyManager not found");
                }
            }

            return _instance;
        }
    }

    [Header("Party Settings")]
    [SerializeField] private float partyDurationMinutes = 10f; // Real-time minutes for a party
    [SerializeField] private int defaultGuestLimit = 20;

    [Header("Current Party")]
    private NetworkedParty _networkedParty;
    [SerializeField] private float timeBetweenEachGuestArrival = 15.0f;

    [Header("Game State")]
    [SerializeField] private bool persistAcrossScenes = true;
    [SerializeField] private Transform guestSpawnLocation;

    // Accessors
    public bool IsPartyActive => _networkedParty != null && _networkedParty.State == PartyState.Active;
    public bool IsPartyOver => NetworkManager.ServerTime.Time >= _networkedParty.PartyEndTime;
    public float PartyDurationMinutes => partyDurationMinutes;
    public NetworkedParty NetworkedParty { get => _networkedParty; set => _networkedParty = value; }
    public Vector3 EntrancePosition => guestSpawnLocation != null ? guestSpawnLocation.position : transform.position;
    private void Awake()
    {
        // If instance already exists and it's not this, destroy this
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning($"Duplicate PartyManager instance on {gameObject.name} destroyed.");
            Destroy(gameObject);
            return;
        }

        // Set this as the instance
        _instance = this;

        if (IsServer && !_instance.GetComponent<NetworkObject>().IsSpawned)
        {
            _instance.GetComponent<NetworkObject>().Spawn();
        }

        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _instance = GetComponent<NetworkedPartyManager>();
        if (_instance == null)
        {
            Debug.LogError("[NetworkedPartyManager] PartyManager component not found!");
            return;
        }
    }

    private void Update()
    {
        // Nothing non-server gets past this
        // Everything after this needs a client side RPC to server to get data.
        if (!IsServer)
        {
            return;
        }

        // Server Logic
        if (_networkedParty != null && _networkedParty.State == PartyState.Active)
        {
            _networkedParty._timeSinceGuestSpawned += Time.deltaTime;

            // Check if it's time to spawn another guest
            if (_networkedParty._timeSinceGuestSpawned >= timeBetweenEachGuestArrival)
            {
                // Check if we haven't reached the guest limit
                if (_networkedParty.currentGuests.Count < _networkedParty.GuestLimit)
                {
                    _networkedParty._timeSinceGuestSpawned = 0.0f;
                    SpawnGuests();
                }
            }
        }

        // Check if current party should end (server-only in multiplayer)
        if (IsPartyActive && IsPartyOver)
        {
            GetComponent<NetworkedPartyManager>().EndPartyServerRPC();
        }
    }

    float GetOutreachScore()
    {
        var settings = OutreachSettings.GetOrDefault();
        float popularity = NetworkedGameManager.Instance != null ? NetworkedGameManager.Instance.TotalPopularity : 0f;
        Grade grade = NetworkedGameManager.Instance != null
            ? NetworkedGameManager.Instance.myFraternity.Value.grade
            : Grade.C;
        float avgAff = NetworkedContactRoster.Instance != null
            ? NetworkedContactRoster.Instance.AverageUnlockedAffinity()
            : 0f;
        int invites = NetworkedPhoneInvites.Instance != null ? NetworkedPhoneInvites.Instance.PendingCount : 0;
        return settings.ComputeOutreach(popularity, grade, avgAff, invites);
    }

    float GetFraternityGradeArrivalModifier()
    {
        Grade grade = NetworkedGameManager.Instance != null
            ? NetworkedGameManager.Instance.myFraternity.Value.grade
            : Grade.C;
        return FraternityGradeCalculator.ArrivalChanceModifier(grade);
    }

    /// <summary>
    /// Named invites first (RSVP via affinity + outreach), then anonymous archetype fillers.
    /// </summary>
    private void SpawnGuests()
    {
        if (_networkedParty == null)
        {
            Debug.LogError("Cannot select guests: Party or NetworkedGameManager is null!");
            return;
        }

        if (_networkedParty.currentGuests.Count >= _networkedParty.GuestLimit)
        {
            Debug.Log($"Party reached guest limit ({_networkedParty.GuestLimit}). Cannot spawn more guests.");
            return;
        }

        float partyPopularity = _networkedParty.Popularity;
        // Reserve space for accepted and conditional social commitments before spawning fillers.
        var social = NetworkedSocialConversation.Instance;
        if (social != null && social.Enabled && social.World != null)
        {
            var snapshot = social.World.Store.Read();
            int reserved = snapshot.npcs.Count(n => n.commitments.Any(c => c.partyId == social.CurrentPartyId && !c.arrived &&
                (c.state == "accepted" || c.state == "conditional")));
            if (_networkedParty.currentGuests.Count + reserved >= _networkedParty.GuestLimit) return;
        }
        Dictionary<GuestClass, GuestSO> guestDict = NetworkedGameManager.Instance.guestClassesDict;
        if (guestDict == null || guestDict.Count == 0)
        {
            Debug.LogWarning("No guests available in NetworkedGameManager dictionary!");
            return;
        }

        var outreach = OutreachSettings.GetOrDefault();
        float outreachScore = GetOutreachScore();

        var invites = NetworkedPhoneInvites.Instance;
        if (invites != null && invites.TryConsumeInvite(out var invite))
        {
            var contact = PhoneContactDatabase.GetById(invite.ContactId);
            NamedNpcSO named = contact != null
                ? NamedNpcDatabase.GetByContactName(contact.displayName)
                : null;

            if (named != null)
            {
                float affinity = NetworkedContactRoster.Instance != null
                    ? NetworkedContactRoster.Instance.GetAffinity(named.npcId)
                    : 0f;
                float chance = Mathf.Clamp(
                    50f + invite.ArrivalBonus + AffinityPartyModifiers.RsvpBonus(affinity)
                    + outreachScore * outreach.namedRsvpPerOutreach,
                    0f,
                    100f);
                float roll = Random.Range(0f, 100f);
                if (roll <= chance)
                {
                    SpawnNamedGuest(named, affinity);
                    Debug.Log($"[NamedInvite] {named.displayName} arrived (roll {roll:F1} <= {chance:F1})");
                }
                else
                {
                    SpawnGuest(guestDict[GuestClass.None]);
                    Debug.Log($"[NamedInvite] {named.displayName} no-show (roll {roll:F1} > {chance:F1})");
                }

                return;
            }

            if (invite.GuestClass != GuestClass.None
                && guestDict.TryGetValue(invite.GuestClass, out var invitedGuestSO)
                && invitedGuestSO != null)
            {
                float invitedChance = Mathf.Clamp(
                    invitedGuestSO.GetModifiedArrivalChance(partyPopularity)
                    + invite.ArrivalBonus
                    + GetFraternityGradeArrivalModifier(),
                    0f,
                    100f);
                float invitedRoll = Random.Range(0f, 100f);
                if (invitedRoll <= invitedChance)
                {
                    SpawnGuest(invitedGuestSO);
                }
                else
                {
                    SpawnGuest(guestDict[GuestClass.None]);
                }

                return;
            }
        }

        GuestClass selectedGuestClass = (GuestClass)Random.Range((int)(GuestClass.First + 1), (int)GuestClass.Count);
        GuestSO selectedGuestSO = guestDict[selectedGuestClass];
        float arrivalChance = selectedGuestSO.GetModifiedArrivalChance(partyPopularity)
                              + outreachScore * outreach.fillerArrivalPerOutreach
                              + GetFraternityGradeArrivalModifier();
        arrivalChance = Mathf.Clamp(arrivalChance, 0f, 100f);
        float fillerRoll = Random.Range(0f, 100f);
        if (fillerRoll <= arrivalChance)
        {
            SpawnGuest(selectedGuestSO);
        }
        else
        {
            SpawnGuest(guestDict[GuestClass.None]);
        }
    }

    bool SpawnNamedGuest(NamedNpcSO named, float affinity, string partyId = null)
    {
        Dictionary<GuestClass, GuestSO> guestDict = NetworkedGameManager.Instance.guestClassesDict;
        if (!guestDict.TryGetValue(named.guestClass, out var guestSO) || guestSO == null)
        {
            guestDict.TryGetValue(GuestClass.None, out guestSO);
        }

        if (guestSO == null)
        {
            return false;
        }

        float pop = named.basePopularity + AffinityPartyModifiers.PopularityOffset(affinity);
        float trouble = named.baseTrouble + AffinityPartyModifiers.TroubleOffset(affinity);
        return SpawnGuest(guestSO, named.displayName, pop, trouble, named.baseCash, named.npcId, partyId);
    }

    private void SpawnGuest(GuestSO guestSO)
    {
        SpawnGuest(guestSO, null, guestSO.popularity, guestSO.troubleMaker, guestSO.cashvalue);
    }

    private bool SpawnGuest(GuestSO guestSO, string displayName, float popularity, float trouble, float cash, string npcId = null, string partyId = null)
    {
        if (guestSO == null || _networkedParty == null)
            return false;

        if (guestSO.prefab == null || guestSO.prefab.GetComponent<NetworkedNPCGuest>() == null || guestSO.prefab.GetComponent<NPCGuest>() == null)
        { Debug.LogError("Guest prefab must have NPCGuest and NetworkedNPCGuest baked before spawn"); return false; }

        GameObject guestObject = NetworkObject.Instantiate(guestSO.prefab, guestSpawnLocation);

        NPCGuest npcGuest = guestObject.GetComponent<NPCGuest>();

        npcGuest.InitializeFromSO(guestSO);
        if (!string.IsNullOrEmpty(displayName))
        {
            guestObject.name = $"PartyGuest_{displayName}";
            npcGuest.SetCharacterStats(popularity, trouble, cash);
        }

        if (!_networkedParty.AddGuest(npcGuest)) { Destroy(guestObject); return false; }

        var networkedGuest = guestObject.GetComponent<NetworkedNPCGuest>();
        networkedGuest.InitializeGuestData(guestSO, npcId, partyId);

        guestObject.GetComponent<NetworkObject>().Spawn(true);

        _networkedParty.AddGuestServerRPC(
            guestObject.GetComponent<NetworkObject>().NetworkObjectId,
            popularity,
            trouble,
            cash);
        return true;
    }

    public void PollSocialAttendanceServer(NetworkedSocialConversation social)
    {
        if (!IsServer || !IsPartyActive || social.World == null) return;
        string partyId = social.CurrentPartyId;
        social.World.ObserveEntrance(partyId, social.PlayersAtEntrance(EntrancePosition), NetworkedSocialConversation.Now);
        foreach (var npc in social.World.Store.Read().npcs)
        {
            var commitment = npc.commitments.Find(c => c.partyId == partyId && c.state == "accepted");
            if (commitment == null || _networkedParty.currentGuests.Any(g => g != null && g.TryGetComponent<NetworkedNPCGuest>(out var net) && net.NpcId == npc.npcId && net.PartyId == partyId)) continue;
            var data = NamedNpcDatabase.GetById(npc.npcId);
            if (data != null && SpawnNamedGuest(data, npc.fraternityRegard, partyId)) social.World.MarkArrived(npc.npcId, partyId);
        }
    }

    public void ResumeSocialPartyServer(float remainingSeconds)
    {
        if (!IsServer || _networkedParty == null || IsPartyActive) return;
        _networkedParty._partyState.Value = PartyState.Active;
        _networkedParty._partyStartTime.Value = (float)NetworkManager.ServerTime.Time - (partyDurationMinutes * 60 - remainingSeconds);
        _networkedParty._partyEndTime.Value = (float)NetworkManager.ServerTime.Time + remainingSeconds;
        NetworkedCampusCalendar.Instance?.EnterPartyPhaseServer();
    }

    public void StartPartyServer()
    {
        if (!IsServer) return;

        Debug.Log($"[NetworkedPartyManager] Server starting party");
        // If start party was called with default parameter

        // Sync via NetworkedParty
        if (_networkedParty != null)
        {
            _networkedParty.StartPartyServerRPC();
        }
    }

    /// <summary>
    /// End the party. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void EndPartyServerRPC()
    {
        if (!IsServer) return;

        Debug.Log("[NetworkedPartyManager] Server ending party.");

        if (_networkedParty != null)
        {
            _networkedParty.EndPartyServerRPC();
        }

        NetworkedPhoneInvites.Instance?.ClearAllServer();
        NetworkedWeekendReady.Instance?.ClearReadyServer();
        NetworkedCampusCalendar.Instance?.EnterDebriefThenAdvanceServer();
        CampusPersistence.SaveFromServer();
    }
}
