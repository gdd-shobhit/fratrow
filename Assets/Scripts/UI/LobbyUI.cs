using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

/// <summary>
/// Binds the Lobby UXML to LobbyManager and SessionManager.
/// Shows session code, player list, and wires name/class/ready/start/leave.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    private UIDocument _document;
    private Label _sessionCodeLabel;
    private VisualElement _playerListRoot;
    private TextField _playerNameField;
    private DropdownField _classDropdown;
    private Button _readyButton;
    private Button _startGameButton;
    private Button _leaveButton;

    private void Awake()
    {
        TryResolveDocument();
    }

    /// <summary>
    /// Find UIDocument on this GameObject or anywhere in the scene so LobbyUI works
    /// whether it sits on the same object as UIDocument or not.
    /// </summary>
    private void TryResolveDocument()
    {
        if (_document != null && _document.rootVisualElement != null)
            return;

        _document = GetComponent<UIDocument>();
        if (_document == null)
            _document = FindFirstObjectByType<UIDocument>();

        if (_document == null)
        {
            Debug.LogWarning("[LobbyUI] No UIDocument found. Add a UIDocument to the Lobby scene (e.g. with Source: LobbyVisualTree.uxml) and ensure LobbyUI is in the scene.");
            return;
        }

        if (_document.rootVisualElement == null)
        {
            Debug.LogWarning("[LobbyUI] UIDocument has no rootVisualElement yet. Will retry in OnEnable.");
            return;
        }

        BindElements();
    }

    private void BindElements()
    {
        if (_document == null || _document.rootVisualElement == null) return;

        var root = _document.rootVisualElement;
        _sessionCodeLabel = root.Q<Label>("SessionCode");
        _playerListRoot = root.Q("PlayerList");
        _playerNameField = root.Q<TextField>("PlayerNameField");
        _classDropdown = root.Q<DropdownField>("ClassDropdown");
        _readyButton = root.Q<Button>("ReadyButton");
        _startGameButton = root.Q<Button>("StartGameButton");
        _leaveButton = root.Q<Button>("LeaveButton");

        if (_sessionCodeLabel == null)
            Debug.LogWarning("[LobbyUI] SessionCode label not found. Is UIDocument Source set to LobbyVisualTree.uxml?");
        if (_playerListRoot == null)
            Debug.LogWarning("[LobbyUI] PlayerList not found. Is UIDocument Source set to LobbyVisualTree.uxml?");
    }

    private void OnEnable()
    {
        TryResolveDocument();
        StartCoroutine(WaitForUIThenBind());
    }

    /// <summary>
    /// UI Toolkit creates rootVisualElement after the document is enabled; wait for it then bind and refresh.
    /// </summary>
    private IEnumerator WaitForUIThenBind()
    {
        if (_document == null)
        {
            _document = FindFirstObjectByType<UIDocument>();
            if (_document == null)
            {
                Debug.LogWarning("[LobbyUI] No UIDocument in scene.");
                yield break;
            }
        }

        for (int i = 0; i < 10; i++)
        {
            if (_document.rootVisualElement != null)
                break;
            yield return null;
        }

        if (_document.rootVisualElement == null)
        {
            Debug.LogWarning("[LobbyUI] UIDocument.rootVisualElement still null after waiting.");
            yield break;
        }

        BindElements();
        if (_sessionCodeLabel == null)
        {
            Debug.LogWarning("[LobbyUI] SessionCode not found in tree. Check LobbyVisualTree.uxml has name=\"SessionCode\".");
            yield break;
        }

        SubscribeToLobbyManager();
        RefreshSessionCode();
        RefreshPlayerList();

        if (_playerNameField != null)
        {
            string saved = PlayerPrefs.GetString("PlayerName", "Player");
            _playerNameField.SetValueWithoutNotify(saved);
            _playerNameField.RegisterValueChangedCallback(OnPlayerNameChanged);
        }

        if (_classDropdown != null)
            _classDropdown.RegisterValueChangedCallback(OnClassChanged);

        if (_readyButton != null)
            _readyButton.clicked += OnReadyClicked;
        if (_startGameButton != null)
            _startGameButton.clicked += OnStartGameClicked;
        if (_leaveButton != null)
            _leaveButton.clicked += OnLeaveClicked;

        // Grace period: refresh again so we catch the host once LobbyManager has registered
        StartCoroutine(RefreshAfterDelay(0.5f));
        StartCoroutine(RefreshAfterDelay(1.5f));
        StartCoroutine(RefreshAfterDelay(3f));
    }

    private void OnDisable()
    {
        UnsubscribeFromLobbyManager();
        if (_playerNameField != null)
            _playerNameField.UnregisterValueChangedCallback(OnPlayerNameChanged);
        if (_classDropdown != null)
            _classDropdown.UnregisterValueChangedCallback(OnClassChanged);
        if (_readyButton != null)
            _readyButton.clicked -= OnReadyClicked;
        if (_startGameButton != null)
            _startGameButton.clicked -= OnStartGameClicked;
        if (_leaveButton != null)
            _leaveButton.clicked -= OnLeaveClicked;
    }

    private void SubscribeToLobbyManager()
    {
        if (LobbyManager.Instance == null) return;
        var lm = LobbyManager.Instance;
        lm.OnPlayerAdded += OnPlayerAdded;
        lm.OnPlayerRemoved += OnPlayerRemoved;
        lm.OnPlayerNameChanged += OnPlayerNameChangedFromNetwork;
        lm.OnPlayerClassChanged += OnPlayerClassChangedFromNetwork;
        lm.OnPlayerReadyChanged += OnPlayerReadyChangedFromNetwork;
    }

    private void UnsubscribeFromLobbyManager()
    {
        if (LobbyManager.Instance == null) return;
        var lm = LobbyManager.Instance;
        lm.OnPlayerAdded -= OnPlayerAdded;
        lm.OnPlayerRemoved -= OnPlayerRemoved;
        lm.OnPlayerNameChanged -= OnPlayerNameChangedFromNetwork;
        lm.OnPlayerClassChanged -= OnPlayerClassChangedFromNetwork;
        lm.OnPlayerReadyChanged -= OnPlayerReadyChangedFromNetwork;
    }

    private void OnPlayerAdded(LobbyPlayerData _) => RefreshPlayerList();
    private void OnPlayerRemoved(ulong _) => RefreshPlayerList();
    private void OnPlayerNameChangedFromNetwork(ulong _, string __) => RefreshPlayerList();
    private void OnPlayerClassChangedFromNetwork(ulong _, PlayerClass __) => RefreshPlayerList();
    private void OnPlayerReadyChangedFromNetwork(ulong _, bool __) => RefreshPlayerList();

    private void RefreshSessionCode()
    {
        if (_sessionCodeLabel == null) return;
        string code = SessionManager.Instance != null ? SessionManager.Instance.GetSessionCode() : "";
        _sessionCodeLabel.text = string.IsNullOrEmpty(code) ? "----" : code;
        if (!string.IsNullOrEmpty(code))
            Debug.Log($"[LobbyUI] Session code displayed in UI: {code}");
    }

    private void RefreshPlayerList()
    {
        if (_playerListRoot == null) return;

        _playerListRoot.Clear();

        if (LobbyManager.Instance == null)
            return;

        foreach (var kvp in LobbyManager.Instance.Players)
        {
            var data = kvp.Value;
            var row = new Label(FormatPlayerLine(data));
            row.style.color = Color.white;
            row.style.fontSize = 20;
            row.style.marginBottom = 8;
            row.style.unityTextAlign = TextAnchor.MiddleLeft;
            _playerListRoot.Add(row);
        }
    }

    private static string FormatPlayerLine(LobbyPlayerData data)
    {
        string host = data.IsHost ? " (Host)" : "";
        string ready = data.IsReady ? " [Ready]" : "";
        return $"{data.PlayerName}{host} - {data.SelectedClass}{ready}";
    }

    private IEnumerator RefreshAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        RefreshSessionCode();
        RefreshPlayerList();
    }

    private void OnPlayerNameChanged(ChangeEvent<string> evt)
    {
        string name = evt.newValue?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return;
        PlayerPrefs.SetString("PlayerName", name);
        LobbyManager.Instance?.RequestSetPlayerName(name);
    }

    private void OnClassChanged(ChangeEvent<string> evt)
    {
        if (System.Enum.TryParse<PlayerClass>(evt.newValue, out var pc))
            LobbyManager.Instance?.RequestSetPlayerClass(pc);
    }

    private void OnReadyClicked()
    {
        // Toggle ready: would need local state or ask LobbyManager for current; for now just send true
        LobbyManager.Instance?.RequestSetReadyStatus(true);
    }

    private void OnStartGameClicked()
    {
        LobbyManager.Instance?.RequestStartGame();
    }

    private void OnLeaveClicked()
    {
        LobbyManager.Instance?.LeaveLobby();
    }
}
