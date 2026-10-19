using System;
using System.Collections.Generic;
using System.Linq;

namespace FratRow.Game
{
    public sealed partial class GameSession
    {
        int DesiredPatrols()
        {
            float maxHeat = State.houses.Max(h => h.heat);
            int desired = 1 + (int)Math.Floor(maxHeat / Math.Max(1f, Config.police.patrolHeatStep));
            if (State.phase == Phase.Party) desired += Config.police.partyNightExtraPatrols;
            return Math.Max(1, Math.Min(Config.police.maxPatrols, desired));
        }

        void EnsurePatrols()
        {
            int desired = DesiredPatrols();
            while (State.patrols.Count < desired)
            {
                var rng = NextRng("patrol-spawn");
                string loc = Config.locations[rng.Range(0, Config.locations.Count)].id;
                int id = State.patrols.Count == 0 ? 0 : State.patrols.Max(x => x.id) + 1;
                State.patrols.Add(new PatrolState { id = id, from = loc, to = loc, progress = 1, dwell = rng.Range(0f, Config.police.patrolDwellSeconds),
                    travelSeconds = Config.police.patrolTravelSeconds });
            }
            for (int i = State.patrols.Count - 1; i >= 0 && State.patrols.Count > desired; i--)
                if (State.patrols[i].id != State.party.dispatchedPatrol) State.patrols.RemoveAt(i);
        }

        void UpdatePatrols(double dt)
        {
            EnsurePatrols();
            foreach (var patrol in State.patrols.ToList())
            {
                if (patrol.progress < 1)
                {
                    patrol.progress = (float)Math.Min(1.0, patrol.progress + dt / Math.Max(0.1f, patrol.travelSeconds));
                    if (patrol.progress >= 1) OnPatrolArrive(patrol);
                    continue;
                }
                if (patrol.mode == "parked")
                {
                    if (State.phase == Phase.Weekday && PlayerHouse.heat >= Config.police.copsParkedHeat) continue;
                    patrol.mode = "patrol";
                }
                patrol.dwell -= (float)dt;
                if (patrol.dwell <= 0) ChooseNextStop(patrol);
            }
        }

        void OnPatrolArrive(PatrolState patrol)
        {
            patrol.dwell = Config.police.patrolDwellSeconds;
            if (patrol.mode == "respond")
            {
                patrol.mode = "patrol";
                PoliceArriveAtParty(patrol);
            }
        }

        void ChooseNextStop(PatrolState patrol)
        {
            var home = Config.playerHomeLocation;
            bool parkWorthy = State.phase == Phase.Weekday && PlayerHouse.heat >= Config.police.copsParkedHeat;
            if (parkWorthy && patrol.to == home && patrol.id == State.patrols.Min(x => x.id))
            {
                patrol.mode = "parked";
                News("police", "A cop car is parked outside " + PlayerHouse.name + ".", PlayerHouse.id);
                return;
            }
            var rng = NextRng("patrol");
            var weights = Config.locations.Select(l =>
            {
                if (l.id == patrol.to) return 0f;
                float w = 1f;
                var house = string.IsNullOrEmpty(l.houseId) ? null : House(l.houseId);
                if (house != null) w += Config.police.patrolHeatBias * house.heat / 25f;
                if (parkWorthy && l.id == home) w += 10f;
                return w;
            }).ToArray();
            int pick = rng.Weighted(weights);
            if (pick < 0) { patrol.dwell = Config.police.patrolDwellSeconds; return; }
            patrol.from = patrol.to;
            patrol.to = Config.locations[pick].id;
            patrol.progress = 0;
            patrol.travelSeconds = Config.police.patrolTravelSeconds;
        }

        /// <summary>A unit is parked/dwelling at the location.</summary>
        public bool PatrolAt(string location) => State.patrols.Any(x => x.to == location && x.progress >= 1);

        /// <summary>A unit is arriving at or just leaving the location.</summary>
        public bool PatrolNear(string location) =>
            State.patrols.Any(x => (x.to == location && x.progress >= 0.6f && x.progress < 1) || (x.from == location && x.progress < 0.3f));

        /// <summary>0 = quiet, 1 = patrols nearby, 2 = unit dispatched to your party, 3 = raid in progress/parked outside.</summary>
        public int PoliceAlertLevel
        {
            get
            {
                var home = Config.playerHomeLocation;
                if (State.party.raided && State.phase == Phase.Party) return 3;
                if (State.patrols.Any(x => x.mode == "parked")) return 3;
                if (State.party.dispatchedPatrol >= 0) return 2;
                return PatrolAt(home) || PatrolNear(home) ? 1 : 0;
            }
        }

        void DispatchPatrolToParty()
        {
            var p = State.party;
            var home = Config.playerHomeLocation;
            EnsurePatrols();
            var unit = State.patrols.Where(x => x.mode != "respond").OrderByDescending(x => x.to == home ? 1 : 0).ThenBy(x => x.id).FirstOrDefault();
            if (unit == null) return;
            p.dispatchedPatrol = unit.id;
            p.talkAttempted = false;
            News("police", "Police are on their way to " + PlayerHouse.name + "! Turn it down or talk them out of it.", PlayerHouse.id);
            Emit(GameEventKind.PoliceDispatched, "dispatched", PlayerHouse.id, value: unit.id);
            if (unit.to == home && unit.progress >= 1)
            {
                PoliceArriveAtParty(unit);
                return;
            }
            unit.from = unit.progress >= 0.5f ? unit.to : unit.from;
            unit.to = home;
            unit.progress = 0;
            unit.mode = "respond";
            unit.travelSeconds = Config.police.respondTravelSeconds;
        }

        void PoliceArriveAtParty(PatrolState unit)
        {
            var p = State.party;
            if (!p.active || p.dispatchedPatrol != unit.id) return;
            p.dispatchedPatrol = -1;
            p.dispatchCooldownUntil = p.tick + Config.police.dispatchCooldownTicks;
            if (p.attention >= Config.police.raidThreshold && !p.policeTalked) Raid();
            else
            {
                if (p.attention >= Config.police.raidThreshold) p.policeTalked = false; // the smooth talk is used up
                Warning();
            }
        }

        void Warning()
        {
            var p = State.party;
            var h = PlayerHouse;
            p.warningIssued = true;
            p.warnings++;
            h.heat = Clamp(h.heat + Config.police.warningHeat, 0, 100);
            p.attention = Clamp(p.attention - 15, 0, 100);
            p.calledCops = 0;
            News("police", "The cops gave " + h.name + " a warning. Keep it down.", h.id);
            Emit(GameEventKind.PoliceWarning, "warning", h.id);
        }

        void Raid()
        {
            var p = State.party;
            var h = PlayerHouse;
            var t = Config.police;
            int open = OpenIncidents.Count(i => i.kind != "celebrity_dropin");
            int fines = t.underageFine * p.underageDrinkers + t.noiseFine + t.incidentFine * open;
            Fine(h, fines, "raid fines");
            h.heat = Clamp(h.heat + t.raidHeat, 0, 100);
            h.dean = Clamp(h.dean - t.raidDean, 0, 100);
            News("raid", "Police raided " + h.name + "'s party! " + p.underageDrinkers + " underage drinkers, $" + fines + " in fines.", h.id, isPublic: true);
            Emit(GameEventKind.Raid, "raid", h.id, value: fines);
            string cited = p.lastServedUnderageBy;
            if (string.IsNullOrEmpty(cited) && p.underageDrinkers > 0)
            {
                var hosts = ConnectedPlayers.Where(x => x.location == Config.playerHomeLocation).ToList();
                if (hosts.Count > 0) cited = hosts[NextRng("raid-cite").Range(0, hosts.Count)].playerId;
            }
            if (!string.IsNullOrEmpty(cited)) Bust(Player(cited), "serving alcohol to minors", "serving drinks to freshmen");
            EndPlayersParty(raided: true);
            BeginDebrief();
        }

        /// <summary>Busted: fine from the treasury, heat, Dean penalty, record, a detention timeout, and a "busted" story
        /// told by whoever saw it (or, with no witnesses, the police blotter reaching the campus hubs).</summary>
        void Bust(PlayerState player, string reason, string gossipDetail = null)
        {
            if (player == null) return;
            var h = PlayerHouse;
            var t = Config.police;
            Fine(h, t.bustFine, "bust fine");
            h.heat = Clamp(h.heat + t.bustHeat, 0, 100);
            h.dean = Clamp(h.dean - t.bustDean, 0, 100);
            player.record++;
            player.rep = Math.Max(-100, player.rep - 2);
            player.detainedUntil = State.time + t.detainSeconds;
            News("busted", player.displayName + " (" + h.name + ") was busted for " + reason + ".", h.id, player.playerId, isPublic: true);
            Emit(GameEventKind.PlayerBusted, reason, h.id, player.playerId, value: t.detainSeconds);
            var saw = NpcsAt(player.location);
            Seed("busted", (Config.StoryKind("busted")?.severity ?? 55) + 5 * Math.Min(5, player.record - 1), saw.Count > 0 ? saw : GossipHubs(),
                h.id, player.playerId, detail: gossipDetail ?? "for " + reason, location: string.IsNullOrEmpty(player.location) ? "campus" : player.location);
        }

        /// <summary>Design §4.4 detection probability for a crime at a location.</summary>
        public float DetectionChance(CrimeDef crime, string location, int witnesses)
        {
            var t = Config.police;
            float p = crime.baseDetect + (PatrolAt(location) ? t.patrolHere : PatrolNear(location) ? t.patrolNear : 0) + witnesses * t.witnessFactor;
            if (AnyRole("risk_manager")) p -= t.riskManagerDetectBonus;
            return Clamp(p, 0.02f, 0.95f);
        }
    }
}
