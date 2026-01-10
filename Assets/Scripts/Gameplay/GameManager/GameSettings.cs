using System;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.FPSSample_2
{
    public enum GlobalGameState
    {
        MainMenu,
        InGame,
        Loading,
    }

    public enum MainMenuState
    {
        MainMenuScreen,
        StartHostPopup,
        DirectConnectPopUp,
        JoinCodePopUp,
    }

    public enum PlayerState
    {
        IsPlaying,
        IsDead,
    }
   
    public class GameSettings : INotifyBindablePropertyChanged
    {
        public static GameSettings Instance { get; private set; } = null!;

        /// <summary>
        /// This INitialOnloadMethod instantiate the singletone instance 
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RuntimeInitializeOnLoad() => Instance = new GameSettings();

        const string k_PlayerNameKey = "PlayerName";
        const string k_PlayerCharacterKey = "PlayerCharacer";
        const string k_ConnectionModeKey = "ConnectionMode";
        const string k_SessionNameKey = "SessionName";

        GameSettings()
        {
            m_PlayerName = PlayerPrefs.GetString(k_PlayerNameKey, Environment.UserName);
            m_PlayerCharacter = PlayerPrefs.GetInt(k_PlayerCharacterKey, 0);  
            m_ConnectionMode = PlayerPrefs.GetInt(k_ConnectionModeKey, 0);
            m_SessionName = PlayerPrefs.GetString(k_SessionNameKey, "default-session");
        }

        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;
        void Notify([CallerMemberName] string property = "") =>
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));

        public AwaitableCompletionSource CancellableUserInputPopUp;
        
        GlobalGameState m_GameState;
        public GlobalGameState GameState
        {
            get => m_GameState;
            set
            {
                if (m_GameState == value)
                {
                    return;
                }

                m_GameState = value;

                Notify(MainMenuStylePropertyName);
                Notify(LoadingScreenStylePropertyName);
                Notify(InGameUIPropertyName);
            }
        }
        
        MainMenuState m_MainMenuState;
        public MainMenuState MainMenuState
        {
            get => m_MainMenuState;
            set
            {
                if (m_MainMenuState == value)
                {
                    return;
                }

                m_MainMenuState = value;
                Notify(MainMenuStylePropertyName);
                Notify(JoinSessionStylePropertyName);
                Notify(StartHostStylePropertyName);
                Notify(DirectConnectStylePropertyName);
            }
        }
        
        PlayerState m_PlayerState;
        public PlayerState PlayerState
        {
            get => m_PlayerState;
            set
            {
                if (m_PlayerState == value)
                    return;

                m_PlayerState = value;
                //Notify(RespawnScreenStylePropertyName); //TODO: Define the respawn screen style
            }
        }

        public static readonly string MainMenuStylePropertyName = nameof(MainMenuStyle);
        [CreateProperty]
        DisplayStyle MainMenuStyle => m_GameState == GlobalGameState.MainMenu && 
                                      MainMenuState == MainMenuState.MainMenuScreen ? 
                                      DisplayStyle.Flex : DisplayStyle.None;

        public static readonly string JoinSessionStylePropertyName = nameof(JoinSessionStyle);
        [CreateProperty]
        DisplayStyle JoinSessionStyle => m_GameState == GlobalGameState.MainMenu && 
                                         MainMenuState == MainMenuState.JoinCodePopUp ? 
                                         DisplayStyle.Flex : DisplayStyle.None;

        public static readonly string StartHostStylePropertyName = nameof(StartHostStyle);
        [CreateProperty]
        DisplayStyle StartHostStyle => m_GameState == GlobalGameState.MainMenu && 
                                       MainMenuState == MainMenuState.StartHostPopup ? 
                                       DisplayStyle.Flex : DisplayStyle.None;

        public static readonly string DirectConnectStylePropertyName = nameof(DirectConnectStyle);
        [CreateProperty]
        DisplayStyle DirectConnectStyle => m_GameState == GlobalGameState.MainMenu && 
                                           MainMenuState == MainMenuState.DirectConnectPopUp ? 
                                           DisplayStyle.Flex : DisplayStyle.None;

        public static readonly string LoadingScreenStylePropertyName = nameof(LoadingScreenStyle);
        [CreateProperty]
        DisplayStyle LoadingScreenStyle
        {
            get
            {
                if (m_GameState == GlobalGameState.Loading)
                {
                    return DisplayStyle.Flex;
                }

                LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.NotLoading,  0.0f);
                return DisplayStyle.None;
            }
        }

        public static readonly string InGameUIPropertyName = nameof(InGameUI);
        [CreateProperty]
        DisplayStyle InGameUI => m_GameState == GlobalGameState.InGame ? DisplayStyle.Flex : DisplayStyle.None;

        bool m_IsPauseMenuOpen = false;
        public bool IsPauseMenuOpen
        {
            get => m_IsPauseMenuOpen;
            set
            {
                if (m_IsPauseMenuOpen == value)
                {
                    return;
                }

                m_IsPauseMenuOpen = value;
                Utils.SetCursorVisible(value);
                Notify(PauseMenuStylePropertyName);
                Notify(MobileControlsOpacityPropertyName);
            }
        }
        public static readonly string PauseMenuStylePropertyName = nameof(PauseMenuStyle);
        [CreateProperty]
        public DisplayStyle PauseMenuStyle => IsPauseMenuOpen ? DisplayStyle.Flex : DisplayStyle.None;

        public static readonly string MobileControlsOpacityPropertyName = nameof(MobileControlsPauseMenuOpacity);
        [CreateProperty]
        public StyleFloat MobileControlsPauseMenuOpacity => IsPauseMenuOpen ? 0.5f : 1f;

        string m_PlayerName;
        [CreateProperty]
        public string PlayerName
        {
            get => m_PlayerName;
            set
            {
                if (m_PlayerName == value)
                {
                    return;
                }

                m_PlayerName = value;
                PlayerPrefs.SetString(k_PlayerNameKey, value);
            }
        }

        private int m_PlayerCharacter = 0;
        [CreateProperty]
        public int PlayerCharacter
        {
            get => m_PlayerCharacter;
            set
            {
                if (m_PlayerCharacter == value)
                {
                    return;
                }

                m_PlayerCharacter = value;
                PlayerPrefs.SetInt(k_PlayerCharacterKey, value);
            }
        }
        
        int m_ConnectionMode = 0;
        [CreateProperty]
        public int ConnectionMode
        {
            get => m_ConnectionMode;
            set
            {
                if (m_ConnectionMode == value)
                    return;

                m_ConnectionMode = value;
                PlayerPrefs.SetInt(k_ConnectionModeKey, value);
            }
        }

        string m_SessionName = "default-session";
        [CreateProperty]
        public string SessionName
        {
            get => m_SessionName;
            set
            {
                if (m_SessionName == value)
                    return;

                m_SessionName = value;
                PlayerPrefs.SetString(k_SessionNameKey, value);
            }
        }

        public static readonly string MainMenuSceneLoadedPropertyName = nameof(MainMenuSceneLoadedStyle);
        [CreateProperty]
        DisplayStyle MainMenuSceneLoadedStyle => m_MainMenuSceneLoaded ? DisplayStyle.None : DisplayStyle.Flex;

        bool m_MainMenuSceneLoaded;
        public bool MainMenuSceneLoaded
        {
            get => m_MainMenuSceneLoaded;
            set
            {
                if (m_MainMenuSceneLoaded == value)
                    return;

                m_MainMenuSceneLoaded = value;
                Notify(MainMenuSceneLoadedPropertyName);
            }
        }
    }
}
