using System;
using System.Collections.Generic;
using Systems.Campus;
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
        public int InvitesSentThisWeek { get; private set; }
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

        public void ApplySharedRoster(NetworkedContactRoster roster)
        {
            if (roster == null)
            {
                return;
            }

            EnsureStates();
            NamedNpcDatabase.EnsureInitialized();
            bool changed = false;
            foreach (var npc in NamedNpcDatabase.All)
            {
                var contact = npc.ResolveContact();
                if (contact == null)
                {
                    continue;
                }

                if (!_states.TryGetValue(contact.Id, out var state))
                {
                    state = new ContactRuntimeState(contact);
                    _states[contact.Id] = state;
                }

                if (roster.TryGet(npc.npcId, out var net))
                {
                    if (state.unlocked != net.Unlocked || !Mathf.Approximately(state.affinity, net.Affinity))
                    {
                        state.unlocked = net.Unlocked;
                        state.affinity = net.Affinity;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                OnContactsChanged?.Invoke();
            }
        }

        public void UnlockContactLocal(ContactSO contact, float affinityDelta = 5f)
        {
            if (contact == null)
            {
                return;
            }

            EnsureStates();
            if (!_states.TryGetValue(contact.Id, out var state))
            {
                state = new ContactRuntimeState(contact);
                _states[contact.Id] = state;
            }

            state.unlocked = true;
            state.affinity += affinityDelta;
            OnContactsChanged?.Invoke();
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

        public float AverageUnlockedAffinity()
        {
            EnsureStates();
            float sum = 0f;
            int count = 0;
            foreach (var kv in _states)
            {
                if (!kv.Value.unlocked)
                {
                    continue;
                }

                sum += kv.Value.affinity;
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        public void ClearInvitesForNewWeek()
        {
            EnsureStates();
            foreach (var kv in _states)
            {
                kv.Value.invitedForNextParty = false;
            }

            InvitesSentThisWeek = 0;
            OnContactsChanged?.Invoke();
        }

        public bool SendTemplate(ContactSO contact, MessageTemplateSO template)
        {
            if (contact == null || template == null)
            {
                return false;
            }

            var socialNpc = NamedNpcDatabase.GetByContactName(contact.displayName);
            if (socialNpc != null && NetworkedSocialConversation.Manages(socialNpc.npcId)) return false;

            EnsureStates();
            if (!_states.TryGetValue(contact.Id, out var state) || !state.unlocked)
            {
                return false;
            }

            _messages.Append(contact.Id, template.body, fromPlayer: true);
            state.affinity += template.affinityDelta;

            var npc = NamedNpcDatabase.GetByContactName(contact.displayName);
            if (npc != null && NetworkedContactRoster.Instance != null && NetworkedContactRoster.Instance.IsServer)
            {
                NetworkedContactRoster.Instance.AddAffinity(npc.npcId, template.affinityDelta);
            }
            else if (npc != null)
            {
                NetworkedContactRoster.Instance?.RequestAffinity(npc.npcId, template.affinityDelta);
            }

            if (!string.IsNullOrWhiteSpace(template.localReply))
            {
                _messages.Append(contact.Id, template.localReply, fromPlayer: false);
            }

            OnThreadChanged?.Invoke(contact.Id);

            if (template.appliesInvite)
            {
                state.invitedForNextParty = true;
                InvitesSentThisWeek++;
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
