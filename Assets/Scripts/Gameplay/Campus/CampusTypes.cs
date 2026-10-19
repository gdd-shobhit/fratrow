using System;
using UnityEngine;

namespace Systems.Campus
{
    public enum GamePhase : byte
    {
        Weekday = 0,
        Party = 1,
        Debrief = 2
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
