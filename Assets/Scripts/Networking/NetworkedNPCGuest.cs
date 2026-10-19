using System;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// NetworkBehaviour component for NPCGuest that synchronizes guest data across the network.
/// Server authoritative spawning - only server can spawn guests.
/// </summary>
[RequireComponent(typeof(NPCGuest))]
public class NetworkedNPCGuest : NetworkBehaviour
{
    // Network synchronized variables
    private NetworkVariable<GuestClass> _guestClass = new NetworkVariable<GuestClass>(GuestClass.None);
    private NetworkVariable<float> _popularity = new NetworkVariable<float>(0f);
    private NetworkVariable<float> _troubleMaker = new NetworkVariable<float>(0f);
    private NetworkVariable<float> _cashValue = new NetworkVariable<float>(0f);
    readonly NetworkVariable<FixedString64Bytes> _npcId = new NetworkVariable<FixedString64Bytes>();
    readonly NetworkVariable<FixedString64Bytes> _partyId = new NetworkVariable<FixedString64Bytes>();
    public string NpcId => _npcId.Value.ToString();
    public string PartyId => _partyId.Value.ToString();

    // Reference to local NPCGuest component
    private NPCGuest _npcGuest;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Get local NPCGuest component
        _npcGuest = GetComponent<NPCGuest>();
        if (_npcGuest == null)
        {
            Debug.LogError("[NetworkedNPCGuest] NPCGuest component not found!");
            return;
        }

        // Subscribe to network variable changes
        _guestClass.OnValueChanged += OnGuestClassChanged;
        _popularity.OnValueChanged += OnPopularityChanged;
        _troubleMaker.OnValueChanged += OnTroubleMakerChanged;
        _cashValue.OnValueChanged += OnCashValueChanged;

        // If we're the server and have local data, sync it
        if (IsServer && _npcGuest != null)
        {
            SyncFromLocalGuest();
        }
        else if (NetworkedGameManager.Instance.guestClassesDict.TryGetValue(_guestClass.Value, out var data))
        {
            _npcGuest.InitializeFromSO(data);
            _npcGuest.SetCharacterStats(_popularity.Value, _troubleMaker.Value, _cashValue.Value);
        }
        if (!string.IsNullOrEmpty(NpcId))
        { gameObject.name = "PartyGuest_" + NpcId; _npcGuest.ApplyNamedIdentity(NpcId); }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unsubscribe from network variable changes
        _guestClass.OnValueChanged -= OnGuestClassChanged;
        _popularity.OnValueChanged -= OnPopularityChanged;
        _troubleMaker.OnValueChanged -= OnTroubleMakerChanged;
        _cashValue.OnValueChanged -= OnCashValueChanged;
    }

    /// <summary>
    /// Initialize guest data from GuestSO. Server-only method.
    /// Called when server spawns a new guest.
    /// </summary>
    public void InitializeGuestData(GuestSO guestSO, string npcId = null, string partyId = null)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[NetworkedNPCGuest] InitializeGuestData called on client! Only server can initialize.");
            return;
        }

        if (guestSO == null)
        {
            Debug.LogError("[NetworkedNPCGuest] Cannot initialize with null GuestSO!");
            return;
        }

        Debug.Log($"[NetworkedNPCGuest] Server initializing guest: {guestSO.guestName}");

        _guestClass.Value = guestSO.guestClass;
        _npcGuest = GetComponent<NPCGuest>();
        _popularity.Value = _npcGuest != null ? _npcGuest.Popularity : guestSO.popularity;
        _troubleMaker.Value = _npcGuest != null ? _npcGuest.TroubleMaker : guestSO.troubleMaker;
        _cashValue.Value = _npcGuest != null ? _npcGuest.CashValue : guestSO.cashvalue;
        _npcId.Value = new FixedString64Bytes(npcId ?? "");
        _partyId.Value = new FixedString64Bytes(partyId ?? "");

        // Initialize local NPCGuest component
        if (_npcGuest != null && IsSpawned)
        {
            InitializeGuestDataOnClientRPC();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void InitializeGuestDataOnClientRPC()
    {
        if (IsServer) return;
        _npcGuest.InitializeFromSO(NetworkedGameManager.Instance.guestClassesDict[_guestClass.Value]);
        _npcGuest.SetCharacterStats(_popularity.Value, _troubleMaker.Value, _cashValue.Value);
        _npcGuest.ApplyNamedIdentity(NpcId);
    }

    /// <summary>
    /// Sync network variables from local guest data (server-side).
    /// </summary>
    private void SyncFromLocalGuest()
    {
        if (!IsServer || _npcGuest == null) return;

        _guestClass.Value = _npcGuest.guestClass;
        _popularity.Value = _npcGuest.Popularity;
        _troubleMaker.Value = _npcGuest.TroubleMaker;
        _cashValue.Value = _npcGuest.CashValue;
    }

    // Network variable change handlers
    private void OnGuestClassChanged(GuestClass oldValue, GuestClass newValue)
    {
        if (_npcGuest != null)
        {
            _npcGuest.guestClass = newValue;
        }
    }

    private void OnPopularityChanged(float oldValue, float newValue)
    {
        if (_npcGuest != null)
        {
            _npcGuest.SetCharacterStats(newValue, _troubleMaker.Value, _cashValue.Value);
        }
    }

    private void OnTroubleMakerChanged(float oldValue, float newValue)
    {
        if (_npcGuest != null)
        {
            _npcGuest.SetCharacterStats(_popularity.Value, newValue, _cashValue.Value);
        }
    }

    private void OnCashValueChanged(float oldValue, float newValue)
    {
        if (_npcGuest != null)
        {
            _npcGuest.SetCharacterStats(_popularity.Value, _troubleMaker.Value, newValue);
        }
    }

    // Public getters for network variables
    public GuestClass GuestClass => _guestClass.Value;
    public float Popularity => _popularity.Value;
    public float TroubleMaker => _troubleMaker.Value;
    public float CashValue => _cashValue.Value;
}
