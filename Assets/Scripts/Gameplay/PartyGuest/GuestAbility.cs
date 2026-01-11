using System;
using UnityEngine;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Serializable class representing a single ability/effect that a guest type can provide.
    /// Multiple abilities can be attached to a single guest type.
    /// </summary>
    [Serializable]
    public class GuestAbility
    {
        [Header("Ability Info")]
        [Tooltip("The name of this ability")]
        public string AbilityName = "New Ability";

        [Tooltip("Description of what this ability does")]
        [TextArea(2, 4)]
        public string AbilityDescription = "";

        [Header("Ability Identifier")]
        [Tooltip("Unique identifier for this ability within the guest type (used for runtime lookup and effect application)")]
        public uint AbilityID = 0;

        [Tooltip("Optional: Icon or visual representation of this ability")]
        public Sprite AbilityIcon;

        [Header("Ability Value")]
        [Tooltip("The numeric value for this ability. Used when applying the ability effect (e.g., bonus to popularity, cash generation, etc.)")]
        public float AbilityValue = 0f;

        /// <summary>
        /// Gets a summary of the ability as a formatted string.
        /// </summary>
        public string GetAbilitySummary()
        {
            return $"{AbilityName}: {AbilityDescription}";
        }
    }
}
