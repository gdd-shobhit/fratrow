using System;
using System.Collections.Generic;
using System.Linq;

namespace FratRow.Game
{
    public sealed partial class GameSession
    {
        /// <summary>Score of the players' party per design §4.6 (before probation/raid point effects).</summary>
        public float PlayersPartyScore()
        {
            var p = State.party;
            if (PlayerHouse.suspended || (!p.active && !p.ended)) return 0;
            float fun = p.guests.Count == 0 ? 0 : (p.ended ? p.fun : p.guests.Average(g => g.mood));
            int handled = Math.Max(0, p.incidentsTotal - p.incidentsEscalated);
            float notable = TopNotable(p.guests.Where(g => !string.IsNullOrEmpty(g.npcId)).Select(g => g.popularity));
            float score = p.guests.Count * (fun / 100f) + notable / Config.scoring.notableScoreDivisor + p.hype / 10f
                - Config.party.incidentPenaltyEscalated * p.incidentsEscalated - Config.party.incidentPenaltyHandled * handled;
            if (p.raided) score *= Config.police.raidScoreFactor;
            return Math.Max(0, score);
        }

        /// <summary>Debrief settlement in the order of design §3.8.</summary>
        void Settle()
        {
            var p = State.party;
            var player = PlayerHouse;
            var standingBefore = Ranked().Select(h => h.id).ToList();
            SnapshotWeekendDestinations();
            State.storiesThisWeek = 0; // the gossip week runs Sunday to Saturday
            State.gossipWeekStart = DayIndex;
            ScoreRivalParties();
            p.score = PlayersPartyScore();

            // 1. Rank and cup points.
            var scores = State.houses.Select(h => new { h, score = h.isPlayer ? p.score : State.rivalParties.Find(r => r.houseId == h.id)?.score ?? 0 })
                .OrderByDescending(x => x.score).ThenByDescending(x => x.h.popularity).ThenBy(x => x.h.id, StringComparer.Ordinal).ToList();
            var week = new WeekResult { semester = State.semester, week = State.week };
            for (int rank = 0; rank < scores.Count; rank++)
            {
                var h = scores[rank].h;
                var rp = State.rivalParties.Find(r => r.houseId == h.id);
                bool raided = h.isPlayer ? p.raided : rp?.raided == true;
                int points = h.suspended ? 0 : (int)Math.Round(Config.scoring.cupPoints[rank] * (h.probation ? Config.dean.probationPointsFactor : 1f));
                h.cupPoints += points;
                h.lastRank = rank + 1;

                // 2. Popularity.
                float delta = h.suspended ? -3 : Config.scoring.rankPop[rank];
                if (raided) delta += p.fun >= Config.police.infamyFun && h.isPlayer ? Config.police.infamyBonus : -Config.police.raidPopPenalty;
                if (h.isPlayer && !h.suspended)
                    delta += (p.fun - 50f) / 10f + p.notablePopularity / Config.scoring.notableDivisor;
                else if (rp != null) delta += rp.notablePopularity / (Config.scoring.notableDivisor * 2);
                delta += Clamp(h.weekSabotagePop, -Config.scoring.sabotagePopCap, Config.scoring.sabotagePopCap);
                // Word of mouth: what campus has been saying about the house all week (design/gdd/campus-gossip.md §4.4).
                float wordOfMouth = G.enabled ? Clamp(WordOfMouth(h.id) * G.wordOfMouthScale, -G.wordOfMouthCap, G.wordOfMouthCap) : 0;
                delta += wordOfMouth;
                if (delta > 0) delta *= Math.Max(0.1f, 1f - h.popularity / Config.scoring.popularitySoftCap);
                h.popularity = Clamp(h.popularity + delta, 0, 100);
                var def = Config.House(h.id);
                if (!h.isPlayer && def != null) h.popularity += (def.popularity - h.popularity) * Config.rivals.weeklyRegress;
                week.entries.Add(new WeekEntry
                {
                    houseId = h.id, score = scores[rank].score, rank = rank + 1, cupPoints = points, popDelta = delta, raided = raided,
                    guests = h.isPlayer ? p.guests.Count : rp?.namedGuests ?? 0, fun = h.isPlayer ? p.fun : 0, suspended = h.suspended,
                    wordOfMouth = wordOfMouth
                });
            }
            week.guests = p.guests.Count;
            week.peakGuests = p.peakGuests;
            week.namedGuests = p.guests.Where(g => !string.IsNullOrEmpty(g.npcId)).Select(g => g.npcId).Distinct().ToList();
            week.fun = p.fun;
            week.hype = p.hype;
            week.incidents = p.incidentsTotal;
            week.escalated = p.incidentsEscalated;
            week.underageDrinkers = p.underageDrinkers;
            week.cashCollected = p.cashCollected;
            week.warnings = p.warnings;
            week.peakAttention = p.peakAttention;
            week.notablePopularity = p.notablePopularity;
            week.raided = p.raided;
            State.history.Add(week);
            int playerRank = week.entries.Find(e => e.houseId == player.id).rank;

            // 3. Cash.
            player.cash += Math.Max(0, p.cashCollected);
            if (p.cashCollected < 0) Fine(player, -p.cashCollected, "party damages");
            foreach (var h in State.houses)
            {
                if (h.isPlayer) h.cash += Config.economy.dues * TotalMembers(h);
                if (week.entries.Find(e => e.houseId == h.id).rank == 1) h.cash += Config.economy.sponsorship;
            }

            // 4. Heat.
            foreach (var h in State.houses) h.heat = Clamp(h.heat - Config.police.weeklyHeatCooldown, 0, 100);
            foreach (var crew in Config.shop.Where(i => i.category == "service" && i.heatReduction > 0))
            {
                if (StashCount(player, crew.id) <= 0) continue;
                AddStash(player, crew.id, -1);
                player.heat = Clamp(player.heat - crew.heatReduction, 0, 100);
                player.trashed = false;
            }

            // 5. GPA and Dean sanctions for next week.
            foreach (var h in State.houses)
            {
                h.gpa = HouseGpa(h);
                h.dean -= Config.dean.weeklyDrift;
                if (h.gpa >= Config.dean.gpaHigh) h.dean += Config.dean.gpaDean;
                else if (h.gpa < Config.dean.gpaLow) h.dean -= Config.dean.gpaDean;
                h.dean = Clamp(h.dean, 0, 100);
                bool wasSanctioned = h.probation || h.suspended, wasSuspended = h.suspended;
                h.suspended = h.dean < Config.dean.suspensionDean;
                h.probation = !h.suspended && h.dean < Config.dean.probationDean;
                if (h.suspended) News("dean", h.name + " has been SUSPENDED by the Dean for next weekend.", h.id, isPublic: true);
                else if (h.probation) News("dean", h.name + " is on probation next week.", h.id, isPublic: true);
                else if (wasSanctioned && h.isPlayer) News("dean", h.name + " is back in good standing with the Dean.", h.id);
                // A new sanction leaks from the house's own members and the campus hubs.
                if ((h.suspended && !wasSuspended) || (h.probation && !wasSanctioned))
                {
                    string kind = h.suspended ? "dean_suspended" : "dean_probation";
                    Seed(kind, Config.StoryKind(kind)?.severity ?? 55, GossipHubs().Concat(h.npcMembers), h.id, location: Config.quadLocation);
                }
            }

            // 6. Rush.
            ApplyPartyRushEffects();
            BidDay();

            // 7. Grades + standings.
            foreach (var h in State.houses) h.guarded = false;
            PushStandings();

            // 8. Social: end this party, publish weekly campus news (one memory per NPC).
            // A party suspended at party time was already cancelled and cleared (BeginParty). One still open here happened, even if
            // this settlement just suspended the house for NEXT week.
            bool partyHappened = !string.IsNullOrEmpty(State.socialPartyId);
            social.EndParty(State.socialPartyId, cancelled: !partyHappened);
            var missed = partyHappened ? social.Commitments(State.socialPartyId).Where(c => c.state == "missed").ToList() : new List<CommitmentView>();
            State.socialPartyId = "";
            var winner = House(week.entries[0].houseId);
            var lines = new List<string> { winner.name + " threw the best party of week " + (State.week + 1) + "." };
            foreach (var e in week.entries.Where(e => e.raided)) lines.Add(House(e.houseId).name + "'s party was raided by police.");
            foreach (var e in week.entries.Where(e => e.suspended)) lines.Add(House(e.houseId).name + " was suspended.");
            lines.Add(player.name + " finished #" + playerRank + " this week.");
            social.PublishNews("Campus news: " + string.Join(" ", lines), AllNpcIds());
            News("party_result", "Week " + (State.week + 1) + " results: " + string.Join(", ", week.entries.Select(e => "#" + e.rank + " " + House(e.houseId).name + " (" + e.score.ToString("0") + ")")) + ".",
                player.id, isPublic: true);
            // 9. Gossip: Saturday's stories are seeded and Sunday brunch spreads them (design/gdd/campus-gossip.md §3.5).
            GossipWeekend(standingBefore, week, missed);
            weekendDestinations = null;
            Emit(GameEventKind.WeekSettled, "week " + (State.week + 1), player.id, value: playerRank);
        }

        float HouseGpa(HouseState h)
        {
            var d = Config.dean;
            var grades = new List<float>();
            if (h.isPlayer)
                foreach (var p in State.players) grades.Add(Math.Min(4f, d.baseStudentGpa + d.studyGpaPerSession * Math.Min(p.studyThisWeek, d.studySessionCap)));
            foreach (var id in h.npcMembers) grades.Add(RosterNpc(id)?.gpa ?? 2.8f);
            int abstractMembers = Math.Max(0, h.memberCount - h.npcMembers.Count);
            float baseline = h.isPlayer ? 2.8f : Config.House(h.id)?.gpa ?? 2.8f;
            float total = grades.Sum() + abstractMembers * baseline;
            int count = grades.Count + abstractMembers;
            return count == 0 ? baseline : total / count;
        }

        // ---------- Rush / Bid Day (design §3.10, §4.7) ----------

        void AddRushBonus(NpcGameState npc, string houseId, float delta)
        {
            var b = npc.rushBonus.Find(x => x.houseId == houseId);
            if (b == null) npc.rushBonus.Add(new HouseValue { houseId = houseId, value = delta });
            else b.value += delta;
        }

        float RushBonus(NpcGameState npc, string houseId) => npc.rushBonus.Find(x => x.houseId == houseId)?.value ?? 0;

        /// <summary>A PNM's interest (0..100) in pledging a house.</summary>
        public float RushInterest(string npcId, string houseId)
        {
            var npc = Npc(npcId);
            var def = RosterNpc(npcId);
            var h = House(houseId);
            if (npc == null || def == null || h == null) return 0;
            int rank = RankOf(h);
            float hearsay = G.enabled ? G.rushWeight * Opinion(npcId, h.id) : 0;
            if (h.isPlayer)
                return Clamp(Config.rush.playerInterestBase + social.RegardForPlayers(npcId) / 2f + RushBonus(npc, h.id)
                    + (State.houses.Count - rank) * Config.rush.rankBonus + hearsay, 0, 100);
            return Clamp(h.popularity * ConcernWeight(def) * Config.rush.rivalPopularityFactor + RivalRegard(npc, h.id) + RushBonus(npc, h.id)
                + (State.houses.Count - rank) * Config.rush.rivalRankBonus + 5 + hearsay, 0, 100);
        }

        void ApplyPartyRushEffects()
        {
            var p = State.party;
            foreach (var npc in State.npcs.Where(n => n.isPnm && n.attendedPlayersParty))
            {
                if (p.fun >= 60) AddRushBonus(npc, Config.playerHouseId, Config.rush.partyFunBonus);
                else if (p.fun < 40) AddRushBonus(npc, Config.playerHouseId, -Config.rush.partyBadPenalty);
            }
            foreach (var rival in Rivals)
                foreach (var npc in State.npcs.Where(n => n.isPnm && string.IsNullOrEmpty(n.memberOf)))
                    AddRushBonus(npc, rival.id, Config.rush.rivalWeeklyEffort * rival.prestige * 0.5f);
        }

        void BidDay()
        {
            var rng = Rng("bidday");
            if (State.week < Config.rush.bidsOpenWeek)
            {
                // Rush week: interest builds, nobody pledges yet. Player bids stay queued for the first Bid Day.
                News("bid", "Rush continues. Bid Day opens after week " + Config.rush.bidsOpenWeek + ".", PlayerHouse.id);
                return;
            }
            var pnms = State.npcs.Where(n => n.isPnm && string.IsNullOrEmpty(n.memberOf)).ToList();
            foreach (var rival in Rivals)
            {
                if (rival.suspended) continue;
                // Each rival bids only on its best few prospects per week.
                foreach (var npc in pnms.Where(n => RushInterest(n.npcId, rival.id) >= Config.rush.rivalBidInterest)
                    .OrderByDescending(n => RushInterest(n.npcId, rival.id)).ThenBy(n => n.npcId, StringComparer.Ordinal).Take(Config.rush.rivalBidsPerWeek))
                    if (!npc.bids.Contains(rival.id)) npc.bids.Add(rival.id);
            }
            foreach (var npc in pnms)
            {
                if (npc.bids.Count == 0) continue;
                var best = npc.bids.Select(id => new { id, interest = RushInterest(npc.npcId, id) + rng.Range(0f, 3f) })
                    .OrderByDescending(x => x.interest).ThenBy(x => x.id, StringComparer.Ordinal).First();
                string name = RosterNpc(npc.npcId)?.displayName ?? npc.npcId;
                bool offeredByPlayers = npc.bids.Contains(Config.playerHouseId);
                if (best.interest >= Config.rush.acceptBidInterest) JoinHouse(npc, House(best.id));
                else if (offeredByPlayers) News("bid", name + " turned down your bid (for now).", PlayerHouse.id, npcId: npc.npcId);
                npc.bids.Clear();
            }
            // Walk-on pledges.
            var ranked = Ranked();
            foreach (var h in State.houses)
            {
                if (h.suspended) continue;
                float share = (State.houses.Count + 1 - ranked.IndexOf(h) - 1) / 10f;
                int walkOns = rng.Poissonish(Config.rush.walkOnPnms * share);
                if (walkOns <= 0) continue;
                h.memberCount += walkOns;
                if (h.isPlayer) News("member", walkOns + " walk-on pledge" + (walkOns == 1 ? "" : "s") + " joined " + h.name + ".", h.id);
            }
            foreach (var npc in State.npcs) foreach (var b in npc.rushBonus) b.value *= 0.8f;
        }

        void JoinHouse(NpcGameState npc, HouseState h)
        {
            if (h == null) return;
            // Turning down the players' bid for a rival is rush drama; a notable pledge is news either way.
            bool snub = !h.isPlayer && npc.bids.Contains(Config.playerHouseId);
            npc.memberOf = h.id;
            npc.destination = "";
            h.memberCount++;
            h.npcMembers.Add(npc.npcId);
            gossipVersion++; // members ignore gossip about their own house
            social.SetAffiliation(npc.npcId, AffiliationFor(h.id));
            string name = RosterNpc(npc.npcId)?.displayName ?? npc.npcId;
            float pledgePop = RosterNpc(npc.npcId)?.popularity ?? 0;
            if (snub || pledgePop >= G.notablePledge)
                Seed(snub ? "pledge_snub" : "pledged", (snub ? 40 : 25) + pledgePop * 1.2f, new[] { npc.npcId }.Concat(h.npcMembers).Concat(GossipHubs()),
                    h.id, npcId: npc.npcId, otherHouseId: snub ? Config.playerHouseId : "", location: h.homeLocation);
            News("member", name + " accepted a bid from " + h.name + ".", h.id, npcId: npc.npcId, isPublic: true);
            Emit(GameEventKind.MemberJoined, name, h.id, npcId: npc.npcId, flag: h.isPlayer);
        }
    }
}
