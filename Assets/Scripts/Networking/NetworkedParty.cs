using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// States a party can be in.
/// </summary>
public enum PartyState
{
    NotStarted,
    Active,
    Ended
}

/// <summary>
/// NetworkBehaviour component for Party that synchronizes party state across the network.
/// Server authoritative - only server can modify party state.
/// </summary>
public class NetworkedParty : NetworkBehaviour
{
    [SerializeField]
    public NetworkVariable<int> _dayNumber = new NetworkVariable<int>(0);
    public NetworkVariable<int> _guestLimit = new NetworkVariable<int>(20);
    public NetworkVariable<float> _popularity = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _troubleMaker = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _cashValue = new NetworkVariable<float>(0f);
    public NetworkVariable<PartyState> _partyState = new NetworkVariable<PartyState>(PartyState.NotStarted);
    public NetworkVariable<float> _partyStartTime = new NetworkVariable<float>(0f);
    public NetworkVariable<float> _partyEndTime = new NetworkVariable<float>(0f);
    
    [SerializeField]
    private int DayNumberUI = 0;

    public float _timeSinceGuestSpawned = 0f;
    public List<NPCGuest> currentGuests = new List<NPCGuest>();

    public float inGameTimeStart = 22.0f;
    public float inGameTimeEnd = 2.0f;

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

        _dayNumber.OnValueChanged += OnDayNumberChanged;

        if (NetworkedPartyManager.Instance.NetworkedParty != this)
        {
            NetworkedPartyManager.Instance.NetworkedParty = this;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _dayNumber.OnValueChanged -= OnDayNumberChanged;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void StartPartyServerRPC()
    {
        if (!IsServer || _partyState.Value == PartyState.Active) return;

        Debug.Log($"[NetworkedParty] Server starting party - Day {_dayNumber.Value}, Guest Limit: {_guestLimit.Value}");

         _dayNumber.Value++;
         DayNumberUI = _dayNumber.Value;

        _partyState.Value = PartyState.Active;
        _partyStartTime.Value = (float)NetworkManager.ServerTime.Time;
        
        float durationInSeconds = NetworkedPartyManager.Instance.PartyDurationMinutes * 60f;
        _partyEndTime.Value = (float)NetworkManager.ServerTime.Time + durationInSeconds;
        _timeSinceGuestSpawned = 0f;

        _popularity.Value = 0f;
        _troubleMaker.Value = 0f;
        _cashValue.Value = 0f;

        OnPartyStartedClientRPC();
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Owner)]
    private void OnPartyStartedClientRPC()
    {
        Debug.Log("[NetworkedParty] Party started on client.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void EndPartyServerRPC()
    {
        if (!IsServer) return;

        Debug.Log("[NetworkedParty] Server ending party.");

        _partyState.Value = PartyState.Ended;
        End();

        OnPartyEndedClientRPC();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnPartyEndedClientRPC()
    {
        Debug.Log("[NetworkedParty] Party ended on client.");
    }

    public void End()
    {
        DistributePartyEndRewards();
    }

    private void DistributePartyEndRewards()
    {
        if (_partyState.Value != PartyState.Ended)
        {
            Debug.LogWarning($"Distributing rewards when party state was: {_partyState.Value}");
            return;
        }

        NetworkedGameManager.Instance.AddTotalCashValue(CashValue);
        NetworkedGameManager.Instance.AddTotalPopularityValue(Popularity);

        Debug.Log($"Party rewards distributed: +{CashValue} Cash, +{Popularity} Popularity");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void AddGuestServerRPC(ulong guestNetworkId, float popularity, float troubleMaker, float cashValue)
    {
        if (!IsServer) return;

        Debug.Log($"[NetworkedParty] Server adding guest {guestNetworkId}");

        _popularity.Value += popularity;
        _troubleMaker.Value += troubleMaker;
        _cashValue.Value += cashValue;

        OnGuestAddedClientRPC(guestNetworkId, popularity, troubleMaker, cashValue);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnGuestAddedClientRPC(ulong guestNetworkId, float popularity, float troubleMaker, float cashValue)
    {
        Debug.Log($"[NetworkedParty] Guest added on client: {NetworkManager.Singleton.LocalClientId}");
    }

    public void UpdateTimeSinceGuestSpawned(float deltaTime)
    {
        if (IsServer)
        {
            _timeSinceGuestSpawned += deltaTime;
        }
    }

    private void OnDayNumberChanged(int oldValue, int newValue)
    {
        DayNumberUI = _dayNumber.Value;
        OnDayNumberChangedEvent?.Invoke(newValue);
    }

    public bool AddGuest(NPCGuest guest)
    {
        if (currentGuests.Count >= GuestLimit)
        {
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

    public float GetCurrentInGameTime()
    {
        if (State != PartyState.Active)
            return inGameTimeStart;

        float elapsedRealTime = (float)NetworkManager.ServerTime.Time - PartyStartTime;
        float inGameHoursElapsed = (elapsedRealTime / 60f) * 4f;
        float currentTime = inGameTimeStart + inGameHoursElapsed;

        if (currentTime >= 24.0f)
            currentTime -= 24.0f;

        return currentTime;
    }

    public bool RemoveGuest(NPCGuest guest)
    {
        return currentGuests.Remove(guest);
    }

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
