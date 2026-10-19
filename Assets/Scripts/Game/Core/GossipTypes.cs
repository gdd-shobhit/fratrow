using System;
using System.Collections.Generic;

namespace FratRow.Game
{
    /// <summary>
    /// One kind of campus story (design/gdd/campus-gossip.md §3.2). Texts are indexed by spice: 0 = as it happened,
    /// 1 = exaggerated, 2 = wildly exaggerated. Placeholders: {house} {other} {player} {npc} {quote} {detail}.
    /// Example: <c>new StoryKindDef { id = "party_raided", topic = "party", valence = -1, severity = 70, texts = { "...", "...", "..." } }</c>
    /// </summary>
    [Serializable] public sealed class StoryKindDef
    {
        public string id;
        /// <summary>party, conduct, status, rush, said, smear: grouping for UI and listener relevance.</summary>
        public string topic = "party";
        /// <summary>+1 good / -1 bad for the story's house (and its player, when the listener knows who).</summary>
        public int valence = -1;
        /// <summary>Base severity 0..100 before the seeding site scales it by the event's magnitude.</summary>
        public float severity = 50;
        /// <summary>Opinion shares: subject house (× valence), named player (× valence), second house (signed as given).</summary>
        public float houseWeight = 1, playerWeight, otherHouseWeight;
        public List<string> texts = new List<string>();
    }

    /// <summary>
    /// A smear claim used by <c>spread_rumor</c> and rival rumors. <c>truth</c> names a house flag that makes it true
    /// (probation, suspended, trashed, raided); "" means the claim is always fabricated.
    /// </summary>
    [Serializable] public sealed class SmearClaimDef
    {
        public string id;
        public string text;
        public string truth = "";
    }

    /// <summary>Gossip tuning (design/gdd/campus-gossip.md §7). Every value is a key under <c>gossip</c> in GameLoopConfig.json.</summary>
    [Serializable] public sealed class GossipTuning
    {
        public bool enabled = true;
        /// <summary>Significance gate: an event below this severity never becomes a story.</summary>
        public float seedThreshold = 35;
        public int maxActiveStories = 16;
        public int maxNewStoriesPerWeek = 14;
        /// <summary>No single kind may flood the week (e.g. a crew inviting every notable NPC).</summary>
        public int maxPerKindPerWeek = 3;
        /// <summary>Stories at or above this severity ignore the weekly cap (scandals always spread).</summary>
        public float capBypassSeverity = 80;
        /// <summary>Weekday clock hours at which co-located NPCs swap stories.</summary>
        public float[] roundHours = { 10, 15 };
        /// <summary>Sunday-brunch rounds right after the weekly settlement.</summary>
        public int brunchRounds = 2;
        public float tellBase = 0.75f;
        /// <summary>Bad news is juicier: negative stories' telling appeal × this.</summary>
        public float negativityBias = 1.25f;
        /// <summary>Tie strengths. Negative stories only travel over ties ≥ negativeTieMin (trust gates nasty gossip).</summary>
        public float tieBrother = 1f, tieCliqueTogether = 0.9f, tieSameParty = 0.65f, tieClique = 0.55f, tieTogether = 0.45f;
        public float negativeTieMin = 0.55f;
        /// <summary>Familiarity = scheduled hours two NPCs share per week. Together, each hour adds tie (capped); close friends
        /// (≥ friendHours) text each other even when apart.</summary>
        public float familiarityPerHour = 0.03f, familiarityCap = 0.35f, friendHours = 12, tieFriend = 0.6f;
        /// <summary>A teller stops spreading after this many tellings to people who already knew (Maki–Thompson stifling).</summary>
        public int stifleAfter = 2;
        /// <summary>Per retelling: chance a detail level is lost (names → "someone"), and base chance to exaggerate (× teller gossipiness).</summary>
        public float levelingChance = 0.45f, sharpenChance = 0.3f;
        public int maxSpice = 2;
        public float[] spiceMultiplier = { 1f, 1.25f, 1.5f };
        public float beliefBase = 0.35f, beliefTie = 0.35f, beliefRepeat = 0.3f, loyaltyDiscount = 0.3f;
        /// <summary>A story changes an opinion once belief ≥ beliefThreshold, or belief ≥ multiSourceBelief from 2+ different tellers.</summary>
        public float beliefThreshold = 0.6f, multiSourceBelief = 0.4f;
        public float spreadMinBelief = 0.25f;
        /// <summary>Daily heat retention = base + severity/100 × perSeverity; a story retires below retireHeat.</summary>
        public float heatRetentionBase = 0.55f, heatRetentionPerSeverity = 0.35f, retireHeat = 8;
        /// <summary>Weeks before a believed story stops affecting opinions (linear fade) and is forgotten.</summary>
        public int memoryWeeks = 3;
        /// <summary>Opinion points from one fully believed severity-100 story; per-NPC opinions are clamped to ±opinionCap.</summary>
        public float opinionScale = 30, opinionCap = 50;
        /// <summary>Negativity bias: good news moves an opinion this fraction as much as bad news of the same weight.</summary>
        public float positiveOpinionFactor = 0.6f;
        /// <summary>Party appeal and rush interest per opinion point.</summary>
        public float destinationWeight = 0.35f, rushWeight = 0.3f;
        /// <summary>Settlement popularity from campus word of mouth = clamp(mean opinion × scale, ±cap).</summary>
        public float wordOfMouthScale = 0.12f, wordOfMouthCap = 4;
        /// <summary>NPCs at or above this gossipiness hear institutional news first (the campus hubs).</summary>
        public float hubGossip = 0.75f;
        public int hubCount = 3;
        /// <summary>Party-night seeding.</summary>
        public float legendaryFun = 75, flopFun = 35, notableSightingFactor = 1.6f;
        public int partyStoriesMax = 3;
        /// <summary>Status seeding: cup-standing places moved, or raw popularity swing, that make a house the talk of campus.</summary>
        public int rankSwingPlaces = 2;
        public float popularitySwing = 12;
        public float notablePledge = 15;
        /// <summary>Who-said-what seeding weights (see §4.1).</summary>
        public float saidGrievance = 45, saidTrustDrop = 15, saidRegardDrop = 10, saidWithdrew = 25, charmBase = 25;
        public int quoteChars = 140;
        public float smearSeverity = 50, exposedSeverity = 65, backfireSeverity = 55;
        /// <summary>Player actions.</summary>
        public int askPerNpcPerDay = 1, denyPerNpcPerDay = 1, tracePerDay = 4;
        public float denyPower = 0.5f, denyTrueFactor = 0.35f, denyBackfireBelief = 0.3f, exposeBeliefFactor = 0.2f;
        /// <summary>Rival AI tracing of the players' smears: daily chance = base + caution × perCaution once pressure exists.</summary>
        public float rivalTraceBase = 0.15f, rivalTraceCaution = 0.35f;
        public int rivalTraceReach = 8;
        /// <summary>An NPC whose opinion of a player is at or below this refuses to give them a number.</summary>
        public float numberRefuseOpinion = -12;
        public float viralShare = 0.25f;
        /// <summary>Write NPC memory receipts for every story (true) or only stories involving the players (false, default):
        /// rival-only stories still move opinions, but each receipt grows the social journal.</summary>
        public bool ledgerAllStories;
    }

    /// <summary>A piece of campus gossip (design/gdd/campus-gossip.md §3.1). Persisted; the truth flag is host-only.</summary>
    [Serializable] public sealed class GossipStory
    {
        public int id;
        public string kind;
        /// <summary>House the story reflects on, a second house (culprit/victim), a player, an NPC involved.</summary>
        public string houseId = "", otherHouseId = "", playerId = "", npcId = "";
        /// <summary>Who-said-what: the speaker's own line (single line, capped). Data only, never instructions.</summary>
        public string quote = "";
        public string detail = "";
        /// <summary>witness | npc:&lt;id&gt; | player:&lt;id&gt; | house:&lt;id&gt; — who started telling it.</summary>
        public string origin = "witness";
        public bool fabricated;
        public float severity;
        public float heat;
        /// <summary>Absolute day index (see <c>GameSession.DayIndex</c>).</summary>
        public int createdDay;
        public int semester, week;
        /// <summary>Social ledger event the initial knowers witnessed (empty with a null bridge).</summary>
        public string socialEventId = "";
        public bool exposed;
        public bool retired;
        public bool viralReported;
        public List<string> tracedBy = new List<string>();
    }

    /// <summary>What one NPC knows of one story: their version, belief and spreading state.</summary>
    [Serializable] public sealed class GossipKnowledge
    {
        public int storyId;
        public string npcId;
        /// <summary>NPC id of the teller, or witness | player:&lt;id&gt; | house:&lt;id&gt;.</summary>
        public string heardFrom = "";
        /// <summary>Distinct tellers so far (capped list, for repetition and multi-source belief).</summary>
        public List<string> tellers = new List<string>();
        /// <summary>0 = full (names, quote), 1 = secondhand (names, no quote), 2 = vague ("one of the Kappa guys").</summary>
        public int detail;
        public int spice;
        public float belief;
        public int stifles;
        public bool spreader = true;
        public int learnedDay;
        /// <summary>Social ledger event this NPC knows the story through (origin record or gossip receipt).</summary>
        public string ledger = "";
    }

    /// <summary>Read-only story summary for UI/dev tools. Omits the truth flag on purpose.</summary>
    public struct StoryView
    {
        public int id;
        public string kind, topic, text, houseId, otherHouseId, playerId, npcId;
        public int valence, knowers, believers;
        public float severity, heat;
        public bool exposed, retired;
    }

    /// <summary>One story as a particular NPC tells it.</summary>
    public struct HeardView
    {
        public int storyId;
        public string kind, text, heardFrom;
        public int detail, spice;
        public float belief;
    }
}
