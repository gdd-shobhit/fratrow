using System;
using System.Collections.Generic;
using Systems.Dialogue;
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
    /// Server-authoritative schedules; clients spawn local interactable visuals from replicated state.
    /// </summary>
    public class NetworkedCampusNpcDirector : NetworkBehaviour
    {
        static NetworkedCampusNpcDirector _instance;
        public static NetworkedCampusNpcDirector Instance => _instance;

        [SerializeField] float pollSeconds = 2f;
        [SerializeField] GameObject visualPrefab;

        NetworkList<NamedNpcNetState> _states;
        readonly Dictionary<FixedString64Bytes, NamedNpcVisual> _visuals = new();
        /// <summary>Parallel to NetworkList indices on server — avoids FixedString.ToString() each poll.</summary>
        readonly List<NamedNpcSO> _npcDataByIndex = new();

        float _timer;

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

            // Locations must exist before first schedule resolve
            if (CampusLocationRegistry.Instance == null)
            {
                FindFirstObjectByType<CampusSystemsBootstrap>()?.SendMessage("OnNetworkSpawn", SendMessageOptions.DontRequireReceiver);
            }

            if (FindFirstObjectByType<CampusLocationRegistry>() == null)
            {
                var regGo = new GameObject("CampusLocationRegistry");
                regGo.AddComponent<CampusLocationRegistry>();
                CreateFallbackLoc(regGo, "PartyHouse", Vector3.zero);
                CreateFallbackLoc(regGo, "Shop", new Vector3(12f, 0f, 0f));
                CreateFallbackLoc(regGo, "Quad", new Vector3(-12f, 0f, 8f));
                CreateFallbackLoc(regGo, "Gym", new Vector3(8f, 0f, -10f));
                CreateFallbackLoc(regGo, "Library", new Vector3(-8f, 0f, -10f));
                CreateFallbackLoc(regGo, "Dorm", new Vector3(0f, 0f, 14f));
                FindFirstObjectByType<CampusLocationRegistry>()?.Rebuild();
            }

            if (IsServer)
            {
                SeedStates();
                ApplySchedulesServer(force: true);
            }

            RebuildAllVisuals();
        }

        static void CreateFallbackLoc(GameObject parent, string id, Vector3 pos)
        {
            var go = new GameObject($"Loc_{id}");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = pos;
            go.AddComponent<CampusLocation>().Configure(id, id);
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

            var cal = NetworkedCampusCalendar.Instance;
            if (cal == null || cal.Phase != GamePhase.Weekday)
            {
                if (cal != null)
                    for (int i = 0; i < _states.Count; i++)
                    {
                        var state = _states[i];
                        if (state.Visible && NetworkedSocialConversation.Manages(state.NpcId.ToString()))
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

        void SeedStates()
        {
            NamedNpcDatabase.EnsureInitialized();
            _npcDataByIndex.Clear();
            foreach (var npc in NamedNpcDatabase.All)
            {
                _npcDataByIndex.Add(npc);
                _states.Add(new NamedNpcNetState
                {
                    NpcId = npc.npcId,
                    LocationId = default,
                    Visible = false,
                    Position = Vector3.zero
                });
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
                    next.Position = loc.Position;
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
            if (data == null || CampusDialogueService.Instance == null || CampusDialogueService.Instance.IsRunning)
            {
                return;
            }

            float pop = 0f;
            Grade grade = Grade.C;
            if (NetworkedGameManager.Instance != null)
            {
                pop = NetworkedGameManager.Instance.TotalPopularity;
                var frat = NetworkedGameManager.Instance.myFraternity.Value;
                grade = frat.grade;
                pop = Mathf.Max(pop, frat.popularity);
            }

            bool canUnlock = data.MeetsUnlockGates(pop, grade);
            CampusDialogueService.Instance.StartNamedNpcDialogue(data, canUnlock);
        }
    }
}
