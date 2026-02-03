using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NetworkBehaviour component for UserCharacter that synchronizes player data across the network.
/// Each player owns their character NetworkObject.
/// </summary>
[RequireComponent(typeof(UserCharacter))]
public class NetworkedUserCharacter : NetworkBehaviour
{
    // Network synchronized variables
    private NetworkVariable<PlayerClass> _playerClass = new NetworkVariable<PlayerClass>(PlayerClass.President);
    private NetworkVariable<float> _popularity = new NetworkVariable<float>(0f);
    private NetworkVariable<float> _troubleMaker = new NetworkVariable<float>(0f);
    private NetworkVariable<float> _cashValue = new NetworkVariable<float>(0f);

    // Reference to local UserCharacter component
    private UserCharacter _userCharacter;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Get local UserCharacter component
        _userCharacter = GetComponent<UserCharacter>();
        if (_userCharacter == null)
        {
            Debug.LogError("[NetworkedUserCharacter] UserCharacter component not found!");
            return;
        }

        // Subscribe to network variable changes
        _playerClass.OnValueChanged += OnPlayerClassChanged;
        _popularity.OnValueChanged += OnPopularityChanged;
        _troubleMaker.OnValueChanged += OnTroubleMakerChanged;
        _cashValue.OnValueChanged += OnCashValueChanged;

        // If we're the owner and have local data, sync it
        if (IsOwner && _userCharacter != null)
        {
            SyncFromLocalCharacter();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unsubscribe from network variable changes
        _playerClass.OnValueChanged -= OnPlayerClassChanged;
        _popularity.OnValueChanged -= OnPopularityChanged;
        _troubleMaker.OnValueChanged -= OnTroubleMakerChanged;
        _cashValue.OnValueChanged -= OnCashValueChanged;
    }

    /// <summary>
    /// Initialize player character data from PlayerClassSO. Server-only method.
    /// Called when server spawns a player character.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void InitializePlayerDataServerRPC(PlayerClass playerClass, float popularity, float troubleMaker, float cashValue)
    {
        if (!IsServer) return;

        Debug.Log($"[NetworkedUserCharacter] Server initializing player: {playerClass}");

        _playerClass.Value = playerClass;
        _popularity.Value = popularity;
        _troubleMaker.Value = troubleMaker;
        _cashValue.Value = cashValue;

        // Initialize on all clients
        InitializePlayerDataClientRPC(playerClass, popularity, troubleMaker, cashValue);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void InitializePlayerDataClientRPC(PlayerClass playerClass, float popularity, float troubleMaker, float cashValue)
    {
        // Update local character
        if (_userCharacter != null)
        {
            _userCharacter.currentClass = playerClass;
            _userCharacter.SetCharacterStats(popularity, troubleMaker, cashValue);
        }
    }

    /// <summary>
    /// Update player stats. Owner calls this, server validates and syncs.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void UpdateStatsServerRPC(float popularity, float troubleMaker, float cashValue)
    {
        if (!IsServer) return;

        // Server validates and updates
        _popularity.Value = popularity;
        _troubleMaker.Value = troubleMaker;
        _cashValue.Value = cashValue;
    }

    /// <summary>
    /// Sync network variables from local character data (owner-side).
    /// </summary>
    private void SyncFromLocalCharacter()
    {
        if (!IsOwner || _userCharacter == null) return;

        // Only sync if we're the owner (use public BaseCharacter properties)
        UpdateStatsServerRPC(
            _userCharacter.Popularity,
            _userCharacter.TroubleMaker,
            _userCharacter.CashValue
        );

        if (_userCharacter.currentClass != _playerClass.Value)
        {
            // Update class via server
            // Note: This might need a separate RPC if class changes during gameplay
        }
    }

    // Network variable change handlers
    private void OnPlayerClassChanged(PlayerClass oldValue, PlayerClass newValue)
    {
        if (_userCharacter != null)
        {
            _userCharacter.currentClass = newValue;
        }
    }

    private void OnPopularityChanged(float oldValue, float newValue)
    {
        if (_userCharacter != null)
        {
            _userCharacter.SetCharacterStats(newValue, _troubleMaker.Value, _cashValue.Value);
        }
    }

    private void OnTroubleMakerChanged(float oldValue, float newValue)
    {
        if (_userCharacter != null)
        {
            _userCharacter.SetCharacterStats(_popularity.Value, newValue, _cashValue.Value);
        }
    }

    private void OnCashValueChanged(float oldValue, float newValue)
    {
        if (_userCharacter != null)
        {
            _userCharacter.SetCharacterStats(_popularity.Value, _troubleMaker.Value, newValue);
        }
    }

    // Public getters for network variables
    public PlayerClass PlayerClass => _playerClass.Value;
    public float Popularity => _popularity.Value;
    public float TroubleMaker => _troubleMaker.Value;
    public float CashValue => _cashValue.Value;
}
