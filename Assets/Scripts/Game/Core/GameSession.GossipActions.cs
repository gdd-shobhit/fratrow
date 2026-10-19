using System;
using System.Collections.Generic;
using System.Linq;

namespace FratRow.Game
{
    public sealed partial class GameSession
    {
        // ---------- Player gossip actions (design §3.7) ----------

        /// <summary>Common validation: gossip on, a known NPC, reachable in person or by text.</summary>
        ActionResult GossipTarget(PlayerState p, PlayerAction a, out RosterNpc def, out string channel)
        {
            def = RosterNpc(a.target);
            channel = "";
            if (!G.enabled) return ActionResult.Fail("unknown_action", "Gossip is turned off.");
            if (State.phase == Phase.SemesterEnd) return ActionResult.Fail("wrong_phase", "The semester is over.");
            var npc = Npc(a.target);
            if (def == null || npc == null) return ActionResult.Fail("invalid_target", "Unknown person.");
            string here = NpcLocation(def.id);
            bool inPerson = !string.IsNullOrEmpty(here) && here == p.location;
            if (!inPerson && !npc.contactUnlocked) return ActionResult.Fail("wrong_location", def.displayName + " isn't here and you don't have their number.");
            channel = inPerson ? "in_person" : "text";
            return null;
        }

        GossipStory StoryArg(PlayerAction a) => int.TryParse(a.item, out int id) ? Story(id) : null;

        /// <summary><c>ask_gossip</c> target=NPC: what they've heard, in their version, with story ids for deny/trace/expose.</summary>
        ActionResult AskGossip(PlayerState p, PlayerAction a)
        {
            var fail = GossipTarget(p, a, out var def, out _);
            if (fail != null) return fail;
            string key = ActionIds.AskGossip + ":" + def.id;
            if (CounterValue(p.daily, key) >= G.askPerNpcPerDay) return ActionResult.Fail("cap_reached", "You already caught up with " + def.displayName + " today.");
            CounterAdd(p.daily, key);
            var npc = Npc(def.id);
            var heard = State.gossip.Where(k => k.npcId == def.id)
                .Select(k => new { k, s = Story(k.storyId) })
                .Where(x => x.s != null && !(npc.memberOf != "" && npc.memberOf == x.s.houseId && Config.StoryKind(x.s.kind)?.valence < 0))
                .OrderBy(x => x.s.retired).ThenByDescending(x => x.s.heat).ThenByDescending(x => x.s.id).Take(3).ToList();
            if (heard.Count == 0) return ActionResult.Ok(def.displayName + " hasn't heard anything worth repeating.");
            // Each story capped so three of them fit the 512-byte replicated reply.
            return ActionResult.Ok(def.displayName + ": " + string.Join(" ", heard.Select(x => "[#" + x.s.id + "] " + Short(StoryText(x.s, x.k.detail, x.k.spice), 150) +
                (x.k.belief < 0.4f ? " (not sure it's true)" : ""))));
        }

        static string Short(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 1).TrimEnd() + "…";

        /// <summary>
        /// <c>deny_rumor</c> target=NPC item=story id. Only for bad stories about the players' house or a brother. Denying to someone who
        /// hadn't heard spreads it (innuendo); denying something they witnessed fails; a fabricated story is easier to deny than a true one.
        /// </summary>
        ActionResult DenyRumor(PlayerState p, PlayerAction a)
        {
            var fail = GossipTarget(p, a, out var def, out string channel);
            if (fail != null) return fail;
            var s = StoryArg(a);
            if (s == null) return ActionResult.Fail("invalid_target", "Which rumor? Ask around first.");
            if (s.houseId != Config.playerHouseId && Player(s.playerId) == null) return ActionResult.Fail("invalid_target", "That one isn't about us.");
            if ((Config.StoryKind(s.kind)?.valence ?? 0) > 0) return ActionResult.Fail("invalid_target", "Why would you deny that?");
            // One denial per NPC per day for the whole crew: four brothers can't take turns wearing someone down.
            var npc = Npc(def.id);
            if (npc.deniedDay != DayIndex) { npc.deniedDay = DayIndex; npc.deniesToday = 0; }
            if (npc.deniesToday >= G.denyPerNpcPerDay) return ActionResult.Fail("cap_reached", def.displayName + " already heard your side today.");
            npc.deniesToday++;
            var k = Know(def.id, s.id);
            int valence = Config.StoryKind(s.kind)?.valence ?? 0;
            // The NPC remembers being told it isn't true. The record also says whether they still hold it against anyone.
            string claim = StoryText(s, 0, 0);
            string denial = NewLedgerId("denial", p.playerId, def.id, claim);
            var heard = new List<string> { def.id };
            string text = p.displayName + " insisted to you that this is not true: " + claim;
            if (k == null)
            {
                // They only have the denier's word, not the original record: secondhand detail, never a full-detail link.
                AddKnowledge(new GossipKnowledge { storyId = s.id, npcId = def.id, heardFrom = "player:" + p.playerId, tellers = { "player:" + p.playerId },
                    detail = 1, belief = G.denyBackfireBelief, learnedDay = DayIndex, ledger = denial });
                QueueObservation(denial, "denial", p.playerId, def.id, text, channel, heard, s, 0);
                if (s.retired && !s.exposed && DayIndex - s.createdDay < 7 * Math.Max(1, G.memoryWeeks) && State.stories.Count(x => !x.retired) < G.maxActiveStories)
                { s.retired = false; s.heat = Math.Max(s.heat, G.retireHeat * 2); }
                return ActionResult.Ok(def.displayName + " hadn't heard that one... until now. Now they're curious.");
            }
            if (k.heardFrom == "witness")
            {
                QueueObservation(denial, "denial", p.playerId, def.id, text, channel, heard, s, valence);
                return ActionResult.Ok(def.displayName + " was there. Nobody's buying the denial.");
            }
            float credibility = Clamp(0.35f + social.RegardForPlayers(def.id) / 200f + (social.KnowsPlayer(def.id, p.playerId) ? 0.1f : 0f), 0.1f, 1f);
            float power = G.denyPower * credibility * (s.fabricated ? 1.3f : G.denyTrueFactor);
            k.belief = Clamp(k.belief * (1 - power), 0, 1);
            if (!s.fabricated) k.spice = 0; // "It wasn't THAT bad."
            if (k.belief < G.spreadMinBelief) k.spreader = false;
            QueueObservation(denial, "denial", p.playerId, def.id, text, channel, heard, s, Believes(k) ? valence : 0);
            gossipVersion++;
            return ActionResult.Ok(k.belief < G.beliefThreshold ? def.displayName + " buys it. Mostly." : def.displayName + " doesn't look convinced.");
        }

        /// <summary><c>trace_rumor</c> target=NPC item=story id: who told them. Reaching a rival house's source marks the story traced.</summary>
        ActionResult TraceRumor(PlayerState p, PlayerAction a)
        {
            var fail = GossipTarget(p, a, out var def, out _);
            if (fail != null) return fail;
            var s = StoryArg(a);
            if (s == null) return ActionResult.Fail("invalid_target", "Which rumor? Ask around first.");
            if (CounterValue(p.daily, ActionIds.TraceRumor) >= G.tracePerDay) return ActionResult.Fail("cap_reached", "You've chased enough rumors for one day.");
            CounterAdd(p.daily, ActionIds.TraceRumor);
            var k = Know(def.id, s.id);
            if (k == null) return ActionResult.Ok(def.displayName + " hasn't heard that one.");
            string sourceHouse = s.origin.StartsWith("house:", StringComparison.Ordinal) ? s.origin.Substring(6) : "";
            if (sourceHouse != "" && Npc(def.id).memberOf == sourceHouse) return ActionResult.Ok(def.displayName + " won't say where they heard it.");
            if (k.heardFrom == "witness") return ActionResult.Ok(def.displayName + " says they saw it happen themselves.");
            if (k.heardFrom.StartsWith("player:", StringComparison.Ordinal))
                return ActionResult.Ok(def.displayName + " heard it from " + (Player(k.heardFrom.Substring(7))?.displayName ?? "one of your brothers") + ".");
            if (k.heardFrom.StartsWith("house:", StringComparison.Ordinal))
                return Traced(s, def.displayName + " says a " + HouseName(k.heardFrom.Substring(6)) + " brother told them.");
            var teller = RosterNpc(k.heardFrom);
            string name = teller?.displayName ?? k.heardFrom;
            if (sourceHouse != "" && Npc(k.heardFrom)?.memberOf == sourceHouse)
                return Traced(s, def.displayName + " heard it from " + name + " (" + HouseName(sourceHouse) + ").");
            return ActionResult.Ok(def.displayName + " heard it from " + name + ".");
        }

        ActionResult Traced(GossipStory s, string message)
        {
            if (!s.tracedBy.Contains(Config.playerHouseId)) s.tracedBy.Add(Config.playerHouseId);
            return ActionResult.Ok(message + " That's the source. Expose it (story #" + s.id + ") if it's a lie.");
        }

        /// <summary><c>expose_rumor</c> item=story id: publicly debunk a traced smear and turn it on whoever started it.</summary>
        ActionResult ExposeRumor(PlayerState p, PlayerAction a)
        {
            if (!G.enabled) return ActionResult.Fail("unknown_action", "Gossip is turned off.");
            if (State.phase == Phase.SemesterEnd) return ActionResult.Fail("wrong_phase", "The semester is over.");
            var s = StoryArg(a);
            if (s == null) return ActionResult.Fail("invalid_target", "Which rumor?");
            if (s.exposed) return ActionResult.Fail("already_done", "That one's already been blown open.");
            if (!s.tracedBy.Contains(Config.playerHouseId)) return ActionResult.Fail("invalid_target", "You don't know who started it. Trace it first.");
            if (!s.fabricated) return ActionResult.Fail("invalid_target", "It checks out. There's nothing to expose.");
            Expose(s, Config.playerHouseId, p.location);
            return ActionResult.Ok("You blew it open: " + HouseName(s.origin.StartsWith("house:", StringComparison.Ordinal) ? s.origin.Substring(6) : "") +
                " made that up. People are already talking.");
        }

        // ---------- Who said what (design §3.3, §4.1) ----------

        const int MaxRememberedTurns = 512;

        /// <summary>
        /// After a committed conversation turn: a turn that really changed something (a grievance, a big trust drop, a withdrawn RSVP)
        /// becomes a story the NPC and any bystanders tell; charming a notable NPC into coming becomes a good one. Ordinary talk never does.
        /// </summary>
        void SeedFromTurn(PlayerState p, RosterNpc def, string requestId)
        {
            if (!G.enabled || def == null) return;
            var impact = social.TurnImpact(p.playerId, requestId);
            if (!impact.known) return;
            // A replayed or retried commit returns the same stored result; it must not seed (or refresh) anything again.
            string turnKey = p.playerId + "/" + (requestId ?? "");
            if (State.gossipTurns.Contains(turnKey)) return;
            State.gossipTurns.Add(turnKey);
            while (State.gossipTurns.Count > MaxRememberedTurns) State.gossipTurns.RemoveAt(0);
            // Bystanders only while the NPC is still with the player: after a slow reply the scene may be gone.
            bool inPerson = impact.channel == "in_person";
            bool together = inPerson && !string.IsNullOrEmpty(p.location) && NpcLocation(def.id) == p.location;
            string location = together ? p.location : impact.channel ?? "text";
            var bystanders = together ? NpcsAt(p.location).Where(x => x != def.id).ToList() : new List<string>();
            bool withdrew = (impact.previousCommitment == "accepted" || impact.previousCommitment == "conditional") && impact.commitment == "declined";
            float bad = (impact.grievance ? G.saidGrievance : 0) + (impact.trust <= -5 ? G.saidTrustDrop : 0) +
                (impact.regard < 0 ? G.saidRegardDrop : 0) + (withdrew ? G.saidWithdrew : 0);
            if (bad > 0)
            {
                var s = Seed("who_said", bad * (0.8f + def.popularity / 100f), new[] { def.id }, Config.playerHouseId, p.playerId, def.id,
                    quote: impact.text, origin: "npc:" + def.id, socialEventId: impact.speechEventId, location: location);
                if (s != null && bystanders.Count > 0) AddKnowers(s, bystanders, null, location, "witness", _ => 0.9f);
                return;
            }
            // Charming someone is only news when it's an upset: a notable person more popular than the house going there.
            float upset = def.popularity - PlayerHouse.popularity;
            bool charmed = impact.commitment == "accepted" && impact.previousCommitment != "accepted" && def.popularity >= Config.scoring.notablePopularity && upset > 0;
            if (charmed)
                Seed("who_charmed", G.charmBase + upset * 1.5f + (impact.trust > 0 ? 10 : 0), new[] { def.id }.Concat(bystanders),
                    Config.playerHouseId, p.playerId, def.id, origin: "npc:" + def.id, location: location);
        }
    }
}
