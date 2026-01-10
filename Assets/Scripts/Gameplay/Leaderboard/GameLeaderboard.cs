using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.FPSSample_2.UI;

// This is a GhostMonoBehaviour that will be spawned as a singleton manager for the leaderboard.
// It can be found on the client using FindObjectOfType<LeaderboardManager>()
// On the server, its entity can be found by querying for the PlayerScoreEntry buffer.
namespace Gameplay.Leaderboard
{
    [ResetOnPlayMode(resetMethod: "ResetStaticState")]
    public class LeaderboardManager : GhostMonoBehaviour, IUpdateServer, IUpdateClient
    {
        public static LeaderboardManager Instance { get; private set; }

        private struct KillInfo
        {
            public int KillerId;
            public int VictimId;
        }

        private Queue<KillInfo> _killQueue = new Queue<KillInfo>();
        private Queue<FixedString64Bytes> _joinedQueue = new Queue<FixedString64Bytes>();
#pragma warning disable UDR0001
        // This is reset from ResetOnPlayMode attribute
        private static Queue<(int networkId, FixedString64Bytes playerName)> _pendingPlayers =
            new Queue<(int, FixedString64Bytes)>();
#pragma warning restore UDR0001

        protected static void ResetStaticState()
        {
            _pendingPlayers.Clear();
            _pendingPlayers = null;
        }

        public struct PlayerScoreEntry : IBufferElementData
        {
            [GhostField] public int NetworkId; // Used as the unique key for a player
            [GhostField] public FixedString64Bytes PlayerName;
            [GhostField] public int Kills;
            [GhostField] public int Deaths;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }

            Instance = this;
        }

        public static void AddPlayer(int networkId, FixedString64Bytes playerName)
        {
            _pendingPlayers.Enqueue((networkId, playerName));
        }

        public void RemovePlayer(int networkId)
        {
            if (Role != MultiplayerRole.Server)
            {
                Debug.LogWarning("RemovePlayer can only be called on the server.");
                return;
            }

            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked() || !GhostGameObject.World.IsCreated)
            {
                return;
            }

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId == networkId)
                {
                    buffer.RemoveAt(i);
                    return;
                }
            }
        }

        public void AddKill(int killer, int victim)
        {
            if (Role != MultiplayerRole.Server)
            {
                Debug.LogWarning("AddKill can only be called on the server.");
                return;
            }

            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked() || !GhostGameObject.World.IsCreated)
            {
                Debug.LogWarning("[SERVER] NOT LINKED!");
                return;
            }

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId == killer)
                {
                    var entry = buffer[i];
                    entry.Kills++;
                    buffer[i] = entry;
                    break;
                }
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId == victim)
                {
                    var entry = buffer[i];
                    entry.Deaths++;
                    buffer[i] = entry;
                    break;
                }
            }

            AnnounceKillFeed(killer, victim);
        }

        public void AnnouncePlayerJoined(FixedString64Bytes playerName)
        {
            _joinedQueue.Enqueue(playerName);
        }

        private void AnnounceKillFeed(int killer, int victim)
        {
            _killQueue.Enqueue(new KillInfo { KillerId = killer, VictimId = victim });
        }

        public void AddDeath(int networkId)
        {
            if (Role != MultiplayerRole.Server)
            {
                Debug.LogWarning("AddDeath can only be called on the server.");
                return;
            }

            if (!GhostGameObject.IsGhostLinked()) return;

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId == networkId)
                {
                    var entry = buffer[i];
                    entry.Deaths++;
                    buffer[i] = entry;
                    return;
                }
            }
        }


        public List<PlayerScoreEntry> GetScores()
        {
            var scores = new List<PlayerScoreEntry>();
            if (GhostGameObject.IsGhostLinked())
            {
                var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
                foreach (var entry in buffer)
                {
                    scores.Add(entry);
                }
            }

            return scores;
        }


        public void UpdateServer(float deltaTime)
        {
            if (!GhostGameObject.IsGhostLinked())
            {
                Debug.Log("[Server] LeaderboardManager not linked yet.");
                return;
            }

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();

            while (_pendingPlayers.Count > 0)
            {
                var (networkId, playerName) = _pendingPlayers.Dequeue();

                bool alreadyExists = false;
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].NetworkId == networkId)
                    {
                        alreadyExists = true;
                        Debug.LogWarning(
                            $"[Server] Player {networkId} already exists in leaderboard, skipping add from queue.");
                        break;
                    }
                }

                if (!alreadyExists)
                {
                    buffer.Add(new PlayerScoreEntry
                    {
                        NetworkId = networkId,
                        PlayerName = playerName,
                        Kills = 0,
                        Deaths = 0
                    });
                    Debug.Log($"[Server] Added player {networkId} ({playerName}) to leaderboard from queue.");

                    // Announce join *after* successfully adding to the buffer
                    AnnouncePlayerJoined(playerName);
                }
            }

            while (_killQueue.Count > 0)
            {
                KillInfo kill = _killQueue.Dequeue();

                FixedString64Bytes killerName = "[Unknown]";
                FixedString64Bytes victimName = "[Unknown]";

                // Find names from the buffer
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].NetworkId == kill.KillerId)
                    {
                        killerName = buffer[i].PlayerName;
                    }

                    if (buffer[i].NetworkId == kill.VictimId)
                    {
                        victimName = buffer[i].PlayerName;
                    }
                }

                // Create and broadcast the RPC
                var killFeedRpc = new KillFeedEntryRpc
                {
                    KillerName = killerName,
                    VictimName = victimName
                };
                GhostGameObject.BroadcastRPC(killFeedRpc);
                ActionFeed.Instance.AnnounceKill(killerName.ToString(), victimName.ToString());
            }

            while (_joinedQueue.Count > 0)
            {
                var playerName = _joinedQueue.Dequeue();

                var joinRpc = new PlayerJoinedEntryRpc
                {
                    PlayerName = playerName,
                };
                GhostGameObject.BroadcastRPC(joinRpc);
                ActionFeed.Instance.AnnouncePlayerJoined(playerName.ToString());
            }
        }

        public void UpdateClient(float deltaTime)
        {
            if (!GhostGameObject.IsGhostLinked()) return;

            // Consume any kill feed RPCs received this frame
            while (GhostGameObject.ConsumeRPC(out KillFeedEntryRpc killFeedRpc))
            {
                // Invoke the event for the UI to handle
                ActionFeed.Instance.AnnounceKill(killFeedRpc.KillerName.ToString(),
                    killFeedRpc.VictimName.ToString());
            }

            while (GhostGameObject.ConsumeRPC(out PlayerJoinedEntryRpc joinRpc))
            {
                ActionFeed.Instance.AnnouncePlayerJoined(joinRpc.PlayerName.ToString());
            }
        }
    }

    public struct KillFeedEntryRpc : IRpcCommand
    {
        public FixedString64Bytes KillerName;
        public FixedString64Bytes VictimName;
    }

    public struct PlayerJoinedEntryRpc : IRpcCommand
    {
        public FixedString64Bytes PlayerName;
    }
}