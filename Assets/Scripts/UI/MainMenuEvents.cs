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

        // Initialize network services (also ensures NetworkManager.Singleton is set)
        if (NetworkBootstrap.Instance != null)
        {
            bool initialized = await NetworkBootstrap.Instance.InitializeServicesAsync();
            if (!initialized)
            {
                Debug.LogError("[MainMenuEvents] Failed to initialize network services! " + NetworkBootstrap.Instance.LastServicesError);
                HostLocallyOrGiveUp();
                return;
            }
        }

        // CreateSession + WithRelayNetwork starts NGO host; do not call StartHost separately
        if (SessionManager.Instance != null)
        {
            bool sessionCreated = await SessionManager.Instance.CreateSessionAsync("PartyGame", 4);
            if (!sessionCreated)
            {
                Debug.LogError("[MainMenuEvents] Failed to create session!");
                HostLocallyOrGiveUp();
                return;
            }
        }

        SceneManager.LoadScene(LobbySceneName, LoadSceneMode.Single);
    }

    const string LobbySceneName = "Lobby";

    /// <summary>Join code that connects directly to a host on this machine (development, no Unity Services).</summary>
    const string LocalJoinCode = "LOCAL";

    /// <summary>
    /// Development fallback when Unity Services or Relay are unavailable: host over direct transport so the
    /// lobby → game loop can still be played. Friends on this machine/LAN join with "LOCAL" or the host's IP.
    /// </summary>
    void HostLocallyOrGiveUp()
    {
        if (!NetworkBootstrap.LocalFallbackAllowed || NetworkBootstrap.Instance == null) return;
        if (!NetworkBootstrap.Instance.StartLocalHost()) return;
        Debug.LogWarning("[MainMenuEvents] Unity Services unavailable: hosting LOCALLY. Others on this machine join with code " +
                         LocalJoinCode + ", or with this computer's IP on the LAN.");
        SceneManager.LoadScene(LobbySceneName, LoadSceneMode.Single);
    }

    /// <summary>"LOCAL" or an IPv4 address means a direct connection instead of a Relay session code.</summary>
    static bool TryGetDirectAddress(string code, out string address)
    {
        address = null;
        if (string.Equals(code, LocalJoinCode, System.StringComparison.OrdinalIgnoreCase)) { address = "127.0.0.1"; return true; }
        if (System.Net.IPAddress.TryParse(code, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            address = ip.ToString();
            return true;
        }
        return false;
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

        if (TryGetDirectAddress(sessionCode, out string directAddress))
        {
            if (!NetworkBootstrap.LocalFallbackAllowed || NetworkBootstrap.Instance == null || !NetworkBootstrap.Instance.StartLocalClient(directAddress))
            {
                Debug.LogError("[MainMenuEvents] Direct join to " + directAddress + " failed (development builds only).");
                return;
            }

            if (joinPanel != null)
            {
                joinPanel.style.display = DisplayStyle.None;
            }

            SceneManager.LoadScene(LobbySceneName, LoadSceneMode.Single);
            return;
        }

        if (NetworkBootstrap.Instance != null)
        {
            bool initialized = await NetworkBootstrap.Instance.InitializeServicesAsync();
            if (!initialized)
            {
                Debug.LogError("[MainMenuEvents] Failed to initialize network services! " + NetworkBootstrap.Instance.LastServicesError +
                               (NetworkBootstrap.LocalFallbackAllowed ? " To join a local host instead, enter the code " + LocalJoinCode + "." : ""));
                return;
            }
        }

        // JoinSession starts NGO client via the session's Relay network; do not call StartClient separately
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

        SceneManager.LoadScene(LobbySceneName, LoadSceneMode.Single);
    }

    private void OnAllButtonsClicked(ClickEvent evt)
    {
        // For each button like transition/ sounds etc
    }
}
