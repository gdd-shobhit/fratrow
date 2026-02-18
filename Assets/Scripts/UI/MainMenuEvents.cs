using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class MainMenuEvents : MonoBehaviour
{
    private UIDocument document;
    private Button startButton;
    private Button joinButton;
    private VisualElement joinPanel;
    private TextField sessionCodeField;
    private Button joinConfirmButton;
    private Button joinCancelButton;

    private List<Button> menuButtons = new List<Button>();

    private void Awake()
    {
        document = GetComponent<UIDocument>();
        startButton = document.rootVisualElement.Q("Start") as Button;
        
        if (startButton != null)
        {
            startButton.RegisterCallback<ClickEvent>(OnStartButtonClicked);
        }

        joinButton = document.rootVisualElement.Q("Join") as Button;
        joinPanel = document.rootVisualElement.Q("JoinPanel");
        sessionCodeField = document.rootVisualElement.Q<TextField>("SessionCodeField");
        joinConfirmButton = document.rootVisualElement.Q<Button>("JoinConfirmButton");
        joinCancelButton = document.rootVisualElement.Q<Button>("JoinCancelButton");

        if (joinButton != null)
        {
            joinButton.RegisterCallback<ClickEvent>(OnJoinButtonClicked);
        }
        if (joinConfirmButton != null)
        {
            joinConfirmButton.RegisterCallback<ClickEvent>(OnJoinConfirmClicked);
        }
        if (joinCancelButton != null)
        {
            joinCancelButton.RegisterCallback<ClickEvent>(OnJoinCancelClicked);
        }

        menuButtons = document.rootVisualElement.Query<Button>().ToList();
        for(int i = 0; i < menuButtons.Count; ++i)
        {
            menuButtons[i].RegisterCallback<ClickEvent>(OnAllButtonsClicked); 
        }
    }

    private void OnDisable()
    {
        if (startButton != null)
        {
            startButton.UnregisterCallback<ClickEvent>(OnStartButtonClicked);
        }
        if (joinButton != null)
        {
            joinButton.UnregisterCallback<ClickEvent>(OnJoinButtonClicked);
        }
        if (joinConfirmButton != null)
        {
            joinConfirmButton.UnregisterCallback<ClickEvent>(OnJoinConfirmClicked);
        }
        if (joinCancelButton != null)
        {
            joinCancelButton.UnregisterCallback<ClickEvent>(OnJoinCancelClicked);
        }
        for (int i = 0; i < menuButtons.Count; ++i)
        {
            menuButtons[i].UnregisterCallback<ClickEvent>(OnAllButtonsClicked);
        }
    }

    private async void OnStartButtonClicked(ClickEvent evt)
    {
        if (ParrelSyncRuntime.IsParrelSyncClone)
        {
            Debug.Log("[MainMenuEvents] ParrelSync clone cannot host – use Join to connect to the host.");
            return;
        }
        Debug.Log("[MainMenuEvents] Start button clicked - Creating session as host...");

        // Initialize network services
        if (NetworkBootstrap.Instance != null)
        {
            bool initialized = await NetworkBootstrap.Instance.InitializeServicesAsync();
            if (!initialized)
            {
                Debug.LogError("[MainMenuEvents] Failed to initialize network services!");
                return;
            }
        }

        // Create session
        if (SessionManager.Instance != null)
        {
            bool sessionCreated = await SessionManager.Instance.CreateSessionAsync("PartyGame", 4);
            if (!sessionCreated)
            {
                Debug.LogError("[MainMenuEvents] Failed to create session!");
                return;
            }
        }

        // Start as host
        if (NetworkBootstrap.Instance != null)
        {
            NetworkBootstrap.Instance.StartHost();
        }

        // Load lobby scene
        SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
    }

    private void OnJoinButtonClicked(ClickEvent evt)
    {
        if (joinPanel != null)
        {
            joinPanel.style.display = DisplayStyle.Flex;
            if (sessionCodeField != null)
            {
                sessionCodeField.value = "";
                sessionCodeField.Focus();
            }
        }
    }

    private void OnJoinCancelClicked(ClickEvent evt)
    {
        if (joinPanel != null)
        {
            joinPanel.style.display = DisplayStyle.None;
        }
    }

    private async void OnJoinConfirmClicked(ClickEvent evt)
    {
        string sessionCode = sessionCodeField != null ? sessionCodeField.value?.Trim().ToUpperInvariant() ?? "" : "";
        if (string.IsNullOrEmpty(sessionCode))
        {
            Debug.LogWarning("[MainMenuEvents] Please enter a session code.");
            return;
        }

        Debug.Log($"[MainMenuEvents] Joining session: {sessionCode}");

        if (NetworkBootstrap.Instance != null)
        {
            bool initialized = await NetworkBootstrap.Instance.InitializeServicesAsync();
            if (!initialized)
            {
                Debug.LogError("[MainMenuEvents] Failed to initialize network services!");
                return;
            }
        }

        if (SessionManager.Instance != null)
        {
            bool sessionJoined = await SessionManager.Instance.JoinSessionAsync(sessionCode);
            if (!sessionJoined)
            {
                Debug.LogError("[MainMenuEvents] Failed to join session! Check the code and try again.");
                return;
            }
        }

        if (joinPanel != null)
        {
            joinPanel.style.display = DisplayStyle.None;
        }

        if (NetworkBootstrap.Instance != null)
        {
            // Wait for LobbySystems to spawn on client before loading Lobby scene (avoids LobbyManager null)
            NetworkBootstrap.Instance.StartClient();    
            SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
        }
    }

    private void OnAllButtonsClicked(ClickEvent evt)
    {
        // For each button like transition/ sounds etc
    }
}
