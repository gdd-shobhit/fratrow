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

    private List<Button> menuButtons = new List<Button>();

    private void Awake()
    {
        document = GetComponent<UIDocument>();
        startButton = document.rootVisualElement.Q("Start") as Button;
        
        if (startButton != null)
        {
            startButton.RegisterCallback<ClickEvent>(OnStartButtonClicked);
        }

        // Create Join button if it doesn't exist
        joinButton = document.rootVisualElement.Q("Join") as Button;
        if (joinButton == null)
        {
            var buttonContainer = startButton?.parent;
            if (buttonContainer != null)
            {
                joinButton = new Button();
                joinButton.text = "Join";
                joinButton.name = "Join";
                joinButton.style.marginLeft = 20;
                buttonContainer.Add(joinButton);
            }
        }

        if (joinButton != null)
        {
            joinButton.RegisterCallback<ClickEvent>(OnJoinButtonClicked);
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
        for (int i = 0; i < menuButtons.Count; ++i)
        {
            menuButtons[i].UnregisterCallback<ClickEvent>(OnAllButtonsClicked);
        }
    }

    private async void OnStartButtonClicked(ClickEvent evt)
    {
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

    private async void OnJoinButtonClicked(ClickEvent evt)
    {
        Debug.Log("[MainMenuEvents] Join button clicked - Opening join dialog...");

        // TODO: Show UI dialog to enter session code
        // For now, we'll use a simple input field or hardcoded test
        string sessionCode = ""; // Get from UI input - TODO: Implement UI input

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

        // Join session
        if (SessionManager.Instance != null && !string.IsNullOrEmpty(sessionCode))
        {
            bool sessionJoined = await SessionManager.Instance.JoinSessionAsync(sessionCode);
            if (!sessionJoined)
            {
                Debug.LogError("[MainMenuEvents] Failed to join session!");
                return;
            }
        }

        // Start as client
        if (NetworkBootstrap.Instance != null)
        {
            NetworkBootstrap.Instance.StartClient();
        }

        // Load lobby scene
        SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
    }

    private void OnAllButtonsClicked(ClickEvent evt)
    {
        // For each button like transition/ sounds etc
    }
}
