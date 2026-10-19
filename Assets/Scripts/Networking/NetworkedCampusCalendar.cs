using Unity.Netcode;
using UnityEngine;

namespace Systems.Campus
{
    /// <summary>
    /// Server-authoritative weekday/party phase and real-time campus clock.
    /// Mirrors party timing: ServerTime anchor + scaled in-game hours.
    /// </summary>
    public class NetworkedCampusCalendar : NetworkBehaviour
    {
        static NetworkedCampusCalendar _instance;
        public static NetworkedCampusCalendar Instance => _instance;

        [SerializeField] float weekdayDurationMinutes = 8f;
        [SerializeField] float inGameHoursPerRealMinute = 4f;
        [SerializeField] float weekdayInGameStartHour = 8f;

        readonly NetworkVariable<GamePhase> _phase = new NetworkVariable<GamePhase>(GamePhase.Weekday);
        readonly NetworkVariable<int> _dayOfWeek = new NetworkVariable<int>(0);
        readonly NetworkVariable<int> _weekIndex = new NetworkVariable<int>(0);
        readonly NetworkVariable<float> _weekdayStartServerTime = new NetworkVariable<float>(0f);
        // Campaign clock anchor: hour at anchor time + rate. Written by the host when the core's day/phase/rate changes.
        readonly NetworkVariable<bool> _campaignClock = new NetworkVariable<bool>(false);
        readonly NetworkVariable<float> _anchorHour = new NetworkVariable<float>(8f);
        readonly NetworkVariable<double> _anchorServerTime = new NetworkVariable<double>(0);
        readonly NetworkVariable<float> _hoursPerSecond = new NetworkVariable<float>(0f);

        public GamePhase Phase => _phase.Value;
        public int DayOfWeek => _dayOfWeek.Value;
        public int WeekIndex => _weekIndex.Value;
        public float WeekdayDurationMinutes => weekdayDurationMinutes;

        public event System.Action<GamePhase> OnPhaseChanged;
        public event System.Action<int> OnDayChanged;

        void Awake()
        {
            _instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _instance = this;
            _phase.OnValueChanged += HandlePhaseChanged;
            _dayOfWeek.OnValueChanged += HandleDayChanged;

            if (IsServer && Mathf.Approximately(_weekdayStartServerTime.Value, 0f))
            {
                BeginWeekdayServer();
            }
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= HandlePhaseChanged;
            _dayOfWeek.OnValueChanged -= HandleDayChanged;
            if (_instance == this)
            {
                _instance = null;
            }

            base.OnNetworkDespawn();
        }

        void HandlePhaseChanged(GamePhase prev, GamePhase next) => OnPhaseChanged?.Invoke(next);
        void HandleDayChanged(int prev, int next) => OnDayChanged?.Invoke(next);

        /// <summary>
        /// Host: mirror the campaign core's calendar. Re-anchors the clock only when day/phase/rate change or it drifts,
        /// so the NetworkVariables are not rewritten every frame.
        /// </summary>
        public void ApplyCampaignServer(GamePhase phase, int day, int week, float clockHour, float hoursPerSecond)
        {
            if (!IsServer)
            {
                return;
            }

            if (!_campaignClock.Value) _campaignClock.Value = true;
            if (_phase.Value != phase) _phase.Value = phase;
            if (_dayOfWeek.Value != day) _dayOfWeek.Value = day;
            if (_weekIndex.Value != week) _weekIndex.Value = week;
            double now = NetworkManager.ServerTime.Time;
            float predicted = _anchorHour.Value + (float)(now - _anchorServerTime.Value) * _hoursPerSecond.Value;
            if (!Mathf.Approximately(_hoursPerSecond.Value, hoursPerSecond) || Mathf.Abs(Mathf.Repeat(predicted, 24f) - clockHour) > 0.05f)
            {
                _anchorHour.Value = clockHour;
                _anchorServerTime.Value = now;
                _hoursPerSecond.Value = hoursPerSecond;
            }
        }

        public float GetCurrentInGameTime()
        {
            if (_campaignClock.Value)
            {
                float hour = _anchorHour.Value + (float)(NetworkManager.ServerTime.Time - _anchorServerTime.Value) * _hoursPerSecond.Value;
                return Mathf.Repeat(hour, 24f);
            }

            if (Phase != GamePhase.Weekday)
            {
                return weekdayInGameStartHour;
            }

            float elapsedReal = (float)NetworkManager.ServerTime.Time - _weekdayStartServerTime.Value;
            float hours = weekdayInGameStartHour + (elapsedReal / 60f) * inGameHoursPerRealMinute;
            while (hours >= 24f)
            {
                hours -= 24f;
            }

            return hours;
        }

        public void BeginWeekdayServer()
        {
            // The campaign core owns phase transitions when it is running.
            if (!IsServer || _campaignClock.Value)
            {
                return;
            }

            _phase.Value = GamePhase.Weekday;
            _weekdayStartServerTime.Value = (float)NetworkManager.ServerTime.Time;
            Debug.Log($"[CampusCalendar] Weekday started day={DayOfWeek} week={WeekIndex}");
        }

        public void RestoreSocialDayServer(int day)
        {
            if (!IsServer || _campaignClock.Value) return;
            _dayOfWeek.Value = day % 7;
            _weekIndex.Value = day / 7;
        }

        public void EnterPartyPhaseServer()
        {
            // The campaign core owns phase transitions when it is running.
            if (!IsServer || _campaignClock.Value)
            {
                return;
            }

            _phase.Value = GamePhase.Party;
            Debug.Log("[CampusCalendar] Entered Party phase");
        }

        public void EnterDebriefThenAdvanceServer()
        {
            // The campaign core owns phase transitions when it is running.
            if (!IsServer || _campaignClock.Value)
            {
                return;
            }

            _phase.Value = GamePhase.Debrief;
            AdvanceDayServer();
            BeginWeekdayServer();
        }

        void AdvanceDayServer()
        {
            int next = DayOfWeek + 1;
            if (next > 6)
            {
                next = 0;
                _weekIndex.Value = WeekIndex + 1;
            }

            _dayOfWeek.Value = next;
        }
    }
}
