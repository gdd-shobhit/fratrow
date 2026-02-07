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
    public int SelectedMeshIndex;
    public int SelectedMaterialIndex;

    public LobbyPlayerData()
    {
        PlayerName = "Player";
        SelectedClass = PlayerClass.President;
        IsReady = false;
        NetworkId = 0;
        IsHost = false;
        SelectedMeshIndex = 0;
        SelectedMaterialIndex = 0;
    }

    public LobbyPlayerData(string playerName, PlayerClass playerClass, ulong networkId, bool isHost)
    {
        PlayerName = playerName;
        SelectedClass = playerClass;
        IsReady = false;
        NetworkId = networkId;
        IsHost = isHost;
        SelectedMeshIndex = 0;
        SelectedMaterialIndex = 0;
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
            IsHost = this.IsHost,
            SelectedMeshIndex = this.SelectedMeshIndex,
            SelectedMaterialIndex = this.SelectedMaterialIndex
        };
    }

    public override string ToString()
    {
        return $"{PlayerName} (Class: {SelectedClass}, Ready: {IsReady}, Host: {IsHost})";
    }
}
