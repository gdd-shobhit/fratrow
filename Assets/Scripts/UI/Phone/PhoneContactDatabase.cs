using System.Collections.Generic;
using UnityEngine;

namespace Systems.Phone
{
    public static class PhoneContactDatabase
    {
        public const string ContactsResourcesPath = "Contacts";
        public const string TemplatesResourcesPath = "MessageTemplates";

        static readonly List<ContactSO> _contacts = new List<ContactSO>();
        static readonly List<MessageTemplateSO> _templates = new List<MessageTemplateSO>();
        static readonly Dictionary<SerializableGuid, ContactSO> _byId = new Dictionary<SerializableGuid, ContactSO>();
        static bool _initialized;

        public static IReadOnlyList<ContactSO> Contacts
        {
            get
            {
                EnsureInitialized();
                return _contacts;
            }
        }

        public static IReadOnlyList<MessageTemplateSO> Templates
        {
            get
            {
                EnsureInitialized();
                return _templates;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Initialize() => EnsureInitialized();

        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _contacts.Clear();
            _templates.Clear();
            _byId.Clear();

            var loadedContacts = Resources.LoadAll<ContactSO>(ContactsResourcesPath);
            if (loadedContacts != null && loadedContacts.Length > 0)
            {
                foreach (var c in loadedContacts)
                {
                    if (c == null) continue;
                    c.EnsureId();
                    AddContact(c);
                }
            }
            else
            {
                SeedDefaultContacts();
            }

            var loadedTemplates = Resources.LoadAll<MessageTemplateSO>(TemplatesResourcesPath);
            if (loadedTemplates != null && loadedTemplates.Length > 0)
            {
                foreach (var t in loadedTemplates)
                {
                    if (t != null) _templates.Add(t);
                }
            }
            else
            {
                SeedDefaultTemplates();
            }
        }

        public static ContactSO GetById(SerializableGuid id)
        {
            EnsureInitialized();
            return _byId.TryGetValue(id, out var c) ? c : null;
        }

        static void AddContact(ContactSO contact)
        {
            if (!_byId.TryAdd(contact.Id, contact))
            {
                Debug.LogWarning($"[PhoneContactDatabase] Duplicate contact id {contact.Id.ToHexString()} on '{contact.displayName}'.");
                return;
            }
            _contacts.Add(contact);
        }

        static ContactSO MakeContact(string name, GuestClass guestClass, ContactGender gender, bool unlocked = true)
        {
            var c = ScriptableObject.CreateInstance<ContactSO>();
            c.name = name;
            c.displayName = name;
            c.linkedGuestClass = guestClass;
            c.gender = gender;
            c.startsUnlocked = unlocked;
            c.EnsureId();
            return c;
        }

        static MessageTemplateSO MakeTemplate(string name, MessageTemplateKind kind, string body, bool invite, float bonus, string reply, float affinity = 0f)
        {
            var t = ScriptableObject.CreateInstance<MessageTemplateSO>();
            t.name = name;
            t.kind = kind;
            t.body = body;
            t.appliesInvite = invite;
            t.arrivalBonus = bonus;
            t.localReply = reply;
            t.affinityDelta = affinity;
            return t;
        }

        static void SeedDefaultContacts()
        {
            AddContact(MakeContact("Brad", GuestClass.Jock, ContactGender.Guy));
            AddContact(MakeContact("Tyler", GuestClass.Jock, ContactGender.Guy));
            AddContact(MakeContact("Megan", GuestClass.MeanGirl, ContactGender.Girl));
            AddContact(MakeContact("Ashley", GuestClass.MeanGirl, ContactGender.Girl));
            AddContact(MakeContact("Kevin", GuestClass.Nerd, ContactGender.Guy));
            AddContact(MakeContact("Priya", GuestClass.Nerd, ContactGender.Girl));
            AddContact(MakeContact("Sam", GuestClass.None, ContactGender.Both));
            AddContact(MakeContact("Jordan", GuestClass.None, ContactGender.Both, unlocked: false));
        }

        static void SeedDefaultTemplates()
        {
            _templates.Add(MakeTemplate(
                "Invite",
                MessageTemplateKind.Invite,
                "party at the house friday. come thru",
                invite: true,
                bonus: 80f,
                reply: "bet i'll try to make it",
                affinity: 5f));

            _templates.Add(MakeTemplate(
                "HangOut",
                MessageTemplateKind.HangOut,
                "u free to hang later?",
                invite: false,
                bonus: 0f,
                reply: "maybe, text me",
                affinity: 2f));

            _templates.Add(MakeTemplate(
                "Weird",
                MessageTemplateKind.Weird,
                "do u think raccoons have frats",
                invite: false,
                bonus: 0f,
                reply: "??? what",
                affinity: -1f));
        }
    }
}
