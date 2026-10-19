using System.Collections;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Binds the Lobby UXML to LobbyManager and SessionManager.
/// Shows session code, ready count, fraternity name, player list, and wires name/class/frat name/ready/start/leave.
/// Styling comes from HUD.uss + Lobby.uss so the lobby matches the in-game HUD.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    private UIDocument _document;
    private Label _sessionCodeLabel;
    private Label _readyCountLabel;
    private Label _lobbyStatusLabel;
    private VisualElement _playerListRoot;
    private Label _playerCountLabel;
    private TextField _playerNameField;
    private DropdownField _classDropdown;
    private Button _readyButton;
    private Button _startGameButton;
    private Button _leaveButton;

    // Fraternity: the host gets a field, everyone else a read-only name
    private TextField _fratNameField;
    private Label _fratNameLabel;
    private Label _fratNameHint;

    // Appearance cyclers
    private Button _skinLeftArrow;
    private Button _skinRightArrow;
    private Label _skinLabel;
    private Button _meshLeftArrow;
    private Button _meshRightArrow;
    private Label _meshLabel;

    // Class stat meters (read-only, show selected class stats)
    private VisualElement _popularityFill;
    private Label _popularityValue;
    private VisualElement _troubleMakerFill;
    private Label _troubleMakerValue;
    private VisualElement _cashFill;
    private Label _cashValue;

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
        _readyCountLabel = root.Q<Label>("ReadyCount");
        _lobbyStatusLabel = root.Q<Label>("LobbyStatus");
        _playerListRoot = root.Q("PlayerList");
        _playerCountLabel = root.Q<Label>("PlayerCount");
        _playerNameField = root.Q<TextField>("PlayerNameField");
        _classDropdown = root.Q<DropdownField>("ClassDropdown");
        _readyButton = root.Q<Button>("ReadyButton");
        _startGameButton = root.Q<Button>("StartGameButton");
        _leaveButton = root.Q<Button>("LeaveButton");

        _fratNameField = root.Q<TextField>("FratNameField");
        _fratNameLabel = root.Q<Label>("FratNameLabel");
        _fratNameHint = root.Q<Label>("FratNameHint");

        _skinLeftArrow = root.Q<Button>("SkinLeftArrow");
        _skinRightArrow = root.Q<Button>("SkinRightArrow");
        _skinLabel = root.Q<Label>("SkinLabel");
        _meshLeftArrow = root.Q<Button>("MeshLeftArrow");
        _meshRightArrow = root.Q<Button>("MeshRightArrow");
        _meshLabel = root.Q<Label>("MeshLabel");

        _popularityFill = root.Q("PopularityFill");
        _popularityValue = root.Q<Label>("PopularityValue");
        _troubleMakerFill = root.Q("TroubleMakerFill");
        _troubleMakerValue = root.Q<Label>("TroubleMakerValue");
        _cashFill = root.Q("CashFill");
        _cashValue = root.Q<Label>("CashValue");

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

        SetupFratName();

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
        RefreshStatMeters();
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
        RefreshStatMeters();
        RefreshAppearanceLabels();
        RefreshFratName();
    }

    private void OnDisable()
    {
        UnsubscribeFromLobbyManager();
        if (_playerNameField != null)
            _playerNameField.UnregisterValueChangedCallback(OnPlayerNameChanged);
        if (_fratNameField != null)
            _fratNameField.UnregisterValueChangedCallback(OnFratNameEdited);
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
        lm.OnFratNameChanged += OnFratNameChangedFromNetwork;
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
        lm.OnFratNameChanged -= OnFratNameChangedFromNetwork;
    }

    private void OnPlayerAdded(LobbyPlayerData data)
    {
        Debug.Log($"[LobbyUI] OnPlayerAdded event: {data.PlayerName} (clientId={data.NetworkId})");
        RefreshPlayerList();
        RefreshStatMeters();
        RefreshClassDropdown();
    }
    private void OnPlayerRemoved(ulong _) => RefreshPlayerList();
    private void OnPlayerNameChangedFromNetwork(ulong _, string __) => RefreshPlayerList();
    private void OnPlayerClassChangedFromNetwork(ulong clientId, PlayerClass _)
    {
        RefreshPlayerList();
        if (IsLocalPlayer(clientId))
        {
            RefreshStatMeters();
            RefreshClassDropdown();
        }
    }
    private void OnPlayerReadyChangedFromNetwork(ulong _, bool __) => RefreshPlayerList();
    private void OnFratNameChangedFromNetwork(string _) => RefreshFratName();

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

    private static bool IsHost()
    {
        var nm = NetworkManager.Singleton;
        return nm != null && nm.IsHost;
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element != null)
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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
            _skinLabel.text = materialList[idx] != null ? materialList[idx].name : LobbyText.Fit + (idx + 1);
        }

        if (_meshLabel != null && meshList != null && meshList.Count > 0)
        {
            int idx = ((data.SelectedMeshIndex % meshList.Count) + meshList.Count) % meshList.Count;
            _meshLabel.text = meshList[idx] != null ? meshList[idx].name : LobbyText.Body + (idx + 1);
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
        _sessionCodeLabel.text = string.IsNullOrEmpty(code) ? LobbyText.NoSessionCode : code;
        if (!string.IsNullOrEmpty(code))
            Debug.Log($"[LobbyUI] Session code displayed in UI: {code}");
    }

    /// <summary>
    /// The host types the name (saved for next time, like the player name); everyone else sees it read-only.
    /// </summary>
    private void SetupFratName()
    {
        bool host = IsHost();
        SetDisplay(_fratNameField, host);
        SetDisplay(_fratNameLabel, !host);
        if (_fratNameHint != null)
            _fratNameHint.text = host ? LobbyText.FratHintHost : LobbyText.FratHintClient;

        if (host && _fratNameField != null)
        {
            _fratNameField.maxLength = FratRow.Game.HouseNames.MaxLength;
            string current = LobbyManager.Instance != null ? LobbyManager.Instance.FratName : "";
            if (string.IsNullOrEmpty(current))
                current = PlayerPrefs.GetString(LobbyManager.FratNamePrefKey, "");
            _fratNameField.SetValueWithoutNotify(current);
            _fratNameField.RegisterValueChangedCallback(OnFratNameEdited);
        }

        RefreshFratName();
    }

    private void OnFratNameEdited(ChangeEvent<string> evt)
    {
        string name = FratRow.Game.HouseNames.Sanitize(evt.newValue);
        PlayerPrefs.SetString(LobbyManager.FratNamePrefKey, name);
        LobbyManager.Instance?.RequestSetFratName(name);
    }

    /// <summary>
    /// Clients show the replicated name. The host's own field is never written back while they type
    /// (sanitizing mid-edit would eat trailing spaces).
    /// </summary>
    private void RefreshFratName()
    {
        if (_fratNameLabel == null || IsHost()) return;

        string name = LobbyManager.Instance != null ? LobbyManager.Instance.FratName : "";
        bool named = !string.IsNullOrWhiteSpace(name);
        _fratNameLabel.text = named ? name : LobbyText.UnnamedHouse;
        _fratNameLabel.EnableInClassList("lobby-frat-name--unset", !named);
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
            _playerListRoot.Add(BuildPlayerRow(kvp.Value));
        }

        if (count == 0)
        {
            var empty = new Label(LobbyText.WaitingForPlayers);
            empty.AddToClassList("hud-muted");
            _playerListRoot.Add(empty);
        }

        if (_playerCountLabel != null)
            _playerCountLabel.text = count.ToString(CultureInfo.InvariantCulture) + "/" + LobbyManager.MaxPlayers.ToString(CultureInfo.InvariantCulture);

        RefreshBanner();
        RefreshStartGameButton();
    }

    /// <summary>One roster row: name, HOST chip, class, ready chip. Your own row is highlighted like the HUD standings.</summary>
    private static VisualElement BuildPlayerRow(LobbyPlayerData data)
    {
        var row = new VisualElement();
        row.AddToClassList("lobby-player-row");
        row.EnableInClassList("lobby-player-row--local", IsLocalPlayer(data.NetworkId));

        var name = new Label(data.PlayerName);
        name.AddToClassList("lobby-player-name");
        row.Add(name);

        if (data.IsHost)
        {
            var host = new Label(LobbyText.HostChip);
            host.AddToClassList("hud-chip");
            host.AddToClassList("lobby-chip--host");
            row.Add(host);
        }

        var playerClass = new Label(data.SelectedClass.ToString());
        playerClass.AddToClassList("lobby-player-class");
        row.Add(playerClass);

        var ready = new Label(data.IsReady ? LobbyText.ReadyChip : LobbyText.NotReadyChip);
        ready.AddToClassList("hud-chip");
        if (data.IsReady) ready.AddToClassList("hud-chip--good");
        row.Add(ready);

        return row;
    }

    /// <summary>Banner sub-row: "Ready 2/3" plus what the lobby is waiting on.</summary>
    private void RefreshBanner()
    {
        var lm = LobbyManager.Instance;
        int count = lm != null ? lm.PlayerCount : 0;
        int ready = lm != null ? lm.ReadyPlayerCount : 0;

        if (_readyCountLabel != null)
            _readyCountLabel.text = LobbyText.ReadyCount + ready.ToString(CultureInfo.InvariantCulture) + "/" + count.ToString(CultureInfo.InvariantCulture);

        if (_lobbyStatusLabel != null)
        {
            if (count == 0)
                _lobbyStatusLabel.text = LobbyText.WaitingForPlayers;
            else if (ready < count)
                _lobbyStatusLabel.text = LobbyText.WaitingOnReady;
            else
                _lobbyStatusLabel.text = IsHost() ? LobbyText.AllReadyHost : LobbyText.AllReadyClient;
        }
    }

    private void RefreshStartGameButton()
    {
        RefreshReadyButton();
        if (_startGameButton == null) return;

        if (!IsHost())
        {
            _startGameButton.style.display = DisplayStyle.None;
            return;
        }

        _startGameButton.style.display = DisplayStyle.Flex;
        bool canStart = LobbyManager.Instance != null && LobbyManager.Instance.CanStartGame();
        _startGameButton.SetEnabled(canStart);
    }

    /// <summary>
    /// Fill each meter with the selected class's stat. Numbers are shown next to the bar, so the stat is never colour-only.
    /// </summary>
    private void RefreshStatMeters()
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

        SetMeter(_popularityFill, _popularityValue, classSO.popularity, 0f, 100f);
        SetMeter(_troubleMakerFill, _troubleMakerValue, classSO.troubleMaker, -100f, 100f);
        SetMeter(_cashFill, _cashValue, classSO.cashvalue, 0f, 100f);
    }

    private static void SetMeter(VisualElement fill, Label value, float amount, float lowValue, float highValue)
    {
        if (fill != null)
            fill.style.width = Length.Percent(Mathf.InverseLerp(lowValue, highValue, amount) * 100f);
        if (value != null)
            value.text = Mathf.RoundToInt(amount).ToString(CultureInfo.InvariantCulture);
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
            RefreshStatMeters();
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
        bool ready = IsLocalReady();
        _readyButton.text = ready ? LobbyText.Unready : LobbyText.Ready;
        _readyButton.EnableInClassList("lobby-button--ready-on", ready);
    }

    private void OnStartGameClicked()
    {
        LobbyManager.Instance?.RequestStartGame();
    }

    private void OnLeaveClicked()
    {
        LobbyManager.Instance?.LeaveLobby();
    }

    /// <summary>
    /// Every lobby string built in code, in one place (same convention as GameplayUI's HudText). The project has no
    /// localization tables yet; when it adopts one, swap these for table lookups.
    /// </summary>
    static class LobbyText
    {
        public const string NoSessionCode = "----";
        public const string Ready = "Ready";
        public const string Unready = "Unready";
        public const string ReadyCount = "Ready ";
        public const string HostChip = "HOST";
        public const string ReadyChip = "READY";
        public const string NotReadyChip = "NOT READY";
        public const string WaitingForPlayers = "Waiting for players...";
        public const string WaitingOnReady = "Waiting for everyone to ready up";
        public const string AllReadyHost = "Everyone's ready. Start when you are";
        public const string AllReadyClient = "Everyone's ready. Waiting on the host";
        public const string UnnamedHouse = "Unnamed house";
        public const string FratHintHost = "Only you (the host) can name the house.";
        public const string FratHintClient = "The host names the house.";
        public const string Fit = "Fit ";
        public const string Body = "Body ";
    }
}
