using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Systems.Phone
{
    /// <summary>
    /// Local phone logic: contacts, threads, template sends.
    /// Only gameplay-effecting sends (invites) hit the network.
    /// </summary>
    public class PhoneController : MonoBehaviour
    {
        readonly Dictionary<SerializableGuid, ContactRuntimeState> _states = new Dictionary<SerializableGuid, ContactRuntimeState>();
        readonly PhoneMessageStore _messages = new PhoneMessageStore();

        public PhoneMessageStore Messages => _messages;
        public event Action OnContactsChanged;
        public event Action<SerializableGuid> OnThreadChanged;

        void Awake()
        {
            EnsureStates();
        }

        public void EnsureStates()
        {
            PhoneContactDatabase.EnsureInitialized();
            foreach (var contact in PhoneContactDatabase.Contacts)
            {
                if (!_states.ContainsKey(contact.Id))
                {
                    _states[contact.Id] = new ContactRuntimeState(contact);
                }
            }
        }

        public IEnumerable<ContactSO> GetUnlockedContacts()
        {
            EnsureStates();
            foreach (var contact in PhoneContactDatabase.Contacts)
            {
                if (_states.TryGetValue(contact.Id, out var state) && state.unlocked)
                {
                    yield return contact;
                }
            }
        }

        public ContactRuntimeState GetState(SerializableGuid contactId)
        {
            EnsureStates();
            return _states.TryGetValue(contactId, out var state) ? state : null;
        }

        public bool SendTemplate(ContactSO contact, MessageTemplateSO template)
        {
            if (contact == null || template == null)
            {
                return false;
            }

            EnsureStates();
            if (!_states.TryGetValue(contact.Id, out var state) || !state.unlocked)
            {
                return false;
            }

            _messages.Append(contact.Id, template.body, fromPlayer: true);
            state.affinity += template.affinityDelta;

            if (!string.IsNullOrWhiteSpace(template.localReply))
            {
                _messages.Append(contact.Id, template.localReply, fromPlayer: false);
            }

            OnThreadChanged?.Invoke(contact.Id);

            if (template.appliesInvite)
            {
                state.invitedForNextParty = true;
                RequestInviteOnServer(contact, template);
            }

            OnContactsChanged?.Invoke();
            return true;
        }

        static void RequestInviteOnServer(ContactSO contact, MessageTemplateSO template)
        {
            var invites = NetworkedPhoneInvites.Instance;
            if (invites == null)
            {
                Debug.LogWarning("[PhoneController] NetworkedPhoneInvites missing — invite stored locally only.");
                return;
            }

            invites.RequestInviteServerRpc(contact.Id, (int)contact.linkedGuestClass, template.arrivalBonus);
        }
    }
}
