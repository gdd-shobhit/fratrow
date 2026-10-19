using System;
using System.Collections.Generic;
using FratRow.Game;
using FratRow.Social;
using UnityEngine;

namespace Systems.Campus
{
    /// <summary>
    /// Named campus NPCs. Precedence: <c>Resources/NamedNPCs</c> ScriptableObject assets, then the authored roster
    /// (<c>Resources/Campus/CampusRoster.json</c> + expanded <c>PersonalityArchetypes.json</c>, via
    /// <see cref="NetworkedCampaign.LoadRoster"/>), then the hardcoded legacy seeds when the roster cannot be loaded.
    /// Example: <c>var brad = NamedNpcDatabase.GetById("Brad");</c>
    /// </summary>
    public static class NamedNpcDatabase
    {
        public const string ResourcesPath = "NamedNPCs";
        static readonly List<NamedNpcSO> _npcs = new List<NamedNpcSO>();
        static readonly Dictionary<string, NamedNpcSO> _byId = new Dictionary<string, NamedNpcSO>();
        static bool _initialized;
        static CampusRoster _roster;
        static bool _rosterAttempted;

        /// <summary>
        /// The expanded campus roster (hand-written + archetype NPCs), parsed once and cached; null when it cannot be
        /// loaded (one warning is logged). Shared with <see cref="Systems.Phone.PhoneContactDatabase"/> so the JSON is parsed once.
        /// Example: <c>var roster = NamedNpcDatabase.Roster; if (roster != null) Debug.Log(roster.npcs.Count);</c>
        /// </summary>
        public static CampusRoster Roster
        {
            get
            {
                if (_rosterAttempted)
                {
                    return _roster;
                }

                _rosterAttempted = true;
                try
                {
                    _roster = NetworkedCampaign.LoadRoster();
                    if (_roster == null || _roster.npcs == null || _roster.npcs.Count == 0)
                    {
                        Debug.LogWarning("[NamedNpcDatabase] Campus roster is empty; using legacy seeds.");
                        _roster = null;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[NamedNpcDatabase] Campus roster could not be loaded; using legacy seeds. " + e.Message);
                    _roster = null;
                }

                return _roster;
            }
        }

        public static IReadOnlyList<NamedNpcSO> All
        {
            get
            {
                EnsureInitialized();
                return _npcs;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Init() => EnsureInitialized();

        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _npcs.Clear();
            _byId.Clear();

            var loaded = Resources.LoadAll<NamedNpcSO>(ResourcesPath);
            if (loaded != null && loaded.Length > 0)
            {
                foreach (var n in loaded)
                {
                    if (n != null)
                    {
                        Add(n);
                    }
                }
            }
            else
            {
                SeedDefaults();
            }
        }

        /// <summary>
        /// Maps a roster guest-class name ("None", "Nerd", "MeanGirl", "Jock"; case-insensitive) to <see cref="GuestClass"/>.
        /// Unknown or empty values map to <see cref="GuestClass.None"/>.
        /// Example: <c>NamedNpcDatabase.ParseGuestClass("MeanGirl") == GuestClass.MeanGirl</c>
        /// </summary>
        public static GuestClass ParseGuestClass(string value)
        {
            if (string.Equals(value, "Nerd", StringComparison.OrdinalIgnoreCase)) return GuestClass.Nerd;
            if (string.Equals(value, "MeanGirl", StringComparison.OrdinalIgnoreCase)) return GuestClass.MeanGirl;
            if (string.Equals(value, "Jock", StringComparison.OrdinalIgnoreCase)) return GuestClass.Jock;
            return GuestClass.None;
        }

        public static NamedNpcSO GetById(string id)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return _byId.TryGetValue(id, out var n) ? n : null;
        }

        public static NamedNpcSO GetByContactName(string displayName)
        {
            EnsureInitialized();
            foreach (var n in _npcs)
            {
                string key = string.IsNullOrWhiteSpace(n.contactDisplayName) ? n.displayName : n.contactDisplayName;
                if (string.Equals(key, displayName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return n;
                }
            }

            return null;
        }

        static void Add(NamedNpcSO npc)
        {
            if (string.IsNullOrWhiteSpace(npc.npcId))
            {
                return;
            }

            if (!_byId.TryAdd(npc.npcId, npc))
            {
                Debug.LogWarning($"[NamedNpcDatabase] Duplicate npcId {npc.npcId}");
                return;
            }

            _npcs.Add(npc);
        }

        static NamedNpcSO Make(
            string id,
            GuestClass guestClass,
            NamedNpcRarity rarity,
            float minPop,
            Grade minGrade,
            float unlockChance,
            float pop,
            float trouble,
            float cash,
            bool recruitable,
            params ScheduleEntry[] schedule)
        {
            var n = ScriptableObject.CreateInstance<NamedNpcSO>();
            n.name = id;
            n.npcId = id;
            n.displayName = id;
            n.contactDisplayName = id;
            n.guestClass = guestClass;
            n.rarity = rarity;
            n.unlockMinPopularity = minPop;
            n.unlockMinGrade = minGrade;
            n.unlockChance = unlockChance;
            n.basePopularity = pop;
            n.baseTrouble = trouble;
            n.baseCash = cash;
            n.isRecruitable = recruitable;
            n.schedule = new List<ScheduleEntry>(schedule);
            return n;
        }

        static ScheduleEntry S(int day, float start, float end, string loc) =>
            new ScheduleEntry { dayOfWeek = day, startHour = start, endHour = end, locationId = loc };

        static void SeedDefaults()
        {
            if (SeedFromRoster())
            {
                return;
            }

            SeedLegacyDefaults();
        }

        /// <summary>Builds runtime NamedNpcSO instances from the authored roster. False (nothing added) when unavailable.</summary>
        static bool SeedFromRoster()
        {
            var roster = Roster;
            if (roster == null)
            {
                return false;
            }

            var built = new List<NamedNpcSO>(roster.npcs.Count);
            try
            {
                foreach (var r in roster.npcs)
                {
                    if (r != null && !string.IsNullOrWhiteSpace(r.id))
                    {
                        built.Add(FromRoster(r));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NamedNpcDatabase] Campus roster could not be mapped; using legacy seeds. " + e.Message);
                foreach (var n in built)
                {
                    UnityEngine.Object.DestroyImmediate(n);
                }

                return false;
            }

            foreach (var n in built)
            {
                Add(n);
            }

            return _npcs.Count > 0;
        }

        static NamedNpcSO FromRoster(RosterNpc r)
        {
            string display = string.IsNullOrWhiteSpace(r.displayName) ? r.id : r.displayName;
            var n = ScriptableObject.CreateInstance<NamedNpcSO>();
            n.name = r.id;
            n.npcId = r.id;
            n.displayName = display;
            n.contactDisplayName = display;
            n.guestClass = ParseGuestClass(r.guestClass);
            n.rarity = ParseRarity(r.rarity);
            n.unlockMinPopularity = r.unlockMinPopularity;
            n.unlockMinGrade = ParseGrade(r.unlockMinGrade);
            n.unlockChance = Mathf.Clamp01(r.unlockChance);
            n.basePopularity = r.popularity;
            n.baseTrouble = r.partyTrouble;
            n.baseCash = r.partyCash;
            n.isRecruitable = r.isPnm;
            n.recruitMinAffinity = r.recruitMinAffinity;
            n.socialStatusConcern = CampusRoster.Concern(r);
            n.socialPersona = r.persona;
            n.socialVoice = SocialVoice.Copy(r.voice);
            n.schedule = new List<ScheduleEntry>(r.schedule != null ? r.schedule.Count : 0);
            if (r.schedule != null)
            {
                foreach (var e in r.schedule)
                {
                    if (e != null)
                    {
                        n.schedule.Add(S(e.day, e.start, e.end, e.location));
                    }
                }
            }

            return n;
        }

        static NamedNpcRarity ParseRarity(string value)
        {
            if (string.Equals(value, "Rare", StringComparison.OrdinalIgnoreCase)) return NamedNpcRarity.Rare;
            if (string.Equals(value, "Legendary", StringComparison.OrdinalIgnoreCase)) return NamedNpcRarity.Legendary;
            return NamedNpcRarity.Common;
        }

        /// <summary>Letter grade A..F (worst fraternity grade allowed to unlock); anything else means no gate (F).</summary>
        static Grade ParseGrade(string value)
        {
            string v = value?.Trim();
            if (string.IsNullOrEmpty(v) || v.Length != 1)
            {
                return Grade.F;
            }

            switch (char.ToUpperInvariant(v[0]))
            {
                case 'A': return Grade.A;
                case 'B': return Grade.B;
                case 'C': return Grade.C;
                case 'D': return Grade.D;
                case 'E': return Grade.E;
                default: return Grade.F;
            }
        }

        /// <summary>Legacy hardcoded seeds, used only when the roster JSON is missing or invalid.</summary>
        static void SeedLegacyDefaults()
        {
            // 15 named NPCs with Stardew-style schedules across weekdays.
            Add(Make("Brad", GuestClass.Jock, NamedNpcRarity.Common, 0, Grade.F, 1f, 15, 10, 5, false,
                S(0, 8, 12, "Gym"), S(0, 12, 18, "Quad"), S(1, 8, 16, "Gym"), S(2, 10, 18, "PartyHouse"),
                S(3, 8, 14, "Gym"), S(4, 12, 20, "Shop"), S(5, 10, 22, "PartyHouse"), S(6, 12, 18, "Quad")));

            Add(Make("Tyler", GuestClass.Jock, NamedNpcRarity.Common, 0, Grade.F, 1f, 12, 18, 4, false,
                S(0, 10, 16, "Gym"), S(1, 12, 18, "Quad"), S(2, 8, 14, "Gym"), S(3, 14, 20, "Shop"),
                S(4, 10, 18, "PartyHouse"), S(5, 12, 22, "PartyHouse"), S(6, 10, 16, "Gym")));

            Add(Make("Megan", GuestClass.MeanGirl, NamedNpcRarity.Rare, 25, Grade.D, 0.7f, 20, 25, 8, false,
                S(0, 10, 14, "Library"), S(0, 14, 20, "Quad"), S(1, 12, 18, "Shop"), S(2, 10, 16, "Library"),
                S(3, 14, 20, "PartyHouse"), S(4, 10, 18, "Quad"), S(5, 14, 22, "PartyHouse"), S(6, 12, 18, "Shop")));

            Add(Make("Ashley", GuestClass.MeanGirl, NamedNpcRarity.Common, 5, Grade.F, 0.9f, 10, 20, 6, false,
                S(0, 12, 18, "Shop"), S(1, 10, 16, "Quad"), S(2, 14, 20, "Library"), S(3, 10, 16, "Shop"),
                S(4, 12, 18, "Quad"), S(5, 16, 22, "PartyHouse"), S(6, 10, 16, "Library")));

            Add(Make("Kevin", GuestClass.Nerd, NamedNpcRarity.Common, 0, Grade.F, 1f, 8, -5, 3, true,
                S(0, 8, 18, "Library"), S(1, 8, 18, "Library"), S(2, 10, 16, "Quad"), S(3, 8, 18, "Library"),
                S(4, 12, 18, "Shop"), S(5, 14, 20, "PartyHouse"), S(6, 10, 16, "Library")));

            Add(Make("Priya", GuestClass.Nerd, NamedNpcRarity.Common, 0, Grade.F, 1f, 10, -8, 4, true,
                S(0, 9, 17, "Library"), S(1, 12, 18, "Quad"), S(2, 9, 17, "Library"), S(3, 10, 16, "Shop"),
                S(4, 9, 17, "Library"), S(5, 15, 21, "PartyHouse"), S(6, 11, 17, "Quad")));

            Add(Make("Sam", GuestClass.None, NamedNpcRarity.Common, 0, Grade.F, 1f, 5, 5, 5, false,
                S(0, 10, 16, "Quad"), S(1, 10, 16, "Shop"), S(2, 10, 16, "Gym"), S(3, 10, 16, "Quad"),
                S(4, 10, 16, "Library"), S(5, 12, 22, "PartyHouse"), S(6, 10, 18, "Quad")));

            Add(Make("Jordan", GuestClass.None, NamedNpcRarity.Rare, 30, Grade.C, 0.55f, 18, 12, 7, true,
                S(0, 11, 17, "Dorm"), S(1, 10, 16, "Quad"), S(2, 12, 18, "Shop"), S(3, 10, 16, "Library"),
                S(4, 14, 20, "PartyHouse"), S(5, 16, 23, "PartyHouse"), S(6, 12, 18, "Dorm")));

            Add(Make("Alex", GuestClass.Jock, NamedNpcRarity.Common, 10, Grade.E, 0.85f, 14, 8, 5, false,
                S(0, 8, 14, "Gym"), S(1, 8, 14, "Gym"), S(2, 12, 18, "Quad"), S(3, 8, 14, "Gym"),
                S(4, 12, 18, "Shop"), S(5, 14, 22, "PartyHouse"), S(6, 10, 16, "Gym")));

            Add(Make("Riley", GuestClass.MeanGirl, NamedNpcRarity.Common, 5, Grade.F, 0.9f, 12, 15, 6, false,
                S(0, 12, 18, "Quad"), S(1, 10, 16, "Shop"), S(2, 12, 18, "Library"), S(3, 14, 20, "Quad"),
                S(4, 10, 16, "Dorm"), S(5, 15, 22, "PartyHouse"), S(6, 11, 17, "Shop")));

            Add(Make("Casey", GuestClass.None, NamedNpcRarity.Legendary, 60, Grade.B, 0.35f, 35, 30, 15, true,
                S(0, 14, 18, "Library"), S(1, 16, 20, "Quad"), S(2, 14, 18, "Shop"), S(3, 16, 20, "Dorm"),
                S(4, 18, 22, "PartyHouse"), S(5, 20, 24, "PartyHouse"), S(6, 14, 20, "Gym")));

            Add(Make("Morgan", GuestClass.Nerd, NamedNpcRarity.Rare, 20, Grade.D, 0.65f, 16, 0, 8, true,
                S(0, 9, 15, "Library"), S(1, 9, 15, "Library"), S(2, 12, 18, "Quad"), S(3, 9, 15, "Library"),
                S(4, 12, 18, "Shop"), S(5, 16, 21, "PartyHouse"), S(6, 10, 16, "Library")));

            Add(Make("Jamie", GuestClass.Jock, NamedNpcRarity.Common, 0, Grade.F, 1f, 11, 14, 4, false,
                S(0, 10, 16, "Gym"), S(1, 12, 18, "Quad"), S(2, 10, 16, "Gym"), S(3, 12, 18, "Shop"),
                S(4, 10, 16, "Gym"), S(5, 14, 22, "PartyHouse"), S(6, 11, 17, "Quad")));

            Add(Make("Avery", GuestClass.MeanGirl, NamedNpcRarity.Rare, 35, Grade.C, 0.5f, 22, 28, 9, false,
                S(0, 13, 19, "Shop"), S(1, 11, 17, "Quad"), S(2, 13, 19, "Library"), S(3, 11, 17, "Dorm"),
                S(4, 15, 21, "PartyHouse"), S(5, 17, 23, "PartyHouse"), S(6, 12, 18, "Shop")));

            Add(Make("Quinn", GuestClass.None, NamedNpcRarity.Common, 8, Grade.E, 0.9f, 9, 6, 5, true,
                S(0, 10, 16, "Dorm"), S(1, 10, 16, "Library"), S(2, 10, 16, "Quad"), S(3, 10, 16, "Shop"),
                S(4, 10, 16, "Gym"), S(5, 13, 21, "PartyHouse"), S(6, 11, 17, "Dorm")));

            Add(Make("Drew", GuestClass.Jock, NamedNpcRarity.Rare, 28, Grade.C, 0.6f, 17, 16, 7, false,
                S(0, 9, 15, "Gym"), S(1, 11, 17, "Quad"), S(2, 9, 15, "Gym"), S(3, 13, 19, "Shop"),
                S(4, 11, 17, "Library"), S(5, 15, 22, "PartyHouse"), S(6, 10, 16, "Gym")));
        }
    }
}
