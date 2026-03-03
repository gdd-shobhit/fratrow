using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NetworkBehaviour component for Party that synchronizes party state across the network.
/// Server authoritative - only server can modify party state.
/// </summary>
public class NetworkedParty : NetworkBehaviour
{
    // Network synchronized variables
    //private NetworkVariable<Party> _networkedParty = new NetworkVariable<Party>();
    public NetworkVariable<int> _dayNumber = new NetworkVariable<int>(1);
    public NetworkVariable<int> _guestLimit = new NetworkVariable<int>(20);
    public NetworkVariable<float> _popularity = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _troubleMaker = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _cashValue = new NetworkVariable<float>(0f);
    public NetworkVariable<PartyState> _partyState = new NetworkVariable<PartyState>(PartyState.NotStarted);
    public NetworkVariable<float> _partyStartTime = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _partyEndTime = new NetworkVariable<float>(0f);
    public float _timeSinceGuestSpawned = 0f;

    // Network list for guest IDs (we'll track guest NetworkObjects)
    private NetworkList<ulong> _guestNetworkIds = new NetworkList<ulong>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Subscribe to network variable changes
        _dayNumber.OnValueChanged += OnDayNumberChanged;
        _popularity.OnValueChanged += OnPopularityChanged;
        _troubleMaker.OnValueChanged += OnTroubleMakerChanged;
        _cashValue.OnValueChanged += OnCashValueChanged;
        _partyState.OnValueChanged += OnPartyStateChanged;
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
    public void StartPartyServerRPC(int dayNumber, int guestLimit)
    {
        if (!IsServer || _partyState.Value == PartyState.Active) return;

        Debug.Log($"[NetworkedParty] Server starting party - Day {dayNumber}, Guest Limit: {guestLimit}");

        _dayNumber.Value = dayNumber;
        _guestLimit.Value = guestLimit;
        _partyState.Value = PartyState.Active;
        _partyStartTime.Value = Time.time;
        
        float durationInSeconds = PartyManager.Instance != null 
            ? PartyManager.Instance.GetPartyDurationInMinutes() * 60f 
            : 600f; // Default 10 in game minutes
        _partyEndTime.Value = Time.time + durationInSeconds;
        _timeSinceGuestSpawned = 0f;

        // Reset stats
        _popularity.Value = 0f;
        _troubleMaker.Value = 0f;
        _cashValue.Value = 0f;

        // Notify clients
        OnPartyStartedClientRPC(guestLimit);
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Owner)]
    private void OnPartyStartedClientRPC(int guestLimit)
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

        // Notify clients
        OnPartyEndedClientRPC();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnPartyEndedClientRPC()
    {
        Debug.Log("[NetworkedParty] Party ended on client.");
        _partyState.Value = PartyState.Ended;
        this.End();
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
            Debug.LogWarning($"Distributing rewards when party state was: {state}");
            return;
        }

        // Rewards
        NetworkedGameManager.Instance._totalCashValue.Value += cashValue;
        NetworkedGameManager.Instance._totalPopularity.Value += popularity;

        Debug.Log($"Party rewards distributed: +{cashValue} Cash, +{popularity} Popularity");
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
        Debug.Log($"[NetworkedParty] Guest added on client: {guestNetworkId}");

        // Update local party stats
        if (_localParty != null)
        {
            _localParty.popularity = _popularity.Value;
            _localParty.troubleMaker = _troubleMaker.Value;
            _localParty.cashValue = _cashValue.Value;
        }
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
        _cashValue.Value -= cashValue;

        // Notify clients
        OnGuestRemovedClientRPC(guestNetworkId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnGuestRemovedClientRPC(ulong guestNetworkId)
    {
        Debug.Log($"[NetworkedParty] Guest removed on client: {guestNetworkId}");
        
        // Update local party stats
        if (_localParty != null)
        {
            _localParty.popularity = _popularity.Value;
            _localParty.troubleMaker = _troubleMaker.Value;
            _localParty.cashValue = _cashValue.Value;
        }
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
        if (_localParty != null)
        {
            _localParty.dayNumber = newValue;
        }
    }

    private void OnPopularityChanged(float oldValue, float newValue)
    {
        if (_localParty != null)
        {
            _localParty.popularity = newValue;
        }
    }

    private void OnTroubleMakerChanged(float oldValue, float newValue)
    {
        if (_localParty != null)
        {
            _localParty.troubleMaker = newValue;
        }
    }

    private void OnCashValueChanged(float oldValue, float newValue)
    {
        if (_localParty != null)
        {
            _localParty.cashValue = newValue;
        }
    }

    private void OnPartyStateChanged(PartyState oldValue, PartyState newValue)
    {
        if (_localParty != null)
        {
            _localParty.state = newValue;
        }
    }

    /// <summary>
    /// Sync network variables from local party object (server-side initialization).
    /// </summary>
    private void SyncFromLocalParty()
    {
        if (!IsServer || _localParty == null) return;

        _dayNumber.Value = _localParty.dayNumber;
        _guestLimit.Value = _localParty.guestLimit;
        _popularity.Value = _localParty.popularity;
        _troubleMaker.Value = _localParty.troubleMaker;
        _cashValue.Value = _localParty.cashValue;
        _partyState.Value = _localParty.state;
        _partyStartTime.Value = _localParty.partyStartTime;
        _partyEndTime.Value = _localParty.partyEndTime;
        _timeSinceGuestSpawned = _localParty.timeSinceGuestSpawned;
    }

    public bool AddGuest(NPCGuest guest)
    {
        if (current.Count >= guestLimit)
        {
            // TODO: Show a UI popup to the player that the party is at capacity and show UI for Guest Limit button
            Debug.LogWarning($"Party is at capacity ({guestLimit}). Cannot add more guests.");
            return false;
        }

        if (guest != null && !currentGuests.Contains(guest))
        {
            currentGuests.Add(guest);
            return true;
        }

        return false;
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
