using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Registry ScriptableObject that stores all Guest Type assets.
    /// Provides lookup by ID and name.
    /// </summary>
    [CreateAssetMenu(fileName = "GuestTypeRegistry", menuName = "Party Game/Guest Type Registry", order = 6)]
    public class GuestTypeRegistry : ScriptableObject
    {
        [Header("Guest Types")]
        [Tooltip("List of all guest type assets")]
        public List<GuestType> GuestTypes = new List<GuestType>();

        /// <summary>
        /// Gets a guest type by its TypeID.
        /// </summary>
        /// <param name="typeID">The TypeID of the guest type</param>
        /// <returns>The GuestType with matching TypeID, or null if not found</returns>
        public GuestType GetGuestTypeByID(uint typeID)
        {
            foreach (var guestType in GuestTypes)
            {
                if (guestType != null && guestType.TypeID == typeID)
                {
                    return guestType;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets a guest type by its name.
        /// </summary>
        /// <param name="typeName">The name of the guest type</param>
        /// <returns>The GuestType with matching name, or null if not found</returns>
        public GuestType GetGuestTypeByName(string typeName)
        {
            foreach (var guestType in GuestTypes)
            {
                if (guestType != null && guestType.TypeName == typeName)
                {
                    return guestType;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets a guest type by its enum value.
        /// </summary>
        /// <param name="typeEnum">The GuestTypeEnum value</param>
        /// <returns>The GuestType with matching enum value, or null if not found</returns>
        public GuestType GetGuestTypeByEnum(GuestTypeEnum typeEnum)
        {
            return GetGuestTypeByID((uint)typeEnum);
        }

        /// <summary>
        /// Validates the registry and logs warnings for issues.
        /// </summary>
        public void ValidateRegistry()
        {
            if (GuestTypes == null || GuestTypes.Count == 0)
            {
                Debug.LogWarning("GuestTypeRegistry: Registry is empty!");
                return;
            }

            var typeIDs = new HashSet<uint>();
            int nullCount = 0;

            foreach (var guestType in GuestTypes)
            {
                if (guestType == null)
                {
                    nullCount++;
                    continue;
                }

                if (typeIDs.Contains(guestType.TypeID))
                {
                    Debug.LogWarning($"GuestTypeRegistry: Duplicate TypeID {guestType.TypeID} found for '{guestType.TypeName}'!");
                }
                else
                {
                    typeIDs.Add(guestType.TypeID);
                }
            }

            if (nullCount > 0)
            {
                Debug.LogWarning($"GuestTypeRegistry: Found {nullCount} null entries in GuestTypes list!");
            }
        }

        private void OnValidate()
        {
            ValidateRegistry();
        }
    }
}
