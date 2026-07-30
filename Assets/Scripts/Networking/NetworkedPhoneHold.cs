using Unity.Netcode;
using UnityEngine;

namespace Systems.Phone
{
    /// <summary>
    /// Replicates phone-hold pose only. UI navigation stays local.
    /// </summary>
    public class NetworkedPhoneHold : NetworkBehaviour
    {
        const string HoldingPhoneParam = "HoldingPhone";

        readonly NetworkVariable<bool> _isHoldingPhone = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        Animator _animator;
        int _holdingParamHash;
        bool _hasHoldingParam;

        public bool IsHoldingPhone => _isHoldingPhone.Value;

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            CacheAnimatorParam();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _isHoldingPhone.OnValueChanged += OnHoldingChanged;
            ApplyHoldingVisual(_isHoldingPhone.Value);
        }

        public override void OnNetworkDespawn()
        {
            _isHoldingPhone.OnValueChanged -= OnHoldingChanged;
            base.OnNetworkDespawn();
        }

        public void SetHoldingPhone(bool holding)
        {
            if (!IsOwner)
            {
                return;
            }

            // Optimistic local visual for owner
            ApplyHoldingVisual(holding);
            SetHoldingPhoneServerRpc(holding);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SetHoldingPhoneServerRpc(bool holding)
        {
            _isHoldingPhone.Value = holding;
        }

        void OnHoldingChanged(bool previous, bool current)
        {
            ApplyHoldingVisual(current);
        }

        void CacheAnimatorParam()
        {
            _holdingParamHash = Animator.StringToHash(HoldingPhoneParam);
            _hasHoldingParam = false;
            if (_animator == null)
            {
                return;
            }

            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == _holdingParamHash && p.type == AnimatorControllerParameterType.Bool)
                {
                    _hasHoldingParam = true;
                    break;
                }
            }
        }

        void ApplyHoldingVisual(bool holding)
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
                CacheAnimatorParam();
            }

            if (_hasHoldingParam && _animator != null)
            {
                _animator.SetBool(_holdingParamHash, holding);
            }
        }
    }
}
