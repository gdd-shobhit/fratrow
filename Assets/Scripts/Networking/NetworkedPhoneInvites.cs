using System;
using Unity.Netcode;
using UnityEngine;

namespace Systems.Phone
{
    /// <summary>
    /// Shared pending invites that bias party guest spawning.
    /// Phone UI navigation is not replicated — only gameplay invites are.
    /// </summary>
    public class NetworkedPhoneInvites : NetworkBehaviour
    {
        static NetworkedPhoneInvites _instance;
        public static NetworkedPhoneInvites Instance => _instance;

        readonly NetworkList<PendingPhoneInvite> _pendingInvites = new NetworkList<PendingPhoneInvite>();

        void Awake()
        {
            _instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            base.OnDestroy();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestInviteServerRpc(SerializableGuid contactId, int guestClass, float arrivalBonus)
        {
            if (!IsServer)
            {
                return;
            }

            var contact = PhoneContactDatabase.GetById(contactId);
            var named = contact != null ? Systems.Campus.NamedNpcDatabase.GetByContactName(contact.displayName) : null;
            if (named != null && NetworkedSocialConversation.Manages(named.npcId)) return;

            if (guestClass < (int)GuestClass.None || guestClass > (int)GuestClass.Jock)
            {
                guestClass = (int)GuestClass.None;
            }

            // Replace existing invite for same contact
            for (var i = 0; i < _pendingInvites.Count; i++)
            {
                if (_pendingInvites[i].ContactId == contactId)
                {
                    _pendingInvites[i] = new PendingPhoneInvite(contactId, (GuestClass)guestClass, arrivalBonus);
                    Debug.Log($"[PhoneInvites] Updated invite for {contactId.ToHexString()} → {(GuestClass)guestClass} (+{arrivalBonus})");
                    return;
                }
            }

            _pendingInvites.Add(new PendingPhoneInvite(contactId, (GuestClass)guestClass, arrivalBonus));
            Debug.Log($"[PhoneInvites] Pending invite {(GuestClass)guestClass} (+{arrivalBonus}) count={_pendingInvites.Count}");
        }

        /// <summary>Server-only: pick an invite to bias the next spawn, or null if none.</summary>
        public bool TryConsumeInvite(out PendingPhoneInvite invite)
        {
            invite = default;
            if (!IsServer || _pendingInvites == null || _pendingInvites.Count == 0)
            {
                return false;
            }

            invite = _pendingInvites[0];
            _pendingInvites.RemoveAt(0);
            var contact = PhoneContactDatabase.GetById(invite.ContactId);
            var named = contact != null ? Systems.Campus.NamedNpcDatabase.GetByContactName(contact.displayName) : null;
            if (named != null && NetworkedSocialConversation.Manages(named.npcId)) return false;
            return true;
        }

        public int PendingCount => _pendingInvites?.Count ?? 0;

        public void ClearAllServer()
        {
            if (!IsServer || _pendingInvites == null)
            {
                return;
            }

            _pendingInvites.Clear();
        }
    }

    public struct PendingPhoneInvite : INetworkSerializable, IEquatable<PendingPhoneInvite>
    {
        public SerializableGuid ContactId;
        public GuestClass GuestClass;
        public float ArrivalBonus;

        public PendingPhoneInvite(SerializableGuid contactId, GuestClass guestClass, float arrivalBonus)
        {
            ContactId = contactId;
            GuestClass = guestClass;
            ArrivalBonus = arrivalBonus;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            ContactId.NetworkSerialize(serializer);
            serializer.SerializeValue(ref GuestClass);
            serializer.SerializeValue(ref ArrivalBonus);
        }

        public bool Equals(PendingPhoneInvite other)
        {
            return ContactId == other.ContactId
                   && GuestClass == other.GuestClass
                   && Mathf.Approximately(ArrivalBonus, other.ArrivalBonus);
        }

        public override bool Equals(object obj) => obj is PendingPhoneInvite other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ContactId, GuestClass, ArrivalBonus);
    }
}
