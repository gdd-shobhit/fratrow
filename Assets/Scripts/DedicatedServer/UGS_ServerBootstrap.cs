#if UNITY_SERVER && UGS_SERVER
using System;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.GhostBridge;
using Unity.NetCode;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Unity.Services.Authentication.Server;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FPSSample_2
{
    public class UGSDedicatedServerBootstrap : MonoBehaviour
    {
        IMultiplaySessionManager m_MultiplayManager;
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
            Debug.Log($"FPS2 server -> Initializing Unity Cloud Service ...");
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    Debug.LogError($"FPS2 server -> Failed to initialize Unity Cloud Service.");
                    return;
                }
            }
            Debug.Log($"FPS2 server -> Unity Cloud Service is initialized.");
            Debug.Log($"FPS2 server -> Authenticating ...");
            if (!ServerAuthenticationService.Instance.IsAuthorized)
            {
                Debug.Log($"FPS2 server -> start authorization.");
                await ServerAuthenticationService.Instance.SignInFromServerAsync();
                if (!ServerAuthenticationService.Instance.IsAuthorized)
                {
                    Debug.Log($"FPS2 server -> Failed to Authenticate.");
                    return;
                }
            }
            Debug.Log($"FPS2 server -> Authorized.");
            Debug.Log($"FPS2 server -> Allocating and getting networkHandler ...");
            var networkHandler = new EntityNetworkHandler();
            try
            {
                // In Services 1.0.0, StartMultiplaySessionManagerAsync returns once the Manager is created
                // but is not waiting on the server allocation.
                // The callback registration here to wait on completion of the allocation, and then
                // then NetworkEndpoint created after the allocation is used to  initialize the server.
                var managerCallbacks = new MultiplaySessionManagerEventCallbacks();
                var serverAllocationTask = new TaskCompletionSource<IMultiplayAllocation>();
                managerCallbacks.Allocated += allocation => serverAllocationTask.SetResult(allocation);

                // Request server allocation
                m_MultiplayManager = await MultiplayerServerService.Instance.StartMultiplaySessionManagerAsync(
                    new MultiplaySessionManagerOptions
                    {
                        SessionOptions = new SessionOptions { MaxPlayers = GameManager.MaxPlayer }
                            .WithDirectNetwork()
                            .WithNetworkHandler(networkHandler)
                            .WithBackfillingConfiguration(
                                enable: true, 
                                automaticallyRemovePlayers: true, 
                                autoStart: true, 
                                playerConnectionTimeout: 30, 
                                backfillingLoopInterval: 1),
                        MultiplayServerOptions = new MultiplayServerOptions(
                            "server", "gameplay", "1", "gameplay", false),
                        Callbacks = managerCallbacks,
                    });
                _ = await serverAllocationTask.Task;
            }
            catch (Exception e)
            {
                Debug.LogError($"FPS2 server -> Multiplay services didn't start, see following exception.");
                Debug.LogException(e);
                return;
            }

            Debug.Log($"FPS2 server -> Retrieved network handler. ({networkHandler.ListenEndpoint.ToString()})");
            var listenEndpoint = await networkHandler.ListenEndpoint;

            m_MultiplayManager.Session.PlayerHasLeft += async _ =>
            {
                if (m_MultiplayManager.Session.PlayerCount == 0)
                {
                    Debug.Log("FPS2 Server -> Last player is leaving the server, closing...");
                    await m_MultiplayManager.Session.DeleteAsync();
                    Application.Quit();
                }
            };

            Debug.Log($"FPS2 server -> Start listening ...");
            GameSettings.Instance.GameState = GlobalGameState.Loading;
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.InitializeConnection);

            ConnectionSettings.Instance.Port = listenEndpoint.Port.ToString();
            GameConnection gameConnection = GameConnection.GetServerConnectionSettings(listenEndpoint);
            GameManager.SetGameConnection(gameConnection);
            
            Debug.Log($"FPS2 server -> Creating server world ...");
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.CreateWorld);
            GameManager.DestroyLocalSimulationWorld();
            var serverWorld = GhostBridgeBootstrap.Instance.ServerWorld;
            GhostGameObjectUpdateSystem.GatherWorldSystems(serverWorld);

            using var drvQuery = serverWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            drvQuery.CompleteDependency();
            var serverDriver = drvQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW;
            GhostBridgeManager.Instance.SetServerNetworkStreamDriver(serverDriver);
            serverDriver.Listen(listenEndpoint);
            
            Debug.Log($"FPS2 server -> Loading game scene ...");
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadGameScene);
            await ScenesLoader.LoadGameplayAsync(serverWorld, null);
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadingDone);
            await m_MultiplayManager.SetPlayerReadinessAsync(true);
            Debug.Log($"FPS2 server -> Successfully started the server on {listenEndpoint.ToString()}.");
            GameSettings.Instance.GameState = GlobalGameState.InGame;
        }
    }
}
#endif
