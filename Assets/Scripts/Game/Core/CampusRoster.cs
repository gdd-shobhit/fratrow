using System;
using System.Collections.Generic;
using System.Linq;
using FratRow.Social;

namespace FratRow.Game
{
    /// <summary>One weekly schedule slot. dayOfWeek 0=Mon..6=Sun; hours are in-game (overnight when start &gt; end).</summary>
    [Serializable] public sealed class RosterScheduleEntry
    {
        public int day;
        public float start;
        public float end;
        public string location;
    }

    /// <summary>Starting opinion of a rival house, -100..100. Players' house regard lives in the social backend.</summary>
    [Serializable] public sealed class RosterHouseAffinity
    {
        public string houseId;
        public int value;
    }

    /// <summary>
    /// Authored campus NPC. The single source for Unity's <c>NamedNpcDatabase</c>/<c>PhoneContactDatabase</c>
    /// seed defaults, the game core and the social backend.
    /// Example: <c>{"id":"Ashley","gender":"girl","guestClass":"MeanGirl","popularity":10,"persona":"...","voice":{"tone":"assertive"}}</c>
    /// </summary>
    [Serializable] public sealed class RosterNpc
    {
        public string id;
        public string displayName;
        /// <summary>"girl", "boy" or "unspecified" — identity only; persona drives behaviour.</summary>
        public string gender = "unspecified";
        /// <summary>Unity GuestClass name: None, Nerd, MeanGirl, Jock.</summary>
        public string guestClass = "None";
        /// <summary>Common, Rare, Legendary.</summary>
        public string rarity = "Common";
        public string summary;
        public string persona;
        public NpcVoiceProfile voice = new NpcVoiceProfile();
        /// <summary>Public campus popularity 0..100 (same scale as house popularity).</summary>
        public float popularity = 10;
        /// <summary>Low, Moderate, High.</summary>
        public string statusConcern = "Moderate";
        /// <summary>Party contribution when attending (existing NamedNpcSO semantics).</summary>
        public float partyTrouble;
        public float partyCash = 5;
        public bool startsUnlocked;
        public float unlockMinPopularity;
        /// <summary>Worst fraternity grade allowed to unlock, A..F.</summary>
        public string unlockMinGrade = "F";
        public float unlockChance = 1f;
        /// <summary>Potential new member: can be rushed and offered a bid.</summary>
        public bool isPnm;
        public float recruitMinAffinity = 40f;
        /// <summary>Freshman/underage: alcohol served to them is a party crime.</summary>
        public bool underage;
        public float gpa = 3.0f;
        public List<RosterHouseAffinity> rivalAffinity = new List<RosterHouseAffinity>();
        public List<RosterScheduleEntry> schedule = new List<RosterScheduleEntry>();
    }

    /// <summary>
    /// Personality type expanded into <see cref="count"/> distinct, individually named NPCs (each with its own social memory).
    /// Persona placeholders: {name}, {they}, {them}, {their}, {They}, {Their}.
    /// </summary>
    [Serializable] public sealed class PersonalityArchetype
    {
        public string id;
        public string summary;
        public string personaTemplate;
        /// <summary>Allowed genders for copies: "girl"/"boy". PNM copies are always boys.</summary>
        public List<string> genders = new List<string> { "girl", "boy" };
        public int count = 2;
        public string guestClass = "None";
        public string rarity = "Common";
        public List<string> tones = new List<string> { "casual" };
        public List<string> slangLevels = new List<string> { "light" };
        public List<string> statusConcerns = new List<string> { "Moderate" };
        /// <summary>Gender-neutral example lines; each copy gets up to two distinct lines.</summary>
        public List<string> exampleLinePool = new List<string>();
        public float popularityMin = 5, popularityMax = 25;
        public float partyTroubleMin = 0, partyTroubleMax = 20;
        public float partyCashMin = 2, partyCashMax = 8;
        public float gpaMin = 2.5f, gpaMax = 3.5f;
        /// <summary>Chance a boy copy is a PNM.</summary>
        public float pnmChance;
        public float underageChance = 0.3f;
        public float unlockChance = 0.6f;
        public float unlockMinPopularity;
        public bool startsUnlocked;
        /// <summary>Weekday hangouts used to generate schedules (location ids).</summary>
        public List<string> favoriteLocations = new List<string> { "Quad", "Library", "Shop" };
        public List<RosterHouseAffinity> rivalLean = new List<RosterHouseAffinity>();
    }

    /// <summary>Root of <c>Assets/Resources/Campus/PersonalityArchetypes.json</c> (the generated NPC tier).</summary>
    [Serializable] public sealed class ArchetypeCatalog
    {
        public int schema = 1;
        public int seed = 1337;
        public List<string> boyNames = new List<string>();
        public List<string> girlNames = new List<string>();
        public List<PersonalityArchetype> archetypes = new List<PersonalityArchetype>();

        public static ArchetypeCatalog Parse(string json, IStateCodec codec) => codec.Decode<ArchetypeCatalog>(json) ?? new ArchetypeCatalog();
    }

    /// <summary>Root of <c>Assets/Resources/Campus/CampusRoster.json</c>.</summary>
    [Serializable] public sealed class CampusRoster
    {
        public int schema = 1;
        public List<RosterNpc> npcs = new List<RosterNpc>();

        /// <summary>Parses and validates roster JSON with the host's codec.</summary>
        public static CampusRoster Parse(string json, IStateCodec codec)
        {
            var roster = codec.Decode<CampusRoster>(json) ?? throw new FormatException("Empty campus roster");
            roster.Normalize();
            roster.Validate();
            return roster;
        }

        public RosterNpc Get(string id) => npcs.Find(n => n.id == id);

        public void Normalize()
        {
            if (npcs == null) npcs = new List<RosterNpc>();
            foreach (var n in npcs)
            {
                if (string.IsNullOrWhiteSpace(n.displayName)) n.displayName = n.id;
                if (n.voice == null) n.voice = new NpcVoiceProfile();
                if (n.voice.exampleLines == null) n.voice.exampleLines = Array.Empty<string>();
                if (n.voice.gender == "unspecified" && (n.gender == "girl" || n.gender == "boy")) n.voice.gender = n.gender;
                if (n.rivalAffinity == null) n.rivalAffinity = new List<RosterHouseAffinity>();
                if (n.schedule == null) n.schedule = new List<RosterScheduleEntry>();
                if (string.IsNullOrWhiteSpace(n.summary)) n.summary = n.displayName;
            }
        }

        public void Validate()
        {
            var ids = new HashSet<string>();
            foreach (var n in npcs)
            {
                if (string.IsNullOrWhiteSpace(n.id) || n.id.Length > 48 || !ids.Add(n.id)) throw new FormatException("Invalid or duplicate roster id: " + n.id);
                if (string.IsNullOrWhiteSpace(n.persona) || n.persona.Length > 900) throw new FormatException("Roster persona missing/too long: " + n.id);
                SocialVoice.Validate(n.voice);
                if (!Enum.TryParse<StatusConcern>(n.statusConcern, out _)) throw new FormatException("Invalid statusConcern: " + n.id);
                if (n.popularity < 0 || n.popularity > 100) throw new FormatException("Roster popularity must be 0..100: " + n.id);
                foreach (var s in n.schedule)
                    if (s.day < 0 || s.day > 6 || s.start < 0 || s.start > 24 || s.end < 0 || s.end > 24 || string.IsNullOrWhiteSpace(s.location))
                        throw new FormatException("Invalid schedule entry: " + n.id);
            }
        }

        /// <summary>Location the NPC is scheduled at, or null when off-campus/unscheduled.</summary>
        public static string LocationAt(RosterNpc npc, int day, float hour)
        {
            foreach (var e in npc.schedule)
            {
                if (e.day != day) continue;
                bool inRange = e.start <= e.end ? hour >= e.start && hour < e.end : hour >= e.start || hour < e.end;
                if (inRange) return e.location;
            }
            return null;
        }

        public static StatusConcern Concern(RosterNpc npc) =>
            Enum.TryParse<StatusConcern>(npc.statusConcern, out var c) ? c : StatusConcern.Moderate;

        public IEnumerable<RosterNpc> Pnms => npcs.Where(n => n.isPnm);

        /// <summary>
        /// Appends the generated tier: every archetype becomes <c>count</c> named NPCs. Deterministic for a catalog seed,
        /// so the same names/personas come back on every load (saved social memory stays attached to the same people).
        /// Example: <c>roster.Expand(ArchetypeCatalog.Parse(json, codec));</c>
        /// </summary>
        public void Expand(ArchetypeCatalog catalog)
        {
            if (catalog?.archetypes == null || catalog.archetypes.Count == 0) return;
            var used = new HashSet<string>(npcs.Select(n => n.id), StringComparer.OrdinalIgnoreCase);
            for (int a = 0; a < catalog.archetypes.Count; a++)
            {
                var t = catalog.archetypes[a];
                if (string.IsNullOrWhiteSpace(t.id) || string.IsNullOrWhiteSpace(t.personaTemplate)) continue;
                for (int copy = 0; copy < Math.Max(0, t.count); copy++)
                {
                    var rng = GameRng.For(catalog.seed, "archetype:" + t.id, copy);
                    var genders = (t.genders ?? new List<string>()).Where(g => g == "girl" || g == "boy").ToList();
                    if (genders.Count == 0) genders.Add("girl");
                    string gender = genders[rng.Range(0, genders.Count)];
                    bool pnm = gender == "boy" && rng.Chance(t.pnmChance);
                    var pool = gender == "boy" ? catalog.boyNames : catalog.girlNames;
                    string name = PickName(pool, used, rng);
                    if (name == null) break; // name pool exhausted
                    used.Add(name);
                    bool he = gender == "boy";
                    string persona = t.personaTemplate.Replace("{name}", name)
                        .Replace("{They}", he ? "He" : "She").Replace("{Their}", he ? "His" : "Her")
                        .Replace("{they}", he ? "he" : "she").Replace("{them}", he ? "him" : "her").Replace("{their}", he ? "his" : "her");
                    var lines = (t.exampleLinePool ?? new List<string>()).OrderBy(_ => rng.NextULong()).Take(2).ToArray();
                    var npc = new RosterNpc
                    {
                        id = name, displayName = name, gender = gender, guestClass = t.guestClass ?? "None", rarity = t.rarity ?? "Common",
                        summary = t.summary, persona = persona.Length > 900 ? persona.Substring(0, 900) : persona,
                        voice = new NpcVoiceProfile { gender = gender, tone = Pick(t.tones, "casual", rng), slang = Pick(t.slangLevels, "light", rng), exampleLines = lines },
                        popularity = (float)Math.Round(rng.Range(t.popularityMin, t.popularityMax)),
                        statusConcern = Pick(t.statusConcerns, "Moderate", rng),
                        partyTrouble = (float)Math.Round(rng.Range(t.partyTroubleMin, t.partyTroubleMax)),
                        partyCash = (float)Math.Round(rng.Range(t.partyCashMin, t.partyCashMax)),
                        startsUnlocked = t.startsUnlocked, unlockChance = t.unlockChance, unlockMinPopularity = t.unlockMinPopularity,
                        isPnm = pnm, underage = rng.Chance(pnm ? Math.Max(0.6f, t.underageChance) : t.underageChance),
                        gpa = (float)Math.Round(rng.Range(t.gpaMin, t.gpaMax), 2),
                        rivalAffinity = (t.rivalLean ?? new List<RosterHouseAffinity>()).Select(l => new RosterHouseAffinity { houseId = l.houseId, value = l.value + rng.Range(-8, 9) }).ToList(),
                        schedule = GenerateSchedule(t.favoriteLocations, rng)
                    };
                    npcs.Add(npc);
                }
            }
            Normalize();
            Validate();
        }

        static string Pick(List<string> options, string fallback, GameRng rng) =>
            options == null || options.Count == 0 ? fallback : options[rng.Range(0, options.Count)];

        static string PickName(List<string> pool, HashSet<string> used, GameRng rng)
        {
            var free = (pool ?? new List<string>()).Where(n => !string.IsNullOrWhiteSpace(n) && n.All(char.IsLetterOrDigit) && !used.Contains(n)).ToList();
            return free.Count == 0 ? null : free[rng.Range(0, free.Count)];
        }

        /// <summary>Two or three weekday slots per day (Mon–Fri, 08:00–20:00) across the archetype's favourite locations.</summary>
        static List<RosterScheduleEntry> GenerateSchedule(List<string> favorites, GameRng rng)
        {
            var locations = favorites == null || favorites.Count == 0 ? new List<string> { "Quad" } : favorites;
            var schedule = new List<RosterScheduleEntry>();
            for (int day = 0; day < 5; day++)
            {
                float hour = 8 + rng.Range(0, 3);
                int slots = rng.Range(2, 4);
                for (int s = 0; s < slots && hour < 20; s++)
                {
                    float end = Math.Min(20, hour + rng.Range(2, 5));
                    schedule.Add(new RosterScheduleEntry { day = day, start = hour, end = end, location = locations[rng.Range(0, locations.Count)] });
                    hour = end + rng.Range(0, 2);
                }
            }
            return schedule;
        }
    }
}
