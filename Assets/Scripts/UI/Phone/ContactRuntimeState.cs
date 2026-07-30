using System;

namespace Systems.Phone
{
    [Serializable]
    public class ContactRuntimeState
    {
        public SerializableGuid contactId;
        public bool unlocked;
        public float affinity;
        public bool invitedForNextParty;

        public ContactRuntimeState() { }

        public ContactRuntimeState(ContactSO contact)
        {
            contactId = contact.Id;
            unlocked = contact.startsUnlocked;
            affinity = 0f;
            invitedForNextParty = false;
        }
    }
}
