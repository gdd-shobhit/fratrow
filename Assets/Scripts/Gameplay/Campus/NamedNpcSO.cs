using System.Collections.Generic;
using FratRow.Social;
using Systems.Phone;
using UnityEngine;

namespace Systems.Campus
{
    [CreateAssetMenu(fileName = "NamedNpc", menuName = "Campus/Named NPC")]
    public class NamedNpcSO : ScriptableObject
    {
        public string npcId = "Brad";
        public string displayName = "Brad";
        public GuestClass guestClass = GuestClass.Jock;
        public string contactDisplayName;
        public string yarnNodeName;

        public NamedNpcRarity rarity = NamedNpcRarity.Common;
        public float unlockMinPopularity;
        public Grade unlockMinGrade = Grade.F;
        [Range(0f, 1f)] public float unlockChance = 1f;

        public float basePopularity;
        public float baseTrouble;
        public float baseCash;
        public bool isRecruitable;
        public float recruitMinAffinity = 40f;

        [Header("Social brain authoring")]
        public StatusConcern socialStatusConcern = StatusConcern.Moderate;
        [TextArea] public string socialPersona;
        public NpcVoiceProfile socialVoice;

        public List<ScheduleEntry> schedule = new List<ScheduleEntry>();

        public string YarnNode => string.IsNullOrWhiteSpace(yarnNodeName) ? npcId : yarnNodeName;

        public ContactSO ResolveContact()
        {
            string key = string.IsNullOrWhiteSpace(contactDisplayName) ? displayName : contactDisplayName;
            return PhoneContactDatabase.GetByDisplayName(key);
        }

        public bool TryGetLocationFor(int dayOfWeek, float inGameHour, out string locationId)
        {
            locationId = null;
            if (schedule == null)
            {
                return false;
            }

            foreach (var entry in schedule)
            {
                if (entry.dayOfWeek != dayOfWeek)
                {
                    continue;
                }

                float start = entry.startHour;
                float end = entry.endHour;
                bool inRange = start <= end
                    ? inGameHour >= start && inGameHour < end
                    : inGameHour >= start || inGameHour < end;

                if (inRange)
                {
                    locationId = entry.locationId;
                    return !string.IsNullOrWhiteSpace(locationId);
                }
            }

            return false;
        }

        public bool MeetsUnlockGates(float fratPopularity, Grade fratGrade)
        {
            if (fratPopularity < unlockMinPopularity)
            {
                return false;
            }

            // Grade enum: A=0 best ... F=5 worst. Lower ordinal = better.
            if ((int)fratGrade > (int)unlockMinGrade)
            {
                return false;
            }

            return true;
        }
    }
}
