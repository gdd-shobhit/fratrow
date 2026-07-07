using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Serializable data structure representing a player in the lobby.
/// Used to synchronize player information across the network.
/// </summary>
[Serializable]
public struct LobbyPlayerData : INetworkSerializable
{
    public string PlayerName;
    public PlayerClass SelectedClass;
    public bool IsReady;
    public ulong NetworkId;
    public bool IsHost;
    public int SelectedMeshIndex;
    public int SelectedMaterialIndex;

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

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref PlayerName);
        serializer.SerializeValue(ref SelectedClass, new FastBufferWriter.ForEnums());
        serializer.SerializeValue(ref SelectedMeshIndex, new FastBufferWriter.ForPrimitives());
        serializer.SerializeValue(ref SelectedMaterialIndex, new FastBufferWriter.ForPrimitives());
        serializer.SerializeValue(ref IsReady, new FastBufferWriter.ForPrimitives());
        serializer.SerializeValue(ref IsHost, new FastBufferWriter.ForPrimitives());
        serializer.SerializeValue(ref NetworkId, new FastBufferWriter.ForPrimitives());
    }

    public override string ToString()
    {
        return $"{PlayerName} (Class: {SelectedClass}, Ready: {IsReady}, Host: {IsHost})";
    }
}
