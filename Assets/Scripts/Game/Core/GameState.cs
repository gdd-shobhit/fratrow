using System;
using System.Collections.Generic;

namespace FratRow.Game
{
    /// <summary>Values match Unity's <c>Systems.Campus.GamePhase</c>. Int-backed so Unity's JsonUtility round-trips saves.</summary>
    public enum Phase { Weekday = 0, Party = 1, Debrief = 2, SemesterEnd = 3 }

    [Serializable] public sealed class StashEntry { public string itemId; public int count; }
    [Serializable] public sealed class Counter { public string key; public int count; }
    [Serializable] public sealed class HouseValue { public string houseId; public float value; }

    /// <summary>Persisted per-house state (design §3.2).</summary>
    [Serializable] public sealed class HouseState
    {
        public string id;
        public string name;
        public string nickname;
        public bool isPlayer;
        public string homeLocation;
        public float popularity;
        public int cash;
        public float heat;
        public float dean;
        public float gpa;
        /// <summary>Abstract/NPC brothers (players are counted separately for the player house).</summary>
        public int memberCount;
        public List<string> npcMembers = new List<string>();
        public int cupPoints;
        public int rating = 50;
        public string grade = "C";
        public bool probation;
        public bool suspended;
        public bool trashed;
        public bool guarded;
        public bool deanMetThisWeek;
        public string theme = "";
        public int cover;
        public List<StashEntry> stash = new List<StashEntry>();
        // Personality copied from config at creation so saves survive config edits.
        public float prestige, chaos, aggression, caution;
        // Rival AI plan for the current week.
        public int weekBudget;
        public float sabotageIntent;
        public float sabotageScoreMod;
        public float weekSabotagePop;
        public int lastRank;
    }

    /// <summary>Persisted per-player state (design §3.3).</summary>
    [Serializable] public sealed class PlayerState
    {
        public string playerId;
        public string displayName;
        public string role = "brother";
        public string location = "";
        /// <summary>Within entrance range of the house (set by the host adapter; harness treats home location as entrance).</summary>
        public bool atEntrance;
        public bool connected = true;
        public bool ready;
        public double detainedUntil;
        public double busyUntil;
        public int record;
        public float rep;
        public int studyThisWeek;
        /// <summary>Intoxication, same units as <see cref="GuestState.drunk"/> (design/gdd/drunk-mechanic.md). Sobers up over time.</summary>
        public float drunk;
        /// <summary>Simulated time before which the next drink is refused (pacing).</summary>
        public double nextDrinkAt;
        public List<Counter> daily = new List<Counter>();
        public List<Counter> weekly = new List<Counter>();
    }

    /// <summary>Game-side NPC state. Social memory/regard toward the players' house lives in the social backend.</summary>
    [Serializable] public sealed class NpcGameState
    {
        public string npcId;
        public string memberOf = "";
        public bool isPnm;
        public List<HouseValue> rivalRegard = new List<HouseValue>();
        /// <summary>Mechanical rush interest additions per house (rush talks, parties, crimes witnessed).</summary>
        public List<HouseValue> rushBonus = new List<HouseValue>();
        public List<string> bids = new List<string>();
        /// <summary>This week's party destination: houseId, "home" or "" (undecided).</summary>
        public string destination = "";
        public int destinationArrivalTick = -1;
        public int rushTalksToday;
        /// <summary>Phone contact unlocked for the players' house (text channel allowed).</summary>
        public bool contactUnlocked;
        public bool attendedPlayersParty;
    }

    [Serializable] public sealed class GuestState
    {
        public int guestId;
        public string npcId = "";
        public int archetype;
        public bool underage;
        public float drunk;
        public float mood;
        public float trouble;
        public float popularity;
        public float cash;
        public int arrivedTick;
        public bool left;
        public bool drankUnderage;
    }

    [Serializable] public sealed class IncidentState
    {
        public int id;
        /// <summary>fight, passed_out, noise_complaint, property_damage, keg_dry, celebrity_dropin, stink_bomb.</summary>
        public string kind;
        public int openedTick;
        public int guestId = -1;
        public bool resolved;
        public bool escalated;
    }

    /// <summary>The players' party for the current week (design §3.11). <c>active</c> replaces null checks for JsonUtility.</summary>
    [Serializable] public sealed class PartyState
    {
        public bool active;
        public bool ended;
        public string socialPartyId = "";
        public int tick;
        public int ticksTotal;
        public int capacity;
        public int nextGuestId = 1;
        public int nextIncidentId = 1;
        public List<GuestState> guests = new List<GuestState>();
        public List<IncidentState> incidents = new List<IncidentState>();
        public List<string> pendingArrivals = new List<string>();
        public List<int> pendingArrivalTicks = new List<int>();
        public float hype;
        public float noise;
        public float trouble;
        public float attention;
        public float fun;
        public float vibe;
        public int incidentsTotal;
        public int incidentsEscalated;
        public int warnings;
        public float peakAttention;
        public int underageDrinkers;
        public int cashCollected;
        public float notablePopularity;
        public bool cardCheck;
        public float cardCheckCoverage;
        public float securityTroubleMult = 1;
        public int musicDownTick = -100;
        public int djTick = -100;
        public int serveTick = -100;
        public int stinkBombTicks;
        public float calledCops;
        public int dispatchedPatrol = -1;
        public int dispatchCooldownUntil;
        public bool warningIssued;
        public bool policeTalked;
        public bool talkAttempted;
        /// <summary>Opened containers: remaining servings per drink item id.</summary>
        public List<StashEntry> openServings = new List<StashEntry>();
        public string servingPlayer = "";
        public List<int> rivalCopsTicks = new List<int>();
        public List<string> rivalCopsBy = new List<string>();
        public List<int> rivalStinkTicks = new List<int>();
        public List<string> rivalStinkBy = new List<string>();
        public bool raided;
        public string lastServedUnderageBy = "";
        public bool kegDryReported;
        public float score;
        public int peakGuests;
    }

    /// <summary>Abstract rival party result for the current week.</summary>
    [Serializable] public sealed class RivalPartyState
    {
        public string houseId;
        public float moodPenalty;
        public float attention;
        public bool raided;
        public bool stinkBombed;
        public int namedGuests;
        public float notablePopularity;
        public float score;
    }

    [Serializable] public sealed class PatrolState
    {
        public int id;
        public string from;
        public string to;
        public float progress;
        public float dwell;
        /// <summary>patrol, respond, parked.</summary>
        public string mode = "patrol";
        public float travelSeconds;
    }

    [Serializable] public sealed class NewsItem
    {
        public long seq;
        public int semester, week, day;
        /// <summary>Machine kind: party_result, raid, warning, busted, sabotage, bid, member, dean, rival_sabotage, incident, semester ...</summary>
        public string kind;
        public string text;
        public string houseId = "";
        public string playerId = "";
        public string npcId = "";
        /// <summary>Public news is shared with every NPC's memory at debrief.</summary>
        public bool isPublic;
    }

    [Serializable] public sealed class WeekEntry
    {
        public string houseId;
        public float score;
        public int rank;
        public int cupPoints;
        public float popDelta;
        public bool raided;
        public int guests;
        public float fun;
        public bool suspended;
    }

    [Serializable] public sealed class WeekResult
    {
        public int semester, week;
        public List<WeekEntry> entries = new List<WeekEntry>();
        // Players' party summary (the live PartyState is reset when the next week starts).
        public int guests, peakGuests, incidents, escalated, underageDrinkers, cashCollected, warnings;
        public float fun, hype, peakAttention, notablePopularity;
        public bool raided;
        public List<string> namedGuests = new List<string>();
    }

    /// <summary>Whole persisted campaign (one save per campaign).</summary>
    [Serializable] public sealed class GameState
    {
        public int schema = 1;
        public string campaignId;
        public int seed;
        public bool started;
        public double time;
        public int semester = 1;
        public int week;
        public int day;
        public Phase phase;
        public double phaseElapsed;
        public double partyTickAccumulator;
        public bool weekendStartedEarly;
        public long eventSeq;
        public long newsSeq;
        public List<HouseState> houses = new List<HouseState>();
        public List<PlayerState> players = new List<PlayerState>();
        public List<NpcGameState> npcs = new List<NpcGameState>();
        public List<PatrolState> patrols = new List<PatrolState>();
        public PartyState party = new PartyState();
        public List<RivalPartyState> rivalParties = new List<RivalPartyState>();
        public List<NewsItem> news = new List<NewsItem>();
        public List<WeekResult> history = new List<WeekResult>();
        public string socialPartyId = "";
        public string semesterWinner = "";

        /// <summary>Restores empty collections after JsonUtility/System.Text.Json decoding.</summary>
        public void Normalize()
        {
            if (houses == null) houses = new List<HouseState>();
            if (players == null) players = new List<PlayerState>();
            if (npcs == null) npcs = new List<NpcGameState>();
            if (patrols == null) patrols = new List<PatrolState>();
            if (party == null) party = new PartyState();
            if (rivalParties == null) rivalParties = new List<RivalPartyState>();
            if (news == null) news = new List<NewsItem>();
            if (history == null) history = new List<WeekResult>();
            foreach (var h in houses) { if (h.stash == null) h.stash = new List<StashEntry>(); if (h.npcMembers == null) h.npcMembers = new List<string>(); }
            foreach (var p in players) { if (p.daily == null) p.daily = new List<Counter>(); if (p.weekly == null) p.weekly = new List<Counter>(); }
            foreach (var n in npcs)
            {
                if (n.rivalRegard == null) n.rivalRegard = new List<HouseValue>();
                if (n.rushBonus == null) n.rushBonus = new List<HouseValue>();
                if (n.bids == null) n.bids = new List<string>();
            }
            if (party.guests == null) party.guests = new List<GuestState>();
            if (party.incidents == null) party.incidents = new List<IncidentState>();
            if (party.pendingArrivals == null) party.pendingArrivals = new List<string>();
            if (party.pendingArrivalTicks == null) party.pendingArrivalTicks = new List<int>();
            if (party.openServings == null) party.openServings = new List<StashEntry>();
            if (party.rivalCopsTicks == null) party.rivalCopsTicks = new List<int>();
            if (party.rivalCopsBy == null) party.rivalCopsBy = new List<string>();
            if (party.rivalStinkTicks == null) party.rivalStinkTicks = new List<int>();
            if (party.rivalStinkBy == null) party.rivalStinkBy = new List<string>();
            foreach (var w in history) { if (w.entries == null) w.entries = new List<WeekEntry>(); if (w.namedGuests == null) w.namedGuests = new List<string>(); }
        }
    }
}
