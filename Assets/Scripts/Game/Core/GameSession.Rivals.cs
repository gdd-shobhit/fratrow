using System;
using System.Collections.Generic;
using System.Linq;

namespace FratRow.Game
{
    public sealed partial class GameSession
    {
        /// <summary>Design §3.7 week start: budget, theme, sabotage intent and weekly income for every rival.</summary>
        void RivalWeekStart()
        {
            var rng = Rng("rival-week");
            var ranked = Ranked();
            var player = PlayerHouse;
            foreach (var h in Rivals)
            {
                h.cash += Config.economy.rivalWeeklyIncome + h.memberCount * Config.economy.dues / 2;
                h.dean = Clamp(h.dean + Config.rivals.deanRecoveryBase + h.caution * Config.rivals.deanRecoveryCaution, 0, 100);
                h.weekBudget = (int)(h.cash * (Config.rivals.budgetBase + Config.rivals.budgetPrestige * h.prestige));
                h.cash -= h.weekBudget;
                h.theme = Config.themes.Count == 0 ? "" : Config.themes[rng.Range(0, Config.themes.Count)].id;
                bool threatened = ranked.IndexOf(player) < ranked.IndexOf(h) || Math.Abs(player.popularity - h.popularity) <= Config.rivals.closeRace;
                h.sabotageIntent = h.aggression * (threatened ? 1.3f : 1f) * (1 - h.caution * h.heat / 100f) * (h.suspended ? 0.5f : 1f);
            }
        }

        /// <summary>Design §3.7 daily: each rival may prank, spread a rumor about, or poach from a target house.</summary>
        void RivalDaily()
        {
            var ranked = Ranked();
            foreach (var rival in Rivals.ToList())
            {
                var rng = Rng("rival-day-" + rival.id);
                if (!rng.Chance(rival.sabotageIntent * Config.rivals.dailySabotage)) continue;
                var player = PlayerHouse;
                bool playerThreat = ranked.IndexOf(player) < ranked.IndexOf(rival) || Math.Abs(player.popularity - rival.popularity) <= Config.rivals.closeRace;
                HouseState target = playerThreat || rng.Chance(0.6) ? player : NextHouseUp(ranked, rival) ?? player;
                if (target == rival) continue;
                float[] weights = { 0.4f + rival.chaos, 0.3f + rival.prestige, 0.2f + rival.caution * 0.5f };
                int action = rng.Weighted(weights);
                if (target.guarded)
                {
                    target.guarded = false;
                    rival.dean = Clamp(rival.dean - 2, 0, 100);
                    if (target.isPlayer) News("defense", "Your brothers caught a " + rival.name + " pledge sneaking around the house and chased him off.", target.id);
                    continue;
                }
                switch (action)
                {
                    case 0: RivalPrank(rival, target, rng); break;
                    case 1: RivalRumor(rival, target, rng); break;
                    default: RivalPoach(rival, target, rng); break;
                }
            }
        }

        static HouseState NextHouseUp(List<HouseState> ranked, HouseState h)
        {
            int i = ranked.IndexOf(h);
            return i > 0 ? ranked[i - 1] : ranked.Count > 1 ? ranked[1] : null;
        }

        void RivalPrank(HouseState rival, HouseState target, GameRng rng)
        {
            target.trashed = true;
            target.popularity = Clamp(target.popularity - Config.rivals.prankPopLoss, 0, 100);
            target.weekSabotagePop -= Config.rivals.prankPopLoss;
            bool attributed = rng.Chance(0.5);
            string by = attributed ? " Word is it was " + rival.name + "." : "";
            if (target.isPlayer) News("rival_sabotage", PlayerHouse.name + "'s house got trashed overnight." + by + " Clean it before Saturday.", target.id, isPublic: true);
            else target.sabotageScoreMod -= 3;
            var witnesses = NpcsAt(target.homeLocation);
            string seen = null;
            if (attributed && witnesses.Count > 0)
                seen = social.Observe("vandalism", rival.name, target.name, rival.name + " brothers trashed " + target.name + "'s house.", target.homeLocation, witnesses);
            Seed("house_trashed", Config.StoryKind("house_trashed")?.severity ?? 40, witnesses, target.id, otherHouseId: attributed ? rival.id : "",
                socialEventId: seen, location: target.homeLocation);
        }

        void RivalRumor(HouseState rival, HouseState target, GameRng rng)
        {
            var listeners = State.npcs.Where(n => string.IsNullOrEmpty(n.memberOf) && n.memberOf != rival.id).OrderBy(_ => rng.NextULong()).Take(2).ToList();
            var claim = PickClaim("", "rival-claim-" + rival.id);
            foreach (var npc in listeners)
            {
                if (target.isPlayer) AddRushBonus(npc, target.id, -Config.rivals.rumorRushLoss);
                else
                {
                    var r = npc.rivalRegard.Find(x => x.houseId == target.id);
                    if (r == null) npc.rivalRegard.Add(new HouseValue { houseId = target.id, value = -5 }); else r.value = Clamp(r.value - 5, -100, 100);
                }
            }
            if (listeners.Count == 0) return;
            // The smear is a real, traceable story: told by a named member when the rival has recruits, else "a Kappa brother".
            string teller = rival.npcMembers.OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
            string tellerName = teller == null ? "A " + rival.name + " brother" : (RosterNpc(teller)?.displayName ?? teller) + " from " + rival.name;
            var ids = listeners.Select(n => n.npcId).ToList();
            // Only smears about the players become NPC memory (as before); rival-vs-rival ones stay game-side (ADR-0002 §4).
            string seen = target.isPlayer ? social.Observe("rumor_about_house", rival.name, target.name, tellerName + " told you that " + target.name + " " + claim.text + ".", "Quad", ids) : null;
            Seed("smear", G.smearSeverity, ids, target.id, origin: "house:" + rival.id, fabricated: !ClaimTrue(claim, target), detail: claim.text,
                socialEventId: seen, location: "Quad", heardFrom: teller ?? "house:" + rival.id,
                beliefFor: id => HearsayBelief("smear", target.id, false, 0.8f, id, G.tieTogether));
            if (target.isPlayer) News("rival_sabotage", rival.name + " is spreading rumors about " + target.name + ".", target.id);
        }

        void RivalPoach(HouseState rival, HouseState target, GameRng rng)
        {
            var pnm = State.npcs.Where(n => n.isPnm && string.IsNullOrEmpty(n.memberOf)).OrderBy(_ => rng.NextULong()).FirstOrDefault();
            if (pnm == null) return;
            AddRushBonus(pnm, rival.id, Config.rush.poachBonus);
            if (target.isPlayer) News("rival_sabotage", rival.name + " has been rushing " + (RosterNpc(pnm.npcId)?.displayName ?? pnm.npcId) + " hard.", target.id, npcId: pnm.npcId);
        }

        void StartRivalParties()
        {
            State.rivalParties.Clear();
            foreach (var h in Rivals) State.rivalParties.Add(new RivalPartyState { houseId = h.id });
        }

        /// <summary>Rivals may call the cops on, or stink-bomb, the players' party at a random tick (blocked by guard_house).</summary>
        void ScheduleRivalPartySabotage()
        {
            var p = State.party;
            if (!p.active) return;
            var ranked = Ranked();
            var rng = Rng("rival-party");
            foreach (var rival in Rivals)
            {
                bool threat = ranked.IndexOf(PlayerHouse) < ranked.IndexOf(rival) || Math.Abs(PlayerHouse.popularity - rival.popularity) <= Config.rivals.closeRace;
                float mult = rival.aggression * (threat ? 1.3f : 1f);
                int lo = Math.Max(1, (int)(p.ticksTotal * 0.3f)), hi = Math.Max(lo + 1, (int)(p.ticksTotal * 0.8f));
                if (rng.Chance(Config.rivals.partyCopsChance * mult)) { p.rivalCopsTicks.Add(rng.Range(lo, hi)); p.rivalCopsBy.Add(rival.id); }
                if (rng.Chance(Config.rivals.partyStinkChance * mult)) { p.rivalStinkTicks.Add(rng.Range(lo, hi)); p.rivalStinkBy.Add(rival.id); }
            }
        }

        void ProcessRivalPartySabotage(int tick)
        {
            var p = State.party;
            var h = PlayerHouse;
            for (int i = 0; i < p.rivalCopsTicks.Count; i++)
            {
                if (p.rivalCopsTicks[i] != tick) continue;
                var rival = House(p.rivalCopsBy[i]);
                if (h.guarded) { h.guarded = false; News("defense", "You caught a " + rival?.name + " pledge on the phone with the cops outside and ran him off.", h.id); continue; }
                p.calledCops += Config.police.calledCopsAttention;
                News("rival_sabotage", "Someone called in a noise complaint on " + h.name + ".", h.id);
            }
            for (int i = 0; i < p.rivalStinkTicks.Count; i++)
            {
                if (p.rivalStinkTicks[i] != tick) continue;
                var rival = House(p.rivalStinkBy[i]);
                if (h.guarded) { h.guarded = false; News("defense", "You stopped a " + rival?.name + " brother sneaking in with a stink bomb.", h.id); continue; }
                p.stinkBombTicks = 6;
                OpenIncident("stink_bomb", -1);
                var rng = Rng("stink", tick);
                foreach (var g in PresentGuests.ToList()) if (rng.Chance(Config.party.stinkBombLeaveShare)) Leave(g, "stink_bomb");
                News("rival_sabotage", "Someone stink-bombed " + h.name + "'s party!", h.id, isPublic: true);
            }
        }

        /// <summary>Design §4.6 abstract rival party score and raid roll.</summary>
        void ScoreRivalParties()
        {
            foreach (var rp in State.rivalParties)
            {
                var h = House(rp.houseId);
                if (h == null) continue;
                if (h.suspended) { rp.score = 0; continue; }
                var rng = Rng("rival-score-" + h.id);
                float raidChance = Clamp(Config.rivals.rivalRaidBase + h.heat / 250f + h.chaos * 0.08f + rp.attention / 150f - h.caution * 0.05f, 0, 0.8f);
                rp.raided = rng.Chance(raidChance);
                // Same shape as the players' score (guests × fun + notable + hype − incidents), simulated abstractly.
                float capacity = Capacity(h) * (h.probation ? Config.dean.probationCapacityFactor : 1f);
                float fill = Clamp(Config.party.rivalBaseFill + h.popularity / 200f + h.weekBudget / 3000f +
                    (string.IsNullOrEmpty(h.theme) ? 0 : 0.05f) + rng.Range(-0.05f, 0.05f), 0.1f, 1f);
                float guests = Math.Min(capacity, rp.namedGuests + capacity * fill);
                float fun = Clamp(45 + h.weekBudget / 30f + h.chaos * 15 + rng.Range(-8f, 8f) - rp.moodPenalty, 5, 100);
                float hype = 20 + h.prestige * 20 + h.chaos * 20;
                int incidents = rng.Poissonish(h.chaos * 4);
                int escalated = Math.Min(incidents, rng.Poissonish(h.chaos * (1 - h.caution) * 2));
                float score = guests * fun / 100f + rp.notablePopularity / Config.scoring.notableScoreDivisor + hype / 10f
                    - Config.party.incidentPenaltyEscalated * escalated - Config.party.incidentPenaltyHandled * (incidents - escalated) + h.sabotageScoreMod;
                rp.score = Math.Max(0, score * (rp.raided ? Config.police.raidScoreFactor : 1f));
                if (rp.raided)
                {
                    h.heat = Clamp(h.heat + Config.police.raidHeat / 2f, 0, 100);
                    h.dean = Clamp(h.dean - Config.police.raidDean / 2f, 0, 100);
                    News("raid", "Police raided " + h.name + "'s party.", h.id, isPublic: true);
                }
            }
        }
    }
}
