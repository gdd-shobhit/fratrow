using System.Security.Cryptography.X509Certificates;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NetworkBehaviour singleton for PartyManager.
/// Server-only: guest spawning logic, party timing.
/// Syncs party state changes across the network.
/// </summary>
[RequireComponent(typeof(PartyManager))]
public class NetworkedPartyManager : NetworkBehaviour
{
    private PartyManager _partyManager;
    private NetworkedParty _networkedParty;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _partyManager = GetComponent<PartyManager>();
        if (_partyManager == null)
        {
            Debug.LogError("[NetworkedPartyManager] PartyManager component not found!");
            return;
        }

        // Find or create NetworkedParty component
        _networkedParty = FindFirstObjectByType<NetworkedParty>();
        if (_networkedParty == null)
        {
            Debug.LogError("This SHOULDNT be null. Check scene if it already is there");
        }
    }

    /// <summary>
    /// Start a party. Server-only method.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StartPartyServerRPC(int dayNumber, int guestLimit)
    {
        if (!IsServer) return;

        Debug.Log($"[NetworkedPartyManager] Server starting party - Day {dayNumber}, Guest Limit: {guestLimit}");

        if(_partyManager != null)
        {
            _partyManager.StartParty(guestLimit);
        }

        // Sync via NetworkedParty
        if (_networkedParty != null)
        {
            _networkedParty.StartPartyServerRPC(dayNumber, guestLimit);
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
        if (_partyManager != null)
        {
            _partyManager.EndParty();
        }
        // Sync via NetworkedParty
        if (_networkedParty != null)
        {
            _networkedParty.EndPartyServerRPC();
        }
    }

    private void Update()
    {
        // Only server handles party timing and guest spawning
        if (!IsServer) return;

        if (_partyManager != null && _partyManager.IsPartyActive)
        {
            // Update networked party time tracking
            if (_networkedParty != null)
            {
                _networkedParty.UpdateTimeSinceGuestSpawned(Time.deltaTime);
            }
        }
    }
}
