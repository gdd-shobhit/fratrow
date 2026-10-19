using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FratRow.Game;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Binds the in-game HUD (<c>HUDVisualTree.uxml</c> / <c>HUD.uss</c>) to the replicated campaign state in
/// <see cref="NetworkedCampaign"/> (design/gdd/core-game-loop.md §3). Display only: the HUD never writes game state.
/// <para>
/// Update model: the in-game clock and the phase countdown are refreshed every frame from cached strings (no allocation
/// unless the displayed minute/second changes). Everything else refreshes on the campaign's change events, coalesced to
/// once per frame through dirty flags. Countdowns, news dimming and the toast timer run on a 250 ms UI scheduler tick.
/// No transitions/animations are used (none are specified), so there is nothing to skip for reduced-motion users.
/// </para>
/// <para>
/// While no campaign is running (host's campaign failed to start, or not spawned yet) the legacy elements fall back to
/// <see cref="NetworkedGameManager"/> / <see cref="NetworkedParty"/>.
/// </para>
/// Example (override a replicated value from a debug tool): <c>FindAnyObjectByType&lt;GameplayUIManager&gt;().SetDrunkLevel(0.4f, 2);</c>
/// </summary>
public class GameplayUIManager : MonoBehaviour
{
    [SerializeField] UIDocument document;

    [Header("Meter colour thresholds (mirror Resources/Campus/GameLoopConfig.json)")]
    [Tooltip("police.warningThreshold: party attention at which a unit is dispatched (meter turns amber).")]
    [SerializeField] float attentionWarning = 40f;
    [Tooltip("police.raidThreshold: party attention at which an arriving unit raids (meter turns red).")]
    [SerializeField] float attentionDanger = 55f;
    [Tooltip("police.patrolHeatStep: every step of heat adds a patrol (heat bar turns amber).")]
    [SerializeField] float heatWarning = 30f;
    [Tooltip("police.copsParkedHeat: a cop car parks outside the house (heat bar turns red).")]
    [SerializeField] float heatDanger = 70f;
    [Tooltip("dean.probationDean: below this the house goes on probation (Dean bar turns red).")]
    [SerializeField] float deanDanger = 30f;
    [Tooltip("House cash that fills the legacy Cash slider completely.")]
    [SerializeField] float cashSliderFullAt = 1000f;

    [Header("Feed")]
    [SerializeField, Range(1, 12)] int newsLines = 6;
    [Tooltip("Seconds before a news line dims.")]
    [SerializeField] float newsFadeSeconds = 20f;
    [Tooltip("Seconds the last action result stays on screen.")]
    [SerializeField] float toastSeconds = 4f;

    // ---------------- Dirty flags ----------------
    const int DirtyHud = 1, DirtyHouses = 2, DirtyBrothers = 4, DirtyStash = 8, DirtyIncidents = 16, DirtyAll = 31;
    const int MaxIncidentLines = 6;
    const long SlowTickMs = 250;

    // ---------------- Elements ----------------
    VisualElement _root, _marker;
    IVisualElementScheduledItem _slowTick;

    // Legacy (kept names)
    Slider _popularitySlider, _troubleMakerSlider, _cashSlider;
    Label _cashValueLabel, _dayLabel, _currentGuestsLabel, _timeLabel, _drunkLabel;
    Label _fratNameLabel, _fratGradeLabel, _fratRatingLabel;

    // Phase banner
    VisualElement _phaseBanner, _phaseSubRow;
    Label _phaseWeek, _phaseDay, _phaseClock, _phaseName, _phaseTimeCaption, _phaseTimeLeft, _readyStatus;
    VisualElement _policeAlert;
    Label _policeAlertText;

    // House
    VisualElement _houseStats, _flagProbation, _flagSuspended, _flagTrashed, _flagGuarded;
    Label _houseRank, _housePopularity, _houseCash, _houseGpa, _houseMembers, _houseCupPoints;
    Meter _heatMeter, _deanMeter;

    // Standings
    VisualElement _standings, _standingsRows;
    readonly List<StandingRow> _standingRowPool = new List<StandingRow>();

    // Party
    VisualElement _partyPanel, _partyIncidentList;
    Label _partyGuests, _partyUnderage, _partyCardCheck, _partyTheme, _partyCover, _partyCash, _partyIncidentsHeader, _partyIncidentsEmpty;
    Label _partyAttentionState;
    Meter _funMeter, _hypeMeter, _noiseMeter, _troubleMeter, _attentionMeter;
    readonly List<Label> _incidentLabels = new List<Label>();

    // Player / stash
    VisualElement _playerStatus, _detainedRow;
    Label _brotherLine, _brotherRecord, _detainedValue, _stashSummary;

    // News / toast
    VisualElement _newsFeed, _newsItems;
    readonly List<Label> _newsLabels = new List<Label>();
    Label _toast;

    // ---------------- Bindings ----------------
    NetworkedCampaign _campaign;
    NetworkedGameManager _legacyGm;
    NetworkedParty _legacyParty;
    int _legacyAttempts;
    float _nextLegacyBindAt;
    int _dirty = DirtyAll;
    bool _campaignActiveShown;
    bool _hasCampaignShown; // false until the first visibility pass after a (re)bind

    // ---------------- Display caches (sentinels force the first write) ----------------
    int _shownMinute = int.MinValue, _shownRemaining = int.MinValue, _shownDetained = int.MinValue;
    int _shownPhase = -1, _shownWeekKey = -1, _shownDay = -1, _shownAbsDay = -1, _shownAlertKey = -1, _shownReadyKey = -1;
    int _shownPartyActive = -1, _shownGuestsKey = -1, _shownUnderage = -1, _shownCardCheck = -1, _shownCover = -1, _shownPartyCash = -1;
    int _shownAttentionLevel = -1, _shownLegacyGuestsKey = -1, _shownLegacyMinute = int.MinValue, _shownPop = -1, _shownHeat = -1, _shownCash = int.MinValue;
    FixedString32Bytes _shownTheme;
    bool _themeShown;
    HouseNetState _shownPlayerHouse;
    bool _hasPlayerHouse;
    int _shownHouseCount = -1;
    BrotherNetState _shownBrother;
    int _shownBrotherState = -1; // -1 unknown, 0 missing, 1 present
    bool _localReady;
    float _detainedUntil;
    readonly List<HouseNetState> _sortedHouses = new List<HouseNetState>(8);
    readonly StringBuilder _sb = new StringBuilder(256);

    // News ring (index 0 = newest), survives UI rebinds.
    string[] _newsText;
    float[] _newsTime;
    bool[] _newsAlert;
    float[] _newsOpacityShown;
    int _newsCount;

    bool _toastVisible;
    float _toastHideAt;

    // ---------------- String caches (zero-alloc steady state) ----------------
    static readonly string[] s_clock = new string[24 * 60];
    static readonly string[] s_duration = new string[60 * 60];
    static readonly string[] s_ints = new string[1001];
    static readonly string[] s_dayShort = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
    static readonly HouseRankComparer s_byRank = new HouseRankComparer();

    // =====================================================================
    // Lifecycle
    // =====================================================================

    void Awake()
    {
        if (document == null) document = GetComponent<UIDocument>();
        AllocateNewsRing();
    }

    void OnEnable()
    {
        _dirty = DirtyAll;
        _slowTick?.Resume();
    }

    void OnDisable()
    {
        _slowTick?.Pause();
        UnbindCampaign();
        UnbindLegacy();
    }

    void Update()
    {
        if (ElementsStale()) BindElements();
        if (_root == null) return;

        PollCampaign();
        bool active = _campaign != null && _campaign.IsCampaignActive;
        if (active != _campaignActiveShown || !_hasCampaignShown) ApplyCampaignVisibility(active);
        if (!active) return;

        if (_dirty != 0) FlushDirty();
        TickClock();
    }

    // =====================================================================
    // Element binding
    // =====================================================================

    bool ElementsStale()
    {
        if (document == null) return false;
        var root = document.rootVisualElement;
        if (root == null) return false;
        if (!ReferenceEquals(root, _root)) return true;
        // UIDocument re-clones its tree when re-enabled; a detached marker means our references are stale.
        return _marker != null && _marker.panel == null;
    }

    void BindElements()
    {
        _slowTick?.Pause();
        _root = document.rootVisualElement;
        var r = _root;

        _popularitySlider = r.Q<Slider>("Popularity");
        _troubleMakerSlider = r.Q<Slider>("TroubleMaker");
        _cashSlider = r.Q<Slider>("Cash");
        _cashValueLabel = r.Q<Label>("CashValueText");
        _dayLabel = r.Q<Label>("DayNumber");
        _currentGuestsLabel = r.Q<Label>("CurrentGuests");
        _timeLabel = r.Q<Label>("Time");
        _drunkLabel = r.Q<Label>("DrunkLevel");
        _fratNameLabel = r.Q<Label>("FratName");
        _fratGradeLabel = r.Q<Label>("FratGrade");
        _fratRatingLabel = r.Q<Label>("FratRating");

        _phaseBanner = r.Q("PhaseBanner");
        _phaseSubRow = r.Q("PhaseSubRow");
        _phaseWeek = r.Q<Label>("PhaseWeek");
        _phaseDay = r.Q<Label>("PhaseDay");
        _phaseClock = r.Q<Label>("PhaseClock");
        _phaseName = r.Q<Label>("PhaseName");
        _phaseTimeCaption = r.Q<Label>("PhaseTimeCaption");
        _phaseTimeLeft = r.Q<Label>("PhaseTimeLeft");
        _readyStatus = r.Q<Label>("ReadyStatus");
        _policeAlert = r.Q("PoliceAlert");
        _policeAlertText = r.Q<Label>("PoliceAlertText");

        _houseStats = r.Q("HouseStats");
        _houseRank = r.Q<Label>("HouseRank");
        _housePopularity = r.Q<Label>("HousePopularity");
        _houseCash = r.Q<Label>("HouseCash");
        _houseGpa = r.Q<Label>("HouseGpa");
        _houseMembers = r.Q<Label>("HouseMembers");
        _houseCupPoints = r.Q<Label>("HouseCupPoints");
        _flagProbation = r.Q("FlagProbation");
        _flagSuspended = r.Q("FlagSuspended");
        _flagTrashed = r.Q("FlagTrashed");
        _flagGuarded = r.Q("FlagGuarded");
        _heatMeter = new Meter(r.Q("HouseHeatFill"), r.Q<Label>("HouseHeatValue"));
        _deanMeter = new Meter(r.Q("HouseDeanFill"), r.Q<Label>("HouseDeanValue"));

        _standings = r.Q("Standings");
        _standingsRows = r.Q("StandingsRows");
        _standingRowPool.Clear();

        _partyPanel = r.Q("PartyPanel");
        _partyGuests = r.Q<Label>("PartyGuests");
        _partyUnderage = r.Q<Label>("PartyUnderage");
        _partyCardCheck = r.Q<Label>("PartyCardCheck");
        _partyTheme = r.Q<Label>("PartyTheme");
        _partyCover = r.Q<Label>("PartyCover");
        _partyCash = r.Q<Label>("PartyCash");
        _partyIncidentsHeader = r.Q<Label>("PartyIncidentsHeader");
        _partyIncidentsEmpty = r.Q<Label>("PartyIncidentsEmpty");
        _partyIncidentList = r.Q("PartyIncidentList");
        _partyAttentionState = r.Q<Label>("PartyAttentionState");
        _funMeter = new Meter(r.Q("PartyFunFill"), r.Q<Label>("PartyFunValue"));
        _hypeMeter = new Meter(r.Q("PartyHypeFill"), r.Q<Label>("PartyHypeValue"));
        _noiseMeter = new Meter(r.Q("PartyNoiseFill"), r.Q<Label>("PartyNoiseValue"));
        _troubleMeter = new Meter(r.Q("PartyTroubleFill"), r.Q<Label>("PartyTroubleValue"));
        _attentionMeter = new Meter(r.Q("PartyAttentionFill"), r.Q<Label>("PartyAttentionValue"));
        _incidentLabels.Clear();
        if (_partyIncidentList != null)
        {
            _partyIncidentList.Clear();
            for (int i = 0; i < MaxIncidentLines; i++)
            {
                var label = NewLabel("hud-incident");
                SetDisplay(label, false);
                _partyIncidentList.Add(label);
                _incidentLabels.Add(label);
            }
        }

        _playerStatus = r.Q("PlayerStatus");
        _brotherLine = r.Q<Label>("BrotherLine");
        _brotherRecord = r.Q<Label>("BrotherRecord");
        _detainedRow = r.Q("BrotherDetainedRow");
        _detainedValue = r.Q<Label>("BrotherDetained");
        _stashSummary = r.Q<Label>("StashSummary");

        _newsFeed = r.Q("NewsFeed");
        _newsItems = r.Q("NewsItems");
        _newsLabels.Clear();
        if (_newsItems != null)
        {
            _newsItems.Clear();
            for (int i = 0; i < _newsText.Length; i++)
            {
                var label = NewLabel("hud-news-item");
                _newsItems.Add(label);
                _newsLabels.Add(label);
            }
        }
        _toast = r.Q<Label>("ActionToast");

        _marker = r.Q("GameplayInfo") ?? r.Q("HUD");

        // The HUD is display only: never let it eat clicks meant for other documents (phone, inventory, shop).
        r.Query<VisualElement>().ForEach(SetPickingIgnore);

        SetupStatSliders();
        SetDisplay(_toast, false);
        _toastVisible = false;
        if (_drunkLabel != null) _drunkLabel.text = HudText.NoValue;

        ResetDisplayCaches();
        ApplyNewsLabels(Time.unscaledTime);
        _hasCampaignShown = false;
        _slowTick = r.schedule.Execute(SlowTick).Every(SlowTickMs);
        if (!enabled) _slowTick.Pause();
    }

    void ResetDisplayCaches()
    {
        _shownMinute = _shownRemaining = _shownDetained = _shownLegacyMinute = _shownCash = int.MinValue;
        _shownPhase = _shownWeekKey = _shownDay = _shownAbsDay = _shownAlertKey = _shownReadyKey = -1;
        _shownPartyActive = _shownGuestsKey = _shownUnderage = _shownCardCheck = _shownCover = _shownPartyCash = -1;
        _shownAttentionLevel = _shownLegacyGuestsKey = _shownPop = _shownHeat = -1;
        _themeShown = false;
        _hasPlayerHouse = false;
        _shownHouseCount = -1;
        _shownBrotherState = -1;
        if (_newsOpacityShown != null) for (int i = 0; i < _newsOpacityShown.Length; i++) _newsOpacityShown[i] = -1f;
        _dirty = DirtyAll;
    }

    void SetupStatSliders()
    {
        SetupSlider(_popularitySlider);
        SetupSlider(_troubleMakerSlider);
        SetupSlider(_cashSlider);
    }

    static void SetupSlider(Slider slider)
    {
        if (slider == null) return;
        slider.lowValue = 0f;
        slider.highValue = 100f;
    }

    // =====================================================================
    // Campaign binding
    // =====================================================================

    void PollCampaign()
    {
        NetworkedCampaign current = NetworkedCampaign.Instance;
        if (current == null || !current.IsSpawned) current = null;
        if (ReferenceEquals(current, _campaign)) return;
        UnbindCampaign();
        if (current != null) BindCampaign(current);
    }

    void BindCampaign(NetworkedCampaign campaign)
    {
        _campaign = campaign;
        campaign.OnHudChanged += HandleHudChanged;
        campaign.OnHousesChanged += HandleHousesChanged;
        campaign.OnBrothersChanged += HandleBrothersChanged;
        campaign.OnStashChanged += HandleStashChanged;
        campaign.OnIncidentsChanged += HandleIncidentsChanged;
        campaign.OnNews += HandleNews;
        campaign.OnActionResult += HandleActionResult;

        // Backlog: RecentNews is oldest-first, the ring is newest-first.
        _newsCount = 0;
        var recent = campaign.RecentNews;
        int start = Mathf.Max(0, recent.Count - _newsText.Length);
        float now = Time.unscaledTime;
        for (int i = start; i < recent.Count; i++) InsertNews(recent[i], now);
        ApplyNewsLabels(now);
        _dirty = DirtyAll;
        _hasCampaignShown = false;
    }

    void UnbindCampaign()
    {
        if (ReferenceEquals(_campaign, null)) return;
        // The managed object stays valid after Unity destroys it, so unsubscribing is always safe.
        _campaign.OnHudChanged -= HandleHudChanged;
        _campaign.OnHousesChanged -= HandleHousesChanged;
        _campaign.OnBrothersChanged -= HandleBrothersChanged;
        _campaign.OnStashChanged -= HandleStashChanged;
        _campaign.OnIncidentsChanged -= HandleIncidentsChanged;
        _campaign.OnNews -= HandleNews;
        _campaign.OnActionResult -= HandleActionResult;
        _campaign = null;
        _hasCampaignShown = false;
    }

    void HandleHudChanged(CampaignHudState _) => _dirty |= DirtyHud;
    void HandleHousesChanged() => _dirty |= DirtyHouses;
    void HandleBrothersChanged() => _dirty |= DirtyBrothers;
    void HandleStashChanged() => _dirty |= DirtyStash;
    void HandleIncidentsChanged() => _dirty |= DirtyIncidents;

    void HandleNews(NewsNet item)
    {
        float now = Time.unscaledTime;
        InsertNews(item, now);
        ApplyNewsLabels(now);
    }

    void HandleActionResult(ActionResultNet result)
    {
        if (_toast == null) return;
        string message = result.Message.ToString();
        if (string.IsNullOrEmpty(message)) message = result.Code.ToString();
        _toast.text = message;
        _toast.EnableInClassList("hud-toast--ok", result.Ok);
        _toast.EnableInClassList("hud-toast--fail", !result.Ok);
        SetDisplay(_toast, true);
        _toastVisible = true;
        _toastHideAt = Time.unscaledTime + toastSeconds;
    }

    void ApplyCampaignVisibility(bool active)
    {
        _campaignActiveShown = active;
        _hasCampaignShown = true;
        SetDisplay(_phaseBanner, active);
        SetDisplay(_houseStats, active);
        SetDisplay(_standings, active);
        SetDisplay(_playerStatus, active);
        SetDisplay(_newsFeed, active);
        if (!active)
        {
            SetDisplay(_partyPanel, false);
            _shownPartyActive = -1;
            _legacyAttempts = 0;
            RefreshLegacyAll();
        }
        else
        {
            _dirty = DirtyAll;
        }
    }

    void FlushDirty()
    {
        int d = _dirty;
        _dirty = 0;
        if ((d & DirtyHud) != 0) RefreshHud();
        if ((d & DirtyHouses) != 0) RefreshHouses();
        if ((d & (DirtyBrothers | DirtyHud)) != 0) RefreshBrother();
        if ((d & DirtyStash) != 0) RefreshStash();
        if ((d & DirtyIncidents) != 0) RefreshIncidents();
    }

    // =====================================================================
    // Per-frame: clock + phase countdown (cached strings only)
    // =====================================================================

    void TickClock()
    {
        var h = _campaign.Hud;
        bool clockPhase = h.Phase == (byte)Phase.Weekday || h.Phase == (byte)Phase.Party;
        if (clockPhase)
        {
            int minute = (int)(_campaign.CurrentClockHour * 60f);
            if (minute != _shownMinute)
            {
                _shownMinute = minute;
                string clock = ClockText(minute);
                if (_phaseClock != null) _phaseClock.text = clock;
                if (_timeLabel != null) _timeLabel.text = clock;
            }
        }
        else if (_shownMinute != -1)
        {
            _shownMinute = -1;
            if (_timeLabel != null) _timeLabel.text = HudText.NoValue;
        }

        if (h.Phase != (byte)Phase.SemesterEnd)
        {
            var nm = NetworkManager.Singleton;
            double elapsed = nm != null ? nm.ServerTime.Time - h.AnchorServerTime : 0.0;
            int remaining = Mathf.CeilToInt(Mathf.Max(0f, h.PhaseRemaining - (float)elapsed));
            if (remaining != _shownRemaining)
            {
                _shownRemaining = remaining;
                if (_phaseTimeLeft != null) _phaseTimeLeft.text = DurationText(remaining);
            }
        }
    }

    // =====================================================================
    // Slow tick (250 ms): countdowns, toast, news fade, legacy fallback
    // =====================================================================

    void SlowTick()
    {
        float now = Time.unscaledTime;
        if (_toastVisible && now >= _toastHideAt)
        {
            _toastVisible = false;
            SetDisplay(_toast, false);
        }

        UpdateNewsOpacity(now);

        if (_detainedUntil > 0f)
        {
            int secs = Mathf.CeilToInt(_detainedUntil - now);
            if (secs <= 0) { _detainedUntil = 0f; secs = 0; SetDisplay(_detainedRow, false); }
            if (secs != _shownDetained)
            {
                _shownDetained = secs;
                if (_detainedValue != null) _detainedValue.text = DurationText(secs);
            }
        }

        if (!_campaignActiveShown)
        {
            if ((_legacyGm == null || _legacyParty == null) && now >= _nextLegacyBindAt)
            {
                _nextLegacyBindAt = now + 1f;
                TryBindLegacy();
            }
            LegacyTick();
        }
    }

    // =====================================================================
    // HUD state → banner, police, party, legacy rows
    // =====================================================================

    void RefreshHud()
    {
        var h = _campaign.Hud;

        if (h.Phase != _shownPhase)
        {
            _shownPhase = h.Phase;
            _shownMinute = int.MinValue;
            _shownRemaining = int.MinValue;
            var phase = (Phase)h.Phase;
            if (_phaseName != null) _phaseName.text = HudText.PhaseName(phase);
            if (_phaseTimeCaption != null) _phaseTimeCaption.text = HudText.PhaseTimeCaption(phase);
            if (_phaseBanner != null)
            {
                _phaseBanner.EnableInClassList("hud-phase--weekday", phase == Phase.Weekday);
                _phaseBanner.EnableInClassList("hud-phase--party", phase == Phase.Party);
                _phaseBanner.EnableInClassList("hud-phase--debrief", phase == Phase.Debrief);
                _phaseBanner.EnableInClassList("hud-phase--semester-end", phase == Phase.SemesterEnd);
            }
            SetDisplay(_phaseClock, phase == Phase.Weekday || phase == Phase.Party);
            SetDisplay(_phaseDay, phase != Phase.SemesterEnd);
            SetDisplay(_phaseSubRow, phase != Phase.SemesterEnd);
            _shownWeekKey = -1;
        }

        int weekKey = h.Phase == (byte)Phase.SemesterEnd ? 100000 + h.Semester : h.Week * 1000 + h.SemesterWeeks;
        if (weekKey != _shownWeekKey)
        {
            _shownWeekKey = weekKey;
            if (_phaseWeek != null)
                _phaseWeek.text = h.Phase == (byte)Phase.SemesterEnd
                    ? HudText.Semester + IntText(h.Semester)
                    : HudText.Week + IntText(h.Week + 1) + "/" + IntText(h.SemesterWeeks);
        }

        if (h.Day != _shownDay)
        {
            _shownDay = h.Day;
            if (_phaseDay != null) _phaseDay.text = s_dayShort[Mathf.Clamp(h.Day, 0, 6)];
        }

        int absDay = h.Week * 7 + h.Day + 1;
        if (absDay != _shownAbsDay)
        {
            _shownAbsDay = absDay;
            if (_dayLabel != null) _dayLabel.text = IntText(absDay);
        }

        RefreshPolice(h);
        RefreshParty(h);
        RefreshReady(h);
    }

    void RefreshPolice(in CampaignHudState h)
    {
        int level = Mathf.Clamp(h.PoliceAlert, 0, 3);
        int key = level * 2 + (h.Raided ? 1 : 0);
        if (key == _shownAlertKey) return;
        _shownAlertKey = key;
        if (_policeAlertText != null) _policeAlertText.text = HudText.PoliceAlert(level, h.Raided);
        if (_policeAlert != null)
            for (int i = 0; i <= 3; i++) _policeAlert.EnableInClassList(HudText.PoliceClass[i], i == level);
    }

    void RefreshParty(in CampaignHudState h)
    {
        int partyActive = h.PartyActive ? 1 : 0;
        if (partyActive != _shownPartyActive)
        {
            _shownPartyActive = partyActive;
            SetDisplay(_partyPanel, h.PartyActive);
        }

        int guestsKey = h.PartyActive ? h.Guests * 10000 + h.Capacity : -2;
        if (guestsKey != _shownGuestsKey)
        {
            _shownGuestsKey = guestsKey;
            string guests = h.PartyActive ? IntText(h.Guests) + "/" + IntText(h.Capacity) : IntText(0);
            if (_currentGuestsLabel != null) _currentGuestsLabel.text = guests;
            if (_partyGuests != null) _partyGuests.text = guests + HudText.GuestsSuffix;
        }

        if (!h.PartyActive) return;

        _funMeter.Set(h.Fun, 0);
        _hypeMeter.Set(h.Hype, 0);
        _noiseMeter.Set(h.Noise, 0);
        _troubleMeter.Set(h.Trouble, 0);
        int attentionLevel = h.Attention >= attentionDanger ? 2 : h.Attention >= attentionWarning ? 1 : 0;
        _attentionMeter.Set(h.Attention, attentionLevel);
        if (attentionLevel != _shownAttentionLevel)
        {
            _shownAttentionLevel = attentionLevel;
            if (_partyAttentionState != null)
            {
                _partyAttentionState.text = HudText.AttentionState[attentionLevel];
                _partyAttentionState.EnableInClassList("hud-text--warn", attentionLevel == 1);
                _partyAttentionState.EnableInClassList("hud-text--danger", attentionLevel == 2);
            }
        }

        if (h.UnderageDrinkers != _shownUnderage)
        {
            _shownUnderage = h.UnderageDrinkers;
            if (_partyUnderage != null)
            {
                _partyUnderage.text = HudText.Underage + IntText(h.UnderageDrinkers);
                _partyUnderage.EnableInClassList("hud-chip--danger", h.UnderageDrinkers > 0);
            }
        }

        int card = h.CardCheck ? 1 : 0;
        if (card != _shownCardCheck)
        {
            _shownCardCheck = card;
            if (_partyCardCheck != null)
            {
                _partyCardCheck.text = h.CardCheck ? HudText.CardCheckOn : HudText.CardCheckOff;
                _partyCardCheck.EnableInClassList("hud-chip--good", h.CardCheck);
                _partyCardCheck.EnableInClassList("hud-chip--warn", !h.CardCheck);
            }
        }

        if (!_themeShown || !h.Theme.Equals(_shownTheme))
        {
            _themeShown = true;
            _shownTheme = h.Theme;
            if (_partyTheme != null) _partyTheme.text = HudText.Theme + (h.Theme.Length > 0 ? h.Theme.ToString() : HudText.NoTheme);
        }

        if (h.Cover != _shownCover)
        {
            _shownCover = h.Cover;
            if (_partyCover != null) _partyCover.text = HudText.Cover + MoneyText(h.Cover);
        }

        if (h.CashCollected != _shownPartyCash)
        {
            _shownPartyCash = h.CashCollected;
            if (_partyCash != null) _partyCash.text = HudText.DoorCash + MoneyText(h.CashCollected);
        }
    }

    void RefreshReady(in CampaignHudState h)
    {
        bool weekday = h.Phase == (byte)Phase.Weekday;
        int key = weekday ? 1 + h.ReadyCount * 1000 + h.PlayerCount * 2 + (_localReady ? 1 : 0) : 0;
        if (key == _shownReadyKey) return;
        _shownReadyKey = key;
        SetDisplay(_readyStatus, weekday);
        if (!weekday || _readyStatus == null) return;
        _readyStatus.text = HudText.Ready + IntText(h.ReadyCount) + "/" + IntText(Mathf.Max(1, (int)h.PlayerCount)) +
                            (_localReady ? HudText.YouReady : HudText.YouNotReady);
        _readyStatus.EnableInClassList("hud-text--good", _localReady);
    }

    // =====================================================================
    // Houses → house panel, standings, legacy sliders
    // =====================================================================

    void RefreshHouses()
    {
        var houses = _campaign.Houses;
        _sortedHouses.Clear();
        for (int i = 0; i < houses.Count; i++) _sortedHouses.Add(houses[i]);
        _sortedHouses.Sort(s_byRank);

        for (int i = 0; i < _sortedHouses.Count; i++)
        {
            if (!_sortedHouses[i].IsPlayer) continue;
            ApplyPlayerHouse(_sortedHouses[i], _sortedHouses.Count);
            break;
        }

        if (_standingsRows == null) return;
        while (_standingRowPool.Count < _sortedHouses.Count)
        {
            var row = new StandingRow();
            _standingsRows.Add(row.Root);
            _standingRowPool.Add(row);
        }
        for (int i = 0; i < _standingRowPool.Count; i++)
        {
            bool used = i < _sortedHouses.Count;
            SetDisplay(_standingRowPool[i].Root, used);
            if (used) _standingRowPool[i].Apply(_sortedHouses[i]);
        }
    }

    void ApplyPlayerHouse(in HouseNetState h, int houseCount)
    {
        if (_hasPlayerHouse && houseCount == _shownHouseCount && h.Equals(_shownPlayerHouse)) return;
        _hasPlayerHouse = true;
        _shownHouseCount = houseCount;
        _shownPlayerHouse = h;

        string name = h.Name.ToString();
        if (_fratNameLabel != null) _fratNameLabel.text = string.IsNullOrWhiteSpace(name) ? HudText.Fraternity : name;
        string grade = h.Grade.Length > 0 ? h.Grade.ToString() : HudText.NoValue;
        if (_fratGradeLabel != null)
        {
            _fratGradeLabel.text = grade;
            ApplyGradeClass(_fratGradeLabel, grade);
        }
        if (_fratRatingLabel != null) _fratRatingLabel.text = HudText.Rating + IntText(h.Rating);
        if (_houseRank != null)
        {
            string rank = h.Rank > 0 ? "#" + IntText(h.Rank) + HudText.Of + IntText(houseCount) : HudText.NoValue;
            if (h.LastRank > 0 && h.Rank > 0 && h.LastRank != h.Rank) rank += HudText.WasRank + IntText(h.LastRank) + ")";
            _houseRank.text = rank;
        }
        if (_housePopularity != null) _housePopularity.text = IntText(Mathf.RoundToInt(h.Popularity));
        if (_houseCash != null) _houseCash.text = MoneyText(h.Cash);
        if (_houseGpa != null) _houseGpa.text = h.Gpa.ToString("0.00", CultureInfo.InvariantCulture);
        if (_houseMembers != null) _houseMembers.text = IntText(h.Members);
        if (_houseCupPoints != null) _houseCupPoints.text = IntText(h.CupPoints);
        _heatMeter.Set(h.Heat, h.Heat >= heatDanger ? 2 : h.Heat >= heatWarning ? 1 : 0);
        _deanMeter.Set(h.Dean, h.Dean < deanDanger ? 2 : 0);
        SetDisplay(_flagProbation, h.Probation);
        SetDisplay(_flagSuspended, h.Suspended);
        SetDisplay(_flagTrashed, h.Trashed);
        SetDisplay(_flagGuarded, h.Guarded);

        ApplyPopularity(h.Popularity);
        ApplyHeatSlider(h.Heat);
        ApplyCash(h.Cash);
    }

    static void ApplyGradeClass(VisualElement badge, string grade)
    {
        char g = grade.Length > 0 ? char.ToUpperInvariant(grade[0]) : ' ';
        badge.EnableInClassList("frat-grade-badge--a", g == 'A');
        badge.EnableInClassList("frat-grade-badge--b", g == 'B');
        badge.EnableInClassList("frat-grade-badge--c", g == 'C');
        badge.EnableInClassList("frat-grade-badge--d", g == 'D');
        badge.EnableInClassList("frat-grade-badge--f", g == 'F' || g == 'E');
    }

    // =====================================================================
    // Brothers → local player line, detained countdown, ready flag
    // =====================================================================

    void RefreshBrother()
    {
        var brothers = _campaign.Brothers;
        var nm = NetworkManager.Singleton;
        int best = -1;
        if (nm != null)
        {
            ulong local = nm.LocalClientId;
            for (int i = 0; i < brothers.Count; i++)
            {
                var b = brothers[i];
                if (b.ClientId != local) continue;
                // Unbound/disconnected rows replicate ClientId 0 (same as the host); prefer the connected one.
                if (b.Connected) { best = i; break; }
                if (best < 0) best = i;
            }
        }

        if (best < 0)
        {
            if (_shownBrotherState != 0)
            {
                _shownBrotherState = 0;
                _localReady = false;
                if (_brotherLine != null) _brotherLine.text = HudText.JoiningHouse;
                if (_brotherRecord != null) _brotherRecord.text = string.Empty;
                _detainedUntil = 0f;
                SetDisplay(_detainedRow, false);
            }
            RefreshReady(_campaign.Hud);
            return;
        }

        var me = brothers[best];
        if (_shownBrotherState == 1 && me.Equals(_shownBrother))
        {
            RefreshReady(_campaign.Hud);
            return;
        }
        _shownBrotherState = 1;
        _shownBrother = me;
        _localReady = me.Ready;
        SetDrunkLevel(me.Drunk, me.DrunkTier);

        if (_brotherLine != null)
            _brotherLine.text = me.Name.ToString() + HudText.Sep + HudText.RoleName(me.Role.ToString()) + HudText.Sep + HudText.At +
                                HudText.LocationName(me.Location.ToString());
        if (_brotherRecord != null)
            _brotherRecord.text = HudText.Rep + IntText(Mathf.RoundToInt(me.Rep)) + HudText.Sep + HudText.Record + IntText(me.Record) +
                                  HudText.Sep + HudText.Studied + IntText(me.StudyThisWeek);

        if (me.DetainedRemaining > 0f)
        {
            _detainedUntil = Time.unscaledTime + me.DetainedRemaining;
            _shownDetained = int.MinValue;
            SetDisplay(_detainedRow, true);
        }
        else
        {
            _detainedUntil = 0f;
            SetDisplay(_detainedRow, false);
        }
        RefreshReady(_campaign.Hud);
    }

    // =====================================================================
    // Stash / incidents
    // =====================================================================

    void RefreshStash()
    {
        if (_stashSummary == null) return;
        var stash = _campaign.Stash;
        _sb.Clear();
        for (int i = 0; i < stash.Count; i++)
        {
            var s = stash[i];
            if (s.Count <= 0) continue;
            if (_sb.Length > 0) _sb.Append(HudText.Sep);
            _sb.Append(HudText.ItemName(s.ItemId.ToString())).Append(" x").Append(s.Count);
        }
        _stashSummary.text = _sb.Length > 0 ? _sb.ToString() : HudText.StashEmpty;
    }

    void RefreshIncidents()
    {
        var incidents = _campaign.Incidents;
        int count = incidents.Count;
        if (_partyIncidentsHeader != null) _partyIncidentsHeader.text = HudText.IncidentsHeader + IntText(count) + ")";
        SetDisplay(_partyIncidentsEmpty, count == 0);
        for (int i = 0; i < _incidentLabels.Count; i++)
        {
            var label = _incidentLabels[i];
            bool overflow = i == _incidentLabels.Count - 1 && count > _incidentLabels.Count;
            bool used = i < count;
            SetDisplay(label, used);
            if (!used) continue;
            if (overflow)
            {
                label.text = "+" + IntText(count - i) + HudText.More;
                continue;
            }
            var inc = incidents[i];
            label.text = HudText.IncidentName(inc.Kind.ToString()) + " #" + IntText(inc.Id);
        }
    }

    // =====================================================================
    // News feed / toast
    // =====================================================================

    void AllocateNewsRing()
    {
        int n = Mathf.Clamp(newsLines, 1, 12);
        _newsText = new string[n];
        _newsTime = new float[n];
        _newsAlert = new bool[n];
        _newsOpacityShown = new float[n];
        _newsCount = 0;
    }

    void InsertNews(in NewsNet item, float now)
    {
        int n = _newsText.Length;
        for (int i = Mathf.Min(_newsCount, n - 1); i > 0; i--)
        {
            _newsText[i] = _newsText[i - 1];
            _newsTime[i] = _newsTime[i - 1];
            _newsAlert[i] = _newsAlert[i - 1];
        }
        _newsText[0] = item.Text.ToString();
        _newsTime[0] = now;
        _newsAlert[0] = HudText.IsAlertNews(item.Kind.ToString());
        _newsCount = Mathf.Min(_newsCount + 1, n);
    }

    void ApplyNewsLabels(float now)
    {
        for (int i = 0; i < _newsLabels.Count; i++)
        {
            var label = _newsLabels[i];
            bool used = i < _newsCount;
            SetDisplay(label, used);
            if (!used) continue;
            label.text = _newsText[i];
            label.EnableInClassList("hud-news-item--alert", _newsAlert[i]);
            _newsOpacityShown[i] = -1f;
        }
        UpdateNewsOpacity(now);
    }

    void UpdateNewsOpacity(float now)
    {
        int n = Mathf.Min(_newsLabels.Count, _newsCount);
        for (int i = 0; i < n; i++)
        {
            // Older lines sit lower and dimmer; anything past newsFadeSeconds dims further.
            float opacity = 1f - 0.12f * i;
            if (now - _newsTime[i] > newsFadeSeconds) opacity *= 0.45f;
            if (Mathf.Approximately(opacity, _newsOpacityShown[i])) continue;
            _newsOpacityShown[i] = opacity;
            _newsLabels[i].style.opacity = opacity;
        }
    }

    // =====================================================================
    // Legacy fallback (no campaign running)
    // =====================================================================

    void TryBindLegacy()
    {
        if (_legacyAttempts > 60) return; // ~60 s of 1 Hz retries, then stay quiet
        _legacyAttempts++;

        if (_legacyGm == null)
        {
            // FindFirstObjectByType directly: NetworkedGameManager.Instance fabricates an object when none exists.
            var gm = FindFirstObjectByType<NetworkedGameManager>();
            if (gm != null)
            {
                _legacyGm = gm;
                gm.OnPopularityChanged += LegacyPopularityChanged;
                gm.OnTroubleChanged += LegacyTroubleChanged;
                gm.OnCashValueChanged += LegacyCashChanged;
                gm.OnFraternityChanged += LegacyFraternityChanged;
            }
        }

        if (_legacyParty == null)
        {
            var pm = FindFirstObjectByType<NetworkedPartyManager>();
            var party = pm != null ? pm.NetworkedParty : null;
            if (party != null)
            {
                _legacyParty = party;
                party.OnDayNumberChangedEvent += LegacyDayChanged;
            }
        }

        RefreshLegacyAll();
    }

    void UnbindLegacy()
    {
        if (!ReferenceEquals(_legacyGm, null))
        {
            _legacyGm.OnPopularityChanged -= LegacyPopularityChanged;
            _legacyGm.OnTroubleChanged -= LegacyTroubleChanged;
            _legacyGm.OnCashValueChanged -= LegacyCashChanged;
            _legacyGm.OnFraternityChanged -= LegacyFraternityChanged;
            _legacyGm = null;
        }
        if (!ReferenceEquals(_legacyParty, null))
        {
            _legacyParty.OnDayNumberChangedEvent -= LegacyDayChanged;
            _legacyParty = null;
        }
        _legacyAttempts = 0;
    }

    /// <summary>Reads current legacy values (fixes sliders/labels that previously waited for the first change event).</summary>
    void RefreshLegacyAll()
    {
        if (_campaignActiveShown) return;
        if (_legacyGm != null)
        {
            ApplyPopularity(_legacyGm.TotalPopularity);
            ApplyHeatSlider(_legacyGm.TotalTroubleMaking);
            ApplyCash(_legacyGm.TotalCashValue);
            ApplyFraternityPanel(_legacyGm.IsSpawned ? _legacyGm.myFraternity.Value : _legacyGm.fraternityLocal);
        }
        if (_legacyParty != null) LegacyDayChanged(_legacyParty.DayNumber);
        LegacyTick();
    }

    void LegacyTick()
    {
        var party = _legacyParty;
        if (party == null || !party.IsSpawned) return;
        bool active = party.State == PartyState.Active;
        int guestsKey = party.currentGuests.Count * 10000 + party.GuestLimit;
        if (guestsKey != _shownLegacyGuestsKey)
        {
            _shownLegacyGuestsKey = guestsKey;
            if (_currentGuestsLabel != null) _currentGuestsLabel.text = IntText(party.currentGuests.Count) + "/" + IntText(party.GuestLimit);
        }
        int minute = active ? (int)(party.GetCurrentInGameTime() * 60f) : -1;
        if (minute != _shownLegacyMinute)
        {
            _shownLegacyMinute = minute;
            if (_timeLabel != null) _timeLabel.text = active ? ClockText(minute) : HudText.NoValue;
        }
    }

    void LegacyPopularityChanged(float value) { if (!_campaignActiveShown) ApplyPopularity(value); }
    void LegacyTroubleChanged(float value) { if (!_campaignActiveShown) ApplyHeatSlider(value); }
    void LegacyCashChanged(float value) { if (!_campaignActiveShown) ApplyCash(value); }
    void LegacyFraternityChanged(FraternityData data) { if (!_campaignActiveShown) ApplyFraternityPanel(data); }

    void LegacyDayChanged(int value)
    {
        if (_campaignActiveShown || _dayLabel == null) return;
        _dayLabel.text = IntText(value);
    }

    void ApplyFraternityPanel(FraternityData data)
    {
        // Default struct is Grade A + rating 0: show the starting C/50 until the server seeds it.
        if (FraternityGradeCalculator.IsUninitialized(data))
            data = FraternityGradeCalculator.CreateStarting(string.IsNullOrWhiteSpace(data.fratName) ? HudText.Fraternity : data.fratName);
        if (_fratNameLabel != null) _fratNameLabel.text = string.IsNullOrWhiteSpace(data.fratName) ? HudText.Fraternity : data.fratName;
        if (_fratGradeLabel != null)
        {
            string letter = FraternityGradeCalculator.ToLetter(data.grade);
            _fratGradeLabel.text = letter;
            ApplyGradeClass(_fratGradeLabel, letter);
        }
        if (_fratRatingLabel != null) _fratRatingLabel.text = HudText.Rating + IntText(data.rating);
    }

    // =====================================================================
    // Shared slider/label writers
    // =====================================================================

    void ApplyPopularity(float value)
    {
        int shown = Mathf.RoundToInt(Mathf.Clamp(value, 0f, 100f) * 10f);
        if (shown == _shownPop) return;
        _shownPop = shown;
        SetSlider(_popularitySlider, value);
    }

    void ApplyHeatSlider(float value)
    {
        int shown = Mathf.RoundToInt(Mathf.Clamp(value, 0f, 100f) * 10f);
        if (shown == _shownHeat) return;
        _shownHeat = shown;
        SetSlider(_troubleMakerSlider, value);
    }

    void ApplyCash(float cash)
    {
        int whole = Mathf.RoundToInt(cash);
        if (whole == _shownCash) return;
        _shownCash = whole;
        float normalized = cashSliderFullAt > 0f ? cash / cashSliderFullAt * 100f : 0f;
        SetSlider(_cashSlider, normalized);
        if (_cashValueLabel != null) _cashValueLabel.text = HudText.CashPrefix + MoneyText(whole);
    }

    /// <summary>The existing sliders are styled inverted; keep the original value inversion so the fill reads correctly.</summary>
    static void SetSlider(Slider slider, float actual)
    {
        if (slider == null) return;
        float clamped = Mathf.Clamp(actual, slider.lowValue, slider.highValue);
        slider.SetValueWithoutNotify(slider.highValue + slider.lowValue - clamped);
    }

    /// <summary>
    /// Show the local player's intoxication (0..1 of the drunk cap) and visible tier (0 sober .. 3 wasted). Fed from the
    /// replicated <see cref="BrotherNetState"/> on every brother refresh (design/gdd/drunk-mechanic.md).
    /// Example: <c>hud.SetDrunkLevel(0.35f, 2);</c>
    /// </summary>
    public void SetDrunkLevel(float normalized, byte tier = 0)
    {
        if (_drunkLabel == null) return;
        int pct = Mathf.RoundToInt(Mathf.Clamp01(normalized) * 100f);
        _drunkLabel.text = IntText(pct) + "%" + HudText.DrunkTier(tier);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    static void SetPickingIgnore(VisualElement element) => element.pickingMode = PickingMode.Ignore;

    static Label NewLabel(string className)
    {
        var label = new Label { pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        return label;
    }

    static void SetDisplay(VisualElement element, bool visible)
    {
        if (element == null) return;
        var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (element.style.display.value != display || element.style.display.keyword != StyleKeyword.Undefined)
            element.style.display = display;
    }

    static string IntText(int value)
    {
        if (value >= 0 && value < s_ints.Length) return s_ints[value] ??= value.ToString(CultureInfo.InvariantCulture);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    static string MoneyText(int value) => "$" + value.ToString("N0", CultureInfo.InvariantCulture);

    static string ClockText(int minuteOfDay)
    {
        int m = ((minuteOfDay % 1440) + 1440) % 1440;
        return s_clock[m] ??= (m / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (m % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    static string DurationText(int seconds)
    {
        if (seconds < 0) seconds = 0;
        if (seconds < s_duration.Length)
            return s_duration[seconds] ??= (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        return (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Track + fill meter (0..100) with an optional numeric label and warn/danger colour states.</summary>
    sealed class Meter
    {
        readonly VisualElement _fill;
        readonly Label _value;
        int _shown = -1, _level = -1;

        public Meter(VisualElement fill, Label value)
        {
            _fill = fill;
            _value = value;
        }

        /// <summary>level: 0 normal, 1 warning, 2 danger.</summary>
        public void Set(float value, int level)
        {
            int rounded = Mathf.RoundToInt(Mathf.Clamp(value, 0f, 100f));
            if (rounded != _shown)
            {
                _shown = rounded;
                if (_fill != null) _fill.style.width = Length.Percent(rounded);
                if (_value != null) _value.text = IntText(rounded);
            }
            if (level == _level) return;
            _level = level;
            if (_fill != null)
            {
                _fill.EnableInClassList("hud-meter__fill--warn", level == 1);
                _fill.EnableInClassList("hud-meter__fill--danger", level == 2);
            }
            if (_value != null)
            {
                _value.EnableInClassList("hud-text--warn", level == 1);
                _value.EnableInClassList("hud-text--danger", level == 2);
            }
        }
    }

    /// <summary>One Greek Cup standings row (pooled; rebuilt only when that house's state changes).</summary>
    sealed class StandingRow
    {
        public readonly VisualElement Root;
        readonly Label _rank, _name, _cup, _pop;
        HouseNetState _shown;
        bool _has;

        public StandingRow()
        {
            Root = new VisualElement { pickingMode = PickingMode.Ignore };
            Root.AddToClassList("hud-standings-row");
            _rank = NewLabel("hud-standings-rank");
            _name = NewLabel("hud-standings-name");
            _cup = NewLabel("hud-standings-cup");
            _pop = NewLabel("hud-standings-pop");
            Root.Add(_rank);
            Root.Add(_name);
            Root.Add(_cup);
            Root.Add(_pop);
        }

        public void Apply(in HouseNetState h)
        {
            if (_has && h.Equals(_shown)) return;
            _has = true;
            _shown = h;
            _rank.text = h.Rank > 0 ? IntText(h.Rank) : HudText.NoValue;
            string name = h.Name.ToString();
            if (h.Suspended) name += HudText.SuspendedTag;
            else if (h.Probation) name += HudText.ProbationTag;
            _name.text = name;
            _cup.text = IntText(h.CupPoints);
            _pop.text = IntText(Mathf.RoundToInt(h.Popularity));
            Root.EnableInClassList("hud-standings-row--player", h.IsPlayer);
        }
    }

    sealed class HouseRankComparer : IComparer<HouseNetState>
    {
        public int Compare(HouseNetState a, HouseNetState b)
        {
            int ra = a.Rank > 0 ? a.Rank : int.MaxValue;
            int rb = b.Rank > 0 ? b.Rank : int.MaxValue;
            if (ra != rb) return ra.CompareTo(rb);
            return b.CupPoints.CompareTo(a.CupPoints);
        }
    }

    /// <summary>
    /// Every user-facing HUD string in one place. The project has no localization tables yet; when it adopts one, swap these
    /// for table lookups (action results already carry a stable <c>Code</c> usable as a key).
    /// </summary>
    static class HudText
    {
        public const string NoValue = "-";
        public const string Sep = " · ";
        public const string Fraternity = "Fraternity";
        public const string Week = "Week ";
        public const string Semester = "Semester ";
        public const string Rating = "Rating ";
        public const string Of = " of ";
        public const string WasRank = " (was #";
        public const string CashPrefix = "Cash: ";
        public const string GuestsSuffix = " guests";
        public const string Underage = "Underage drinking: ";
        public const string CardCheckOn = "Card check: ON";
        public const string CardCheckOff = "Card check: OFF";
        public const string Theme = "Theme: ";
        public const string NoTheme = "none";
        public const string Cover = "Cover: ";
        public const string DoorCash = "Door cash: ";
        public const string IncidentsHeader = "INCIDENTS (";
        public const string More = " more";
        public const string Ready = "Ready ";
        public const string YouReady = " · you're ready";
        public const string YouNotReady = " · you're not ready";
        public const string JoiningHouse = "Joining the house...";
        public const string At = "@ ";
        public const string Rep = "Rep ";
        public const string Record = "Record ";
        public const string Studied = "Studied ";
        public const string StashEmpty = "Empty. Hit the Campus Store.";
        public static string DrunkTier(byte tier) => tier == 1 ? " · tipsy" : tier == 2 ? " · drunk" : tier >= 3 ? " · WASTED" : string.Empty;
        public const string SuspendedTag = " (suspended)";
        public const string ProbationTag = " (probation)";

        public static readonly string[] AttentionState = { "calm", "cops dispatched", "raid risk" };
        public static readonly string[] PoliceClass = { "hud-police--0", "hud-police--1", "hud-police--2", "hud-police--3" };

        public static string PhaseName(Phase phase)
        {
            switch (phase)
            {
                case Phase.Weekday: return "Weekday";
                case Phase.Party: return "PARTY NIGHT";
                case Phase.Debrief: return "Debrief";
                case Phase.SemesterEnd: return "Semester over";
                default: return NoValue;
            }
        }

        public static string PhaseTimeCaption(Phase phase)
        {
            switch (phase)
            {
                case Phase.Weekday: return "Day ends in";
                case Phase.Party: return "Party ends in";
                case Phase.Debrief: return "Next week in";
                default: return string.Empty;
            }
        }

        public static string PoliceAlert(int level, bool raided)
        {
            switch (level)
            {
                case 1: return "Patrol nearby";
                case 2: return "Cops dispatched to your party";
                case 3: return raided ? "RAID! The cops shut it down" : "Cops parked outside";
                default: return "All quiet";
            }
        }

        public static string RoleName(string role)
        {
            switch (role)
            {
                case "president": return "President";
                case "social_chair": return "Social Chair";
                case "rush_chair": return "Rush Chair";
                case "treasurer": return "Treasurer";
                case "risk_manager": return "Risk Manager";
                case "brother": return "Brother";
                default: return string.IsNullOrEmpty(role) ? "Brother" : role;
            }
        }

        public static string LocationName(string location)
        {
            switch (location)
            {
                case "": return "around campus";
                case "PartyHouse": return "the house";
                case "House_Kappa": return "Kappa House";
                case "House_Theta": return "Theta House";
                case "House_Lambda": return "Lambda House";
                case "Shop": return "Campus Store";
                case "Library": return "Library";
                case "Gym": return "Gym";
                case "Quad": return "The Quad";
                case "Dorm": return "Freshman Dorms";
                default: return location;
            }
        }

        public static string ItemName(string itemId)
        {
            switch (itemId)
            {
                case "keg": return "Keg";
                case "liquor": return "Liquor";
                case "soda_snacks": return "Soda & Snacks";
                case "speakers": return "Speakers";
                case "decorations": return "Decorations";
                case "security": return "Security";
                case "prank_kit": return "Prank Kit";
                case "stink_bomb": return "Stink Bomb";
                case "cleanup_crew": return "Cleanup Crew";
                default: return itemId;
            }
        }

        public static string IncidentName(string kind)
        {
            switch (kind)
            {
                case "fight": return "Fight";
                case "passed_out": return "Guest passed out";
                case "property_damage": return "Property damage";
                case "noise_complaint": return "Noise complaint";
                case "keg_dry": return "Keg ran dry";
                case "stink_bomb": return "Stink bomb";
                case "celebrity_dropin": return "Celebrity drop-in";
                default: return kind;
            }
        }

        public static bool IsAlertNews(string kind) =>
            kind == "busted" || kind == "raid" || kind == "police" || kind == "suspended" || kind == "rival_sabotage" || kind == "sabotage";
    }
}
