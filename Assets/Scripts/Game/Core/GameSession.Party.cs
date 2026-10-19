using System;
using System.Collections.Generic;
using System.Linq;

namespace FratRow.Game
{
    public sealed partial class GameSession
    {
        static readonly string[] ArchetypeNames = { "None", "Nerd", "MeanGirl", "Jock" };
        public static int ArchetypeIndex(string guestClass)
        {
            int i = Array.IndexOf(ArchetypeNames, guestClass);
            return i < 0 ? 0 : i;
        }

        public IEnumerable<GuestState> PresentGuests => State.party.guests.Where(g => !g.left);
        public IEnumerable<IncidentState> OpenIncidents => State.party.incidents.Where(i => !i.resolved);
        bool HasGear(HouseState h, string itemId) => StashCount(h, itemId) > 0 && Config.Item(itemId)?.persistent == true;
        float GearHype(HouseState h) => Config.shop.Where(i => i.persistent && StashCount(h, i.id) > 0).Sum(i => i.hype);
        float GearNoise(HouseState h) => Config.shop.Where(i => i.persistent && StashCount(h, i.id) > 0).Sum(i => i.noise);
        bool AnyRole(string roleId) => ConnectedPlayers.Any(p => p.role == roleId);
        float RoleMax(Func<RoleDef, float> pick) => ConnectedPlayers.Select(p => pick(RoleOf(p))).DefaultIfEmpty(1f).Max();

        void BeginParty()
        {
            SetPhase(Phase.Party, 5);
            var h = PlayerHouse;
            // Containers brothers opened earlier in the week (pre-game drinking) stay open for the party.
            var opened = State.party.openServings;
            var p = State.party = new PartyState { socialPartyId = State.socialPartyId, openServings = opened ?? new List<StashEntry>() };
            p.ticksTotal = Math.Max(1, (int)Math.Ceiling(Config.partySeconds / Config.partyTickSeconds));
            StartRivalParties();
            if (h.suspended || string.IsNullOrEmpty(p.socialPartyId))
            {
                p.ended = true;
                if (!string.IsNullOrEmpty(State.socialPartyId)) social.EndParty(State.socialPartyId, cancelled: true);
                State.socialPartyId = "";
                News("suspended", h.name + " is suspended by the Dean: no party tonight.", h.id, isPublic: true);
                AssignDestinations(playersOpen: false);
                Emit(GameEventKind.PartyStarted, "suspended", h.id, flag: false);
                return;
            }
            p.active = true;
            p.capacity = PlannedCapacity();
            float vibe = 0;
            foreach (var decor in Config.shop.Where(i => i.category == "decor"))
            {
                int count = StashCount(h, decor.id);
                if (count <= 0) continue;
                vibe += decor.vibe * count;
                AddStash(h, decor.id, -count);
            }
            var theme = Config.Theme(h.theme);
            if (theme != null) vibe = vibe * 1.5f + theme.vibe;
            if (h.trashed) vibe -= Config.party.trashedVibe;
            p.vibe = vibe;
            foreach (var sec in Config.shop.Where(i => i.category == "security" && !i.persistent))
            {
                if (StashCount(h, sec.id) <= 0) continue;
                p.cardCheckCoverage = Math.Max(p.cardCheckCoverage, sec.cardCheck);
                p.securityTroubleMult = Math.Min(p.securityTroubleMult, sec.troubleMult);
                AddStash(h, sec.id, -1);
            }
            p.hype = GearHype(h);
            social.StartParty(p.socialPartyId, State.time);
            ScheduleCommittedArrivals();
            AssignDestinations(playersOpen: true);
            ScheduleRivalPartySabotage();
            News("party", "Party night at " + h.name + "! Capacity " + p.capacity + (theme != null ? ", theme: " + theme.name : "") +
                (h.trashed ? ". The house is still trashed from the prank." : "."), h.id);
            Emit(GameEventKind.PartyStarted, theme?.name ?? "", h.id, flag: true, value: p.capacity);
        }

        /// <summary>Accepted RSVPs (including conditional promises fulfilled at the entrance) get an arrival tick.</summary>
        void ScheduleCommittedArrivals()
        {
            var p = State.party;
            if (!p.active) return;
            var rng = Rng("arrive", p.tick);
            int window = Math.Max(1, (int)(p.ticksTotal * Config.party.committedArrivalWindow));
            foreach (var c in social.Commitments(p.socialPartyId))
            {
                if (c.state != "accepted" || c.arrived) continue;
                var npc = Npc(c.npcId);
                if (npc == null || !string.IsNullOrEmpty(npc.memberOf) || RosterNpc(c.npcId) == null) continue;
                if (p.pendingArrivals.Contains(c.npcId) || p.guests.Any(g => g.npcId == c.npcId)) continue;
                int at = p.tick == 0 ? 1 + rng.Range(0, window) : p.tick + 1 + rng.Range(0, 3);
                p.pendingArrivals.Add(c.npcId);
                p.pendingArrivalTicks.Add(at);
                npc.destination = Config.playerHouseId;
            }
        }

        float RivalRegard(NpcGameState npc, string houseId) => npc.rivalRegard.Find(r => r.houseId == houseId)?.value ?? 0;

        float ConcernWeight(RosterNpc def)
        {
            var d = Config.destination;
            switch (CampusRoster.Concern(def))
            {
                case FratRow.Social.StatusConcern.High: return d.highConcernWeight;
                case FratRow.Social.StatusConcern.Low: return d.lowConcernWeight;
                default: return d.moderateConcernWeight;
            }
        }

        /// <summary>Design §4.8: every uncommitted, unaffiliated named NPC picks a party (or home) once per party night.</summary>
        void AssignDestinations(bool playersOpen)
        {
            var p = State.party;
            var commitments = playersOpen ? social.Commitments(p.socialPartyId) : new List<CommitmentView>();
            var rng = Rng("destinations");
            foreach (var npc in State.npcs)
            {
                var def = RosterNpc(npc.npcId);
                if (def == null || !string.IsNullOrEmpty(npc.memberOf) || npc.destination != "") continue;
                var c = commitments.FirstOrDefault(x => x.npcId == npc.npcId);
                if (c.state == "accepted" || c.state == "conditional") { npc.destination = Config.playerHouseId; continue; }
                string best = "home";
                float bestAppeal = Config.destination.stayHomeAppeal;
                foreach (var h in State.houses)
                {
                    if (h.isPlayer && (!playersOpen || c.state == "declined" || c.state == "missed" || c.state == "cancelled")) continue;
                    if (!h.isPlayer && h.suspended) continue;
                    float regard = h.isPlayer ? social.RegardForPlayers(npc.npcId) : RivalRegard(npc, h.id);
                    var theme = Config.Theme(h.theme);
                    float appeal = ConcernWeight(def) * h.popularity + Config.destination.regardWeight * regard
                        + (theme != null && theme.likedBy.Contains(def.guestClass) ? Config.destination.themeMatch : 0)
                        + rng.Range(0f, Config.destination.appealNoise);
                    if (h.isPlayer && c.state == "deferred" && regard >= 0) appeal += Config.destination.deferredBonus;
                    if (h.isPlayer) appeal -= h.cover * 0.3f;
                    if (G.enabled) appeal += G.destinationWeight * Opinion(npc.npcId, h.id);
                    if (appeal > bestAppeal) { bestAppeal = appeal; best = h.id; }
                }
                npc.destination = best;
                if (best == Config.playerHouseId && p.active)
                {
                    p.pendingArrivals.Add(npc.npcId);
                    p.pendingArrivalTicks.Add(rng.Range(Math.Max(1, (int)(p.ticksTotal * 0.1f)), Math.Max(2, (int)(p.ticksTotal * 0.7f))));
                }
            }
            foreach (var rp in State.rivalParties)
            {
                var going = State.npcs.Where(n => n.destination == rp.houseId).ToList();
                rp.namedGuests = going.Count;
                rp.notablePopularity = TopNotable(going.Select(n => RosterNpc(n.npcId)?.popularity ?? 0));
            }
        }

        /// <summary>Sum of the N most popular notable guests (design §4.6, capped so crowds of VIPs don't run away).</summary>
        float TopNotable(IEnumerable<float> popularities) =>
            popularities.Where(pop => pop >= Config.scoring.notablePopularity).OrderByDescending(pop => pop).Take(Config.scoring.notableCount).Sum();

        void PartyTick()
        {
            var p = State.party;
            var h = PlayerHouse;
            if (!p.active) return;
            p.tick++;
            var rng = Rng("party", p.tick);
            var entrance = new HashSet<string>(ConnectedPlayers.Where(x => x.location == Config.playerHomeLocation && x.atEntrance).Select(x => x.playerId));
            social.ObserveEntrance(p.socialPartyId, entrance, State.time);
            ScheduleCommittedArrivals();
            ProcessRivalPartySabotage(p.tick);

            // 1. Arrivals (doors close for the last fifth of the night).
            bool doorsOpen = p.tick <= p.ticksTotal * 0.8f;
            for (int i = p.pendingArrivals.Count - 1; i >= 0; i--)
            {
                if (p.pendingArrivalTicks[i] > p.tick) continue;
                string npcId = p.pendingArrivals[i];
                p.pendingArrivals.RemoveAt(i);
                p.pendingArrivalTicks.RemoveAt(i);
                if (doorsOpen && PresentGuests.Count() < p.capacity) AddNamedGuest(npcId);
            }
            if (doorsOpen) SpawnFillers(rng);

            // 2. Drinking.
            var present = PresentGuests.ToList();
            bool serving = p.tick - p.serveTick <= 2;
            float coverage = Math.Max(p.cardCheckCoverage, p.cardCheck ? Math.Min(1f, 0.6f * RoleMax(r => r.cardCheckMult)) : 0f);
            bool dry = false;
            foreach (var g in present)
            {
                if (!rng.Chance(Config.party.drinkChance * (serving ? 1.5f : 1f))) continue;
                bool carded = g.underage && rng.Chance(coverage);
                var item = TakeServing(h, allowAlcohol: !carded);
                if (item == null) { dry = true; continue; }
                g.drunk = Math.Min(1.5f, g.drunk + item.strength);
                g.mood = Clamp(g.mood + item.mood, 0, 100);
                if (g.underage && item.alcoholic && !g.drankUnderage)
                {
                    g.drankUnderage = true;
                    p.underageDrinkers++;
                    if (serving && !string.IsNullOrEmpty(p.servingPlayer)) p.lastServedUnderageBy = p.servingPlayer;
                }
            }
            if (dry && !p.kegDryReported && present.Count > 0) { p.kegDryReported = true; OpenIncident("keg_dry", -1); }
            if (p.kegDryReported && HasAnyDrink(h)) { p.kegDryReported = false; ResolveIncidents("keg_dry"); }

            // 3. Hype, noise, mood.
            float baseHype = GearHype(h);
            p.hype = Clamp(Math.Max(baseHype, p.hype - Config.party.hypeDecay - p.hype * Config.party.hypeProportionalDecay), 0, 100);
            bool musicDown = p.tick - p.musicDownTick <= 3;
            p.noise = Clamp(p.hype * 0.5f + present.Count * Config.party.noisePerGuest + GearNoise(h) - (musicDown ? 25 : 0), 0, 100);
            if (p.stinkBombTicks > 0) p.stinkBombTicks--;
            int openBad = OpenIncidents.Count(i => i.kind != "celebrity_dropin");
            float crowd = present.Count / (float)Math.Max(1, p.capacity);
            float crowdTerm = crowd < Config.party.crowdSweetLow ? crowd / Config.party.crowdSweetLow * 10f - 10f :
                crowd > Config.party.crowdSweetHigh ? -(crowd - Config.party.crowdSweetHigh) * 60f : 8f;
            foreach (var g in present)
            {
                float target = 25 + p.hype * 0.25f + p.vibe + crowdTerm + Math.Min(g.drunk, 0.8f) * 20 - openBad * 4 - (p.stinkBombTicks > 0 ? Config.party.stinkBombMood : 0);
                g.mood = Clamp(g.mood + (target - g.mood) * 0.2f, 0, 100);
                if (g.mood < Config.party.leaveMood && rng.Chance(Config.party.leaveChance)) Leave(g, "bored");
            }

            // 4. Trouble.
            present = PresentGuests.ToList();
            float troubleSum = present.Sum(g => Math.Max(0, g.trouble) / 100f * (0.3f + g.drunk));
            p.trouble = Clamp(troubleSum * p.securityTroubleMult / Math.Max(10, p.capacity) * 120f, 0, 100);

            // 5. Incidents and escalation.
            RollIncidents(rng, present);
            EscalateIncidents();

            // 6. Police attention.
            int underagePresent = present.Count(g => g.drankUnderage);
            openBad = OpenIncidents.Count(i => i.kind != "celebrity_dropin");
            float targetAttention = 0.35f * h.heat + 0.35f * p.noise + 8 * openBad + Config.party.underageAttention * underagePresent + p.calledCops;
            p.attention += (targetAttention - p.attention) * 0.25f;
            if (musicDown) p.attention -= Config.party.musicDownAttention;
            p.attention = Clamp(p.attention, 0, 100);
            p.peakAttention = Math.Max(p.peakAttention, p.attention);
            p.calledCops *= 0.9f;
            if (p.attention >= Config.police.warningThreshold && p.dispatchedPatrol < 0 && p.tick >= p.dispatchCooldownUntil)
                DispatchPatrolToParty();

            // 7. Fun.
            p.fun = present.Count == 0 ? 0 : present.Average(g => g.mood);
            p.peakGuests = Math.Max(p.peakGuests, present.Count);
        }

        void SpawnFillers(GameRng rng)
        {
            var p = State.party;
            var h = PlayerHouse;
            int present = PresentGuests.Count();
            if (present >= p.capacity) return;
            int namedPresent = PresentGuests.Count(g => !string.IsNullOrEmpty(g.npcId));
            float appeal = 0.5f * h.popularity + p.hype / 4f + p.vibe + 5 * namedPresent - (h.trashed ? 10 : 0) - 1.2f * h.cover;
            float rivalPull = Rivals.Where(r => !r.suspended).Select(r => r.popularity).DefaultIfEmpty(0).Average() / 100f * 0.3f;
            double expected = Config.party.baseFillerRate * Math.Max(0.1, 0.4 + appeal / 60.0) * (1 - present / (double)p.capacity)
                * RoleMax(r => r.fillerMult) * (1 - rivalPull);
            int count = rng.Poissonish(expected);
            var theme = Config.Theme(h.theme);
            var weights = Config.archetypes.Select(a => Math.Max(1f, a.baseArrivalChance + a.popularityModifier * h.popularity) *
                (theme != null && theme.likedBy.Contains(a.name) ? 1.5f : 1f)).ToArray();
            for (int i = 0; i < count && PresentGuests.Count() < p.capacity; i++)
            {
                int pick = rng.Weighted(weights);
                var a = Config.archetypes[Math.Max(0, pick)];
                var g = new GuestState
                {
                    guestId = p.nextGuestId++, archetype = a.guestClass, underage = rng.Chance(Config.party.underageShare),
                    mood = Config.party.startMood, trouble = Math.Max(0, a.trouble), popularity = a.popularity, cash = a.cash, arrivedTick = p.tick
                };
                p.guests.Add(g);
                CollectCover(g, rng);
                Emit(GameEventKind.GuestArrived, a.name, h.id, guestId: g.guestId, archetype: a.guestClass);
            }
        }

        void AddNamedGuest(string npcId)
        {
            var p = State.party;
            var def = RosterNpc(npcId);
            var npc = Npc(npcId);
            if (def == null || npc == null || p.guests.Any(g => g.npcId == npcId && !g.left)) return;
            var g = new GuestState
            {
                guestId = p.nextGuestId++, npcId = npcId, archetype = ArchetypeIndex(def.guestClass), underage = def.underage,
                mood = Config.party.startMood + 10, trouble = Math.Max(0, def.partyTrouble), popularity = def.popularity, cash = def.partyCash, arrivedTick = p.tick
            };
            p.guests.Add(g);
            npc.attendedPlayersParty = true;
            npc.destination = Config.playerHouseId;
            if (def.popularity >= Config.scoring.notablePopularity)
            {
                p.notablePopularity += def.popularity;
                News("arrival", def.displayName + " showed up at " + PlayerHouse.name + "'s party.", PlayerHouse.id, npcId: npcId, isPublic: true);
            }
            if (social.Commitments(p.socialPartyId).Any(c => c.npcId == npcId && c.state == "accepted" && !c.arrived))
            {
                try { social.MarkArrived(npcId, p.socialPartyId); }
                catch (InvalidOperationException) { /* commitment changed mid-tick; attendance still happened */ }
            }
            CollectCover(g, Rng("cover", g.guestId));
            Emit(GameEventKind.GuestArrived, def.displayName, PlayerHouse.id, npcId: npcId, guestId: g.guestId, archetype: g.archetype);
        }

        void CollectCover(GuestState g, GameRng rng)
        {
            var h = PlayerHouse;
            float willingness = Config.party.coverWillingness * (g.cash >= 0 ? 1f : 0.5f);
            int paid = h.cover > 0 && rng.Chance(willingness) ? h.cover : 0;
            int donation = (int)Math.Round(g.cash / 10f);
            State.party.cashCollected += paid + donation;
        }

        void Leave(GuestState g, string why)
        {
            if (g.left) return;
            g.left = true;
            Emit(GameEventKind.GuestLeft, why, PlayerHouse.id, npcId: g.npcId, guestId: g.guestId, archetype: g.archetype);
        }

        bool HasAnyDrink(HouseState h) =>
            State.party.openServings.Any(s => s.count > 0) || Config.shop.Any(i => i.category == "drink" && StashCount(h, i.id) > 0);

        /// <summary>
        /// Takes one serving, preferring alcohol when allowed; opens a new container from the stash when needed.
        /// <paramref name="only"/> restricts it to one drink item (a brother asking for a specific drink).
        /// </summary>
        ShopItemDef TakeServing(HouseState h, bool allowAlcohol, ShopItemDef only = null)
        {
            var drinks = Config.shop.Where(i => i.category == "drink" && i.servings > 0 && (only == null || i.id == only.id)).ToList();
            var order = drinks.Where(i => i.alcoholic && allowAlcohol).Concat(drinks.Where(i => !i.alcoholic)).ToList();
            foreach (var item in order)
            {
                var open = State.party.openServings.Find(s => s.itemId == item.id && s.count > 0);
                if (open == null && StashCount(h, item.id) > 0)
                {
                    AddStash(h, item.id, -1);
                    open = State.party.openServings.Find(s => s.itemId == item.id);
                    if (open == null) { open = new StashEntry { itemId = item.id }; State.party.openServings.Add(open); }
                    open.count += item.servings;
                }
                if (open != null && open.count > 0) { open.count--; return item; }
            }
            return null;
        }

        void RollIncidents(GameRng rng, List<GuestState> present)
        {
            var p = State.party;
            var t = Config.party;
            if (present.Count < 3) return;
            float avgDrunk = present.Average(g => g.drunk);
            if (OpenIncidents.Count(i => i.kind == "fight") < 2 && rng.Chance(t.fightChance * p.trouble / 50f))
                OpenIncident("fight", present.OrderByDescending(g => g.trouble * (0.3f + g.drunk)).First().guestId);
            if (rng.Chance(t.passOutChance * avgDrunk * 2f))
            {
                var drunkest = present.OrderByDescending(g => g.drunk).First();
                if (drunkest.drunk > 0.5f && !OpenIncidents.Any(i => i.guestId == drunkest.guestId)) OpenIncident("passed_out", drunkest.guestId);
            }
            if (rng.Chance(t.damageChance * p.trouble / 50f)) OpenIncident("property_damage", -1);
            if (!OpenIncidents.Any(i => i.kind == "noise_complaint") && p.noise >= t.noiseComplaintMinNoise && rng.Chance(t.noiseComplaintChance * p.noise / 60f * (1 + PlayerHouse.heat / 100f)))
            {
                OpenIncident("noise_complaint", -1);
                p.attention = Clamp(p.attention + 10, 0, 100);
                PlayerHouse.heat = Clamp(PlayerHouse.heat + 2, 0, 100);
            }
            if (rng.Chance(t.celebrityChance * PlayerHouse.popularity / 50f))
            {
                var star = State.npcs.Where(n => n.destination == "home" && string.IsNullOrEmpty(n.memberOf))
                    .Select(n => RosterNpc(n.npcId)).Where(d => d != null && d.popularity >= Config.scoring.notablePopularity)
                    .OrderByDescending(d => d.popularity).FirstOrDefault();
                if (star != null && present.Count < p.capacity)
                {
                    Npc(star.id).destination = Config.playerHouseId;
                    AddNamedGuest(star.id);
                    var inc = OpenIncident("celebrity_dropin", -1);
                    inc.resolved = true;
                }
            }
        }

        IncidentState OpenIncident(string kind, int guestId)
        {
            var p = State.party;
            var inc = new IncidentState { id = p.nextIncidentId++, kind = kind, openedTick = p.tick, guestId = guestId };
            p.incidents.Add(inc);
            if (kind != "celebrity_dropin" && kind != "keg_dry") p.incidentsTotal++;
            string text = kind == "fight" ? "A fight broke out at the party!" : kind == "passed_out" ? "A guest passed out." :
                kind == "property_damage" ? "Guests are breaking stuff." : kind == "noise_complaint" ? "The neighbours called in a noise complaint." :
                kind == "keg_dry" ? "The drinks ran out!" : kind == "stink_bomb" ? "Someone set off a stink bomb!" : "Someone famous just walked in.";
            Emit(GameEventKind.IncidentOpened, text, PlayerHouse.id, incidentId: inc.id, guestId: guestId);
            return inc;
        }

        void ResolveIncidents(string kind)
        {
            foreach (var i in OpenIncidents.Where(x => x.kind == kind).ToList()) ResolveIncident(i);
        }

        void ResolveIncident(IncidentState inc)
        {
            inc.resolved = true;
            Emit(GameEventKind.IncidentResolved, inc.kind, PlayerHouse.id, incidentId: inc.id, guestId: inc.guestId);
        }

        void EscalateIncidents()
        {
            var p = State.party;
            var h = PlayerHouse;
            var t = Config.party;
            foreach (var inc in OpenIncidents.ToList())
            {
                if (inc.escalated || p.tick - inc.openedTick < t.incidentEscalateTicks) continue;
                inc.escalated = true;
                if (inc.kind != "keg_dry") p.incidentsEscalated++;
                switch (inc.kind)
                {
                    case "fight":
                        p.injuries++;
                        h.heat = Clamp(h.heat + t.injuryHeat, 0, 100);
                        foreach (var g in PresentGuests) g.mood = Clamp(g.mood - 10, 0, 100);
                        News("incident", "A fight at " + h.name + "'s party left someone hurt.", h.id, isPublic: true);
                        ResolveIncident(inc);
                        break;
                    case "passed_out":
                        var guest = p.guests.Find(g => g.guestId == inc.guestId);
                        if (guest != null && guest.underage && guest.drankUnderage)
                        {
                            p.hospitalized++;
                            h.heat = Clamp(h.heat + t.hospitalHeat, 0, 100);
                            h.dean = Clamp(h.dean - t.hospitalDean, 0, 100);
                            News("incident", "An underage guest from " + h.name + "'s party was taken to the hospital.", h.id, isPublic: true);
                        }
                        else h.heat = Clamp(h.heat + 5, 0, 100);
                        if (guest != null) Leave(guest, "ambulance");
                        ResolveIncident(inc);
                        break;
                    case "property_damage":
                        Fine(h, t.damageCost, "property damage");
                        ResolveIncident(inc);
                        break;
                    case "noise_complaint":
                        p.attention = Clamp(p.attention + 10, 0, 100);
                        ResolveIncident(inc);
                        break;
                    case "stink_bomb":
                        ResolveIncident(inc);
                        break;
                }
            }
        }

        void EndPlayersParty(bool raided)
        {
            var p = State.party;
            if (!p.active) return;
            p.active = false;
            p.ended = true;
            p.raided = raided;
            p.fun = p.guests.Count == 0 ? 0 : p.guests.Average(g => g.mood);
            foreach (var g in p.guests) g.left = true;
            p.openServings.Clear();
            p.pendingArrivals.Clear();
            p.pendingArrivalTicks.Clear();
            Emit(GameEventKind.PartyEnded, raided ? "raided" : "ended", PlayerHouse.id, flag: raided, value: p.guests.Count);
        }
    }
}
