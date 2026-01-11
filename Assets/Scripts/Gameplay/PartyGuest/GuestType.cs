using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Enumeration of all available guest types in the game.
    /// Each enum value corresponds to a unique TypeID for guest types.
    /// Add new guest types here when creating new GuestType ScriptableObjects.
    /// </summary>
    public enum GuestTypeEnum : uint
    {
        None = 0,
        MeanGirl = 1,
        VIP = 2,
        Influencer = 3,
        Jock = 4,
        Nerd = 5,
    }

    /// <summary>
    /// ScriptableObject defining a guest type with one or more passive abilities/effects.
    /// Each guest type has a name, description, and a list of abilities that activate when the guest enters the party.
    /// </summary>
    [CreateAssetMenu(fileName = "NewGuestType", menuName = "Party Game/Guest Type", order = 5)]
    public class GuestType : ScriptableObject
    {
        [Header("Guest Type Info")]
        [Tooltip("The name of this guest type (e.g., 'Celebrity', 'Influencer', 'VIP')")]
        public string TypeName = "Default Type";

        [Tooltip("Description of this guest type")]
        [TextArea(3, 6)]
        public string Description = "";

        [Header("Effect Data")]
        [Tooltip("The guest type enum value. This determines the TypeID used for runtime lookup.")]
        public GuestTypeEnum GuestTypeEnum = GuestTypeEnum.None;

        [Tooltip("Optional: Icon or visual representation of this guest type")]
        public Sprite TypeIcon;

        /// <summary>
        /// Gets the TypeID from the GuestTypeEnum value.
        /// This is used for runtime lookup and effect application.
        /// </summary>
        public uint TypeID => (uint)GuestTypeEnum;

        [Header("Abilities")]
        [Tooltip("List of passive abilities this guest type provides. All abilities are applied when the guest enters the party.")]
        public List<GuestAbility> Abilities = new List<GuestAbility>();

        /// <summary>
        /// Gets a summary of the guest type as a formatted string.
        /// </summary>
        public string GetTypeSummary()
        {
            string abilitiesSummary = Abilities != null && Abilities.Count > 0
                ? $"{Abilities.Count} ability(ies)"
                : "No abilities";
            return $"{TypeName}: {abilitiesSummary} - {Description}";
        }

        /// <summary>
        /// Gets the total number of abilities this guest type has.
        /// </summary>
        public int GetAbilityCount()
        {
            return Abilities != null ? Abilities.Count : 0;
        }

        /// <summary>
        /// Gets an ability by its AbilityID.
        /// </summary>
        /// <param name="abilityID">The AbilityID to look up</param>
        /// <returns>The GuestAbility with matching ID, or null if not found</returns>
        public GuestAbility GetAbilityByID(uint abilityID)
        {
            if (Abilities == null) return null;

            foreach (var ability in Abilities)
            {
                if (ability != null && ability.AbilityID == abilityID)
                {
                    return ability;
                }
            }
            return null;
        }

        protected virtual void OnValidate()
        {
            // Ensure TypeID is set if it's 0 (could be set manually or via registry)
            // TypeID should be unique per GuestType asset

            // Validate ability IDs are unique within this type
            if (Abilities != null && Abilities.Count > 0)
            {
                var abilityIDs = new HashSet<uint>();
                foreach (var ability in Abilities)
                {
                    if (ability != null && ability.AbilityID != 0)
                    {
                        if (abilityIDs.Contains(ability.AbilityID))
                        {
                            Debug.LogWarning($"GuestType '{TypeName}': Duplicate AbilityID {ability.AbilityID} found for ability '{ability.AbilityName}'!");
                        }
                        else
                        {
                            abilityIDs.Add(ability.AbilityID);
                        }
                    }
                }
            }
        }
    }
}
