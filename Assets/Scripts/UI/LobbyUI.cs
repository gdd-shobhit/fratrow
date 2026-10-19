using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

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

    // Appearance cyclers
    private Button _skinLeftArrow;
    private Button _skinRightArrow;
    private Label _skinLabel;
    private Button _meshLeftArrow;
    private Button _meshRightArrow;
    private Label _meshLabel;

    // Stat sliders (read-only, show selected class stats)
    private Slider _popularitySlider;
    private Slider _troubleMakerSlider;
    private Slider _cashSlider;

    private void Awake()
    {
        var nm = NetworkManager.Singleton;
        Debug.Log($"[LobbyUI] Awake (scene: {gameObject.scene.name}) NetworkManager={nm != null} IsClient={nm?.IsClient} LobbyManager.Instance={LobbyManager.Instance != null}");
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

        _skinLeftArrow = root.Q<Button>("SkinLeftArrow");
        _skinRightArrow = root.Q<Button>("SkinRightArrow");
        _skinLabel = root.Q<Label>("SkinLabel");
        _meshLeftArrow = root.Q<Button>("MeshLeftArrow");
        _meshRightArrow = root.Q<Button>("MeshRightArrow");
        _meshLabel = root.Q<Label>("MeshLabel");

        _popularitySlider = root.Q<Slider>("Popularity");
        _troubleMakerSlider = root.Q<Slider>("TroubleMaker");
        _cashSlider = root.Q<Slider>("Cash");

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
        StartCoroutine(DelayedRefreshAfterJoin());

        if (_playerNameField != null)
        {
            string saved = PlayerPrefs.GetString("PlayerName", "Player");
            _playerNameField.SetValueWithoutNotify(saved);
            _playerNameField.RegisterValueChangedCallback(OnPlayerNameChanged);
        }

        if (_classDropdown != null)
        {
            RefreshClassDropdown();
            _classDropdown.RegisterValueChangedCallback(OnClassChanged);
        }

        if (_readyButton != null)
            _readyButton.clicked += OnReadyClicked;
        if (_startGameButton != null)
            _startGameButton.clicked += OnStartGameClicked;
        if (_leaveButton != null)
            _leaveButton.clicked += OnLeaveClicked;

        if (_skinLeftArrow != null)
            _skinLeftArrow.clicked += OnSkinLeftClicked;
        if (_skinRightArrow != null)
            _skinRightArrow.clicked += OnSkinRightClicked;
        if (_meshLeftArrow != null)
            _meshLeftArrow.clicked += OnMeshLeftClicked;
        if (_meshRightArrow != null)
            _meshRightArrow.clicked += OnMeshRightClicked;

        RefreshAppearanceLabels();
        SetupStatSliders();
    }

    /// <summary>
    /// Catches lobby state that arrived before we subscribed. Client receives PlayerJoinedLobbyRPC
    /// asynchronously; if they arrive before LobbyUI subscribes, we miss OnPlayerAdded. This delayed
    /// refresh ensures we display the lobby state shortly after join.
    /// </summary>
    private IEnumerator DelayedRefreshAfterJoin()
    {
        yield return new WaitForSeconds(0.5f);
        RefreshPlayerList();
        RefreshClassDropdown();
        RefreshStatSliders();
        RefreshAppearanceLabels();
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
        if (_skinLeftArrow != null)
            _skinLeftArrow.clicked -= OnSkinLeftClicked;
        if (_skinRightArrow != null)
            _skinRightArrow.clicked -= OnSkinRightClicked;
        if (_meshLeftArrow != null)
            _meshLeftArrow.clicked -= OnMeshLeftClicked;
        if (_meshRightArrow != null)
            _meshRightArrow.clicked -= OnMeshRightClicked;
        if (_popularitySlider != null)
            _popularitySlider.UnregisterValueChangedCallback(OnStatSliderValueChanged);
        if (_troubleMakerSlider != null)
            _troubleMakerSlider.UnregisterValueChangedCallback(OnStatSliderValueChanged);
        if (_cashSlider != null)
            _cashSlider.UnregisterValueChangedCallback(OnStatSliderValueChanged);
    }

    private void SubscribeToLobbyManager()
    {
        var lm = LobbyManager.Instance;
        Debug.Log($"[LobbyUI] SubscribeToLobbyManager: LobbyManager.Instance={lm != null}");
        if (lm == null) return;
        lm.OnPlayerAdded += OnPlayerAdded;
        lm.OnPlayerRemoved += OnPlayerRemoved;
        lm.OnPlayerNameChanged += OnPlayerNameChangedFromNetwork;
        lm.OnPlayerClassChanged += OnPlayerClassChangedFromNetwork;
        lm.OnPlayerReadyChanged += OnPlayerReadyChangedFromNetwork;
        lm.OnPlayerMeshChanged += OnPlayerMeshChangedFromNetwork;
        lm.OnPlayerMaterialChanged += OnPlayerMaterialChangedFromNetwork;
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
        lm.OnPlayerMeshChanged -= OnPlayerMeshChangedFromNetwork;
        lm.OnPlayerMaterialChanged -= OnPlayerMaterialChangedFromNetwork;
    }

    private void OnPlayerAdded(LobbyPlayerData data)
    {
        Debug.Log($"[LobbyUI] OnPlayerAdded event: {data.PlayerName} (clientId={data.NetworkId})");
        RefreshPlayerList();
        RefreshStatSliders();
        RefreshClassDropdown();
    }
    private void OnPlayerRemoved(ulong _) => RefreshPlayerList();
    private void OnPlayerNameChangedFromNetwork(ulong _, string __) => RefreshPlayerList();
    private void OnPlayerClassChangedFromNetwork(ulong clientId, PlayerClass _)
    {
        RefreshPlayerList();
        if (IsLocalPlayer(clientId))
        {
            RefreshStatSliders();
            RefreshClassDropdown();
        }
    }
    private void OnPlayerReadyChangedFromNetwork(ulong _, bool __) => RefreshPlayerList();

    private void OnPlayerMeshChangedFromNetwork(ulong clientId, int _)
    {
        RefreshPlayerList();
        if (IsLocalPlayer(clientId))
            RefreshAppearanceLabels();
    }

    private void OnPlayerMaterialChangedFromNetwork(ulong clientId, int _)
    {
        RefreshPlayerList();
        if (IsLocalPlayer(clientId))
            RefreshAppearanceLabels();
    }

    private static bool IsLocalPlayer(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        return nm != null && nm.LocalClientId == clientId;
    }

    private void RefreshAppearanceLabels()
    {
        if (LobbyManager.Instance == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (!LobbyManager.Instance.Players.TryGetValue(nm.LocalClientId, out var data))
            return;

        var meshList = LobbyManager.Instance.MeshList;
        var materialList = LobbyManager.Instance.MaterialList;

        if (_skinLabel != null && materialList != null && materialList.Count > 0)
        {
            int idx = ((data.SelectedMaterialIndex % materialList.Count) + materialList.Count) % materialList.Count;
            _skinLabel.text = materialList[idx] != null ? materialList[idx].name : $"Fit {idx + 1}";
        }

        if (_meshLabel != null && meshList != null && meshList.Count > 0)
        {
            int idx = ((data.SelectedMeshIndex % meshList.Count) + meshList.Count) % meshList.Count;
            _meshLabel.text = meshList[idx] != null ? meshList[idx].name : $"Body {idx + 1}";
        }
    }

    private void OnSkinLeftClicked()
    {
        CycleMaterial(-1);
    }

    private void OnSkinRightClicked()
    {
        CycleMaterial(1);
    }

    private void OnMeshLeftClicked()
    {
        CycleMesh(-1);
    }

    private void OnMeshRightClicked()
    {
        CycleMesh(1);
    }

    private void CycleMaterial(int delta)
    {
        if (LobbyManager.Instance == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        var materialList = LobbyManager.Instance.MaterialList;
        if (materialList == null || materialList.Count == 0) return;
        if (!LobbyManager.Instance.Players.TryGetValue(nm.LocalClientId, out var data))
            return;

        int newIndex = ((data.SelectedMaterialIndex + delta) % materialList.Count + materialList.Count) % materialList.Count;
        LobbyManager.Instance.RequestSetMaterialIndex(newIndex);
        RefreshAppearanceLabels();
    }

    private void CycleMesh(int delta)
    {
        if (LobbyManager.Instance == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        var meshList = LobbyManager.Instance.MeshList;
        if (meshList == null || meshList.Count == 0) return;
        if (!LobbyManager.Instance.Players.TryGetValue(nm.LocalClientId, out var data))
            return;

        int newIndex = ((data.SelectedMeshIndex + delta) % meshList.Count + meshList.Count) % meshList.Count;
        LobbyManager.Instance.RequestSetMeshIndex(newIndex);
        RefreshAppearanceLabels();
    }

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

        var lm = LobbyManager.Instance;
        if (lm == null)
        {
            Debug.Log("[LobbyUI] RefreshPlayerList: LobbyManager.Instance is null");
            return;
        }

        var count = lm.Players.Count;
        Debug.Log($"[LobbyUI] RefreshPlayerList: playerCount={count}");

        foreach (var kvp in lm.Players)
        {
            var data = kvp.Value;
            var row = new Label(FormatPlayerLine(data));
            row.style.color = Color.white;
            row.style.fontSize = 20;
            row.style.marginBottom = 8;
            row.style.unityTextAlign = TextAnchor.MiddleLeft;
            _playerListRoot.Add(row);
        }

        RefreshStartGameButton();
    }

    private void RefreshStartGameButton()
    {
        RefreshReadyButton();
        if (_startGameButton == null) return;

        var nm = NetworkManager.Singleton;
        bool isHost = nm != null && nm.IsHost;

        if (!isHost)
        {
            _startGameButton.style.display = DisplayStyle.None;
            return;
        }

        _startGameButton.style.display = DisplayStyle.Flex;
        bool canStart = LobbyManager.Instance != null && LobbyManager.Instance.CanStartGame();
        _startGameButton.SetEnabled(canStart);
    }

    private static string FormatPlayerLine(LobbyPlayerData data)
    {
        string host = data.IsHost ? " (Host)" : "";
        string ready = data.IsReady ? " [Ready]" : "";
        return $"{data.PlayerName}{host} - {data.SelectedClass}{ready}";
    }

    private void SetupStatSliders()
    {
        if (_popularitySlider != null)
        {
            _popularitySlider.lowValue = 0f;
            _popularitySlider.highValue = 100f;
            _popularitySlider.RegisterValueChangedCallback(OnStatSliderValueChanged);
        }
        if (_troubleMakerSlider != null)
        {
            _troubleMakerSlider.lowValue = -100f;
            _troubleMakerSlider.highValue = 100f;
            _troubleMakerSlider.RegisterValueChangedCallback(OnStatSliderValueChanged);
        }
        if (_cashSlider != null)
        {
            _cashSlider.lowValue = 0f;
            _cashSlider.highValue = 100f;
            _cashSlider.RegisterValueChangedCallback(OnStatSliderValueChanged);
        }
        RefreshStatSliders();
    }

    private void OnStatSliderValueChanged(ChangeEvent<float> evt)
    {
        // Immediately revert any user change - sliders are read-only
        RefreshStatSliders();
    }

    private void RefreshStatSliders()
    {
        PlayerClass selectedClass = PlayerClass.President;
        if (LobbyManager.Instance != null && NetworkManager.Singleton != null &&
            LobbyManager.Instance.Players.TryGetValue(NetworkManager.Singleton.LocalClientId, out var data))
        {
            selectedClass = data.SelectedClass;
        }

        if (NetworkedGameManager.Instance == null || NetworkedGameManager.Instance.playerClassesDict == null)
            return;

        if (!NetworkedGameManager.Instance.playerClassesDict.TryGetValue(selectedClass, out var classSO))
            return;

        // Sliders use inverted="true" for visual design - invert value so 20 displays as 20, not 80
        if (_popularitySlider != null)
            _popularitySlider.SetValueWithoutNotify(InvertForSlider(classSO.popularity, 0f, 100f));
        if (_troubleMakerSlider != null)
            _troubleMakerSlider.SetValueWithoutNotify(InvertForSlider(classSO.troubleMaker, -100f, 100f));
        if (_cashSlider != null)
            _cashSlider.SetValueWithoutNotify(InvertForSlider(classSO.cashvalue, 0f, 100f));
    }

    /// <summary>
    /// Inverts value for display on inverted sliders: actual 20 should show visually as 20.
    /// </summary>
    private static float InvertForSlider(float actualValue, float lowValue, float highValue)
    {
        return highValue + lowValue - actualValue;
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
        {
            LobbyManager.Instance?.RequestSetPlayerClass(pc);
            RefreshStatSliders();
            RefreshClassDropdown();
        }
    }

    private void RefreshClassDropdown()
    {
        if (_classDropdown == null) return;

        PlayerClass selectedClass = PlayerClass.President;
        if (LobbyManager.Instance != null && NetworkManager.Singleton != null &&
            LobbyManager.Instance.Players.TryGetValue(NetworkManager.Singleton.LocalClientId, out var data))
        {
            selectedClass = data.SelectedClass;
        }

        string value = selectedClass.ToString();
        _classDropdown.SetValueWithoutNotify(value);
        _classDropdown.MarkDirtyRepaint();
    }

    private void OnReadyClicked()
    {
        // Toggle from the authoritative lobby roster (server state replicated to every client).
        LobbyManager.Instance?.RequestSetReadyStatus(!IsLocalReady());
    }

    private static bool IsLocalReady()
    {
        var nm = NetworkManager.Singleton;
        var lobby = LobbyManager.Instance;
        return nm != null && lobby != null && lobby.Players.TryGetValue(nm.LocalClientId, out var me) && me.IsReady;
    }

    private void RefreshReadyButton()
    {
        if (_readyButton == null) return;
        _readyButton.text = IsLocalReady() ? "Unready" : "Ready";
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
