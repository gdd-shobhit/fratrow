using System;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.FPSSample_2
{
    public partial class GameManager : MonoBehaviour
    {
        /// <summary>
        /// Safe return to main menu, can be called by the pause menu button.
        /// </summary>
        public async void ReturnToMainMenuAsync()
        {
            Debug.Log($"[{nameof(ReturnToMainMenuAsync)}] Called.");
            if (!CanUseMainMenu)
            {
                QuitAsync();
                return;
            }
            
            if (m_LoadingGameCancel != null)
            {
                Debug.Log($"[{nameof(ReturnToMainMenuAsync)}] Cancelling loading game.");
                m_LoadingGameCancel.Cancel();
                try
                {
                    await m_LoadingGame;
                }
                catch (OperationCanceledException)
                {
                    // Discarding this exception because we're the one asking for it.
                }
                Debug.Log($"[{nameof(ReturnToMainMenuAsync)}] Loading Cancelled, start returning to main menu.");
            }
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.UnloadingGame);
            GameSettings.Instance.GameState = GlobalGameState.Loading;
            
            GameSettings.Instance.IsPauseMenuOpen = false;
            await DisconnectAndUnloadWorlds();
            
            // Restart the main menu scene.
            Start();
            
            Utils.SetCursorVisible(true);
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.BackToMainMenu);
            GameSettings.Instance.GameState = GlobalGameState.MainMenu;
        }
        
        /// <summary>
        /// Safe shutdown of the game. Saves everything that needs to be saved.
        /// </summary>
        public async void QuitAsync()
        {
            await LeaveSessionAsync();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    
        async Task LeaveSessionAsync()
        {
            if (GameConnection == null || GameConnection.Session == null)
            {
                return;
            }

            GameConnection.Session.RemovedFromSession -= OnSessionLeft;
            if (GameConnection.Session.IsHost || GameConnection.Session.IsServer())
            {
                //ConnectionSettings.Instance.SessionCode = null;
            }

            if (GameConnection.Session.IsHost)
            {
                await GameConnection.Session.AsHost().DeleteAsync();
            }
            else
            {
                await GameConnection.Session.LeaveAsync();
            }

            GameConnection = null;
        }
        
        void OnSessionLeft()
        {
            GameConnection = null;
            ReturnToMainMenuAsync();
        }
        
        static async Task DestroyGameSessionWorlds()
        {
            // This prevents the "Cannot dispose world while updating it" error,
            // allowing us to call this from anywhere.
            await Awaitable.EndOfFrameAsync();

            // Destroy netcode worlds:
            for (var i = World.All.Count - 1; i >= 0; i--)
            {
                var world = World.All[i];
                if (world.IsServer() || world.IsClient())
                {
                    world.Dispose();
                }
            }
        }
        
        async Task DisconnectAndUnloadWorlds()
        {
            NetworkStreamReceiveSystem.DriverConstructor = null;
            ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Disconnected;

            bool requestedDisconnect = false;
            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
                    if (query.TryGetSingletonEntity<NetworkId>(out var networkId))
                    {
                        requestedDisconnect = true;
                        world.EntityManager.AddComponentData(networkId, new NetworkStreamRequestDisconnect());
                    }
                }
            }

            if (requestedDisconnect)
            {
                await Awaitable.NextFrameAsync();
            }

            await LeaveSessionAsync();
            await DestroyGameSessionWorlds();
            
            if (GhostBridgeBootstrap.Instance != null)
            {
                // Destroy all "Server" and "Client" GameObjects and all their children.
                Debug.Log($"[{nameof(DisconnectAndUnloadWorlds)}] Destroying multiplayer worlds via GhostBridgeBootstrap.");
                GhostBridgeBootstrap.Instance.DestroyMultiplayerWorlds();
            }
            else
            {
                // Fallback in case bootstrap instance is lost
                Debug.Log($"[{nameof(DisconnectAndUnloadWorlds)}] InvokeAndClearStaticCallbacks");
                GhostSingleton.InvokeAndClearStaticCallbacks();
            }
            
            await ScenesLoader.UnloadGameplayScenesAsync();
        }
    }
}