using System;
using Systems.Campus;
using Systems.Phone;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Systems.Campus
{
    public struct NetworkedContactState : INetworkSerializable, IEquatable<NetworkedContactState>
    {
        public FixedString64Bytes NpcId;
        public bool Unlocked;
        public bool Recruited;
        public float Affinity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NpcId);
            serializer.SerializeValue(ref Unlocked);
            serializer.SerializeValue(ref Recruited);
            serializer.SerializeValue(ref Affinity);
        }

        public bool Equals(NetworkedContactState other) =>
            NpcId.Equals(other.NpcId) && Unlocked == other.Unlocked && Recruited == other.Recruited &&
            Mathf.Approximately(Affinity, other.Affinity);
    }

    /// <summary>
    /// Shared frat contact unlocks, affinity, and recruits. Server authoritative.
    /// </summary>
    public class NetworkedContactRoster : NetworkBehaviour
    {
        static NetworkedContactRoster _instance;
        public static NetworkedContactRoster Instance => _instance;

        NetworkList<NetworkedContactState> _states;

        public event Action OnRosterChanged;

        void Awake()
        {
            _instance = this;
            _states = new NetworkList<NetworkedContactState>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _instance = this;
            _states.OnListChanged += HandleListChanged;

            if (IsServer)
            {
                SeedFromContacts();
                ApplySaveIfPresent();
            }

            PushToLocalPhones();
        }

        void ApplySaveIfPresent()
        {
            var data = CampusPersistence.Load();
            if (data == null)
            {
                return;
            }

            foreach (var entry in data.contacts)
            {
                if (!TryGet(entry.npcId, out var state))
                {
                    state = new NetworkedContactState { NpcId = entry.npcId };
                    _states.Add(state);
                }

                state.Unlocked = entry.unlocked;
                state.Recruited = entry.recruited;
                state.Affinity = entry.affinity;
                WriteState(state);
            }
        }

        public override void OnNetworkDespawn()
        {
            _states.OnListChanged -= HandleListChanged;
            if (_instance == this)
            {
                _instance = null;
            }

            base.OnNetworkDespawn();
        }

        void HandleListChanged(NetworkListEvent<NetworkedContactState> change)
        {
            PushToLocalPhones();
            OnRosterChanged?.Invoke();
        }

        void SeedFromContacts()
        {
            PhoneContactDatabase.EnsureInitialized();
            NamedNpcDatabase.EnsureInitialized();
            foreach (var npc in NamedNpcDatabase.All)
            {
                var contact = npc.ResolveContact();
                bool startsUnlocked = contact != null && contact.startsUnlocked;
                _states.Add(new NetworkedContactState
                {
                    NpcId = npc.npcId,
                    Unlocked = startsUnlocked,
                    Recruited = false,
                    Affinity = 0f
                });
            }
        }

        public bool TryGet(string npcId, out NetworkedContactState state)
        {
            state = default;
            for (int i = 0; i < _states.Count; i++)
            {
                if (_states[i].NpcId.ToString() == npcId)
                {
                    state = _states[i];
                    return true;
                }
            }

            return false;
        }

        public float GetAffinity(string npcId) => TryGet(npcId, out var s) ? s.Affinity : 0f;

        public bool IsUnlocked(string npcId) => TryGet(npcId, out var s) && s.Unlocked;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestUnlockServerRpc(FixedString64Bytes npcId, RpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            TryUnlockServer(npcId.ToString());
        }

        public void RequestUnlock(string npcId) => RequestUnlockServerRpc(new FixedString64Bytes(npcId));

        bool TryUnlockServer(string npcId)
        {
            if (TryGet(npcId, out var existing) && existing.Unlocked) return true;
            var npc = NamedNpcDatabase.GetById(npcId);
            if (npc == null)
            {
                return false;
            }

            float pop = NetworkedGameManager.Instance != null ? NetworkedGameManager.Instance.TotalPopularity : 0f;
            Grade grade = Grade.C;
            if (NetworkedGameManager.Instance != null)
            {
                grade = NetworkedGameManager.Instance.myFraternity.Value.grade;
                pop = Mathf.Max(pop, NetworkedGameManager.Instance.myFraternity.Value.popularity);
                pop = Mathf.Max(pop, NetworkedGameManager.Instance.TotalPopularity);
            }

            if (!npc.MeetsUnlockGates(pop, grade))
            {
                Debug.Log($"[ContactRoster] Unlock rejected for {npcId} (gates)");
                return false;
            }

            if (UnityEngine.Random.value > npc.unlockChance)
            {
                Debug.Log($"[ContactRoster] Unlock rejected for {npcId} (rarity roll)");
                return false;
            }

            SetUnlocked(npcId, true, affinityDelta: 5f);
            NetworkedCampaign.Instance?.NotifyContactUnlockedServer(npcId);
            return true;
        }

        /// <summary>Host: the campaign unlocked a contact (ask_number) — show it on every phone.</summary>
        public void SetUnlockedServer(string npcId, bool unlocked)
        {
            if (!IsServer || string.IsNullOrEmpty(npcId))
            {
                return;
            }

            if (TryGet(npcId, out var state) && state.Unlocked == unlocked)
            {
                return;
            }

            SetUnlocked(npcId, unlocked, affinityDelta: 0f);
        }

        /// <summary>Host: Bid Day result from the campaign (the NPC joined the players' house).</summary>
        public void SetRecruitedServer(string npcId, bool recruited)
        {
            if (!IsServer || string.IsNullOrEmpty(npcId))
            {
                return;
            }

            if (!TryGet(npcId, out var state))
            {
                state = new NetworkedContactState { NpcId = npcId, Unlocked = true };
            }

            if (state.Recruited == recruited)
            {
                return;
            }

            state.Recruited = recruited;
            state.Unlocked = true;
            WriteState(state);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRecruitServerRpc(FixedString64Bytes npcId, RpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            string id = npcId.ToString();
            var campaign = NetworkedCampaign.Instance;
            if (campaign != null && campaign.Session != null)
            {
                // Recruiting is Bid Day: the dialogue command extends a bid that the PNM decides on Sunday.
                campaign.RecruitFromDialogueServer(rpcParams.Receive.SenderClientId, id);
                return;
            }

            var npc = NamedNpcDatabase.GetById(id);
            if (npc == null || !npc.isRecruitable)
            {
                return;
            }

            if (!TryGet(id, out var state) || !state.Unlocked || state.Recruited)
            {
                return;
            }

            if (state.Affinity < npc.recruitMinAffinity)
            {
                return;
            }

            state.Recruited = true;
            WriteState(state);

            if (NetworkedGameManager.Instance != null)
            {
                var frat = NetworkedGameManager.Instance.myFraternity.Value;
                frat.memberCount = Mathf.Min(FraternityData.MAX_MEMBERS_ALLOWED, frat.memberCount + 1);
                NetworkedGameManager.Instance.myFraternity.Value = frat;
                NetworkedGameManager.Instance.fraternityLocal = frat;
                NetworkedGameManager.Instance.RecalculateFraternityGradeServer();
            }

            Debug.Log($"[ContactRoster] Recruited {id}");
        }

        public void RequestRecruit(string npcId) => RequestRecruitServerRpc(new FixedString64Bytes(npcId));

        public void AddAffinity(string npcId, float delta)
        {
            if (NetworkedSocialConversation.Manages(npcId)) return;
            if (!IsServer || !TryGet(npcId, out var state))
            {
                return;
            }

            state.Affinity = Mathf.Clamp(state.Affinity + delta, -100f, 100f);
            WriteState(state);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestAffinityServerRpc(FixedString64Bytes npcId, float delta, RpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            AddAffinity(npcId.ToString(), delta);
        }

        public void RequestAffinity(string npcId, float delta) =>
            RequestAffinityServerRpc(new FixedString64Bytes(npcId), delta);

        public void UnlockSocialScenarioServer(string npcId)
        {
            if (IsServer && NetworkedSocialConversation.Manages(npcId) && TryGet(npcId, out var state) && !state.Unlocked)
            { state.Unlocked = true; WriteState(state); }
        }

        public void ProjectSocialRegardServer(string npcId, int regard)
        {
            if (IsServer && NetworkedSocialConversation.Manages(npcId) && TryGet(npcId, out var state) && !Mathf.Approximately(state.Affinity, regard))
            { state.Affinity = regard; WriteState(state); }
        }

        void SetUnlocked(string npcId, bool unlocked, float affinityDelta)
        {
            if (NetworkedSocialConversation.Manages(npcId)) affinityDelta = 0;
            if (!TryGet(npcId, out var state))
            {
                state = new NetworkedContactState { NpcId = npcId };
                _states.Add(state);
                TryGet(npcId, out state);
            }

            state.Unlocked = unlocked;
            state.Affinity = Mathf.Clamp(state.Affinity + affinityDelta, -100f, 100f);
            WriteState(state);
        }

        void WriteState(NetworkedContactState state)
        {
            for (int i = 0; i < _states.Count; i++)
            {
                if (_states[i].NpcId.Equals(state.NpcId))
                {
                    _states[i] = state;
                    return;
                }
            }

            _states.Add(state);
        }

        void PushToLocalPhones()
        {
            foreach (var phone in FindObjectsByType<PhoneController>(FindObjectsSortMode.None))
            {
                phone.ApplySharedRoster(this);
            }
        }

        public float AverageUnlockedAffinity()
        {
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < _states.Count; i++)
            {
                if (!_states[i].Unlocked)
                {
                    continue;
                }

                sum += _states[i].Affinity;
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        public int UnlockedCount()
        {
            int count = 0;
            for (int i = 0; i < _states.Count; i++)
            {
                if (_states[i].Unlocked)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
