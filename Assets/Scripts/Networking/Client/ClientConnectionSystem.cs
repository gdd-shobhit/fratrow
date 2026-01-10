using UnityEngine;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// This system will connect and re-connect a client to the server
    /// as long as the <see cref="ConnectionSettings.ConnectionState"/> is Connecting or Connected.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class NetcodeClientConnectionSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            CompleteDependency();

            if (ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connected ||
                ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connecting)
            {
                bool hasNetworkStreamConnectionSingleton = 
                    SystemAPI.TryGetSingleton(out NetworkStreamConnection connection);
                
                if (hasNetworkStreamConnectionSingleton)
                {
                    ConnectionSettings.Instance.GameConnectionState =
                        connection.CurrentState == ConnectionState.State.Connected
                            ? ConnectionState.State.Connected
                            : ConnectionState.State.Connecting;
                }
                else
                {
                    //If it just lost connection (GameConnectionState is still connected), return to main menu
                    if (ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connected)
                    {
                        GameManager.Instance.ReturnToMainMenuAsync();
                        return;
                    }
                    
                    //Try to connect to the server
                    if (connection.CurrentState == ConnectionState.State.Unknown)
                    {
                        ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Connecting;
                        if (UnityEngine.Time.frameCount % 120 == 0) 
                        {
                            var networkEndpoint = ConnectionSettings.Instance.ConnectionEndpoint;
                            Debug.Log($"[{World.Name}] Reconnecting to {networkEndpoint.ToString()}...");
                            ref var driver = ref SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRW;
                            driver.Connect(EntityManager, networkEndpoint);
                        }
                    }
                }
            }            
        }
    }
}