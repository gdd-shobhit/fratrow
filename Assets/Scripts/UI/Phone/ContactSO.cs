using UnityEngine;

namespace Systems.Phone
{
    public enum ContactGender
    {
        Unspecified = 0,
        Guy = 1,
        Girl = 2,
        Both = 3
    }

    [CreateAssetMenu(fileName = "Contact", menuName = "Phone/Contact")]
    public class ContactSO : ScriptableObject
    {
        [SerializeField] SerializableGuid id;
        public string displayName;
        public ContactGender gender = ContactGender.Unspecified;
        public GuestClass linkedGuestClass = GuestClass.None;
        public Sprite avatar;
        public bool startsUnlocked = true;
        [TextArea(2, 4)] public string bio;

        public SerializableGuid Id
        {
            get
            {
                EnsureId();
                return id;
            }
        }

        public string ArchetypeTag => linkedGuestClass.ToString();

        public void EnsureId()
        {
            if (id == SerializableGuid.Empty)
            {
                id = SerializableGuid.NewGuid();
            }
        }

        public void SetId(SerializableGuid newId)
        {
            id = newId;
        }

        void OnValidate() => EnsureId();
    }
}
