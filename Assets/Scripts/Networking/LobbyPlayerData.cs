using System;
using UnityEngine;

/// <summary>
/// Serializable data structure representing a player in the lobby.
/// Used to synchronize player information across the network.
/// </summary>
[Serializable]
public class LobbyPlayerData
{
    public string PlayerName;
    public PlayerClass SelectedClass;
    public bool IsReady;
    public ulong NetworkId;
    public bool IsHost;

    public LobbyPlayerData()
    {
        PlayerName = "Player";
        SelectedClass = PlayerClass.President;
        IsReady = false;
        NetworkId = 0;
        IsHost = false;
    }

    public LobbyPlayerData(string playerName, PlayerClass playerClass, ulong networkId, bool isHost)
    {
        PlayerName = playerName;
        SelectedClass = playerClass;
        IsReady = false;
        NetworkId = networkId;
        IsHost = isHost;
    }

    /// <summary>
    /// Create a copy of this lobby player data.
    /// </summary>
    public LobbyPlayerData Clone()
    {
        return new LobbyPlayerData
        {
            PlayerName = this.PlayerName,
            SelectedClass = this.SelectedClass,
            IsReady = this.IsReady,
            NetworkId = this.NetworkId,
            IsHost = this.IsHost
        };
    }

    public override string ToString()
    {
        return $"{PlayerName} (Class: {SelectedClass}, Ready: {IsReady}, Host: {IsHost})";
    }
}
