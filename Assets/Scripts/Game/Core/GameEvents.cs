using System;

namespace FratRow.Game
{
    /// <summary>Stable action ids (design §3.4). The network adapter sends these strings; unknown ids are rejected.</summary>
    public static class ActionIds
    {
        // Weekday
        public const string Talk = "talk", Invite = "invite", RushTalk = "rush_talk", ExtendBid = "extend_bid", Buy = "buy", AskNumber = "ask_number";
        public const string WorkShift = "work_shift", Study = "study", Workout = "workout", Volunteer = "volunteer", MeetDean = "meet_dean";
        public const string SetTheme = "set_theme", SetCover = "set_cover", CleanHouse = "clean_house", ClaimRole = "claim_role", GuardHouse = "guard_house";
        public const string Ready = "ready", StartWeekend = "start_weekend", ContinueDebrief = "continue", StartSemester = "start_semester";
        // Crimes / sabotage (CrimeDef ids)
        public const string Prank = "prank", StealMascot = "steal_mascot", StinkBomb = "stink_bomb", StealKeg = "steal_keg", CallCops = "call_cops_on", SpreadRumor = "spread_rumor";
        // Party night
        public const string ServeDrinks = "serve_drinks", CardCheck = "card_check", Dj = "dj", TurnDownMusic = "turn_down_music";
        public const string BreakUpFight = "break_up_fight", HelpGuest = "help_guest", FixDamage = "fix_damage", KickOut = "kick_out";
        public const string BeerRun = "beer_run", TalkToPolice = "talk_to_police";
        // Gossip (design/gdd/campus-gossip.md §3.7): target = NPC, item = story id.
        public const string AskGossip = "ask_gossip", DenyRumor = "deny_rumor", TraceRumor = "trace_rumor", ExposeRumor = "expose_rumor";
        // Any phase but semester end, at the house (item = optional drink id)
        public const string Drink = "drink";
    }

    /// <summary>
    /// A player request validated by the host. Example:
    /// <c>session.Apply(new PlayerAction { playerId = id, action = ActionIds.Buy, item = "keg", amount = 2 });</c>
    /// </summary>
    [Serializable] public sealed class PlayerAction
    {
        public string playerId;
        public string action;
        /// <summary>NPC id, house id, incident id (as string) or role id depending on the action.</summary>
        public string target = "";
        public string item = "";
        public int amount;
        /// <summary>Second target for two-target actions (e.g. spread_rumor: target=NPC, rival=house).</summary>
        public string rival = "";
        /// <summary>Free text for <c>talk</c> (≤ 800 chars, validated again by the social service).</summary>
        public string text = "";
    }

    /// <summary>Result codes are stable strings for UI localization.</summary>
    [Serializable] public sealed class ActionResult
    {
        public bool ok;
        /// <summary>ok, unknown_action, unknown_player, wrong_phase, wrong_location, not_allowed_role, cap_reached, no_funds, missing_item,
        /// detained, busy, invalid_target, already_done, busted, not_ready, suspended.</summary>
        public string code = "ok";
        public string message = "";
        /// <summary>For talk/invite/rush_talk: the request the adapter must submit to the social service.</summary>
        public string conversationText = "";
        public string conversationChannel = "";
        public string socialPartyId = "";
        /// <summary>For talk-type actions: the speaker's visible <see cref="FratRow.Social.SpeakerConditions"/> value.
        /// <see cref="conversationText"/> is already what they actually said (drunk speech rewritten).</summary>
        public string speakerCondition = "";

        public static ActionResult Ok(string message = "") => new ActionResult { ok = true, code = "ok", message = message };
        public static ActionResult Fail(string code, string message) => new ActionResult { ok = false, code = code, message = message };
    }

    public enum GameEventKind : byte
    {
        PhaseChanged, DayChanged, News, GuestArrived, GuestLeft, IncidentOpened, IncidentResolved,
        PoliceDispatched, PoliceWarning, Raid, PlayerBusted, PartyStarted, PartyEnded, WeekSettled,
        MemberJoined, SemesterEnded, StashChanged, RoleChanged, HouseChanged,
        /// <summary>A story was seeded (value = story id; flag = about the players).</summary>
        Gossip
    }

    /// <summary>Transient notification drained by the adapter each frame (not persisted).</summary>
    public sealed class GameEvent
    {
        public GameEventKind kind;
        public string text = "";
        public string houseId = "";
        public string playerId = "";
        public string npcId = "";
        public int guestId = -1;
        public int archetype;
        public int incidentId = -1;
        public float value;
        public bool flag;
        public override string ToString() => kind + ": " + text;
    }
}
