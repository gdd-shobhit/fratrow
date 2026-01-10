using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct NetworkStatusSystem : ISystem
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        StatusEntity = Entity.Null;
    }
    
    public static Entity StatusEntity { get; private set; }
    
    private const string k_NotConnected = "<color=#ff5555>Not connected!</color>";
    private const string k_RedColor = "#ff5555";
    private const string k_OrangeColor = "#ffb86c";
    private const string k_GreenColor = "#50fa7b";

    public void OnCreate(ref SystemState state)
    {
        var entityManager = state.EntityManager;

        if (!SystemAPI.TryGetSingletonEntity<NetworkStatusSingleton>(out var entity))
        {
            entity = entityManager.CreateEntity(typeof(NetworkStatusSingleton));
            entityManager.SetComponentData(entity, new NetworkStatusSingleton { Status =  k_NotConnected });
        }

        StatusEntity = entity;
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        
        ref var statusSingleton = ref SystemAPI.GetComponentRW<NetworkStatusSingleton>(StatusEntity).ValueRW;
        statusSingleton.Status.Clear();

        if (!SystemAPI.TryGetSingleton<NetworkStreamConnection>(out var connection) ||
            !SystemAPI.TryGetSingleton<NetworkStreamDriver>(out var driver))
        {
            statusSingleton.Status = k_NotConnected;
            return;
        }

        var sb = new FixedString512Bytes();
        var pingColor = new FixedString32Bytes(k_OrangeColor);

        if (SystemAPI.TryGetSingleton<NetworkSnapshotAck>(out var ack) && connection.CurrentState == ConnectionState.State.Connected)
        {
            var pingEstimate = (int)ack.EstimatedRTT;
            const float assumedSimulationTickRate = 60;
            const float lastSimulationTickRateFrameMs = (1000f / assumedSimulationTickRate);
            pingEstimate = (int)math.max(0, pingEstimate - lastSimulationTickRateFrameMs);
            var deviationRTT = (int)ack.DeviationRTT;

            if (ack.EstimatedRTT > 200)
                pingColor.CopyFrom(k_RedColor);
            else if (ack.EstimatedRTT <= 100)
                pingColor.CopyFrom(k_GreenColor);
            
            sb.Append("<color=");
            sb.Append(pingColor);
            sb.Append(">");
            
            sb.Append("Ping:");
            sb.Append(pingEstimate);
            sb.Append('±');
            sb.Append(deviationRTT);
            sb.Append("ms, ");
        }
        else
        {
            sb.Append("<color=");
            sb.Append(pingColor);
            sb.Append(">");
        }

        sb.Append(connection.CurrentState.ToFixedString());
        sb.Append(" @ ");
        sb.Append(driver.GetRemoteEndPoint(connection).ToFixedString());
        sb.Append("</color>");

        // Write the final string to our singleton component.
        statusSingleton.Status = sb;
    }
}
