using System;
using Unity.Netcode;
using UnityEngine;

public enum Grade
{
    First = 0,
    A = First,
    B,
    C,
    D,
    E,
    F,
    Count
}

[Serializable]
public struct FraternityData : INetworkSerializable
{
    public static int MAX_MEMBERS_ALLOWED = 100;
    public string fratName;

    // rating is backend purposes
    public int rating;

    // Grade will be for UI purposes
    public Grade grade;
    public int memberCount;

    // Metrics
    public int popularity;
    public int cashValue;

    public FraternityData(string fratName, int rating, Grade grade, int memberCount, int popularity, int cashValue)
    {
        this.fratName = fratName;
        this.rating = rating;
        this.grade = grade;
        this.memberCount = memberCount;
        this.popularity = popularity;
        this.cashValue = cashValue;
    }
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        // Serialize templates
        serializer.SerializeValue(ref fratName);
        serializer.SerializeValue(ref grade, new FastBufferWriter.ForEnums());
        serializer.SerializeValue(ref rating, new FastBufferWriter.ForPrimitives());
        serializer.SerializeValue(ref memberCount, new FastBufferWriter.ForPrimitives());
        serializer.SerializeValue(ref popularity, new FastBufferWriter.ForPrimitives());
        serializer.SerializeValue(ref cashValue, new FastBufferWriter.ForPrimitives());
    }
}
