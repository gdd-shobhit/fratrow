using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Unity.Netcode;
using Unity.Properties;
using UnityEngine;

/// <summary>
/// NetworkBehaviour component for Party that synchronizes party state across the network.
/// Server authoritative - only server can modify party state.
/// </summary>
public class NetworkedParty : NetworkBehaviour
{
    // Network synchronized variables
    //private NetworkVariable<Party> _networkedParty = new NetworkVariable<Party>();
    [SerializeField]
    public NetworkVariable<int> _dayNumber = new NetworkVariable<int>(-1);
    public NetworkVariable<int> _guestLimit = new NetworkVariable<int>(20);
    public NetworkVariable<float> _popularity = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _troubleMaker = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _cashValue = new NetworkVariable<float>(0f);
    public NetworkVariable<PartyState> _partyState = new NetworkVariable<PartyState>(PartyState.NotStarted);
    public NetworkVariable<float> _partyStartTime = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _partyEndTime = new NetworkVariable<float>(0f);

    // Non-networked variables
    public float _timeSinceGuestSpawned = 0f;
    public List<NPCGuest> currentGuests = new List<NPCGuest>();

    // In-game time tracking (10pm = 22:00, 2am = 02:00)
    public float inGameTimeStart = 22.0f; // 10:00 PM
    public float inGameTimeEnd = 2.0f;    // 2:00 AM (next day)
    // Network list for guest IDs (we'll track guest NetworkObjects)
    private NetworkList<ulong> _guestNetworkIds = new NetworkList<ulong>();

    // Action events
    public event System.Action<int> OnDayNumberChangedEvent;

    private void Awake()
    {
        if (IsServer && !gameObject.GetComponent<NetworkObject>().IsSpawned)
        {
            gameObject.GetComponent<NetworkObject>().Spawn();
        }
    }
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Subscribe to network variable changes
        _dayNumber.OnValueChanged += OnDayNumberChanged;
        _popularity.OnValueChanged += OnPopularityChanged;
        _troubleMaker.OnValueChanged += OnTroubleMakerChanged;
        _cashValue.OnValueChanged += OnCashValueChanged;
        _partyState.OnValueChanged += OnPartyStateChanged;

        if(NetworkedPartyManager.Instance.NetworkedParty != this)
        {
            NetworkedPartyManager.Instance.NetworkedParty = this;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unsubscribe from network variable changes
        _dayNumber.OnValueChanged -= OnDayNumberChanged;
        _popularity.OnValueChanged -= OnPopularityChanged;
        _troubleMaker.OnValueChanged -= OnTroubleMakerChanged;
        _cashValue.OnValueChanged -= OnCashValueChanged;
        _partyState.OnValueChanged -= OnPartyStateChanged;
    }

    /// <summary>
    /// Start the party. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void StartPartyServerRPC()
    {
        if (!IsServer || _partyState.Value == PartyState.Active) return;

        Debug.Log($"[NetworkedParty] Server starting party - Day {_dayNumber.Value}, Guest Limit: {_guestLimit.Value}");

        if(_dayNumber.Value == -1)
        {
            _dayNumber.Value = 1;
        }
        else
        {
            _dayNumber.Value++;
        }

        // Increase guest limit through upgrades?
        //_guestLimit.Value = guestLimit;
        _partyState.Value = PartyState.Active;
        _partyStartTime.Value = (float)NetworkManager.ServerTime.Time;
        
        float durationInSeconds =NetworkedPartyManager.Instance.PartyDurationMinutes * 60f; // Default 10 in game minutes
        _partyEndTime.Value = (float)NetworkManager.ServerTime.Time + durationInSeconds;
        _timeSinceGuestSpawned = 0f;

        // Reset stats
        _popularity.Value = 0f;
        _troubleMaker.Value = 0f;
        _cashValue.Value = 0f;

        // Notify clients
        OnPartyStartedClientRPC();
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Owner)]
    private void OnPartyStartedClientRPC()
    {
        Debug.Log("[NetworkedParty] Party started on client.");
    }

    /// <summary>
    /// End the party. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void EndPartyServerRPC()
    {
        if (!IsServer) return;

        Debug.Log("[NetworkedParty] Server ending party.");

        _partyState.Value = PartyState.Ended;
        End();

        // Notify clients
        OnPartyEndedClientRPC();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnPartyEndedClientRPC()
    {
        Debug.Log("[NetworkedParty] Party ended on client.");
    }

    /// <summary>
    /// End the party
    /// </summary>
    public void End()
    {
        DistributePartyEndRewards();
        // TODO: Achievements hookups

        // TODO: Leaderboard implementation. Distribution of everything else
    }

    /// <summary>
    /// Distribute the rewards after the party
    /// </summary>
    private void DistributePartyEndRewards()
    {
        if (_partyState.Value != PartyState.Ended)
        {
            Debug.LogWarning($"Distributing rewards when party state was: {_partyState.Value}");
            return;
        }

        // Rewards
        NetworkedGameManager.Instance.AddTotalCashValue(CashValue);
        NetworkedGameManager.Instance.AddTotalPopularityValue(Popularity);

        Debug.Log($"Party rewards distributed: +{CashValue} Cash, +{Popularity} Popularity");
    }

    /// <summary>
    /// Add a guest to the party. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void AddGuestServerRPC(ulong guestNetworkId, float popularity, float troubleMaker, float cashValue)
    {
        if (!IsServer) return;

        Debug.Log($"[NetworkedParty] Server adding guest {guestNetworkId}");

        // Update party stats
        _popularity.Value += popularity;
        _troubleMaker.Value += troubleMaker;
        _cashValue.Value += cashValue;

        // Add guest to network list (if we're tracking them)
        // Note: We might track guest NetworkObjects separately

        // Notify clients
        OnGuestAddedClientRPC(guestNetworkId, popularity, troubleMaker, cashValue);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnGuestAddedClientRPC(ulong guestNetworkId, float popularity, float troubleMaker, float cashValue)
    {
        Debug.Log($"[NetworkedParty] Guest added on client: {NetworkManager.Singleton.LocalClientId}");
    }

    /// <summary>
    /// Remove a guest from the party. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RemoveGuestServerRPC(ulong guestNetworkId, float popularity, float troubleMaker, float cashValue)
    {
        if (!IsServer) return;

        Debug.Log($"[NetworkedParty] Server removing guest {guestNetworkId}");

        // Update party stats (subtract guest's contribution)
        _popularity.Value -= popularity;
        _troubleMaker.Value -= troubleMaker;
        
        // Design: Should we deduct cash if we remove people? 
        _cashValue.Value -= cashValue;

        // Notify clients
        OnGuestRemovedClientRPC(guestNetworkId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnGuestRemovedClientRPC(ulong guestNetworkId)
    {
        Debug.Log($"[NetworkedParty] Guest removed on client: {guestNetworkId}"); 
    }

    /// <summary>
    /// Update time since guest spawned. Called by server each frame.
    /// </summary>
    public void UpdateTimeSinceGuestSpawned(float deltaTime)
    {
        if (IsServer)
        {
            _timeSinceGuestSpawned += deltaTime;
        }
    }

    // Network variable change handlers
    private void OnDayNumberChanged(int oldValue, int newValue)
    {
        OnDayNumberChangedEvent?.Invoke(newValue);
    }

    private void OnPopularityChanged(float oldValue, float newValue)
    {

    }

    private void OnTroubleMakerChanged(float oldValue, float newValue)
    {

    }

    private void OnCashValueChanged(float oldValue, float newValue)
    {

    }

    private void OnPartyStateChanged(PartyState oldValue, PartyState newValue)
    {

    }

    public bool AddGuest(NPCGuest guest)
    {
        if (currentGuests.Count >= GuestLimit)
        {
            // TODO: Show a UI popup to the player that the party is at capacity and show UI for Guest Limit button
            Debug.LogWarning($"Party is at capacity ({GuestLimit}). Cannot add more guests.");
            return false;
        }

        if (guest != null && !currentGuests.Contains(guest))
        {
            currentGuests.Add(guest);
            return true;
        }

        return false;
    }

    public void Start()
    {

    }

    /// <summary>
    /// Get the current in-game time based on elapsed real-time.
    /// Returns time in hours (22.0 = 10pm, 2.0 = 2am)
    /// </summary>
    public float GetCurrentInGameTime()
    {
        if (State != PartyState.Active)
            return inGameTimeStart;

        float elapsedRealTime = (float)NetworkManager.ServerTime.Time - PartyStartTime;
        // 10 minutes real-time = 4 hours in-game (240 minutes)
        // So 1 second real-time = 0.4 minutes in-game = 0.00667 hours in-game
        float inGameHoursElapsed = (elapsedRealTime / 60f) * 4f; // Convert real minutes to in-game hours
        float currentTime = inGameTimeStart + inGameHoursElapsed;

        // Handle wrap-around past midnight (2am = 26:00 in 24h format, but we want 2.0)
        if (currentTime >= 24.0f)
            currentTime -= 24.0f;

        return currentTime;
    }

    /// <summary>
    /// Remove a guest from the party
    /// </summary>
    public bool RemoveGuest(NPCGuest guest)
    {
        return currentGuests.Remove(guest);
    }

    // Public getters for network variables
    public int DayNumber => _dayNumber.Value;
    public int GuestLimit => _guestLimit.Value;
    public float Popularity => _popularity.Value;
    public float TroubleMaker => _troubleMaker.Value;
    public float CashValue => _cashValue.Value;
    public PartyState State => _partyState.Value;
    public float PartyStartTime => _partyStartTime.Value;
    public float PartyEndTime => _partyEndTime.Value;
    public float TimeSinceGuestSpawned => _timeSinceGuestSpawned;
}
