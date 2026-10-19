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
            // Self-heal: runtime-created contacts/templates destroyed by an unused-asset sweep are rebuilt.
            // Contact ids are deterministic (DeterministicId), so phone state keyed by id survives a rebuild.
            if (_initialized && !_contacts.Exists(c => c == null) && !_templates.Exists(t => t == null))
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

            EnsureRosterContacts();

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

        public static ContactSO GetByDisplayName(string displayName)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return null;
            }

            foreach (var c in _contacts)
            {
                if (string.Equals(c.displayName, displayName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }

            return null;
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

        static SerializableGuid DeterministicId(string name)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("contact:" + name));
            return new SerializableGuid(
                System.BitConverter.ToUInt32(bytes, 0),
                System.BitConverter.ToUInt32(bytes, 4),
                System.BitConverter.ToUInt32(bytes, 8),
                System.BitConverter.ToUInt32(bytes, 12));
        }

        static ContactSO MakeContact(string name, GuestClass guestClass, ContactGender gender, bool unlocked = true)
        {
            var c = ScriptableObject.CreateInstance<ContactSO>();
            // Runtime-only asset held in a static list: keep it alive across Single scene loads (unused-asset sweep).
            c.hideFlags = HideFlags.DontUnloadUnusedAsset;
            c.name = name;
            c.displayName = name;
            c.linkedGuestClass = guestClass;
            c.gender = gender;
            c.startsUnlocked = unlocked;
            c.SetId(DeterministicId(name));
            return c;
        }

        static MessageTemplateSO MakeTemplate(string name, MessageTemplateKind kind, string body, bool invite, float bonus, string reply, float affinity = 0f)
        {
            var t = ScriptableObject.CreateInstance<MessageTemplateSO>();
            t.hideFlags = HideFlags.DontUnloadUnusedAsset; // survive scene loads (see MakeContact)
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
            AddContact(MakeContact("Megan", GuestClass.MeanGirl, ContactGender.Girl, unlocked: false));
            AddContact(MakeContact("Ashley", GuestClass.MeanGirl, ContactGender.Girl));
            AddContact(MakeContact("Kevin", GuestClass.Nerd, ContactGender.Guy));
            AddContact(MakeContact("Priya", GuestClass.Nerd, ContactGender.Girl));
            AddContact(MakeContact("Sam", GuestClass.None, ContactGender.Both));
            AddContact(MakeContact("Jordan", GuestClass.None, ContactGender.Both, unlocked: false));
            AddContact(MakeContact("Alex", GuestClass.Jock, ContactGender.Both, unlocked: false));
            AddContact(MakeContact("Riley", GuestClass.MeanGirl, ContactGender.Both, unlocked: false));
            AddContact(MakeContact("Casey", GuestClass.None, ContactGender.Both, unlocked: false));
            AddContact(MakeContact("Morgan", GuestClass.Nerd, ContactGender.Both, unlocked: false));
            AddContact(MakeContact("Jamie", GuestClass.Jock, ContactGender.Both));
            AddContact(MakeContact("Avery", GuestClass.MeanGirl, ContactGender.Both, unlocked: false));
            AddContact(MakeContact("Quinn", GuestClass.None, ContactGender.Both, unlocked: false));
            AddContact(MakeContact("Drew", GuestClass.Jock, ContactGender.Both, unlocked: false));
        }

        /// <summary>
        /// Gives every campus roster NPC (Resources/Campus, see <see cref="Systems.Campus.NamedNpcDatabase.Roster"/>) a phone
        /// contact, matched by display name (case-insensitive). Existing contacts — authored assets or legacy seeds — are
        /// left untouched; only missing ones are created.
        /// </summary>
        static void EnsureRosterContacts()
        {
            var roster = Systems.Campus.NamedNpcDatabase.Roster;
            if (roster == null || roster.npcs == null)
            {
                return;
            }

            foreach (var npc in roster.npcs)
            {
                if (npc == null)
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(npc.displayName) ? npc.id : npc.displayName;
                if (string.IsNullOrWhiteSpace(name) || FindByDisplayName(name) != null)
                {
                    continue;
                }

                var contact = MakeContact(
                    name,
                    Systems.Campus.NamedNpcDatabase.ParseGuestClass(npc.guestClass),
                    RosterGender(npc.gender),
                    npc.startsUnlocked);
                contact.bio = npc.summary;
                AddContact(contact);
            }
        }

        static ContactGender RosterGender(string gender)
        {
            if (string.Equals(gender, "girl", System.StringComparison.OrdinalIgnoreCase)) return ContactGender.Girl;
            if (string.Equals(gender, "boy", System.StringComparison.OrdinalIgnoreCase)) return ContactGender.Guy;
            return ContactGender.Both;
        }

        /// <summary>Lookup without triggering initialization (safe to call while seeding).</summary>
        static ContactSO FindByDisplayName(string displayName)
        {
            foreach (var c in _contacts)
            {
                if (c != null && string.Equals(c.displayName, displayName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }

            return null;
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
