using System;
using System.Collections.Generic;
using System.Linq;
using FratRow.Social;

namespace FratRow.Game
{
    public sealed partial class GameSession
    {
        static readonly HashSet<string> PartyHouseActions = new HashSet<string>
        {
            ActionIds.ServeDrinks, ActionIds.CardCheck, ActionIds.Dj, ActionIds.TurnDownMusic, ActionIds.BreakUpFight,
            ActionIds.HelpGuest, ActionIds.FixDamage, ActionIds.KickOut, ActionIds.BeerRun, ActionIds.TalkToPolice
        };

        /// <summary>
        /// Validates and applies one player action (design §3.4). Never throws for bad input; returns a coded failure.
        /// Talk-type actions return the conversation the adapter must submit to the social service.
        /// </summary>
        public ActionResult Apply(PlayerAction a)
        {
            social.BeginTick();
            if (a == null || string.IsNullOrEmpty(a.action)) return ActionResult.Fail("unknown_action", "No action.");
            var p = Player(a.playerId);
            if (p == null) return ActionResult.Fail("unknown_player", "Unknown player.");
            bool talking = a.action == ActionIds.Talk || a.action == ActionIds.Invite || a.action == ActionIds.RushTalk ||
                a.action == ActionIds.AskGossip || a.action == ActionIds.DenyRumor || a.action == ActionIds.TraceRumor || a.action == ActionIds.ExposeRumor;
            if (!talking && p.detainedUntil > State.time)
                return ActionResult.Fail("detained", "You're being held by campus police for " + Math.Ceiling(p.detainedUntil - State.time) + "s.");
            if (!talking && p.busyUntil > State.time) return ActionResult.Fail("busy", "You're still out on a beer run.");
            try
            {
                var crime = Config.Crime(a.action);
                if (crime != null) return Crime(p, crime, a);
                if (PartyHouseActions.Contains(a.action)) return PartyAction(p, a);
                switch (a.action)
                {
                    case ActionIds.Talk:
                    case ActionIds.Invite:
                    case ActionIds.RushTalk: return Conversation(p, a);
                    case ActionIds.ExtendBid: return ExtendBid(p, a);
                    case ActionIds.AskGossip: return AskGossip(p, a);
                    case ActionIds.DenyRumor: return DenyRumor(p, a);
                    case ActionIds.TraceRumor: return TraceRumor(p, a);
                    case ActionIds.ExposeRumor: return ExposeRumor(p, a);
                    case ActionIds.AskNumber: return AskNumber(p, a);
                    case ActionIds.Buy: return Buy(p, a);
                    case ActionIds.Drink: return Drink(p, a);
                    case ActionIds.WorkShift: return WeekdayChore(p, a, Config.shopLocation, Config.actions.workPerDay, () => { int pay = (int)Math.Round(Config.economy.workShiftPay * RoleOf(p).workPayMult); PlayerHouse.cash += pay; return "Worked a shift: +$" + pay + "."; });
                    case ActionIds.Study: return WeekdayChore(p, a, Config.libraryLocation, Config.actions.studyPerDay, () => { p.studyThisWeek++; return "Studied (" + p.studyThisWeek + " sessions this week)."; });
                    case ActionIds.Workout: return WeekdayChore(p, a, Config.gymLocation, Config.actions.workoutPerDay, () => { p.rep += 1; return "Hit the gym."; });
                    case ActionIds.Volunteer: return WeekdayChore(p, a, Config.quadLocation, Config.actions.volunteerPerDay, () =>
                    {
                        PlayerHouse.dean = Clamp(PlayerHouse.dean + Config.dean.volunteerDean, 0, 100);
                        PlayerHouse.heat = Clamp(PlayerHouse.heat - Config.dean.volunteerHeat, 0, 100);
                        return "Volunteered on the Quad. The Dean noticed.";
                    });
                    case ActionIds.MeetDean: return MeetDean(p);
                    case ActionIds.SetTheme: return SetTheme(p, a);
                    case ActionIds.SetCover: return SetCover(p, a);
                    case ActionIds.CleanHouse: return CleanHouse(p);
                    case ActionIds.GuardHouse: return GuardHouse(p);
                    case ActionIds.ClaimRole: return ClaimRole(p, a);
                    case ActionIds.Ready:
                        p.ready = a.amount != 0;
                        return ActionResult.Ok(p.ready ? "Ready for the weekend." : "Not ready.");
                    case ActionIds.StartWeekend:
                        if (!RoleOf(p).canStartWeekend) return ActionResult.Fail("not_allowed_role", "Only the President can start the weekend early.");
                        if (ConnectedPlayers.Any(x => !x.ready && x.playerId != p.playerId)) return ActionResult.Fail("not_ready", "Not everyone is ready.");
                        return StartWeekendEarly();
                    case ActionIds.ContinueDebrief:
                        if (!RoleOf(p).canStartWeekend) return ActionResult.Fail("not_allowed_role", "Only the President can move on.");
                        return ContinueDebrief();
                    case ActionIds.StartSemester:
                        if (!RoleOf(p).canStartWeekend) return ActionResult.Fail("not_allowed_role", "Only the President can start the semester.");
                        return StartNextSemester();
                }
                return ActionResult.Fail("unknown_action", "Unknown action " + a.action + ".");
            }
            finally
            {
                FlushGossipLedger();
                social.BeginTick();
            }
        }

        bool InPhase(params Phase[] phases) => phases.Contains(State.phase);

        ActionResult WeekdayChore(PlayerState p, PlayerAction a, string location, int cap, Func<string> apply)
        {
            if (!InPhase(Phase.Weekday)) return ActionResult.Fail("wrong_phase", "That's a weekday thing.");
            if (p.location != location) return ActionResult.Fail("wrong_location", "Go to " + (Config.Location(location)?.name ?? location) + ".");
            if (CounterValue(p.daily, a.action) >= cap) return ActionResult.Fail("cap_reached", "You've done that enough today.");
            CounterAdd(p.daily, a.action);
            return ActionResult.Ok(apply());
        }

        // ---------- Conversations ----------

        static readonly string[] InviteLines =
        {
            "Hey {npc}, we're throwing a party at {house} this Saturday. Want to come?",
            "{npc}! Party at {house} Saturday night. You should come.",
            "We'd love to have you at our party this weekend, {npc}. You in?"
        };
        static readonly string[] RushLines =
        {
            "Have you thought about rushing {house}? We'd love to have you as a brother.",
            "{npc}, you'd fit right in at {house}. Think about pledging with us.",
            "Rush {house} this week. We want you as a brother, {npc}."
        };

        ActionResult Conversation(PlayerState p, PlayerAction a)
        {
            var def = RosterNpc(a.target);
            var npc = Npc(a.target);
            if (def == null || npc == null) return ActionResult.Fail("invalid_target", "Unknown person.");
            if (State.phase == Phase.SemesterEnd) return ActionResult.Fail("wrong_phase", "The semester is over.");
            string here = NpcLocation(def.id);
            bool inPerson = !string.IsNullOrEmpty(here) && here == p.location;
            if (!inPerson && !npc.contactUnlocked) return ActionResult.Fail("wrong_location", def.displayName + " isn't here and you don't have their number.");
            if (a.action == ActionIds.RushTalk)
            {
                if (!npc.isPnm || !string.IsNullOrEmpty(npc.memberOf)) return ActionResult.Fail("invalid_target", def.displayName + " isn't rushing.");
                if (!inPerson) return ActionResult.Fail("wrong_location", "Rush pitches happen in person.");
                if (npc.rushTalksToday >= Config.actions.rushTalkPerNpcPerDay) return ActionResult.Fail("cap_reached", "You already pitched " + def.displayName + " today.");
            }
            if (a.action == ActionIds.Invite && (State.phase == Phase.Debrief || string.IsNullOrEmpty(State.socialPartyId)))
                return ActionResult.Fail("wrong_phase", "There's no party to invite anyone to right now.");
            if (a.action == ActionIds.Invite && !string.IsNullOrEmpty(npc.memberOf))
                return ActionResult.Fail("invalid_target", def.displayName + " is a " + (House(npc.memberOf)?.name ?? "house") + " member now.");
            string text;
            if (a.action == ActionIds.Talk)
            {
                text = (a.text ?? "").Trim();
                if (text.Length == 0 || text.Length > SocialRules.InputCharacters) return ActionResult.Fail("invalid_target", "Say something (up to 800 characters).");
            }
            else
            {
                var lines = a.action == ActionIds.Invite ? InviteLines : RushLines;
                text = lines[NextRng("line").Range(0, lines.Length)].Replace("{npc}", def.displayName).Replace("{house}", PlayerHouse.name);
            }
            if (a.action == ActionIds.RushTalk) npc.rushTalksToday++;
            // What the NPC hears is what a drunk brother actually manages to say. Button lines stay keyword-safe.
            text = SpokenText(p, text, cosmeticOnly: a.action != ActionIds.Talk);
            return new ActionResult
            {
                ok = true, code = "ok", message = "Talking to " + def.displayName + "...", conversationText = text,
                conversationChannel = inPerson ? "in_person" : "text", socialPartyId = State.socialPartyId ?? "",
                speakerCondition = DrunkCondition(p)
            };
        }

        /// <summary>Adapter callback after the social service committed (or failed) a turn started by <see cref="Apply"/>.</summary>
        public void OnConversationCommitted(string playerId, string npcId, string action, ConversationResult result)
        {
            social.BeginTick();
            var npc = Npc(npcId);
            var p = Player(playerId);
            if (npc == null || p == null || result == null || result.status != "committed") return;
            if (action == ActionIds.RushTalk && result.decision != "decline")
                AddRushBonus(npc, Config.playerHouseId, Config.rush.rushTalkInterest * RoleOf(p).rushMult);
            if (State.phase == Phase.Party) ScheduleCommittedArrivals();
            var def = RosterNpc(npcId);
            if (result.commitment == "accepted" && def != null)
                News("rsvp", def.displayName + " is coming to the party.", PlayerHouse.id, playerId, npcId);
            else if (result.commitment == "conditional" && def != null)
                News("rsvp", def.displayName + " will come if " + p.displayName + " meets them at the door in the first minute.", PlayerHouse.id, playerId, npcId);
            SeedFromTurn(p, def, result.requestId);
            FlushGossipLedger();
        }

        /// <summary>In-person contact unlock with the roster's popularity/grade gates and rarity roll (mirrors NamedNpcSO.MeetsUnlockGates).</summary>
        ActionResult AskNumber(PlayerState p, PlayerAction a)
        {
            var def = RosterNpc(a.target);
            var npc = Npc(a.target);
            if (def == null || npc == null) return ActionResult.Fail("invalid_target", "Unknown person.");
            if (npc.contactUnlocked) return ActionResult.Fail("already_done", "You already have " + def.displayName + "'s number.");
            if (NpcLocation(def.id) != p.location || string.IsNullOrEmpty(p.location)) return ActionResult.Fail("wrong_location", def.displayName + " isn't here.");
            if (CounterValue(p.daily, ActionIds.AskNumber + ":" + def.id) >= 1) return ActionResult.Fail("cap_reached", "Don't push it. Try again tomorrow.");
            CounterAdd(p.daily, ActionIds.AskNumber + ":" + def.id);
            var h = PlayerHouse;
            bool gradeOk = "ABCDEF".IndexOf(h.grade ?? "C", StringComparison.Ordinal) <= "ABCDEF".IndexOf(def.unlockMinGrade ?? "F", StringComparison.Ordinal);
            if (h.popularity < def.unlockMinPopularity || !gradeOk)
                return ActionResult.Fail("not_allowed_role", def.displayName + " isn't interested in giving " + h.name + " their number yet.");
            if (G.enabled && PlayerOpinion(def.id, p.playerId) <= G.numberRefuseOpinion)
                return ActionResult.Fail("not_allowed_role", def.displayName + ": \"I've heard about you.\"");
            if (!NextRng("ask-number").Chance(def.unlockChance)) return ActionResult.Fail("cap_reached", def.displayName + " brushed you off this time.");
            npc.contactUnlocked = true;
            Emit(GameEventKind.HouseChanged, "contact_unlocked", h.id, p.playerId, def.id);
            return ActionResult.Ok("Got " + def.displayName + "'s number.");
        }

        ActionResult ExtendBid(PlayerState p, PlayerAction a)
        {
            if (!RoleOf(p).canExtendBid) return ActionResult.Fail("not_allowed_role", "Only the Rush Chair or President can extend bids.");
            if (InPhase(Phase.SemesterEnd)) return ActionResult.Fail("wrong_phase", "Bids reopen next semester.");
            var npc = Npc(a.target);
            if (npc == null || !npc.isPnm || !string.IsNullOrEmpty(npc.memberOf)) return ActionResult.Fail("invalid_target", "That person can't receive a bid.");
            if (npc.bids.Contains(Config.playerHouseId)) return ActionResult.Fail("already_done", "Bid already extended.");
            npc.bids.Add(Config.playerHouseId);
            return ActionResult.Ok("Bid extended to " + (RosterNpc(a.target)?.displayName ?? a.target) + ". They'll decide on Bid Day (Sunday). Interest: " +
                RushInterest(a.target, Config.playerHouseId).ToString("0") + "/100.");
        }

        // ---------- Economy ----------

        ActionResult Buy(PlayerState p, PlayerAction a)
        {
            var item = Config.Item(a.item);
            if (item == null) return ActionResult.Fail("invalid_target", "The shop doesn't sell that.");
            if (InPhase(Phase.SemesterEnd)) return ActionResult.Fail("wrong_phase", "The shop is closed.");
            if (p.location != Config.shopLocation) return ActionResult.Fail("wrong_location", "Go to the campus store.");
            int qty = Math.Max(1, a.amount);
            var h = PlayerHouse;
            if (StashCount(h, item.id) + qty > item.maxStack) return ActionResult.Fail("cap_reached", "You can't store more " + item.name + ".");
            int cost = (int)Math.Round(item.price * (1 - RoleOf(p).shopDiscount)) * qty;
            if (h.cash < cost) return ActionResult.Fail("no_funds", "Not enough cash ($" + h.cash + " of $" + cost + ").");
            h.cash -= cost;
            AddStash(h, item.id, qty);
            return ActionResult.Ok("Bought " + qty + "x " + item.name + " for $" + cost + ".");
        }

        ActionResult MeetDean(PlayerState p)
        {
            if (!RoleOf(p).canMeetDean) return ActionResult.Fail("not_allowed_role", "Only the President meets the Dean.");
            if (!InPhase(Phase.Weekday)) return ActionResult.Fail("wrong_phase", "The Dean only takes meetings on weekdays.");
            if (p.location != Config.quadLocation) return ActionResult.Fail("wrong_location", "The Dean's office is off the Quad.");
            var h = PlayerHouse;
            if (h.deanMetThisWeek) return ActionResult.Fail("cap_reached", "You already met the Dean this week.");
            h.deanMetThisWeek = true;
            bool wasProbation = h.probation;
            h.dean = Clamp(h.dean + Config.dean.meetDeanGain, 0, 100);
            if (wasProbation && h.dean >= Config.dean.probationDean) { h.probation = false; News("dean", "The Dean lifted " + h.name + "'s probation.", h.id, p.playerId, isPublic: true); }
            return ActionResult.Ok("The Dean appreciated the visit.");
        }

        ActionResult SetTheme(PlayerState p, PlayerAction a)
        {
            var role = RoleOf(p);
            if (!role.canSetTheme && !role.canStartWeekend) return ActionResult.Fail("not_allowed_role", "The Social Chair picks the theme.");
            if (!InPhase(Phase.Weekday)) return ActionResult.Fail("wrong_phase", "Pick the theme before Saturday.");
            if (p.location != Config.playerHomeLocation) return ActionResult.Fail("wrong_location", "Plan the theme at the house.");
            var theme = Config.Theme(a.item);
            if (theme == null) return ActionResult.Fail("invalid_target", "Unknown theme.");
            PlayerHouse.theme = theme.id;
            News("theme", "This Saturday's theme: " + theme.name + ".", PlayerHouse.id, p.playerId);
            return ActionResult.Ok("Theme set: " + theme.name + ".");
        }

        ActionResult SetCover(PlayerState p, PlayerAction a)
        {
            if (!RoleOf(p).canSetCover) return ActionResult.Fail("not_allowed_role", "The Treasurer sets the cover charge.");
            if (!InPhase(Phase.Weekday)) return ActionResult.Fail("wrong_phase", "Set the cover before Saturday.");
            if (a.amount < 0 || a.amount > Config.economy.coverMax) return ActionResult.Fail("invalid_target", "Cover must be $0-$" + Config.economy.coverMax + ".");
            PlayerHouse.cover = a.amount;
            return ActionResult.Ok("Cover charge: $" + a.amount + ".");
        }

        ActionResult CleanHouse(PlayerState p)
        {
            if (!InPhase(Phase.Weekday, Phase.Debrief)) return ActionResult.Fail("wrong_phase", "Not now.");
            if (p.location != Config.playerHomeLocation) return ActionResult.Fail("wrong_location", "Go to the house.");
            if (CounterValue(p.daily, ActionIds.CleanHouse) >= 1) return ActionResult.Fail("cap_reached", "You already cleaned today.");
            CounterAdd(p.daily, ActionIds.CleanHouse);
            var h = PlayerHouse;
            bool wasTrashed = h.trashed;
            h.trashed = false;
            h.heat = Clamp(h.heat - Config.dean.cleanHeat, 0, 100);
            return ActionResult.Ok(wasTrashed ? "Cleaned up the prank mess." : "The house is spotless.");
        }

        ActionResult GuardHouse(PlayerState p)
        {
            if (p.location != Config.playerHomeLocation) return ActionResult.Fail("wrong_location", "You have to be at the house.");
            bool evening = State.phase == Phase.Weekday && ClockHour >= 17;
            if (!(evening || State.phase == Phase.Party)) return ActionResult.Fail("wrong_phase", "Guard duty is for evenings and party night.");
            PlayerHouse.guarded = true;
            return ActionResult.Ok("You're keeping watch. The next sneak attack will be stopped.");
        }

        ActionResult ClaimRole(PlayerState p, PlayerAction a)
        {
            if (!InPhase(Phase.Weekday, Phase.Debrief)) return ActionResult.Fail("wrong_phase", "Roles change outside party night.");
            var role = Config.Role(a.target);
            if (role == null) return ActionResult.Fail("invalid_target", "Unknown role.");
            if (role.unique && State.players.Any(x => x.role == role.id && x.playerId != p.playerId))
                return ActionResult.Fail("already_done", role.name + " is taken.");
            p.role = role.id;
            News("role", p.displayName + " is now " + role.name + ".", PlayerHouse.id, p.playerId);
            Emit(GameEventKind.RoleChanged, role.id, PlayerHouse.id, p.playerId);
            return ActionResult.Ok("You're the " + role.name + ".");
        }

        // ---------- Party night ----------

        ActionResult PartyAction(PlayerState p, PlayerAction a)
        {
            var party = State.party;
            if (State.phase != Phase.Party || !party.active) return ActionResult.Fail("wrong_phase", "Your party isn't running.");
            if (p.location != Config.playerHomeLocation) return ActionResult.Fail("wrong_location", "Get back to the house.");
            var h = PlayerHouse;
            switch (a.action)
            {
                case ActionIds.ServeDrinks:
                    party.serveTick = party.tick;
                    party.servingPlayer = p.playerId;
                    foreach (var g in PresentGuests) g.mood = Clamp(g.mood + 2, 0, 100);
                    return ActionResult.Ok(party.cardCheck || party.cardCheckCoverage > 0 ? "Pouring drinks (IDs being checked)." : "Pouring drinks. Nobody's checking IDs...");
                case ActionIds.CardCheck:
                    party.cardCheck = a.amount != 0;
                    return ActionResult.Ok(party.cardCheck ? "Checking IDs at the door." : "Stopped checking IDs.");
                case ActionIds.Dj:
                    if (GearHype(h) <= 0) return ActionResult.Fail("missing_item", "You need speakers to DJ.");
                    if (party.tick - party.djTick < Config.party.djCooldownTicks) return ActionResult.Fail("cap_reached", "The crowd needs a breather.");
                    party.djTick = party.tick;
                    party.hype = Clamp(party.hype + Config.party.djHype * RoleOf(p).djMult, 0, 100);
                    return ActionResult.Ok("The crowd goes wild. Hype " + party.hype.ToString("0") + ".");
                case ActionIds.TurnDownMusic:
                    party.musicDownTick = party.tick;
                    party.hype = Clamp(party.hype - 10, 0, 100);
                    party.attention = Clamp(party.attention - Config.party.musicDownAttention, 0, 100);
                    ResolveIncidents("noise_complaint");
                    return ActionResult.Ok("Music down. The neighbours calm down a little.");
                case ActionIds.BreakUpFight: return ResolveIncidentAction(p, a, "fight", "Broke up the fight.");
                case ActionIds.HelpGuest: return ResolveIncidentAction(p, a, "passed_out", "Got the guest some water and a ride home.");
                case ActionIds.FixDamage: return ResolveIncidentAction(p, a, "property_damage", "Patched things up before it got worse.");
                case ActionIds.KickOut:
                {
                    var worst = PresentGuests.OrderByDescending(g => g.trouble * (0.3f + g.drunk)).FirstOrDefault();
                    if (worst == null) return ActionResult.Fail("invalid_target", "Nobody to kick out.");
                    Leave(worst, "kicked_out");
                    foreach (var inc in OpenIncidents.Where(i => i.guestId == worst.guestId).ToList()) ResolveIncident(inc);
                    return ActionResult.Ok("Kicked out the worst troublemaker.");
                }
                case ActionIds.BeerRun:
                {
                    var keg = Config.Item("keg");
                    if (keg == null) return ActionResult.Fail("invalid_target", "No kegs for sale.");
                    int cost = (int)Math.Round(keg.price * Config.party.beerRunMarkup * (1 - RoleOf(p).shopDiscount));
                    if (h.cash < cost) return ActionResult.Fail("no_funds", "Not enough cash for a beer run ($" + cost + ").");
                    h.cash -= cost;
                    AddStash(h, keg.id, 1);
                    p.busyUntil = State.time + Config.party.beerRunTicks * Config.partyTickSeconds;
                    return ActionResult.Ok("Beer run: +1 keg for $" + cost + ".");
                }
                case ActionIds.TalkToPolice:
                {
                    if (party.dispatchedPatrol < 0) return ActionResult.Fail("invalid_target", "There are no cops to talk to.");
                    if (party.talkAttempted) return ActionResult.Fail("already_done", "Someone already tried talking to them.");
                    party.talkAttempted = true;
                    float chance = Config.police.talkSuccess + RoleOf(p).policeTalkBonus + p.rep / 200f - Drunk01(p) * Config.drunk.policeTalkPenalty;
                    bool success = NextRng("police-talk").Chance(Clamp(chance, 0.05f, 0.9f));
                    if (success) party.policeTalked = true;
                    return ActionResult.Ok(success ? "You smooth-talked dispatch. If they show up, it'll just be a warning." : "That didn't land. They're still coming.");
                }
            }
            return ActionResult.Fail("unknown_action", "Unknown party action.");
        }

        ActionResult ResolveIncidentAction(PlayerState p, PlayerAction a, string kind, string message)
        {
            IncidentState inc = null;
            if (int.TryParse(a.target, out int id)) inc = OpenIncidents.FirstOrDefault(i => i.id == id && i.kind == kind);
            if (inc == null) inc = OpenIncidents.FirstOrDefault(i => i.kind == kind);
            if (inc == null) return ActionResult.Fail("invalid_target", "Nothing like that is happening.");
            ResolveIncident(inc);
            if (kind == "passed_out")
            {
                var guest = State.party.guests.Find(g => g.guestId == inc.guestId);
                if (guest != null) Leave(guest, "helped_home");
            }
            return ActionResult.Ok(message);
        }

        // ---------- Crimes / sabotage (design §3.6) ----------

        ActionResult Crime(PlayerState p, CrimeDef crime, PlayerAction a)
        {
            if (crime.phase == "weekday" && State.phase != Phase.Weekday) return ActionResult.Fail("wrong_phase", crime.name + " is a weekday move.");
            if (crime.phase == "party" && State.phase != Phase.Party) return ActionResult.Fail("wrong_phase", crime.name + " only works on party night.");
            if (crime.phase == "both" && !InPhase(Phase.Weekday, Phase.Party)) return ActionResult.Fail("wrong_phase", "Not now.");
            if (crime.minHour >= 0 && (ClockHour < crime.minHour || ClockHour >= crime.maxHour))
                return ActionResult.Fail("wrong_phase", crime.name + " only works between " + crime.minHour + ":00 and " + crime.maxHour + ":00.");
            HouseState rival;
            string location = p.location;
            RosterNpc npcDef = null;
            switch (crime.where)
            {
                case "rival_house":
                    rival = House(HouseAtLocation(p.location));
                    if (rival == null || rival.isPlayer) return ActionResult.Fail("wrong_location", "You need to be at a rival house.");
                    if (!string.IsNullOrEmpty(a.target) && a.target != rival.id) return ActionResult.Fail("wrong_location", "That's not this house.");
                    break;
                case "npc":
                    npcDef = RosterNpc(a.target);
                    rival = House(a.rival);
                    if (npcDef == null || rival == null || rival.isPlayer) return ActionResult.Fail("invalid_target", "Pick a person and a rival house.");
                    if (NpcLocation(npcDef.id) != p.location || string.IsNullOrEmpty(p.location)) return ActionResult.Fail("wrong_location", npcDef.displayName + " isn't here.");
                    break;
                default:
                    rival = House(a.target);
                    if (rival == null || rival.isPlayer) return ActionResult.Fail("invalid_target", "Pick a rival house.");
                    location = "phone";
                    break;
            }
            if (crime.phase == "party" && rival.suspended) return ActionResult.Fail("invalid_target", rival.name + " isn't partying tonight.");
            var h = PlayerHouse;
            if (!string.IsNullOrEmpty(crime.requiresItem))
            {
                if (StashCount(h, crime.requiresItem) <= 0) return ActionResult.Fail("missing_item", "You need a " + (Config.Item(crime.requiresItem)?.name ?? crime.requiresItem) + ".");
                AddStash(h, crime.requiresItem, -1);
            }
            var witnesses = location == "phone" ? new List<string>() : NpcsAt(location);
            if (npcDef != null) witnesses.Remove(npcDef.id);
            bool caught = NextRng("crime-" + crime.id).Chance(DetectionChance(crime, location, witnesses.Count, Drunk01(p)));
            string act = crime.name.ToLowerInvariant();
            string phrase = (string.IsNullOrEmpty(crime.gossipPhrase) ? act + " at {rival}" : crime.gossipPhrase).Replace("{rival}", rival.name);
            string rumorEvent = null, crimeEvent = null;
            SmearClaimDef claim = null;
            if (npcDef != null)
            {
                // The listener always knows who told them; that is the rumor's whole mechanism. item may name the claim.
                claim = PickClaim(a.item, "smear-claim");
                rumorEvent = social.Observe("rumor_spread", p.playerId, rival.name, p.displayName + " told you that " + rival.name + " " + claim.text + ".", location, new[] { npcDef.id });
            }
            if (witnesses.Count > 0)
            {
                crimeEvent = social.Observe(crime.id, p.playerId, rival.name, p.displayName + " (" + h.name + ") was seen: " + act + " at " + rival.name + ".", location, witnesses);
                foreach (var w in witnesses) { var n = Npc(w); if (n != null && n.isPnm) AddRushBonus(n, h.id, -Config.rush.crimePenalty); }
            }
            if (caught)
            {
                if (crime.severity == "full") Bust(p, act + " at " + rival.name, phrase);
                else
                {
                    h.dean = Clamp(h.dean - Config.police.traceDean, 0, 100);
                    h.heat = Clamp(h.heat + Config.police.traceHeat, 0, 100);
                    News("busted", "Campus police traced \"" + act + "\" back to " + h.name + ".", h.id, p.playerId, isPublic: true);
                    // A traced lite crime leaks: the police blotter reaches the campus hubs (and anyone who saw or heard it).
                    var kind = Config.StoryKind(crime.gossipKind);
                    if (kind != null)
                        Seed(kind.id, kind.id == "smear_backfire" ? G.backfireSeverity : kind.severity,
                            GossipHubs().Concat(witnesses).Concat(npcDef != null ? new[] { npcDef.id } : new string[0]),
                            h.id, p.playerId, otherHouseId: rival.id, detail: phrase, location: location);
                }
                return new ActionResult { ok = true, code = "busted", message = "Busted! " + (crime.severity == "full" ? "Campus police caught you." : "It got traced back to the house.") };
            }
            if (npcDef != null)
                Seed("smear", G.smearSeverity * (0.8f + 0.4f * Gossipiness(npcDef.id)), new[] { npcDef.id }, rival.id, origin: "player:" + p.playerId,
                    fabricated: !ClaimTrue(claim, rival), detail: claim.text, socialEventId: rumorEvent, location: location, heardFrom: "player:" + p.playerId,
                    beliefFor: id => HearsayBelief("smear", rival.id, false, 0.8f, id, social.KnowsPlayer(id, p.playerId) ? 0.65f : 0.45f));
            else if (witnesses.Count > 0 && Config.StoryKind(crime.gossipKind) != null && crime.gossipKind != "snitched")
                Seed(crime.gossipKind, Config.StoryKind(crime.gossipKind).severity + 5 * Math.Min(6, witnesses.Count), witnesses, h.id, p.playerId,
                    otherHouseId: rival.id, detail: phrase, socialEventId: crimeEvent, location: location);
            // Effects apply only when undetected.
            float popGain = Math.Min(crime.actorPopGain, Math.Max(0, Config.scoring.sabotagePopCap - h.weekSabotagePop));
            h.weekSabotagePop += popGain;
            rival.popularity = Clamp(rival.popularity - crime.rivalPopLoss, 0, 100);
            rival.dean = Clamp(rival.dean - crime.rivalDeanLoss, 0, 100);
            rival.heat = Clamp(rival.heat + crime.rivalHeat, 0, 100);
            if (crime.id == "prank") rival.trashed = true;
            var rp = State.rivalParties.Find(r => r.houseId == rival.id);
            if (rp != null) { rp.moodPenalty += crime.rivalMoodLoss; rp.attention += crime.rivalAttention; if (crime.rivalMoodLoss > 0) rp.stinkBombed |= crime.id == "stink_bomb"; }
            if (!string.IsNullOrEmpty(crime.stashGain) && crime.stashGainCount > 0) AddStash(h, crime.stashGain, crime.stashGainCount);
            if (npcDef != null)
            {
                var n = Npc(npcDef.id);
                var r = n.rivalRegard.Find(x => x.houseId == rival.id);
                if (r == null) n.rivalRegard.Add(new HouseValue { houseId = rival.id, value = -crime.rumorRegardLoss });
                else r.value = Clamp(r.value - crime.rumorRegardLoss, -100, 100);
            }
            News("sabotage", crime.newsText.Replace("{rival}", rival.name).Replace("{actor}", "Someone"), rival.id, isPublic: crime.severity == "full");
            return ActionResult.Ok("Pulled it off" + (witnesses.Count > 0 ? ", but " + witnesses.Count + " people saw you." : " clean."));
        }
    }
}
