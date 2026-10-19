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

        public float GetCurrentInGameTime()
        {
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
            if (!IsServer)
            {
                return;
            }

            _phase.Value = GamePhase.Weekday;
            _weekdayStartServerTime.Value = (float)NetworkManager.ServerTime.Time;
            Debug.Log($"[CampusCalendar] Weekday started day={DayOfWeek} week={WeekIndex}");
        }

        public void RestoreSocialDayServer(int day)
        {
            if (!IsServer) return;
            _dayOfWeek.Value = day % 7;
            _weekIndex.Value = day / 7;
        }

        public void EnterPartyPhaseServer()
        {
            if (!IsServer)
            {
                return;
            }

            _phase.Value = GamePhase.Party;
            Debug.Log("[CampusCalendar] Entered Party phase");
        }

        public void EnterDebriefThenAdvanceServer()
        {
            if (!IsServer)
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
