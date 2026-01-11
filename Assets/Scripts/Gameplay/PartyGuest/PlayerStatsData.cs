using UnityEngine;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// ScriptableObject containing stats data for player characters.
    /// Inherits base stats from BaseCharacterStats.
    /// </summary>
    [CreateAssetMenu(fileName = "NewPlayerStatsData", menuName = "Party Game/Player Stats Data", order = 1)]
    public class PlayerStatsData : BaseCharacterStats
    {
        [Header("Player Specific")]
        [Tooltip("Optional: Player ID or identifier for this stats preset")]
        public int PlayerID = 0;

        [Tooltip("Whether this is the default player stats used for all players")]
        public bool IsDefaultStats = false;

        public override void OnValidate()
        {
            base.OnValidate();
        }
    }
}
