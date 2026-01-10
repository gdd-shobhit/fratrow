using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.GhostBridge;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FPSSample_2
{
    public partial class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public const int MaxPlayer = 32;
        public const string MainMenuSceneName = "MainMenu";
        public const string GameSceneName = "GameScene";
        static public GameConnection GameConnection { get; private set; }

        Task m_LoadingGame;
        CancellationTokenSource m_LoadingGameCancel;
        Task m_LoadingMainMenu;
        CancellationTokenSource m_LoadingMainMenuCancel;

        public UnityEngine.Audio.AudioMixer AudioMixer;
        public int MaxSoundEmitters;
        public int MaxSoundGameObjects;
        public SoundGameObjectPool SoundGameObjects;

        ISoundSystem m_SoundSystem;
        public ISoundSystem SoundSystem => m_SoundSystem;

        bool m_IsHeadless = false;
        public bool IsHeadless => m_IsHeadless;

        public static bool CanUseMainMenu => SceneManager.GetActiveScene().name == MainMenuSceneName;

        private void Awake()
        {
            if (Instance && Instance != this)
            {
                Debug.LogError($"Multiple instances of '{this}' violates the Singleton pattern!", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;

#if UNITY_STANDALONE_LINUX
            m_IsHeadless = true;
#else
            var commandLineArgs = new List<string>(System.Environment.GetCommandLineArgs());
            m_IsHeadless = commandLineArgs.Contains("-batchmode");
#endif
            ConfigVar.Init();

            if (m_IsHeadless)
            {
                m_SoundSystem = new SoundSystemNull();
            }
            else
            {
                m_SoundSystem = new SoundSystem();
                AudioListener audioListener = MainCameraSingleton.Instance.GetComponent<AudioListener>();
                SoundGameObjects = new SoundGameObjectPool("SoundSystemSources", MaxSoundGameObjects);
                m_SoundSystem.Init(audioListener.transform, MaxSoundEmitters, SoundGameObjects, AudioMixer);
            }
        }
        
        async void Start()
        {
            Application.runInBackground = true; //Prevents dropped connections during multiplayer gameplay

            MainCameraSingleton.Instance.GetComponent<Camera>().enabled = true;
            var audioListener = MainCameraSingleton.Instance.GetComponent<AudioListener>();
            if (audioListener != null)
            {
                m_SoundSystem.SetListenerTransform(audioListener.transform);
            }

            GameSettings.Instance.MainMenuSceneLoaded = false;
            if (SceneManager.GetActiveScene().name == "MainMenu")
            {
                m_LoadingMainMenuCancel = new CancellationTokenSource();
                try
                {
                    m_LoadingMainMenu = StartMainMenuAsync(m_LoadingMainMenuCancel.Token);
                    await m_LoadingMainMenu;
                }
                catch (OperationCanceledException)
                {
                    // Nothing to do when the task is cancelled.
                }
                finally
                {
                    m_LoadingMainMenuCancel.Dispose();
                    m_LoadingMainMenuCancel = null;
                }
            }

            // Ensures it only ever loads once
            if (!SceneManager.GetSceneByName("Persistents").isLoaded)
            {
                SceneManager.LoadScene("Scenes/Persistents", LoadSceneMode.Additive);
            }
        }

        public void Update()
        {
            if (m_SoundSystem != null)
            {
                m_SoundSystem.UpdateSoundSystem(false);
            }
        }

        async Task StartMainMenuAsync(CancellationToken cancellationToken)
        {
            DestroyLocalSimulationWorld();
            var clientWorld = ClientServerBootstrap.CreateClientWorld("ClientWorld");
            await ScenesLoader.LoadGameplayAsync(null, clientWorld);
            GameSettings.Instance.MainMenuSceneLoaded = true;
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// This method start the Gameplay session.
        /// </summary>
        /// <remarks>
        /// It is an asynchronous method because it is waiting on several API like <see cref="GameConnection"/> and <see cref="ScenesLoader"/>.
        /// </remarks>
        public async void StartGameAsync(CreationType creationType)
        {
            if (GameSettings.Instance.GameState != GlobalGameState.MainMenu)
            {
                Debug.Log("[StartGameAsync] Called but in-game, cannot start while in-game!");
                return;
            }

            Debug.Log($"[{nameof(StartGameAsync)}] Called with creation type '{creationType}'");

            if (creationType == CreationType.Host)
            {
                GameSettings.Instance.CancellableUserInputPopUp = new AwaitableCompletionSource();
                GameSettings.Instance.MainMenuState = MainMenuState.StartHostPopup;
                try
                {
                    await GameSettings.Instance.CancellableUserInputPopUp.Awaitable;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    GameSettings.Instance.MainMenuState = MainMenuState.MainMenuScreen;
                }
            }
            else if (creationType == CreationType.ConnectAndJoin)
            {
                GameSettings.Instance.CancellableUserInputPopUp = new AwaitableCompletionSource();
                GameSettings.Instance.MainMenuState = MainMenuState.DirectConnectPopUp;
                try
                {
                    await GameSettings.Instance.CancellableUserInputPopUp.Awaitable;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    GameSettings.Instance.MainMenuState = MainMenuState.MainMenuScreen;
                }
            }

            BeginEnteringGame();

            m_LoadingGameCancel = new CancellationTokenSource();
            try
            {
                m_LoadingGame = StartGameAsync(creationType, m_LoadingGameCancel.Token);
                await m_LoadingGame;
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[{nameof(StartGameAsync)}] Loading has been cancelled.");
                return;
            }
            catch (Exception e)
            {
                Debug.LogError($"[{nameof(StartGameAsync)}] Loading has failed, returning to main menu");
                Debug.LogException(e);
                // Disposing the token here because the error has been handled and ReturnToMainMenu should not check it.
                m_LoadingGameCancel.Dispose();
                m_LoadingGameCancel = null;
                ReturnToMainMenuAsync();
                return;
            }
            finally
            {
                m_LoadingGameCancel?.Dispose();
                m_LoadingGameCancel = null;
            }

            FinishLoadingGame();
        }

        void BeginEnteringGame()
        {
            GameSettings.Instance.GameState = GlobalGameState.Loading;
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.StartLoading);
        }

        async Task StartGameAsync(CreationType creationType, CancellationToken cancellationToken)
        {
            if (m_LoadingMainMenuCancel != null || GameSettings.Instance.MainMenuSceneLoaded)
            {
                // Unload already created MainMenu world 
                if (m_LoadingMainMenuCancel != null)
                {
                    m_LoadingMainMenuCancel.Cancel();
                    try
                    {
                        await m_LoadingMainMenu;
                    }
                    catch (OperationCanceledException)
                    {
                        // We are ignoring the cancelled exception as it is expected.
                    }
                }

                //Unload created client world by disconnecting and leaving the current session.
                if (GameSettings.Instance.MainMenuSceneLoaded)
                {
                    await DisconnectAndUnloadWorlds();
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            // Connecting to a Multiplayer Session.
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.InitializeConnection);
            switch (creationType)
            {
                case (CreationType.CreateOrJoin): //Relay
                    {
                        GameConnection = await GameConnection.CreateorJoinGameAsync();
                        break;
                    }
                case (CreationType.Host): //Direct connection - host
                    {
                        GameConnection = await GameConnection.HostGameAsync();
                        break;
                    }
                case (CreationType.ConnectAndJoin): //Direct connection - client
                    {
                        GameConnection = await GameConnection.ConnectGameAsync();
                        break;
                    }
            }

            cancellationToken.ThrowIfCancellationRequested();

            World server = null, client = null;
            if (GameConnection.Session != null)
            {
                GameConnection.Session.RemovedFromSession += OnSessionLeft;
                ConnectionSettings.Instance.SessionCode = GameConnection.Session.Code;
                CreateEntityWorlds(GameConnection.Session, GameConnection.SessionConnectionType, out server, out client);
            }
            else
            {
                CreateEntityWorlds(creationType == CreationType.Host, out server, out client);
            }

            // If the server was created successfully, start listening.
            if (server != null)
            {
                using var drvQuery = server.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
                var serverDriver = drvQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW;
                GhostBridgeManager.Instance.SetServerNetworkStreamDriver(serverDriver);

                serverDriver.Listen(GameConnection.ListenEndpoint);
                await ScenesLoader.LoadGameplayAsync(server, null);
            }

            //The the client world was created, connect to the server
            if (client != null)
            {
                ConnectionSettings.Instance.ConnectionEndpoint = GameConnection.ConnectEndpoint;
                await WaitForPlayerConnectionAsync(cancellationToken);
                await ScenesLoader.LoadGameplayAsync(null, client);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (client != null)
            {
                await WaitForGhostReplicationAsync(client, cancellationToken);
            }
        }

        /// <summary>
        /// Wait until the vast majority of ghosts have been spawned.
        /// If we don't do this, we'll see a bunch of ghosts pop in as the scene loads.
        /// </summary>
        /// <param name="world"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task WaitForGhostReplicationAsync(World world, CancellationToken cancellationToken = default)
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WorldReplication);
            using var ghostCountQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GhostCount>());
            var waitedForTicks = 0;
            while (true)
            {
                if (ghostCountQuery.TryGetSingleton<GhostCount>(out var ghostCount))
                {
                    var synchronizingPercentage = ghostCount.GhostCountOnServer == 0
                        ? math.saturate(ghostCount.GhostCountReceivedOnClient / (float)ghostCount.GhostCountOnServer)
                        : waitedForTicks > 60
                            ? 1f
                            : 0f; // The server has no ghosts to replicate, so ghost loading is complete.

                    LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WorldReplication, synchronizingPercentage);
                    if (synchronizingPercentage > 0.99f)
                    {
                        return;
                    }
                }

                await Awaitable.NextFrameAsync(cancellationToken);
                waitedForTicks++;
            }
        }

        static async Task WaitForAttachedCameraAsync(World world, CancellationToken cancellationToken = default)
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WaitingOnPlayer);
            using var mainEntityCameraQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<MainCamera>());
            while (!mainEntityCameraQuery.HasSingleton<MainCamera>())
            {
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            // Waiting an extra frame so that the player position is properly synced with the server.
            await Awaitable.NextFrameAsync(cancellationToken);
        }

        static void CreateEntityWorlds(ISession session, NetworkType connectionType,
            out World serverWorld, out World clientWorld)
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.CreateWorld);
            DestroyLocalSimulationWorld();

#if UNITY_EDITOR
            if (connectionType == NetworkType.Relay && MultiplayerPlayModePreferences.RequestedNumThinClients > 0)
            {
                Debug.Log($"[{nameof(CreateEntityWorlds)}] ThinClient is disabled when Relay connection is used.");
                MultiplayerPlayModePreferences.RequestedNumThinClients = 0;
            }
#endif

            serverWorld = null;
            clientWorld = null;
            if (session.IsHost)
            {
                serverWorld = ClientServerBootstrap.CreateServerWorld("ServerWorld");
                GhostGameObjectUpdateSystem.GatherWorldSystems(serverWorld);
            }

            if (!session.IsServer())
            {
                clientWorld = ClientServerBootstrap.CreateClientWorld("ClientWorld");
                GhostGameObjectUpdateSystem.GatherWorldSystems(clientWorld);
            }
        }

        static void CreateEntityWorlds(bool isHost, out World serverWorld, out World clientWorld)
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.CreateWorld);
            DestroyLocalSimulationWorld();

            serverWorld = null;
            clientWorld = null;
            if (isHost)
            {
                serverWorld = ClientServerBootstrap.CreateServerWorld("ServerWorld");
                GhostGameObjectUpdateSystem.GatherWorldSystems(serverWorld);
            }

            clientWorld = ClientServerBootstrap.CreateClientWorld("ClientWorld");
            GhostGameObjectUpdateSystem.GatherWorldSystems(clientWorld);
#if UNITY_EDITOR
            Debug.Log($"[{nameof(CreateEntityWorlds)}] ThinClient number is {MultiplayerPlayModePreferences.RequestedNumThinClients}");
#endif
        }

        public static async Task WaitForPlayerConnectionAsync(CancellationToken cancellationToken = default)
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WaitingConnection);
            // The GameManagerSystem is handling the connection/reconnection once the client world is created.
            ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Connecting;
            while (ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connecting)
            {
                await Awaitable.NextFrameAsync(cancellationToken);
            }
        }

        void FinishLoadingGame()
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadingDone);
            GameSettings.Instance.GameState = GlobalGameState.InGame;
        }

        /// <summary>
        /// Destroy all local game simulation worlds if any before creating new server/client worlds.
        /// </summary>
        public static void DestroyLocalSimulationWorld()
        {
            foreach (var world in World.All)
            {
                if (world.Flags == WorldFlags.Game)
                {
                    world.Dispose();
                    break;
                }
            }
        }

        public static void SetGameConnection(GameConnection gameConnection)
        {
            GameConnection = gameConnection;
        }

    }
}
