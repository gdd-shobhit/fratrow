using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI controller for the lobby screen.
/// Handles player list display, class selection, ready status, and lobby actions.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    private UIDocument _document;
    private VisualElement _root;

    // UI Elements
    private Label _sessionCodeLabel;
    private VisualElement _playerList;
    private DropdownField _classDropdown;
    private Button _readyButton;
    private Button _startGameButton;
    private Button _inviteButton;
    private Button _leaveButton;

    // Player slot templates (created dynamically)
    private Dictionary<ulong, VisualElement> _playerSlots = new Dictionary<ulong, VisualElement>();

    // Local state
    private bool _isReady = false;
    private PlayerClass _selectedClass = PlayerClass.President;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        if (_document == null)
        {
            Debug.LogError("[LobbyUI] UIDocument component not found!");
            return;
        }

        _root = _document.rootVisualElement;
        SetupUIElements();
        RegisterCallbacks();
    }

    private void OnEnable()
    {
        // Subscribe to lobby manager events
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnPlayerAdded += OnPlayerAdded;
            LobbyManager.Instance.OnPlayerRemoved += OnPlayerRemoved;
            LobbyManager.Instance.OnPlayerClassChanged += OnPlayerClassChanged;
            LobbyManager.Instance.OnPlayerReadyChanged += OnPlayerReadyChanged;
        }

        // Update UI when enabled
        RefreshUI();
    }

    private void OnDisable()
    {
        // Unsubscribe from events
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnPlayerAdded -= OnPlayerAdded;
            LobbyManager.Instance.OnPlayerRemoved -= OnPlayerRemoved;
            LobbyManager.Instance.OnPlayerClassChanged -= OnPlayerClassChanged;
            LobbyManager.Instance.OnPlayerReadyChanged -= OnPlayerReadyChanged;
        }

        UnregisterCallbacks();
    }

    private void SetupUIElements()
    {
        _sessionCodeLabel = _root.Q<Label>("SessionCode");
        _playerList = _root.Q<VisualElement>("PlayerList");
        _classDropdown = _root.Q<DropdownField>("ClassDropdown");
        _readyButton = _root.Q<Button>("ReadyButton");
        _startGameButton = _root.Q<Button>("StartGameButton");
        _inviteButton = _root.Q<Button>("InviteButton");
        _leaveButton = _root.Q<Button>("LeaveButton");

        // Setup class dropdown
        if (_classDropdown != null)
        {
            _classDropdown.choices = new List<string> { "President", "SocialChair", "Chad", "RichGuy" };
            _classDropdown.index = 0;
        }
    }

    private void RegisterCallbacks()
    {
        if (_classDropdown != null)
        {
            _classDropdown.RegisterValueChangedCallback(OnClassSelected);
        }

        if (_readyButton != null)
        {
            _readyButton.RegisterCallback<ClickEvent>(OnReadyButtonClicked);
        }

        if (_startGameButton != null)
        {
            _startGameButton.RegisterCallback<ClickEvent>(OnStartGameButtonClicked);
        }

        if (_inviteButton != null)
        {
            _inviteButton.RegisterCallback<ClickEvent>(OnInviteButtonClicked);
        }

        if (_leaveButton != null)
        {
            _leaveButton.RegisterCallback<ClickEvent>(OnLeaveButtonClicked);
        }
    }

    private void UnregisterCallbacks()
    {
        if (_classDropdown != null)
        {
            _classDropdown.UnregisterValueChangedCallback(OnClassSelected);
        }

        if (_readyButton != null)
        {
            _readyButton.UnregisterCallback<ClickEvent>(OnReadyButtonClicked);
        }

        if (_startGameButton != null)
        {
            _startGameButton.UnregisterCallback<ClickEvent>(OnStartGameButtonClicked);
        }

        if (_inviteButton != null)
        {
            _inviteButton.UnregisterCallback<ClickEvent>(OnInviteButtonClicked);
        }

        if (_leaveButton != null)
        {
            _leaveButton.UnregisterCallback<ClickEvent>(OnLeaveButtonClicked);
        }
    }

    private void RefreshUI()
    {
        UpdateSessionCode();
        UpdatePlayerList();
        UpdateStartGameButton();
        UpdateInviteButton();
    }

    private void UpdateSessionCode()
    {
        if (_sessionCodeLabel != null && SessionManager.Instance != null)
        {
            string code = SessionManager.Instance.GetSessionCode();
            _sessionCodeLabel.text = string.IsNullOrEmpty(code) ? "----" : code;
        }
    }

    private void UpdatePlayerList()
    {
        if (_playerList == null || LobbyManager.Instance == null) return;

        // Clear existing player slots
        _playerList.Clear();
        _playerSlots.Clear();

        // Create player slots for each player
        foreach (var kvp in LobbyManager.Instance.Players)
        {
            CreatePlayerSlot(kvp.Value);
        }
    }

    private void CreatePlayerSlot(LobbyPlayerData playerData)
    {
        var slot = new VisualElement();
        slot.name = $"PlayerSlot_{playerData.NetworkId}";
        slot.style.flexDirection = FlexDirection.Row;
        slot.style.alignItems = Align.Center;
        slot.style.justifyContent = Justify.SpaceBetween;
        slot.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        slot.style.paddingTop = 10;
        slot.style.paddingBottom = 10;
        slot.style.paddingLeft = 20;
        slot.style.paddingRight = 20;
        slot.style.marginBottom = 10;
        slot.style.borderTopLeftRadius = 5;
        slot.style.borderTopRightRadius = 5;
        slot.style.borderBottomLeftRadius = 5;
        slot.style.borderBottomRightRadius = 5;

        // Player name
        var nameLabel = new Label(playerData.PlayerName);
        nameLabel.style.fontSize = 20;
        nameLabel.style.color = Color.white;
        nameLabel.style.flexGrow = 1;

        // Player class
        var classLabel = new Label(playerData.SelectedClass.ToString());
        classLabel.style.fontSize = 18;
        classLabel.style.color = new Color(0.8f, 0.8f, 1f);
        classLabel.style.width = 150;

        // Ready status
        var readyLabel = new Label(playerData.IsReady ? "✓ Ready" : "Not Ready");
        readyLabel.style.fontSize = 18;
        readyLabel.style.color = playerData.IsReady ? Color.green : Color.yellow;
        readyLabel.style.width = 120;

        // Host indicator
        if (playerData.IsHost)
        {
            var hostLabel = new Label("(Host)");
            hostLabel.style.fontSize = 16;
            hostLabel.style.color = Color.cyan;
            hostLabel.style.width = 80;
            slot.Add(hostLabel);
        }

        slot.Add(nameLabel);
        slot.Add(classLabel);
        slot.Add(readyLabel);

        _playerList.Add(slot);
        _playerSlots[playerData.NetworkId] = slot;
    }

    private void UpdateStartGameButton()
    {
        if (_startGameButton == null) return;

        bool isHost = SessionManager.Instance != null && SessionManager.Instance.IsHost();
        bool allReady = LobbyManager.Instance != null && LobbyManager.Instance.CanStartGame();

        _startGameButton.SetEnabled(isHost && allReady);
        _startGameButton.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void UpdateInviteButton()
    {
        if (_inviteButton == null) return;

        bool isHost = SessionManager.Instance != null && SessionManager.Instance.IsHost();
        _inviteButton.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnClassSelected(ChangeEvent<string> evt)
    {
        string className = evt.newValue;
        if (System.Enum.TryParse<PlayerClass>(className, out PlayerClass playerClass))
        {
            _selectedClass = playerClass;
            
            // Send to server
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.RequestSetPlayerClass(_selectedClass);
            }
        }
    }

    private void OnReadyButtonClicked(ClickEvent evt)
    {
        _isReady = !_isReady;
        
        // Update button text
        if (_readyButton != null)
        {
            _readyButton.text = _isReady ? "Not Ready" : "Ready";
        }

        // Send to server
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.RequestSetReadyStatus(_isReady);
        }
    }

    private void OnStartGameButtonClicked(ClickEvent evt)
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.RequestStartGame();
        }
    }

    private void OnInviteButtonClicked(ClickEvent evt)
    {
        // TODO: Implement Unity Services friend invite system
        Debug.Log("[LobbyUI] Invite friends clicked - TODO: Implement friend invite system");
        
        // For now, copy session code to clipboard
        if (SessionManager.Instance != null)
        {
            string code = SessionManager.Instance.GetSessionCode();
            GUIUtility.systemCopyBuffer = code;
            Debug.Log($"[LobbyUI] Session code copied to clipboard: {code}");
        }
    }

    private void OnLeaveButtonClicked(ClickEvent evt)
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.LeaveLobby();
        }
    }

    // Event handlers from LobbyManager
    private void OnPlayerAdded(LobbyPlayerData playerData)
    {
        UpdatePlayerList();
        UpdateStartGameButton();
    }

    private void OnPlayerRemoved(ulong clientId)
    {
        UpdatePlayerList();
        UpdateStartGameButton();
    }

    private void OnPlayerClassChanged(ulong clientId, PlayerClass playerClass)
    {
        UpdatePlayerList();
    }

    private void OnPlayerReadyChanged(ulong clientId, bool isReady)
    {
        UpdatePlayerList();
        UpdateStartGameButton();
    }
}
