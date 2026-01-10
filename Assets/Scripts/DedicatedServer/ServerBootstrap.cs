#if UNITY_SERVER

using System;
using Unity.Entities;
using Unity.GhostBridge;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FPSSample_2
{
    public class ServerBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            Debug.Log($"FPS2 server -> Awaking.");
            Application.runInBackground = true; 
            Debug.Log($"FPS2 server -> Loading the Persistent scene ...");
            if (!SceneManager.GetSceneByName("Persistents").isLoaded)
            {
                SceneManager.LoadScene("Scenes/Persistents", LoadSceneMode.Additive);
            }
            Debug.Log($"FPS2 server -> Persistent is loaded.");
        }

        async void Start()
        {
            Debug.Log($"FPS2 server -> Starting.");
            GameSettings.Instance.GameState = GlobalGameState.Loading;
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.InitializeConnection);
            ushort port = GetPortFromArgument();
            GameConnection gameConnection = GameConnection.GetServerConnectionSettings(port);
            GameManager.SetGameConnection(gameConnection);
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.CreateWorld);
            GameManager.DestroyLocalSimulationWorld();

            var serverWorld = GhostBridgeBootstrap.Instance.ServerWorld;
            GhostGameObjectUpdateSystem.GatherWorldSystems(serverWorld);
            Debug.Log($"FPS2 server -> ServerWorld is available.");
            
            using var drvQuery = serverWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            drvQuery.CompleteDependency();
            var serverDriver = drvQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW;
            GhostBridgeManager.Instance.SetServerNetworkStreamDriver(serverDriver);
            serverDriver.Listen(gameConnection.ListenEndpoint);
            Debug.Log($"FPS2 server -> Listening on port {port}.");
            
            Debug.Log($"FPS2 server -> Loading the Game Scenes.");
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadGameScene);
            await ScenesLoader.LoadGameplayAsync(serverWorld, null);
            Debug.Log($"FPS2 server -> The Game Scene is loaded.");
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadingDone);
            GameSettings.Instance.GameState = GlobalGameState.InGame;
        }
        
        ushort GetPortFromArgument()
        {
            ushort port = 7979;
            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args)
            {
                if (arg.StartsWith("port="))
                {
                    string portValue = arg.Substring("port=".Length);
                    Debug.Log("Port argument found: " + portValue);
                    if (!ushort.TryParse(portValue, out port))
                    {
                        Debug.LogWarning("Invalid port argument. The default port number 7979 is used.");
                    }
                }
            }
            return port;
        }
    }
}

#endif