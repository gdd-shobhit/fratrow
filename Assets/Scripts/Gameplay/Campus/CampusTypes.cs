using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Systems.Campus
{
    /// <summary>
    /// The scene where the open campus, its NPCs and the campaign loop live (LobbyManager loads it after Start Game).
    /// GameManager persists from the main menu, so campus systems must not build visuals or tick in menu/lobby scenes.
    /// </summary>
    public static class CampusScene
    {
        public const string Name = "Game";
        public static bool IsActive => SceneManager.GetActiveScene().name == Name;
    }

    public enum GamePhase : byte
    {
        Weekday = 0,
        Party = 1,
        Debrief = 2,
        SemesterEnd = 3
    }

    public enum NamedNpcRarity : byte
    {
        Common = 0,
        Rare = 1,
        Legendary = 2
    }

    [Serializable]
    public struct ScheduleEntry
    {
        [Range(0, 6)] public int dayOfWeek;
        [Range(0f, 24f)] public float startHour;
        [Range(0f, 24f)] public float endHour;
        public string locationId;
    }
}
