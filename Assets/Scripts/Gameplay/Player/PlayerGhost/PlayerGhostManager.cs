using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Unity.FPSSample_2
{
    public class PlayerGhostManager : GhostSingleton<PlayerGhostManager>,
        IGhostManager
    {
        public const int k_MaxTotalPlayers = 4;

        private Dictionary<MultiplayerRole, List<PlayerGhost>> m_PlayerGhostsByRole = new();

        public Color CameraClearColour { get; private set; } = Color.black;

        public delegate void PlayerRegisteredCallback(PlayerGhost player);

        public PlayerRegisteredCallback OnPlayerRegistered;

        public delegate void PlayerUnRegisteredCallback(PlayerGhost player);

        public PlayerUnRegisteredCallback OnPlayerUnRegistered;

        public void Register(PlayerGhost player)
        {
            AddPlayerWithRole(player, player.Role);

            if (player.Role != MultiplayerRole.Server)
            {
                AddPlayerWithRole(player, MultiplayerRole.ClientAll);
            }

            OnPlayerRegistered?.Invoke(player);
        }

        private void AddPlayerWithRole(PlayerGhost player, MultiplayerRole role)
        {
            if (!m_PlayerGhostsByRole.ContainsKey(role))
            {
                m_PlayerGhostsByRole.Add(role, new List<PlayerGhost>());
            }

            Debug.Assert(!m_PlayerGhostsByRole[role].Contains(player),
                $"Player {player.gameObject.name} with role {role} already registered to the PlayerGhostManager");
            m_PlayerGhostsByRole[role].Add(player);
        }

        public void Unregister(PlayerGhost player)
        {
            RemovePlayerWithRole(player, player.Role);

            if (player.Role != MultiplayerRole.Server)
            {
                RemovePlayerWithRole(player, MultiplayerRole.ClientAll);
            }

            OnPlayerUnRegistered?.Invoke(player);
        }

        private void RemovePlayerWithRole(PlayerGhost player, MultiplayerRole role)
        {
            Debug.Assert(m_PlayerGhostsByRole.ContainsKey(role),
                $"Trying to unregister player {player.gameObject.name} but list does not exist");
            Debug.Assert(m_PlayerGhostsByRole[role].Contains(player),
                $"Trying to unregister player {player.gameObject.name} but they aren't in the list");

            m_PlayerGhostsByRole[role].RemoveSwapBack(player);
            if (m_PlayerGhostsByRole[role].Count == 0)
            {
                m_PlayerGhostsByRole.Remove(role);
            }
        }

        public List<PlayerGhost> GetPlayersByRole(MultiplayerRole role, bool allClients = true)
        {
            role = allClients && role != MultiplayerRole.Server ? MultiplayerRole.ClientAll : role;

            if (m_PlayerGhostsByRole.ContainsKey(role))
            {
                return m_PlayerGhostsByRole[role];
            }

            return default;
        }

        public bool TryGetPlayersByRole(MultiplayerRole role, out List<PlayerGhost> players)
        {
            if (m_PlayerGhostsByRole.ContainsKey(role))
            {
                players = m_PlayerGhostsByRole[role];
                return true;
            }

            players = default;
            return false;
        }

        public int GetNumPlayersByRole(MultiplayerRole role, bool allClients = true)
        {
            role = allClients && role != MultiplayerRole.Server ? MultiplayerRole.ClientAll : role;

            if (m_PlayerGhostsByRole.ContainsKey(role))
            {
                return m_PlayerGhostsByRole[role].Count;
            }

            return 0;
        }
    }
}