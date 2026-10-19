using Unity.Netcode;
using UnityEngine;

namespace Systems.Campus
{
    /// <summary>
    /// Phone ready-up: each client sets ready; host starts weekend when all connected clients are ready.
    /// Must be present on the GameManager NetworkObject in the prefab/scene (do not AddComponent at runtime).
    /// </summary>
    public class NetworkedWeekendReady : NetworkBehaviour
    {
        static NetworkedWeekendReady _instance;
        public static NetworkedWeekendReady Instance => _instance;

        readonly NetworkVariable<ulong> _readyMask = new NetworkVariable<ulong>(0);

        public event System.Action OnReadyChanged;

        void Awake() => _instance = this;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _instance = this;
            _readyMask.OnValueChanged += HandleReadyMaskChanged;
        }

        public override void OnNetworkDespawn()
        {
            _readyMask.OnValueChanged -= HandleReadyMaskChanged;
            if (_instance == this)
            {
                _instance = null;
            }

            base.OnNetworkDespawn();
        }

        void HandleReadyMaskChanged(ulong previous, ulong current) => OnReadyChanged?.Invoke();

        public bool IsLocalReady
        {
            get
            {
                if (NetworkManager.Singleton == null)
                {
                    return false;
                }

                ulong bit = 1UL << (int)(NetworkManager.Singleton.LocalClientId & 63);
                return (_readyMask.Value & bit) != 0;
            }
        }

        public int ReadyCount
        {
            get
            {
                int count = 0;
                ulong mask = _readyMask.Value;
                while (mask != 0)
                {
                    count += (int)(mask & 1UL);
                    mask >>= 1;
                }

                return count;
            }
        }

        public bool AllConnectedReady()
        {
            if (NetworkManager.Singleton == null)
            {
                return false;
            }

            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                ulong bit = 1UL << (int)(clientId & 63);
                if ((_readyMask.Value & bit) == 0)
                {
                    return false;
                }
            }

            return NetworkManager.Singleton.ConnectedClientsIds.Count > 0;
        }

        public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetReadyServerRpc(bool ready, RpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            ulong clientId = rpcParams.Receive.SenderClientId;
            ulong bit = 1UL << (int)(clientId & 63);
            if (ready)
            {
                _readyMask.Value |= bit;
            }
            else
            {
                _readyMask.Value &= ~bit;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void StartWeekendServerRpc(RpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            ulong sender = rpcParams.Receive.SenderClientId;
            ulong hostId = NetworkManager.LocalClientId;
            if (sender != hostId && sender != NetworkManager.ServerClientId && sender != 0)
            {
                Debug.LogWarning("[WeekendReady] Non-host tried to Start Weekend");
                return;
            }

            if (!AllConnectedReady())
            {
                Debug.LogWarning("[WeekendReady] Not all players ready");
                return;
            }

            _readyMask.Value = 0;
            NetworkedCampusCalendar.Instance?.EnterPartyPhaseServer();
            NetworkedPartyManager.Instance?.StartPartyServer();
            Debug.Log("[WeekendReady] Weekend started");
        }

        public void ClearReadyServer()
        {
            if (IsServer)
            {
                _readyMask.Value = 0;
            }
        }
    }
}
