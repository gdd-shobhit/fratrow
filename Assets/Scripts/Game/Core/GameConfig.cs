using System;
using System.Collections.Generic;
using System.Linq;
using FratRow.Social;

namespace FratRow.Game
{
    /// <summary>A fraternity house definition. Personality values are 0..1 and drive rival AI.</summary>
    [Serializable] public sealed class HouseDef
    {
        public string id;
        public string name;
        public string nickname;
        public bool isPlayer;
        public string homeLocation;
        public float popularity;
        public int cash;
        /// <summary>NPC/abstract brothers at start (players are counted separately).</summary>
        public int members;
        public float dean = 60;
        public float heat;
        public float gpa = 3.0f;
        public float prestige = 0.5f, chaos = 0.5f, aggression = 0.5f, caution = 0.5f;
    }

    /// <summary>Fraternity office held by a player. Multipliers are 1 when not applicable.</summary>
    [Serializable] public sealed class RoleDef
    {
        public string id;
        public string name;
        public string description;
        /// <summary>Lobby PlayerClass name mapped to this role on first join (empty = none).</summary>
        public string lobbyClass = "";
        public bool unique = true;
        public float shopDiscount;
        public float workPayMult = 1, rushMult = 1, djMult = 1, fillerMult = 1, cardCheckMult = 1;
        public float policeTalkBonus, heatDecayBonus, incidentResolveBonus;
        public bool canMeetDean, canSetTheme, canExtendBid, canSetCover, canStartWeekend;
    }

    /// <summary>Party supply sold by the campus shop into the house stash.</summary>
    [Serializable] public sealed class ShopItemDef
    {
        public string id;
        public string name;
        /// <summary>drink, gear, decor, security, crime, service.</summary>
        public string category;
        public int price;
        public int servings;
        public float strength;
        public bool alcoholic;
        public float hype, noise, vibe, mood;
        /// <summary>Persistent gear is not consumed by a party (e.g. speakers).</summary>
        public bool persistent;
        /// <summary>Automatic card-check coverage 0..1 (security).</summary>
        public float cardCheck;
        public float troubleMult = 1;
        public float heatReduction;
        public int maxStack = 20;
    }

    [Serializable] public sealed class ThemeDef
    {
        public string id;
        public string name;
        public float vibe = 5;
        /// <summary>GuestClass names that like this theme (arrival weight ×1.5).</summary>
        public List<string> likedBy = new List<string>();
    }

    /// <summary>Crime/sabotage definition. Effects apply only when not detected.</summary>
    [Serializable] public sealed class CrimeDef
    {
        public string id;
        public string name;
        /// <summary>"full" crimes can bust; "lite" crimes risk only trace penalties.</summary>
        public string severity = "full";
        public float baseDetect = 0.1f;
        public string requiresItem = "";
        /// <summary>rival_house, any, npc.</summary>
        public string where = "rival_house";
        /// <summary>weekday, party, both.</summary>
        public string phase = "both";
        public float minHour = -1, maxHour = -1;
        public float actorPopGain, rivalPopLoss, rivalDeanLoss, rivalHeat, rivalAttention, rivalMoodLoss;
        public string stashGain = "";
        public int stashGainCount;
        public float rumorRegardLoss;
        public string newsText = "{actor} hit {rival}.";
    }

    [Serializable] public sealed class LocationDef
    {
        public string id;
        public string name;
        /// <summary>house, shop, library, gym, quad, dorm.</summary>
        public string kind;
        public string houseId = "";
    }

    /// <summary>Filler guest archetype. Mirrors Unity GuestSO stats (overridable at runtime from assets).</summary>
    [Serializable] public sealed class GuestArchetypeDef
    {
        public int guestClass;
        public string name;
        public float popularity, trouble, cash;
        public float baseArrivalChance = 50, popularityModifier;
    }

    [Serializable] public sealed class PartyTuning
    {
        public int baseCapacity = 30;
        public int capacityPerMember = 1;
        /// <summary>Extra capacity from members is capped so big houses don't scale without bound.</summary>
        public int capacityMemberCap = 20;
        /// <summary>Score penalty per incident left to escalate vs. per incident the brothers handled.</summary>
        public float incidentPenaltyEscalated = 2f, incidentPenaltyHandled = 0.25f;
        /// <summary>Abstract rival parties: base fill share of capacity before popularity/budget.</summary>
        public float rivalBaseFill = 0.2f;
        /// <summary>Ticks between DJ boosts (prevents spamming hype to the cap).</summary>
        public int djCooldownTicks = 4;
        /// <summary>Hype decays by this flat amount plus a share of current hype each tick.</summary>
        public float hypeProportionalDecay = 0.04f;
        public float baseFillerRate = 1.1f;
        public float underageShare = 0.3f;
        public float drinkChance = 0.35f;
        public float startMood = 55, leaveMood = 22, leaveChance = 0.2f;
        public float committedArrivalWindow = 0.25f;
        public float hypeDecay = 2.5f, djHype = 9, speakerBaseHype = 15;
        public float noisePerGuest = 1.2f, speakerNoise = 10;
        /// <summary>Police attention per present guest who has been drinking underage.</summary>
        public float underageAttention = 3f;
        public float crowdSweetLow = 0.6f, crowdSweetHigh = 0.9f;
        public float fightChance = 0.03f, passOutChance = 0.02f, damageChance = 0.015f, celebrityChance = 0.03f, noiseComplaintChance = 0.03f;
        /// <summary>Neighbours only complain above this noise level.</summary>
        public float noiseComplaintMinNoise = 40;
        public int incidentEscalateTicks = 6;
        public int damageCost = 60;
        public float injuryHeat = 10, hospitalHeat = 25, hospitalDean = 10;
        public int beerRunTicks = 3;
        public float beerRunMarkup = 1.25f;
        public float stinkBombMood = 20, stinkBombLeaveShare = 0.15f;
        public float trashedVibe = 15;
        public float coverWillingness = 0.6f;
        public float musicDownAttention = 6;
    }

    [Serializable] public sealed class PoliceTuning
    {
        public float patrolHeatStep = 30;
        public int maxPatrols = 4;
        public int partyNightExtraPatrols = 1;
        public float patrolTravelSeconds = 20, patrolDwellSeconds = 8, respondTravelSeconds = 15;
        public float patrolHeatBias = 2f;
        public float copsParkedHeat = 70;
        public float warningThreshold = 40, raidThreshold = 55;
        public int dispatchCooldownTicks = 12;
        public float patrolHere = 0.55f, patrolNear = 0.25f, witnessFactor = 0.06f, riskManagerDetectBonus = 0.05f;
        public int bustFine = 100;
        public float bustHeat = 10, bustDean = 5, detainSeconds = 45;
        public int underageFine = 50, noiseFine = 100, incidentFine = 25;
        public float raidHeat = 20, raidDean = 15, raidScoreFactor = 0.4f, raidPopPenalty = 8, infamyFun = 80, infamyBonus = 3;
        public float talkSuccess = 0.35f, warningHeat = 5;
        public float calledCopsAttention = 25, traceChance = 0.15f, traceDean = 10, traceHeat = 5;
        public float dailyHeatDecay = 4, weeklyHeatCooldown = 10;
    }

    [Serializable] public sealed class EconomyTuning
    {
        public int dues = 20;
        public int workShiftPay = 40;
        public int sponsorship = 150;
        public int coverDefault = 5, coverMax = 20;
        public int rivalWeeklyIncome = 120;
    }

    [Serializable] public sealed class DeanTuning
    {
        public float probationDean = 30, suspensionDean = 10;
        public float gpaHigh = 3.0f, gpaLow = 2.5f, gpaDean = 5;
        public float studyGpaPerSession = 0.25f, baseStudentGpa = 2.0f;
        public int studySessionCap = 6;
        public float volunteerDean = 1.5f, volunteerHeat = 5;
        /// <summary>The Dean's goodwill erodes a little every week unless earned again.</summary>
        public float weeklyDrift = 2;
        public float meetDeanGain = 6;
        public float cleanHeat = 3, cleanupCrewHeat = 8;
        public float probationCapacityFactor = 0.5f, probationPointsFactor = 0.5f;
        public int finePerDeanPoint = 20;
    }

    [Serializable] public sealed class RushTuning
    {
        public float rushTalkInterest = 6;
        /// <summary>Starting interest in the players' house before regard/rush/rank (design §4.7).</summary>
        public float playerInterestBase = 25;
        public float rivalBidInterest = 50, acceptBidInterest = 60;
        /// <summary>Bid Day only runs from this week index on (0 = first week), so rush spans the semester.</summary>
        public int bidsOpenWeek = 1;
        public int rivalBidsPerWeek = 2;
        public float rivalPopularityFactor = 0.4f, rivalRankBonus = 2;
        public int walkOnPnms = 3;
        public float partyFunBonus = 10, partyBadPenalty = 5, rankBonus = 3, crimePenalty = 6;
        public float rivalWeeklyEffort = 6, poachBonus = 8;
    }

    [Serializable] public sealed class RivalTuning
    {
        public float dailySabotage = 0.35f;
        public float partyCopsChance = 0.25f, partyStinkChance = 0.2f;
        public float budgetBase = 0.15f, budgetPrestige = 0.25f;
        public float semesterRegress = 0.3f;
        /// <summary>Each debrief, rival popularity moves this share back toward its starting value.</summary>
        public float weeklyRegress = 0.1f;
        public float closeRace = 10;
        public float rumorRushLoss = 5;
        public float prankPopLoss = 2;
        public float rivalRaidBase = 0.05f;
        /// <summary>Weekly Dean recovery for rivals: deanRecoveryBase + caution × deanRecoveryCaution.</summary>
        public float deanRecoveryBase = 1, deanRecoveryCaution = 4;
    }

    [Serializable] public sealed class ScoringTuning
    {
        public int[] cupPoints = { 10, 6, 3, 1 };
        public float[] rankPop = { 8, 4, 0, -3 };
        public float notableDivisor = 25;
        public float notablePopularity = 20;
        /// <summary>Party score adds Σ(notable guest popularity) / notableScoreDivisor.</summary>
        public float notableScoreDivisor = 10;
        /// <summary>Only the N most popular notable guests count toward the party score.</summary>
        public int notableCount = 5;
        /// <summary>Popularity gains shrink as a house approaches the top: gain × (1 − pop / softCap).</summary>
        public float popularitySoftCap = 150;
        public float sabotagePopCap = 6;
    }

    [Serializable] public sealed class DestinationTuning
    {
        public float lowConcernWeight = 0.4f, moderateConcernWeight = 0.8f, highConcernWeight = 1.2f;
        public float regardWeight = 0.5f;
        public float stayHomeAppeal = 12;
        public float appealNoise = 12;
        public float deferredBonus = 8;
        public float themeMatch = 6;
    }

    [Serializable] public sealed class ActionTuning
    {
        public int studyPerDay = 2, workPerDay = 1, workoutPerDay = 1, volunteerPerDay = 1, rushTalkPerNpcPerDay = 1;
        public int meetDeanPerWeek = 1;
    }

    /// <summary>
    /// Brother intoxication (design/gdd/drunk-mechanic.md). Same units as <see cref="GuestState.drunk"/>: a serving adds its
    /// item <see cref="ShopItemDef.strength"/> (keg 0.10, liquor 0.25).
    /// </summary>
    [Serializable] public sealed class DrunkTuning
    {
        /// <summary>HUD shows "tipsy" from here; speech is untouched.</summary>
        public float tipsyAt = 0.15f;
        /// <summary>At or above this a brother is drunk: speech rewriting starts and NPCs notice.</summary>
        public float threshold = 0.4f;
        public float wastedAt = 1.0f;
        /// <summary>Hard cap; drinking is refused here. Speech intensity reaches 1 here.</summary>
        public float max = 1.5f;
        /// <summary>Sobering per real second (one keg serving wears off in about a minute).</summary>
        public float soberPerSecond = 0.0015f;
        public float drinkCooldownSeconds = 5;
        /// <summary>Party hype per alcoholic drink a brother has with the guests.</summary>
        public float partyHype = 2;
        /// <summary>Police talk success lost at <see cref="max"/> (scaled by drunk / max).</summary>
        public float policeTalkPenalty = 0.3f;
        /// <summary>Crime detection chance added at <see cref="max"/> (scaled by drunk / max).</summary>
        public float crimeDetect = 0.1f;
        public DrunkSpeechTuning speech = new DrunkSpeechTuning();
    }

    /// <summary>
    /// How hard drunk speech is rewritten (see <see cref="DrunkSpeech"/>). Rates are chances at intensity 1, where
    /// intensity = startIntensity + (1 − startIntensity) × ((drunk − threshold) / (max − threshold)) ^ curve.
    /// Set a rate to 0 to disable that effect.
    /// </summary>
    [Serializable] public sealed class DrunkSpeechTuning
    {
        /// <summary>Intensity the moment a brother crosses the threshold, so becoming drunk is noticeable.</summary>
        public float startIntensity = 0.1f;
        /// <summary>&gt;1 keeps early drunkenness mild and ramps late; &lt;1 ramps early.</summary>
        public float curve = 1f;
        /// <summary>Per letter: neighbouring-key typo, dropped letter, doubled letter.</summary>
        public float typo = 0.05f, dropLetter = 0.03f, doubleLetter = 0.04f;
        /// <summary>Per word: swap two inner letters, slur (s→sh, -ing→-in'), stretch the last letter, say it twice, SHOUT it.</summary>
        public float swapLetters = 0.08f, slur = 0.45f, stretch = 0.15f, repeatWord = 0.06f, shout = 0.05f;
        /// <summary>Per gap between words: insert an interjection.</summary>
        public float interjection = 0.08f;
        /// <summary>Intensity at or above which the whole message comes out lowercase.</summary>
        public float lowercaseAt = 0.25f;
        /// <summary>Per message: append a drunk tangent; cut a long message short and trail off.</summary>
        public float tangent = 0.35f, trailOff = 0.15f;
        /// <summary>Invite/rush button lines only get keyword-safe effects, at this share of the intensity.</summary>
        public float cosmeticScale = 0.7f;
        public List<string> interjections = new List<string>();
        public List<string> tangents = new List<string>();
        public List<string> trailOffs = new List<string>();

        public static List<string> DefaultInterjections() => new List<string> { "*hic*", "uhh", "bro", "dude", "like", "wait", "lol", "hahaha", "ok ok", "listen" };
        public static List<string> DefaultTangents() => new List<string>
        {
            "i love you man", "no but seriously you're like my best friend", "is it hot in here or is it me", "where's my phone",
            "i'm not even that drunk", "ok i need a slice of pizza", "we should get matching tattoos", "who wants to do a keg stand",
            "you have really nice eyebrows", "i think i'm gonna text my ex"
        };
        public static List<string> DefaultTrailOffs() => new List<string> { "... wait what was i saying", "... nevermind", "... where was i", "... ok anyway" };
    }

    /// <summary>
    /// All game-loop tuning (design/gdd/core-game-loop.md §7). Loaded from
    /// <c>Assets/Resources/Campus/GameLoopConfig.json</c>; any missing section keeps its code default.
    /// Example: <c>var config = GameLoopConfig.Parse(json, codec);</c>
    /// </summary>
    [Serializable] public sealed class GameLoopConfig
    {
        public int schema = 1;
        public int semesterWeeks = 6;
        public float weekdaySecondsPerDay = 120, weekdayStartHour = 8, weekdayEndHour = 20;
        public float partySeconds = 480, partyStartHour = 21, partyEndHour = 3, partyTickSeconds = 5;
        public float debriefSeconds = 60, autosaveSeconds = 30;
        public string playerHouseId = "player";
        public string playerHomeLocation = "PartyHouse";
        public string shopLocation = "Shop", libraryLocation = "Library", gymLocation = "Gym", quadLocation = "Quad";
        public List<HouseDef> houses = new List<HouseDef>();
        public List<RoleDef> roles = new List<RoleDef>();
        public List<ShopItemDef> shop = new List<ShopItemDef>();
        public List<ThemeDef> themes = new List<ThemeDef>();
        public List<CrimeDef> crimes = new List<CrimeDef>();
        public List<LocationDef> locations = new List<LocationDef>();
        public List<GuestArchetypeDef> archetypes = new List<GuestArchetypeDef>();
        public PartyTuning party = new PartyTuning();
        public PoliceTuning police = new PoliceTuning();
        public EconomyTuning economy = new EconomyTuning();
        public DeanTuning dean = new DeanTuning();
        public RushTuning rush = new RushTuning();
        public RivalTuning rivals = new RivalTuning();
        public ScoringTuning scoring = new ScoringTuning();
        public DestinationTuning destination = new DestinationTuning();
        public ActionTuning actions = new ActionTuning();
        public DrunkTuning drunk = new DrunkTuning();

        public static GameLoopConfig Parse(string json, IStateCodec codec)
        {
            var c = codec.Decode<GameLoopConfig>(json) ?? new GameLoopConfig();
            c.Normalize();
            c.Validate();
            return c;
        }

        /// <summary>Default config used by tests and as the fallback when no JSON is present.</summary>
        public static GameLoopConfig CreateDefault()
        {
            var c = new GameLoopConfig();
            c.Normalize();
            c.Validate();
            return c;
        }

        public HouseDef House(string id) => houses.Find(h => h.id == id);
        public RoleDef Role(string id) => roles.Find(r => r.id == id);
        public ShopItemDef Item(string id) => shop.Find(i => i.id == id);
        public CrimeDef Crime(string id) => crimes.Find(c => c.id == id);
        public ThemeDef Theme(string id) => themes.Find(t => t.id == id);
        public LocationDef Location(string id) => locations.Find(l => l.id == id);

        /// <summary>Fills empty sections with defaults and nulls with empty values (JsonUtility/STJ differences).</summary>
        public void Normalize()
        {
            if (party == null) party = new PartyTuning();
            if (police == null) police = new PoliceTuning();
            if (economy == null) economy = new EconomyTuning();
            if (dean == null) dean = new DeanTuning();
            if (rush == null) rush = new RushTuning();
            if (rivals == null) rivals = new RivalTuning();
            if (scoring == null) scoring = new ScoringTuning();
            if (destination == null) destination = new DestinationTuning();
            if (actions == null) actions = new ActionTuning();
            if (drunk == null) drunk = new DrunkTuning();
            if (drunk.speech == null) drunk.speech = new DrunkSpeechTuning();
            var speech = drunk.speech;
            if (speech.interjections == null || speech.interjections.Count == 0) speech.interjections = DrunkSpeechTuning.DefaultInterjections();
            if (speech.tangents == null || speech.tangents.Count == 0) speech.tangents = DrunkSpeechTuning.DefaultTangents();
            if (speech.trailOffs == null || speech.trailOffs.Count == 0) speech.trailOffs = DrunkSpeechTuning.DefaultTrailOffs();
            if (houses == null || houses.Count == 0) houses = DefaultHouses();
            if (roles == null || roles.Count == 0) roles = DefaultRoles();
            if (shop == null || shop.Count == 0) shop = DefaultShop();
            if (themes == null || themes.Count == 0) themes = DefaultThemes();
            if (crimes == null || crimes.Count == 0) crimes = DefaultCrimes();
            if (locations == null || locations.Count == 0) locations = DefaultLocations();
            if (archetypes == null || archetypes.Count == 0) archetypes = DefaultArchetypes();
            foreach (var t in themes) if (t.likedBy == null) t.likedBy = new List<string>();
            if (scoring.cupPoints == null || scoring.cupPoints.Length == 0) scoring.cupPoints = new ScoringTuning().cupPoints;
            if (scoring.rankPop == null || scoring.rankPop.Length == 0) scoring.rankPop = new ScoringTuning().rankPop;
        }

        public void Validate()
        {
            if (semesterWeeks < 1 || semesterWeeks > 52) throw new FormatException("semesterWeeks must be 1..52");
            if (weekdaySecondsPerDay <= 0 || partySeconds <= 0 || partyTickSeconds <= 0 || debriefSeconds < 0) throw new FormatException("Phase durations must be positive");
            if (weekdayEndHour <= weekdayStartHour) throw new FormatException("weekdayEndHour must be after weekdayStartHour");
            if (houses.Count(h => h.isPlayer) != 1) throw new FormatException("Exactly one player house is required");
            if (houses.Find(h => h.isPlayer).id != playerHouseId) throw new FormatException("playerHouseId must name the player house");
            if (houses.Count < 2) throw new FormatException("At least one rival house is required");
            if (houses.Select(h => h.id).Distinct().Count() != houses.Count) throw new FormatException("Duplicate house id");
            foreach (var h in houses)
                if (string.IsNullOrEmpty(h.homeLocation) || Location(h.homeLocation) == null) throw new FormatException("House home location missing: " + h.id);
            if (roles.Find(r => r.id == "brother") == null) throw new FormatException("A non-unique 'brother' role is required");
            if (scoring.cupPoints.Length < houses.Count || scoring.rankPop.Length < houses.Count) throw new FormatException("cupPoints/rankPop need one entry per house");
            foreach (var i in shop) if (i.price < 0 || string.IsNullOrEmpty(i.id)) throw new FormatException("Invalid shop item");
            foreach (var c in crimes) if (!string.IsNullOrEmpty(c.requiresItem) && Item(c.requiresItem) == null) throw new FormatException("Crime requires unknown item: " + c.id);
            var d = drunk;
            if (!(d.tipsyAt >= 0 && d.tipsyAt <= d.threshold && d.threshold > 0 && d.threshold <= d.wastedAt && d.wastedAt <= d.max && d.threshold < d.max))
                throw new FormatException("drunk: need 0 <= tipsyAt <= threshold <= wastedAt <= max and threshold < max");
            if (d.soberPerSecond < 0 || d.drinkCooldownSeconds < 0) throw new FormatException("drunk: soberPerSecond and drinkCooldownSeconds must be >= 0");
            var s = d.speech;
            if (s.curve <= 0) throw new FormatException("drunk.speech.curve must be positive");
            foreach (float rate in new[] { s.startIntensity, s.lowercaseAt, s.typo, s.dropLetter, s.doubleLetter, s.swapLetters, s.slur, s.stretch, s.repeatWord, s.shout, s.interjection, s.tangent, s.trailOff, s.cosmeticScale })
                if (rate < 0 || rate > 1) throw new FormatException("drunk.speech rates must be 0..1");
        }

        static List<HouseDef> DefaultHouses() => new List<HouseDef>
        {
            new HouseDef { id = "player", name = "Alpha Beta Rho", nickname = "The Underdogs", isPlayer = true, homeLocation = "PartyHouse",
                popularity = 5, cash = 400, members = 0, dean = 60, heat = 0, gpa = 2.8f, prestige = 0.5f, chaos = 0.5f, aggression = 0.5f, caution = 0.5f },
            new HouseDef { id = "kappa", name = "Kappa Sigma Psi", nickname = "The Yacht Club", homeLocation = "House_Kappa",
                popularity = 60, cash = 1500, members = 30, dean = 70, heat = 10, gpa = 3.1f, prestige = 0.9f, chaos = 0.3f, aggression = 0.5f, caution = 0.7f },
            new HouseDef { id = "theta", name = "Theta Delta Chi", nickname = "Animal House", homeLocation = "House_Theta",
                popularity = 45, cash = 600, members = 25, dean = 40, heat = 35, gpa = 2.4f, prestige = 0.3f, chaos = 0.9f, aggression = 0.7f, caution = 0.2f },
            new HouseDef { id = "lambda", name = "Lambda Nu Tau", nickname = "The Brain Trust", homeLocation = "House_Lambda",
                popularity = 25, cash = 800, members = 18, dean = 85, heat = 0, gpa = 3.6f, prestige = 0.5f, chaos = 0.2f, aggression = 0.2f, caution = 0.9f }
        };

        static List<RoleDef> DefaultRoles() => new List<RoleDef>
        {
            new RoleDef { id = "president", name = "President", lobbyClass = "President", description = "Runs the house; only one who can meet the Dean.",
                canMeetDean = true, canExtendBid = true, canSetCover = true, canStartWeekend = true, policeTalkBonus = 0.25f },
            new RoleDef { id = "social_chair", name = "Social Chair", lobbyClass = "SocialChair", description = "Plans the party theme; better DJ hype and turnout.",
                canSetTheme = true, djMult = 1.5f, fillerMult = 1.1f },
            new RoleDef { id = "rush_chair", name = "Rush Chair", lobbyClass = "Chad", description = "Recruits pledges; rush pitches land harder.",
                canExtendBid = true, rushMult = 1.5f },
            new RoleDef { id = "treasurer", name = "Treasurer", lobbyClass = "RichGuy", description = "Shop discount, better shifts, sets the cover charge.",
                shopDiscount = 0.15f, workPayMult = 1.5f, canSetCover = true },
            new RoleDef { id = "risk_manager", name = "Risk Manager", description = "Cards at the door, calms incidents, talks to cops, cools heat.",
                cardCheckMult = 2f, policeTalkBonus = 0.25f, heatDecayBonus = 2f, incidentResolveBonus = 1f },
            new RoleDef { id = "brother", name = "Brother", unique = false, description = "A loyal brother." }
        };

        static List<ShopItemDef> DefaultShop() => new List<ShopItemDef>
        {
            new ShopItemDef { id = "keg", name = "Keg", category = "drink", price = 60, servings = 40, strength = 0.10f, alcoholic = true, mood = 2 },
            new ShopItemDef { id = "liquor", name = "Liquor Case", category = "drink", price = 90, servings = 25, strength = 0.25f, alcoholic = true, mood = 3 },
            new ShopItemDef { id = "soda_snacks", name = "Soda & Snacks", category = "drink", price = 20, servings = 30, strength = 0f, alcoholic = false, mood = 1.5f },
            new ShopItemDef { id = "speakers", name = "Speaker Stack", category = "gear", price = 80, persistent = true, hype = 15, noise = 10, maxStack = 1 },
            new ShopItemDef { id = "decorations", name = "Decorations", category = "decor", price = 40, vibe = 10, maxStack = 3 },
            new ShopItemDef { id = "security", name = "Hired Security", category = "security", price = 70, cardCheck = 0.6f, troubleMult = 0.8f, maxStack = 1 },
            new ShopItemDef { id = "prank_kit", name = "Prank Kit", category = "crime", price = 30, maxStack = 5 },
            new ShopItemDef { id = "stink_bomb", name = "Stink Bomb", category = "crime", price = 25, maxStack = 5 },
            new ShopItemDef { id = "cleanup_crew", name = "Cleanup Crew", category = "service", price = 50, heatReduction = 8, maxStack = 1 }
        };

        static List<ThemeDef> DefaultThemes() => new List<ThemeDef>
        {
            new ThemeDef { id = "toga", name = "Toga Night", vibe = 6, likedBy = new List<string> { "Jock", "None" } },
            new ThemeDef { id = "neon", name = "Neon Rave", vibe = 7, likedBy = new List<string> { "MeanGirl", "None" } },
            new ThemeDef { id = "casino", name = "Casino Royale", vibe = 6, likedBy = new List<string> { "MeanGirl", "Nerd" } },
            new ThemeDef { id = "beach", name = "Beach Bash", vibe = 5, likedBy = new List<string> { "Jock", "MeanGirl" } },
            new ThemeDef { id = "game_night", name = "Game Night", vibe = 4, likedBy = new List<string> { "Nerd" } }
        };

        static List<CrimeDef> DefaultCrimes() => new List<CrimeDef>
        {
            new CrimeDef { id = "prank", name = "Prank their house", severity = "full", baseDetect = 0.12f, requiresItem = "prank_kit", where = "rival_house", phase = "weekday",
                actorPopGain = 2, rivalPopLoss = 4, rivalDeanLoss = 3, newsText = "{rival}'s lawn was trashed overnight." },
            new CrimeDef { id = "steal_mascot", name = "Steal their mascot", severity = "full", baseDetect = 0.25f, where = "rival_house", phase = "weekday", minHour = 17, maxHour = 20,
                actorPopGain = 4, rivalPopLoss = 3, newsText = "{rival}'s mascot has gone missing." },
            new CrimeDef { id = "stink_bomb", name = "Stink-bomb their party", severity = "full", baseDetect = 0.18f, requiresItem = "stink_bomb", where = "rival_house", phase = "party",
                rivalMoodLoss = 20, newsText = "Someone stink-bombed {rival}'s party." },
            new CrimeDef { id = "steal_keg", name = "Steal their keg", severity = "full", baseDetect = 0.22f, where = "rival_house", phase = "party",
                rivalMoodLoss = 8, stashGain = "keg", stashGainCount = 1, newsText = "{rival} ran dry after a keg vanished." },
            new CrimeDef { id = "call_cops_on", name = "Call in a noise complaint", severity = "lite", baseDetect = 0.15f, where = "any", phase = "party",
                rivalAttention = 25, newsText = "Police got a noise complaint about {rival}." },
            new CrimeDef { id = "spread_rumor", name = "Spread a rumor", severity = "lite", baseDetect = 0.1f, where = "npc", phase = "weekday",
                rumorRegardLoss = 10, newsText = "Rumors are going around about {rival}." }
        };

        static List<LocationDef> DefaultLocations() => new List<LocationDef>
        {
            new LocationDef { id = "PartyHouse", name = "Our House", kind = "house", houseId = "player" },
            new LocationDef { id = "House_Kappa", name = "Kappa House", kind = "house", houseId = "kappa" },
            new LocationDef { id = "House_Theta", name = "Theta House", kind = "house", houseId = "theta" },
            new LocationDef { id = "House_Lambda", name = "Lambda House", kind = "house", houseId = "lambda" },
            new LocationDef { id = "Shop", name = "Campus Store", kind = "shop" },
            new LocationDef { id = "Library", name = "Library", kind = "library" },
            new LocationDef { id = "Gym", name = "Gym", kind = "gym" },
            new LocationDef { id = "Quad", name = "The Quad", kind = "quad" },
            new LocationDef { id = "Dorm", name = "Freshman Dorms", kind = "dorm" }
        };

        // Mirrors the GuestSO assets wired into GameManager.prefab (Generic, Nerd, MeanGirl, Jock).
        static List<GuestArchetypeDef> DefaultArchetypes() => new List<GuestArchetypeDef>
        {
            new GuestArchetypeDef { guestClass = 0, name = "None", popularity = 5, trouble = 0, cash = 20, baseArrivalChance = 100, popularityModifier = 0 },
            new GuestArchetypeDef { guestClass = 1, name = "Nerd", popularity = -5, trouble = 0, cash = 40, baseArrivalChance = 20, popularityModifier = 0.05f },
            new GuestArchetypeDef { guestClass = 2, name = "MeanGirl", popularity = 100, trouble = 60, cash = 50, baseArrivalChance = 5, popularityModifier = 0.1f },
            new GuestArchetypeDef { guestClass = 3, name = "Jock", popularity = 80, trouble = 100, cash = -84, baseArrivalChance = 5, popularityModifier = 0.1f }
        };
    }
}
