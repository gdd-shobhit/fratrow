using System;
using System.Collections.Generic;
using System.Linq;

namespace FratRow.Game
{
    /// <summary>
    /// Host-authoritative game loop (design/gdd/core-game-loop.md). Unity-free and deterministic for a given seed and
    /// action sequence. The host ticks it every frame and applies validated player actions; adapters drain events.
    /// Example:
    /// <code>
    /// var state = GameSession.NewCampaign(config, roster, "main", seed: 42);
    /// var session = new GameSession(config, roster, state, new SocialWorldBridge(world));
    /// session.AddOrUpdatePlayer(playerId, "Sam", "President");
    /// session.Tick(Time.deltaTime);
    /// var result = session.Apply(new PlayerAction { playerId = playerId, action = ActionIds.Buy, item = "keg", amount = 1 });
    /// </code>
    /// </summary>
    public sealed partial class GameSession
    {
        public readonly GameLoopConfig Config;
        public readonly CampusRoster Roster;
        public GameState State { get; }
        readonly ISocialBridge social;
        readonly List<GameEvent> events = new List<GameEvent>();
        readonly Dictionary<string, RosterNpc> rosterById;

        /// <summary>Creates a fresh campaign state from config (houses, NPC game state, week 0).</summary>
        public static GameState NewCampaign(GameLoopConfig config, CampusRoster roster, string campaignId, int seed)
        {
            var s = new GameState { campaignId = campaignId, seed = seed, phase = Phase.Weekday };
            foreach (var d in config.houses)
                s.houses.Add(new HouseState
                {
                    id = d.id, name = d.name, nickname = d.nickname, isPlayer = d.isPlayer, homeLocation = d.homeLocation,
                    popularity = d.popularity, cash = d.cash, heat = d.heat, dean = d.dean, gpa = d.gpa, memberCount = d.members,
                    prestige = d.prestige, chaos = d.chaos, aggression = d.aggression, caution = d.caution, cover = d.isPlayer ? config.economy.coverDefault : 0
                });
            return s;
        }

        public GameSession(GameLoopConfig config, CampusRoster roster, GameState state, ISocialBridge socialBridge)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Roster = roster ?? throw new ArgumentNullException(nameof(roster));
            State = state ?? throw new ArgumentNullException(nameof(state));
            social = socialBridge ?? throw new ArgumentNullException(nameof(socialBridge));
            State.Normalize();
            rosterById = roster.npcs.ToDictionary(n => n.id);
            // Additive: houses/NPCs added to config or roster after the save was created.
            foreach (var d in config.houses)
                if (House(d.id) == null)
                    State.houses.Add(NewCampaign(config, roster, State.campaignId, State.seed).houses.First(h => h.id == d.id));
            foreach (var n in roster.npcs)
                if (Npc(n.id) == null)
                    State.npcs.Add(new NpcGameState
                    {
                        npcId = n.id, isPnm = n.isPnm, contactUnlocked = n.startsUnlocked,
                        rivalRegard = n.rivalAffinity.Select(a => new HouseValue { houseId = a.houseId, value = a.value }).ToList()
                    });
            social.RegisterRoster(roster);
            foreach (var npc in State.npcs.Where(n => !string.IsNullOrEmpty(n.memberOf)))
                social.SetAffiliation(npc.npcId, AffiliationFor(npc.memberOf));
            if (!State.started)
            {
                State.started = true;
                BeginWeek(first: true);
            }
            else if (State.phase == Phase.Weekday && string.IsNullOrEmpty(State.socialPartyId))
                State.socialPartyId = social.EnsurePlannedParty(PlannedCapacity());
            EnsurePatrols();
            PushStandings();
        }

        // ---------- Queries ----------

        public HouseState PlayerHouse => State.houses.First(h => h.isPlayer);
        public HouseState House(string id) => State.houses.Find(h => h.id == id);
        public IEnumerable<HouseState> Rivals => State.houses.Where(h => !h.isPlayer);
        public PlayerState Player(string id) => State.players.Find(p => p.playerId == id);
        readonly Dictionary<string, NpcGameState> npcIndex = new Dictionary<string, NpcGameState>();

        /// <summary>O(1) NPC lookup (polled for every NPC by the Unity director); rebuilt if the list changes.</summary>
        public NpcGameState Npc(string id)
        {
            if (id == null) return null;
            if (npcIndex.Count != State.npcs.Count)
            {
                npcIndex.Clear();
                foreach (var n in State.npcs) npcIndex[n.npcId] = n;
            }
            return npcIndex.TryGetValue(id, out var npc) ? npc : null;
        }
        public RosterNpc RosterNpc(string id) => id != null && rosterById.TryGetValue(id, out var n) ? n : null;
        public RoleDef RoleOf(PlayerState p) => Config.Role(p.role) ?? Config.Role("brother");
        public IEnumerable<PlayerState> ConnectedPlayers => State.players.Where(p => p.connected);
        public int PlayerMembers => State.players.Count;
        public int TotalMembers(HouseState h) => h.memberCount + (h.isPlayer ? State.players.Count : 0);
        public string HouseAtLocation(string location) => Config.Location(location)?.houseId ?? "";

        /// <summary>Houses ordered by Greek Cup points, then popularity (rank 1 first).</summary>
        public List<HouseState> Ranked() => State.houses.OrderByDescending(h => h.cupPoints).ThenByDescending(h => h.popularity).ThenBy(h => h.id, StringComparer.Ordinal).ToList();
        public int RankOf(HouseState h) => Ranked().IndexOf(h) + 1;

        /// <summary>In-game hour 0..24 for the current phase.</summary>
        public float ClockHour
        {
            get
            {
                switch (State.phase)
                {
                    case Phase.Weekday:
                        return (float)(Config.weekdayStartHour + Math.Min(1.0, State.phaseElapsed / Config.weekdaySecondsPerDay) * (Config.weekdayEndHour - Config.weekdayStartHour));
                    case Phase.Party:
                        float duration = (Config.partyEndHour - Config.partyStartHour + 24f) % 24f;
                        if (duration <= 0) duration = 24f;
                        return (float)((Config.partyStartHour + Math.Min(1.0, State.phaseElapsed / Config.partySeconds) * duration) % 24.0);
                    default: return 12f;
                }
            }
        }

        /// <summary>In-game hours advanced per real second in the current phase (for client clock interpolation).</summary>
        public float HoursPerSecond
        {
            get
            {
                if (State.phase == Phase.Weekday) return (Config.weekdayEndHour - Config.weekdayStartHour) / Config.weekdaySecondsPerDay;
                if (State.phase == Phase.Party) { float d = (Config.partyEndHour - Config.partyStartHour + 24f) % 24f; return (d <= 0 ? 24f : d) / Config.partySeconds; }
                return 0f;
            }
        }

        /// <summary>Real seconds left in the current day (weekday) or phase.</summary>
        public double PhaseRemainingSeconds
        {
            get
            {
                switch (State.phase)
                {
                    case Phase.Weekday: return Math.Max(0, Config.weekdaySecondsPerDay - State.phaseElapsed);
                    case Phase.Party: return Math.Max(0, Config.partySeconds - State.phaseElapsed);
                    case Phase.Debrief: return Math.Max(0, Config.debriefSeconds - State.phaseElapsed);
                    default: return 0;
                }
            }
        }

        /// <summary>Where a named NPC is right now (schedule on weekdays; party destination on party night), or null.</summary>
        public string NpcLocation(string npcId)
        {
            var def = RosterNpc(npcId);
            var npc = Npc(npcId);
            if (def == null || npc == null) return null;
            if (State.phase == Phase.Party)
            {
                if (npc.destination == "" || npc.destination == "home") return null;
                if (npc.destination == Config.playerHouseId) return npc.attendedPlayersParty ? Config.playerHomeLocation : null;
                return House(npc.destination)?.homeLocation;
            }
            if (State.phase != Phase.Weekday) return null;
            return CampusRoster.LocationAt(def, State.day, ClockHour);
        }

        /// <summary>Named NPCs currently at a location (witness candidates).</summary>
        public List<string> NpcsAt(string location)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(location)) return list;
            foreach (var n in Roster.npcs) if (NpcLocation(n.id) == location) list.Add(n.id);
            return list;
        }

        public int StashCount(HouseState h, string itemId) => h.stash.Find(e => e.itemId == itemId)?.count ?? 0;

        public void AddStash(HouseState h, string itemId, int delta)
        {
            var e = h.stash.Find(x => x.itemId == itemId);
            if (e == null) { if (delta <= 0) return; e = new StashEntry { itemId = itemId }; h.stash.Add(e); }
            e.count = Math.Max(0, e.count + delta);
            if (e.count == 0) h.stash.Remove(e);
            if (h.isPlayer) Emit(GameEventKind.StashChanged, itemId, h.id, value: StashCount(h, itemId));
        }

        // ---------- Players ----------

        /// <summary>Adds a brother (or refreshes the display name). The first join assigns a role from the lobby class.</summary>
        public PlayerState AddOrUpdatePlayer(string playerId, string displayName, string lobbyClass = "")
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("playerId required");
            var p = Player(playerId);
            if (p == null)
            {
                p = new PlayerState { playerId = playerId, displayName = string.IsNullOrWhiteSpace(displayName) ? "Brother" : displayName };
                var role = Config.roles.Find(r => !string.IsNullOrEmpty(r.lobbyClass) && r.lobbyClass == lobbyClass);
                p.role = role != null && (!role.unique || State.players.All(x => x.role != role.id)) ? role.id : "brother";
                State.players.Add(p);
                social.RegisterPlayer(playerId, p.displayName);
                News("join", p.displayName + " joined " + PlayerHouse.name + " as " + RoleOf(p).name + ".", PlayerHouse.id, playerId);
                Emit(GameEventKind.RoleChanged, p.role, PlayerHouse.id, playerId);
            }
            else if (!string.IsNullOrWhiteSpace(displayName)) p.displayName = displayName;
            p.connected = true;
            return p;
        }

        public void SetPlayerConnected(string playerId, bool connected)
        {
            var p = Player(playerId);
            if (p == null) return;
            p.connected = connected;
            if (!connected) { p.ready = false; p.atEntrance = false; }
        }

        /// <summary>Host adapter reports where a player is (CampusLocation id, "" when between locations).</summary>
        public void SetPlayerLocation(string playerId, string locationId, bool atEntrance)
        {
            var p = Player(playerId);
            if (p == null) return;
            p.location = locationId ?? "";
            p.atEntrance = atEntrance;
        }

        /// <summary>Mirror of the phone contact unlock owned by the Unity contact roster.</summary>
        public void SetContactUnlocked(string npcId, bool unlocked)
        {
            var n = Npc(npcId);
            if (n != null) n.contactUnlocked = unlocked;
        }

        public void DrainEvents(List<GameEvent> into)
        {
            into.AddRange(events);
            events.Clear();
        }

        // ---------- Tick / phases ----------

        /// <summary>Advances simulated time. Safe for any dt (loops through day/tick boundaries).</summary>
        public void Tick(double dt)
        {
            if (dt <= 0) return;
            social.BeginTick();
            // Large steps are processed in slices so patrols, days and party ticks stay ordered.
            while (dt > 0)
            {
                double step = Math.Min(dt, Math.Min(Config.partyTickSeconds, 5.0));
                dt -= step;
                Step(step);
            }
            FlushGossipLedger();
        }

        void Step(double dt)
        {
            State.time += dt;
            State.phaseElapsed += dt;
            UpdatePatrols(dt);
            SoberUp(dt);
            switch (State.phase)
            {
                case Phase.Weekday:
                    if (State.phaseElapsed >= Config.weekdaySecondsPerDay)
                    {
                        State.phaseElapsed -= Config.weekdaySecondsPerDay;
                        EndOfWeekday();
                    }
                    GossipClock();
                    break;
                case Phase.Party:
                    State.partyTickAccumulator += dt;
                    while (State.phase == Phase.Party && State.partyTickAccumulator >= Config.partyTickSeconds)
                    {
                        State.partyTickAccumulator -= Config.partyTickSeconds;
                        PartyTick();
                    }
                    if (State.phase == Phase.Party && State.phaseElapsed >= Config.partySeconds)
                    {
                        if (State.party.active) EndPlayersParty(raided: false);
                        BeginDebrief();
                    }
                    break;
                case Phase.Debrief:
                    if (State.phaseElapsed >= Config.debriefSeconds) AdvanceFromDebrief();
                    break;
            }
        }

        void EndOfWeekday()
        {
            FinishGossipDay();
            if (State.day >= 4)
            {
                BeginParty();
                return;
            }
            State.day++;
            DailyTick();
            Emit(GameEventKind.DayChanged, DayName(State.day), value: State.day);
        }

        /// <summary>Host/President: skip the remaining weekdays (their daily ticks still run).</summary>
        public ActionResult StartWeekendEarly()
        {
            if (State.phase != Phase.Weekday) return ActionResult.Fail("wrong_phase", "The weekend can only start during the week.");
            while (State.day < 4) { FinishGossipDay(); State.day++; DailyTick(); }
            FinishGossipDay();
            State.weekendStartedEarly = true;
            BeginParty();
            return ActionResult.Ok("Weekend started.");
        }

        /// <summary>Host: leave the debrief early.</summary>
        public ActionResult ContinueDebrief()
        {
            if (State.phase != Phase.Debrief) return ActionResult.Fail("wrong_phase", "Not in debrief.");
            AdvanceFromDebrief();
            return ActionResult.Ok();
        }

        void AdvanceFromDebrief()
        {
            FinishGossipDay();
            if (State.week + 1 >= Config.semesterWeeks) { BeginSemesterEnd(); return; }
            State.week++;
            BeginWeek(first: false);
        }

        void BeginWeek(bool first)
        {
            SetPhase(Phase.Weekday, 0);
            State.weekendStartedEarly = false;
            foreach (var h in State.houses) { h.deanMetThisWeek = false; h.theme = ""; h.weekSabotagePop = 0; h.sabotageScoreMod = 0; }
            foreach (var p in State.players) { p.studyThisWeek = 0; p.weekly.Clear(); p.daily.Clear(); p.ready = false; }
            foreach (var n in State.npcs) { n.destination = ""; n.destinationArrivalTick = -1; n.rushTalksToday = 0; n.attendedPlayersParty = false; }
            State.party = new PartyState();
            State.rivalParties.Clear();
            RivalWeekStart();
            State.socialPartyId = PlayerHouse.suspended ? "" : social.EnsurePlannedParty(PlannedCapacity());
            if (first) News("semester", "Semester " + State.semester + " begins. " + PlayerHouse.name + " is the newest house on Greek Row.", isPublic: false);
            News("week", "Week " + (State.week + 1) + " of " + Config.semesterWeeks + " begins." +
                (PlayerHouse.suspended ? " " + PlayerHouse.name + " is SUSPENDED: no party this weekend." :
                 PlayerHouse.probation ? " " + PlayerHouse.name + " is on probation: half capacity and half points." : ""), PlayerHouse.id);
        }

        void BeginDebrief()
        {
            FinishGossipDay();
            SetPhase(Phase.Debrief, 6);
            Settle();
        }

        void BeginSemesterEnd()
        {
            SetPhase(Phase.SemesterEnd, 6);
            var ranked = Ranked();
            State.semesterWinner = ranked[0].id;
            string table = string.Join("; ", ranked.Select((h, i) => (i + 1) + ". " + h.name + " (" + h.cupPoints + " pts)"));
            News("semester", "Semester " + State.semester + " is over. Fraternity of the Year: " + ranked[0].name + ". Final table: " + table, ranked[0].id, isPublic: true);
            social.PublishNews("Campus news: " + ranked[0].name + " won Fraternity of the Year for semester " + State.semester + ".", AllNpcIds());
            Emit(GameEventKind.SemesterEnded, ranked[0].name, ranked[0].id, flag: ranked[0].isPlayer);
        }

        /// <summary>Host: begin the next semester (cup points reset; relationships, members and cash persist).</summary>
        public ActionResult StartNextSemester()
        {
            if (State.phase != Phase.SemesterEnd) return ActionResult.Fail("wrong_phase", "The semester is not over.");
            State.semester++;
            State.week = 0;
            foreach (var h in State.houses)
            {
                h.cupPoints = 0;
                h.heat = 0;
                h.probation = false;
                h.suspended = false;
                if (!h.isPlayer)
                {
                    var def = Config.House(h.id);
                    if (def != null) h.popularity += (def.popularity - h.popularity) * Config.rivals.semesterRegress;
                }
            }
            State.semesterWinner = "";
            BeginWeek(first: true);
            PushStandings();
            return ActionResult.Ok();
        }

        void SetPhase(Phase phase, int day)
        {
            State.phase = phase;
            State.day = day;
            State.phaseElapsed = 0;
            State.partyTickAccumulator = 0;
            Emit(GameEventKind.PhaseChanged, phase.ToString(), value: (int)phase);
        }

        void DailyTick()
        {
            foreach (var p in State.players) p.daily.Clear();
            foreach (var n in State.npcs) n.rushTalksToday = 0;
            bool riskManager = State.players.Any(p => p.role == "risk_manager");
            foreach (var h in State.houses)
            {
                float decay = Config.police.dailyHeatDecay + (h.isPlayer && riskManager ? Config.Role("risk_manager")?.heatDecayBonus ?? 0 : 0);
                h.heat = Clamp(h.heat - decay, 0, 100);
            }
            RivalDaily();
            RivalTraceSmears();
            MorningBriefing();
        }

        /// <summary>One news line per weekday morning summarising what the brothers need to know (design §3.9).</summary>
        void MorningBriefing()
        {
            var h = PlayerHouse;
            var lines = new List<string> { DayName(State.day) + " morning." };
            if (!string.IsNullOrEmpty(State.socialPartyId))
            {
                var rsvps = social.Commitments(State.socialPartyId);
                int yes = rsvps.Count(c => c.state == "accepted"), maybe = rsvps.Count(c => c.state == "conditional" || c.state == "deferred");
                lines.Add(yes + " confirmed for Saturday" + (maybe > 0 ? ", " + maybe + " still deciding" : "") + ".");
            }
            var themed = Rivals.Where(r => !string.IsNullOrEmpty(r.theme) && !r.suspended).Select(r => r.name + ": " + (Config.Theme(r.theme)?.name ?? r.theme)).ToList();
            if (themed.Count > 0) lines.Add("Rival parties: " + string.Join(", ", themed) + ".");
            var hottest = State.houses.OrderByDescending(x => x.heat).First();
            if (hottest.heat >= Config.police.patrolHeatStep) lines.Add("Police are watching " + (hottest.isPlayer ? "OUR house" : hottest.name) + ".");
            if (h.trashed) lines.Add("Our house is still trashed.");
            string word = MorningGossipLine();
            if (word != null) lines.Add(word);
            if (State.day == 4) lines.Add("Party tomorrow night!");
            News("briefing", string.Join(" ", lines), h.id);
        }

        int PlannedCapacity()
        {
            var h = PlayerHouse;
            float cap = Capacity(h);
            if (h.probation) cap *= Config.dean.probationCapacityFactor;
            return Math.Max(4, (int)Math.Round(cap));
        }

        /// <summary>Base capacity plus members (capped), before probation.</summary>
        public float Capacity(HouseState h) =>
            Config.party.baseCapacity + Config.party.capacityPerMember * Math.Min(h.memberCount, Config.party.capacityMemberCap);

        // ---------- Helpers ----------

        internal void News(string kind, string text, string houseId = "", string playerId = "", string npcId = "", bool isPublic = false)
        {
            var n = new NewsItem
            {
                seq = ++State.newsSeq, semester = State.semester, week = State.week, day = State.day, kind = kind, text = text,
                houseId = houseId ?? "", playerId = playerId ?? "", npcId = npcId ?? "", isPublic = isPublic
            };
            State.news.Add(n);
            while (State.news.Count > 80) State.news.RemoveAt(0);
            Emit(GameEventKind.News, text, houseId, playerId, npcId, flag: isPublic);
        }

        internal void Emit(GameEventKind kind, string text = "", string houseId = "", string playerId = "", string npcId = "",
            float value = 0, bool flag = false, int guestId = -1, int archetype = 0, int incidentId = -1)
        {
            events.Add(new GameEvent { kind = kind, text = text ?? "", houseId = houseId ?? "", playerId = playerId ?? "", npcId = npcId ?? "",
                value = value, flag = flag, guestId = guestId, archetype = archetype, incidentId = incidentId });
        }

        GameRng Rng(string purpose, long b = 0) => GameRng.For(State.seed, purpose, State.semester * 1000 + State.week * 10 + State.day, b);
        GameRng NextRng(string purpose) => GameRng.For(State.seed, purpose, ++State.eventSeq, State.semester);

        internal static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
        public static string DayName(int day) => day >= 0 && day < 7 ? new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }[day] : "?";

        List<string> AllNpcIds() => Roster.npcs.Select(n => n.id).ToList();

        string AffiliationFor(string houseId)
        {
            var h = House(houseId);
            return h == null ? "" : h.isPlayer ? "players" : h.name;
        }

        void PushStandings()
        {
            foreach (var h in State.houses) RecomputeGrade(h);
            social.SetStandings(PlayerHouse, Ranked());
        }

        static int CounterValue(List<Counter> list, string key) => list.Find(c => c.key == key)?.count ?? 0;
        static void CounterAdd(List<Counter> list, string key)
        {
            var c = list.Find(x => x.key == key);
            if (c == null) list.Add(new Counter { key = key, count = 1 }); else c.count++;
        }

        void RecomputeGrade(HouseState h)
        {
            float cashN = Clamp(h.cash / 2000f * 100f, 0, 100);
            float membersN = Clamp(TotalMembers(h) / 40f * 100f, 0, 100);
            h.rating = (int)Math.Round(Clamp(0.45f * h.popularity + 0.15f * cashN + 0.20f * membersN + 0.20f * h.dean, 0, 100));
            h.grade = GradeFromRating(h.rating);
        }

        /// <summary>Same thresholds as Unity's FraternityGradeCalculator.</summary>
        public static string GradeFromRating(int rating) =>
            rating >= 85 ? "A" : rating >= 70 ? "B" : rating >= 55 ? "C" : rating >= 40 ? "D" : rating >= 25 ? "E" : "F";

        /// <summary>Deducts a fine; any shortfall becomes a Dean penalty (design §5).</summary>
        void Fine(HouseState h, int amount, string reason)
        {
            if (amount <= 0) return;
            int paid = Math.Min(h.cash, amount);
            h.cash -= paid;
            int shortfall = amount - paid;
            if (shortfall > 0) h.dean = Clamp(h.dean - shortfall / (float)Math.Max(1, Config.dean.finePerDeanPoint), 0, 100);
            if (h.isPlayer) Emit(GameEventKind.HouseChanged, reason, h.id, value: -amount);
        }
    }
}
