using System;
using System.Collections.Generic;
using Systems.Talk;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Systems.Campus
{
    public struct NamedNpcNetState : INetworkSerializable, IEquatable<NamedNpcNetState>
    {
        public FixedString64Bytes NpcId;
        public FixedString64Bytes LocationId;
        public bool Visible;
        public Vector3 Position;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NpcId);
            serializer.SerializeValue(ref LocationId);
            serializer.SerializeValue(ref Visible);
            serializer.SerializeValue(ref Position);
        }

        public bool Equals(NamedNpcNetState other) =>
            NpcId.Equals(other.NpcId)
            && LocationId.Equals(other.LocationId)
            && Visible == other.Visible
            && Position == other.Position;
    }

    /// <summary>
    /// Server-authoritative named-NPC placement; clients spawn local interactable visuals from replicated state.
    /// When the game loop is running (<see cref="NetworkedCampaign"/> host session), locations come from
    /// <see cref="NetworkedCampaign.NpcLocationServer"/> in every phase (weekday schedules, party-night rival houses);
    /// otherwise the legacy NamedNpcSO schedule + campus calendar path is used.
    /// Replication: reliable NetworkList, written only on change, polled every <c>pollSeconds</c> (and on phase change).
    /// </summary>
    public class NetworkedCampusNpcDirector : NetworkBehaviour
    {
        static NetworkedCampusNpcDirector _instance;
        public static NetworkedCampusNpcDirector Instance => _instance;

        [SerializeField] float pollSeconds = 2f;
        [SerializeField] GameObject visualPrefab;
        [Header("Crowding")]
        [Tooltip("NPCs at the same location stand on a ring around its marker; per-NPC radius is between min and max (metres).")]
        [SerializeField] float crowdRadiusMin = 1.5f;
        [SerializeField] float crowdRadiusMax = 3f;

        NetworkList<NamedNpcNetState> _states;
        readonly Dictionary<FixedString64Bytes, NamedNpcVisual> _visuals = new();
        /// <summary>Parallel to NetworkList indices on server — avoids FixedString.ToString() each poll.</summary>
        readonly List<NamedNpcSO> _npcDataByIndex = new();
        /// <summary>Parallel to <see cref="_npcDataByIndex"/>: deterministic per-NPC offset from the location marker.</summary>
        readonly List<Vector3> _offsetByIndex = new();

        float _timer;
        bool _campaignDriven;
        FratRow.Game.Phase _lastCampaignPhase;

        void Awake()
        {
            _instance = this;
            _states = new NetworkList<NamedNpcNetState>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _instance = this;
            _states.OnListChanged += OnStatesChanged;

            // Locations must exist before first schedule resolve (same fallback set as CampusSystemsBootstrap,
            // including the rival houses; adds only missing ids to an existing registry).
            CampusLocationFallbacks.Ensure();

            if (IsServer)
            {
                SeedStates();
                if (TryGetCampaign(out var campaign))
                {
                    ApplyCampaignLocationsServer(campaign);
                }
                else
                {
                    ApplySchedulesServer(force: true);
                }
            }

            RebuildAllVisuals();
        }

        public override void OnNetworkDespawn()
        {
            _states.OnListChanged -= OnStatesChanged;
            foreach (var v in _visuals.Values)
            {
                if (v != null)
                {
                    Destroy(v.gameObject);
                }
            }

            _visuals.Clear();
            _npcDataByIndex.Clear();
            _offsetByIndex.Clear();
            _campaignDriven = false;
            if (_instance == this)
            {
                _instance = null;
            }

            base.OnNetworkDespawn();
        }

        void Update()
        {
            if (!IsServer)
            {
                return;
            }

            // Game-loop path: the campaign is authoritative for where every named NPC is, in every phase.
            if (TryGetCampaign(out var campaign))
            {
                _timer += Time.deltaTime;
                var phase = campaign.Session.State.phase;
                if (!_campaignDriven || phase != _lastCampaignPhase || _timer >= pollSeconds)
                {
                    _timer = 0f;
                    ApplyCampaignLocationsServer(campaign);
                }

                return;
            }

            if (_campaignDriven)
            {
                // Campaign stopped (e.g. host session ended): fall back to the legacy schedule immediately.
                _campaignDriven = false;
                _timer = pollSeconds;
            }

            var cal = NetworkedCampusCalendar.Instance;
            if (cal == null || cal.Phase != GamePhase.Weekday)
            {
                if (cal != null)
                    for (int i = 0; i < _states.Count; i++)
                    {
                        var state = _states[i];
                        // Use the cached string id (no FixedString.ToString() allocation per NPC per frame).
                        string npcId = i < _npcDataByIndex.Count && _npcDataByIndex[i] != null ? _npcDataByIndex[i].npcId : null;
                        if (state.Visible && NetworkedSocialConversation.Manages(npcId))
                        { state.Visible = false; _states[i] = state; }
                    }
                return;
            }

            _timer += Time.deltaTime;
            if (_timer >= pollSeconds)
            {
                _timer = 0f;
                ApplySchedulesServer(force: false);
            }
        }

        /// <summary>Host with a running game-loop session (NetworkedCampaign.Session is host-only).</summary>
        static bool TryGetCampaign(out NetworkedCampaign campaign)
        {
            campaign = NetworkedCampaign.Instance;
            if (campaign != null && campaign.IsServer && campaign.Session != null)
            {
                return true;
            }

            campaign = null;
            return false;
        }

        void SeedStates()
        {
            NamedNpcDatabase.EnsureInitialized();
            _npcDataByIndex.Clear();
            _offsetByIndex.Clear();
            if (_states.Count > 0)
            {
                _states.Clear(); // re-spawn: keep the NetworkList parallel to _npcDataByIndex
            }

            foreach (var npc in NamedNpcDatabase.All)
            {
                _npcDataByIndex.Add(npc);
                _offsetByIndex.Add(CrowdOffset(npc.npcId));
                _states.Add(new NamedNpcNetState
                {
                    NpcId = npc.npcId,
                    LocationId = default,
                    Visible = false,
                    Position = Vector3.zero
                });
            }
        }

        /// <summary>
        /// Deterministic offset on a ring around the location marker (FNV-1a hash of the id → angle and radius), so NPCs
        /// sharing a location don't stack. Stable across sessions and machines.
        /// </summary>
        Vector3 CrowdOffset(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return Vector3.zero;
            }

            uint hash = 2166136261u;
            for (int i = 0; i < npcId.Length; i++)
            {
                hash ^= npcId[i];
                hash *= 16777619u;
            }

            float angle = (hash & 0xFFFFu) / 65536f * Mathf.PI * 2f;
            float min = Mathf.Max(0f, Mathf.Min(crowdRadiusMin, crowdRadiusMax));
            float max = Mathf.Max(crowdRadiusMin, crowdRadiusMax);
            float radius = Mathf.Lerp(min, max, ((hash >> 16) & 0xFFFFu) / 65535f);
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        Vector3 OffsetAt(int index) => index < _offsetByIndex.Count ? _offsetByIndex[index] : Vector3.zero;

        /// <summary>
        /// Game-loop placement (host): location from <see cref="NetworkedCampaign.NpcLocationServer"/> in all phases.
        /// Party night: NPCs at rival parties stand at those houses; NPCs at the players' house are hidden here because
        /// they are spawned there as party guests. NPCs unknown to the session (e.g. extra authored NamedNpcSO assets) keep
        /// their SO schedule on weekdays and are hidden otherwise. Writes only changed entries.
        /// </summary>
        void ApplyCampaignLocationsServer(NetworkedCampaign campaign)
        {
            var session = campaign.Session;
            var phase = session.State.phase;
            bool partyNight = phase == FratRow.Game.Phase.Party;
            string playersHouse = session.Config.playerHomeLocation;
            var registry = CampusLocationRegistry.Instance;
            _campaignDriven = true;
            _lastCampaignPhase = phase;

            for (int i = 0; i < _states.Count; i++)
            {
                NamedNpcSO data = i < _npcDataByIndex.Count ? _npcDataByIndex[i] : null;
                if (data == null)
                {
                    continue;
                }

                string locId;
                if (session.RosterNpc(data.npcId) != null)
                {
                    locId = campaign.NpcLocationServer(data.npcId);
                }
                else if (phase != FratRow.Game.Phase.Weekday || !data.TryGetLocationFor(session.State.day, session.ClockHour, out locId))
                {
                    locId = null;
                }

                var state = _states[i];
                NamedNpcNetState next = state;
                if (string.IsNullOrEmpty(locId)
                    || (partyNight && locId == playersHouse)
                    || registry == null
                    || !registry.TryGet(locId, out var loc))
                {
                    next.Visible = false;
                    next.LocationId = default;
                }
                else
                {
                    next.LocationId = new FixedString64Bytes(locId);
                    next.Visible = true;
                    next.Position = loc.Position + OffsetAt(i);
                }

                if (!next.Equals(state))
                {
                    _states[i] = next;
                }
            }
        }

        void ApplySchedulesServer(bool force)
        {
            var cal = NetworkedCampusCalendar.Instance;
            if (cal == null)
            {
                return;
            }

            float hour = cal.GetCurrentInGameTime();
            int day = cal.DayOfWeek;
            var registry = CampusLocationRegistry.Instance;

            for (int i = 0; i < _states.Count; i++)
            {
                var state = _states[i];
                NamedNpcSO data = i < _npcDataByIndex.Count ? _npcDataByIndex[i] : null;
                if (data == null)
                {
                    continue;
                }

                NamedNpcNetState next = state;

                if (!data.TryGetLocationFor(day, hour, out var locId)
                    || registry == null
                    || !registry.TryGet(locId, out var loc))
                {
                    next.Visible = false;
                    next.LocationId = default;
                }
                else
                {
                    var locFixed = new FixedString64Bytes(locId);
                    if (!force && state.Visible && state.LocationId.Equals(locFixed))
                    {
                        continue;
                    }

                    next.LocationId = locFixed;
                    next.Visible = true;
                    next.Position = loc.Position + OffsetAt(i);
                }

                if (!force && next.Equals(state))
                {
                    continue;
                }

                _states[i] = next;
            }
        }

        void OnStatesChanged(NetworkListEvent<NamedNpcNetState> change)
        {
            switch (change.Type)
            {
                case NetworkListEvent<NamedNpcNetState>.EventType.Add:
                case NetworkListEvent<NamedNpcNetState>.EventType.Insert:
                case NetworkListEvent<NamedNpcNetState>.EventType.Value:
                    UpsertVisual(change.Value);
                    break;
                case NetworkListEvent<NamedNpcNetState>.EventType.Remove:
                case NetworkListEvent<NamedNpcNetState>.EventType.RemoveAt:
                    RemoveVisual(change.Value.NpcId);
                    break;
                case NetworkListEvent<NamedNpcNetState>.EventType.Clear:
                    ClearVisuals();
                    break;
                case NetworkListEvent<NamedNpcNetState>.EventType.Full:
                default:
                    RebuildAllVisuals();
                    break;
            }
        }

        void RebuildAllVisuals()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                UpsertVisual(_states[i]);
            }
        }

        void ClearVisuals()
        {
            foreach (var v in _visuals.Values)
            {
                if (v != null)
                {
                    Destroy(v.gameObject);
                }
            }

            _visuals.Clear();
        }

        void RemoveVisual(FixedString64Bytes npcId)
        {
            if (!_visuals.TryGetValue(npcId, out var visual))
            {
                return;
            }

            _visuals.Remove(npcId);
            if (visual != null)
            {
                Destroy(visual.gameObject);
            }
        }

        void UpsertVisual(NamedNpcNetState state)
        {
            if (!_visuals.TryGetValue(state.NpcId, out var visual) || visual == null)
            {
                visual = CreateVisual(state.NpcId);
                _visuals[state.NpcId] = visual;
            }

            visual.ApplyState(state);
        }

        NamedNpcVisual CreateVisual(FixedString64Bytes npcId)
        {
            string id = npcId.ToString();
            GameObject go;
            if (visualPrefab != null)
            {
                go = Instantiate(visualPrefab);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            }

            go.name = $"CampusNPC_{id}";
            foreach (var nb in go.GetComponents<NetworkBehaviour>())
            {
                Destroy(nb);
            }

            var netObj = go.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                Destroy(netObj);
            }

            var visual = go.GetComponent<NamedNpcVisual>();
            if (visual == null)
            {
                visual = go.AddComponent<NamedNpcVisual>();
            }

            visual.Initialize(id);
            return visual;
        }
    }

    public class NamedNpcVisual : MonoBehaviour, IInteractable
    {
        string _npcId;
        public string NpcId => _npcId;
        bool _visible = true;
        bool _hasCachedVisibility;
        Renderer[] _renderers;
        Collider[] _colliders;

        public void Initialize(string npcId)
        {
            _npcId = npcId;
            CacheComponents();
        }

        void CacheComponents()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        public void ApplyState(NamedNpcNetState state)
        {
            if (_renderers == null || _colliders == null)
            {
                CacheComponents();
            }

            bool visibilityChanged = !_hasCachedVisibility || _visible != state.Visible;
            _visible = state.Visible;
            _hasCachedVisibility = true;
            transform.position = state.Position;

            if (!visibilityChanged)
            {
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].enabled = _visible;
                }
            }

            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                {
                    _colliders[i].enabled = _visible;
                }
            }
        }

        public void Interact()
        {
            if (!_visible)
            {
                return;
            }

            var data = NamedNpcDatabase.GetById(_npcId);
            var panel = NpcTalkPanel.Ensure();
            if (data == null || panel.IsOpen)
            {
                return;
            }

            // The conversation runs through the social backend; unlock gates are applied server-side (ask_number).
            panel.Open(data);
        }
    }
}
