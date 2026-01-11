using UnityEngine;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Base abstract ScriptableObject containing the core stats shared by all characters (players and NPCs).
    /// </summary>
    public abstract class BaseCharacterStats : ScriptableObject
    {
        [Header("Character Info")]
        [Tooltip("The name of this character")]
        public string CharacterName = "Unnamed Character";

        [Header("Character Stats")]
        [Tooltip("How popular this character is (0.0 to 100.0)")]
        [Range(0f, 100f)]
        public float Popularity = 50f;

        [Tooltip("How much of a trouble maker this person is (0.0 to 100.0)")]
        [Range(0f, 100f)]
        public float TroubleMakerRating = 50f;

        [Tooltip("How much cash/wealth this person brings (0.0 to 100.0)")]
        [Range(0f, 100f)]
        public float CashValue = 50f;

        /// <summary>
        /// Validates that all stats are within valid ranges.
        /// </summary>
        public virtual void ValidateStats()
        {
            Popularity = Mathf.Clamp(Popularity, 0f, 100f);
            TroubleMakerRating = Mathf.Clamp(TroubleMakerRating, 0f, 100f);
            CashValue = Mathf.Clamp(CashValue, 0f, 100f);
        }

        /// <summary>
        /// Gets a summary of the character's stats as a formatted string.
        /// </summary>
        public virtual string GetStatsSummary()
        {
            return $"{CharacterName}: Popularity={Popularity:F1}, Trouble={TroubleMakerRating:F1}, Cash={CashValue:F1}";
        }

        public virtual void OnValidate()
        {
            ValidateStats();
        }
    }
}
