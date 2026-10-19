using Systems.Campus;
using StarterAssets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Phone
{
    public enum PhoneApp
    {
        Home = 0,
        Contacts = 1,
        Messages = 2,
        Thread = 3
    }

    /// <summary>
    /// UI Toolkit phone shell: case → screen → apps. Navigation is local-only.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(PhoneController))]
    public class PhoneView : MonoBehaviour
    {
        [SerializeField] UIDocument document;
        [SerializeField] PhoneController controller;
        [SerializeField] NetworkedPhoneHold phoneHold;

        VisualElement _phoneRoot;
        VisualElement _appsHost;
        Label _statusLabel;
        Button _homeContacts;
        Button _homeMessages;
        Button _closeButton;
        Button _navBack;
        Button _readyWeekend;
        Button _startWeekend;
        Label _weekendStatus;

        VisualElement _viewHome;
        VisualElement _viewContacts;
        VisualElement _viewMessages;
        VisualElement _viewThread;
        ScrollView _contactsScroll;
        ScrollView _messagesScroll;
        ScrollView _threadScroll;
        Label _threadTitle;
        VisualElement _templateRow;

        PhoneApp _app = PhoneApp.Home;
        ContactSO _activeContact;
        bool _isOpen;
        bool _weekendEventsBound;

        public bool IsOpen => _isOpen;

        void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (controller == null) controller = GetComponent<PhoneController>();
            if (phoneHold == null) phoneHold = GetComponentInParent<NetworkedPhoneHold>();
        }

        void OnEnable()
        {
            var netObj = GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned && !netObj.IsOwner)
            {
                return;
            }

            if (document != null && document.rootVisualElement != null)
            {
                Bind(document.rootVisualElement);
            }
            if (controller != null)
            {
                controller.OnThreadChanged += HandleThreadChanged;
                controller.OnContactsChanged += RefreshLists;
            }
        }

        void OnDisable()
        {
            BindWeekendReadyEvents(false);
            if (controller != null)
            {
                controller.OnThreadChanged -= HandleThreadChanged;
                controller.OnContactsChanged -= RefreshLists;
            }
        }

        void Start()
        {
            var netObj = GetComponentInParent<NetworkObject>();
            if (netObj != null && !netObj.IsOwner)
            {
                if (document != null)
                {
                    document.enabled = false;
                }
                gameObject.SetActive(false);
                return;
            }

            if (document != null)
            {
                Bind(document.rootVisualElement);
            }
        }

        void Bind(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            _phoneRoot = root.Q<VisualElement>("phone-root");
            _appsHost = root.Q<VisualElement>("phone-apps");
            _statusLabel = root.Q<Label>("phone-status");
            _closeButton = root.Q<Button>("phone-close");
            _navBack = root.Q<Button>("phone-nav-back");

            _viewHome = root.Q<VisualElement>("view-home");
            _viewContacts = root.Q<VisualElement>("view-contacts");
            _viewMessages = root.Q<VisualElement>("view-messages");
            _viewThread = root.Q<VisualElement>("view-thread");
            _contactsScroll = root.Q<ScrollView>("contacts-scroll");
            _messagesScroll = root.Q<ScrollView>("messages-scroll");
            _threadScroll = root.Q<ScrollView>("thread-scroll");
            _threadTitle = root.Q<Label>("thread-title");
            _templateRow = root.Q<VisualElement>("template-row");
            _homeContacts = root.Q<Button>("app-contacts");
            _homeMessages = root.Q<Button>("app-messages");
            _readyWeekend = root.Q<Button>("btn-ready-weekend");
            _startWeekend = root.Q<Button>("btn-start-weekend");
            _weekendStatus = root.Q<Label>("weekend-ready-status");

            if (_closeButton != null)
            {
                _closeButton.clicked -= Hide;
                _closeButton.clicked += Hide;
            }
            if (_navBack != null)
            {
                _navBack.clicked -= OnBack;
                _navBack.clicked += OnBack;
            }
            if (_homeContacts != null)
            {
                _homeContacts.clicked -= OnOpenContacts;
                _homeContacts.clicked += OnOpenContacts;
            }
            if (_homeMessages != null)
            {
                _homeMessages.clicked -= OnOpenMessages;
                _homeMessages.clicked += OnOpenMessages;
            }
            if (_readyWeekend != null)
            {
                _readyWeekend.clicked -= OnToggleReadyWeekend;
                _readyWeekend.clicked += OnToggleReadyWeekend;
            }
            if (_startWeekend != null)
            {
                _startWeekend.clicked -= OnStartWeekend;
                _startWeekend.clicked += OnStartWeekend;
            }

            if (_phoneRoot != null)
            {
                _phoneRoot.style.display = DisplayStyle.None;
            }

            BindWeekendReadyEvents(true);
            RefreshWeekendUi();
            ShowApp(PhoneApp.Home);
        }

        void BindWeekendReadyEvents(bool subscribe)
        {
            var ready = NetworkedWeekendReady.Instance;
            if (ready == null)
            {
                return;
            }

            if (subscribe && !_weekendEventsBound)
            {
                ready.OnReadyChanged += RefreshWeekendUi;
                _weekendEventsBound = true;
            }
            else if (!subscribe && _weekendEventsBound)
            {
                ready.OnReadyChanged -= RefreshWeekendUi;
                _weekendEventsBound = false;
            }
        }

        void OnToggleReadyWeekend()
        {
            var ready = NetworkedWeekendReady.Instance;
            if (ready == null)
            {
                Debug.LogWarning("[PhoneView] NetworkedWeekendReady missing");
                return;
            }

            ready.SetReadyServerRpc(!ready.IsLocalReady);
        }

        void OnStartWeekend()
        {
            var ready = NetworkedWeekendReady.Instance;
            if (ready == null)
            {
                return;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            {
                Debug.LogWarning("[PhoneView] Only host can Start Weekend");
                return;
            }

            ready.StartWeekendServerRpc();
            Hide();
        }

        void RefreshWeekendUi()
        {
            var ready = NetworkedWeekendReady.Instance;
            int readyCount = ready != null ? ready.ReadyCount : 0;
            int connected = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 0;
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            bool localReady = ready != null && ready.IsLocalReady;
            bool allReady = ready != null && ready.AllConnectedReady();

            if (_readyWeekend != null)
            {
                _readyWeekend.text = localReady ? "Unready" : "Ready for Weekend";
            }

            if (_startWeekend != null)
            {
                _startWeekend.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
                _startWeekend.SetEnabled(allReady);
            }

            if (_weekendStatus != null)
            {
                _weekendStatus.text = $"Ready {readyCount}/{Mathf.Max(1, connected)}";
            }

            var cal = NetworkedCampusCalendar.Instance;
            if (_statusLabel != null && cal != null && cal.Phase == GamePhase.Weekday)
            {
                float t = cal.GetCurrentInGameTime();
                int hour = Mathf.FloorToInt(t) % 24;
                int minute = Mathf.FloorToInt((t - Mathf.Floor(t)) * 60f);
                string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
                string day = days[Mathf.Clamp(cal.DayOfWeek, 0, 6)];
                _statusLabel.text = $"{day} {hour:00}:{minute:00}";
            }
        }

        void OnOpenContacts() => ShowApp(PhoneApp.Contacts);
        void OnOpenMessages() => ShowApp(PhoneApp.Messages);

        public void Toggle()
        {
            if (_isOpen) Hide();
            else Show();
        }

        public void Show()
        {
            if (document != null && document.rootVisualElement != null)
            {
                Bind(document.rootVisualElement);
            }

            if (_phoneRoot == null)
            {
                Debug.LogWarning("[PhoneView] phone-root missing. Is Phone.uxml assigned?");
                return;
            }

            controller?.EnsureStates();
            _isOpen = true;
            _phoneRoot.style.display = DisplayStyle.Flex;
            ShowApp(PhoneApp.Home);
            ApplyCursor(true);
            phoneHold?.SetHoldingPhone(true);
            RefreshWeekendUi();
        }

        public void Hide()
        {
            _isOpen = false;
            if (_phoneRoot != null)
            {
                _phoneRoot.style.display = DisplayStyle.None;
            }
            ApplyCursor(false);
            phoneHold?.SetHoldingPhone(false);
        }

        void OnBack()
        {
            switch (_app)
            {
                case PhoneApp.Thread:
                    ShowApp(PhoneApp.Messages);
                    break;
                case PhoneApp.Contacts:
                case PhoneApp.Messages:
                    ShowApp(PhoneApp.Home);
                    break;
                default:
                    Hide();
                    break;
            }
        }

        void ShowApp(PhoneApp app)
        {
            _app = app;
            SetVisible(_viewHome, app == PhoneApp.Home);
            SetVisible(_viewContacts, app == PhoneApp.Contacts);
            SetVisible(_viewMessages, app == PhoneApp.Messages);
            SetVisible(_viewThread, app == PhoneApp.Thread);
            if (_navBack != null)
            {
                _navBack.style.display = app == PhoneApp.Home ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (app == PhoneApp.Contacts) RebuildContacts();
            if (app == PhoneApp.Messages) RebuildMessageList();
            if (app == PhoneApp.Thread) RebuildThread();
            if (app == PhoneApp.Home) RefreshWeekendUi();
        }

        void RefreshLists()
        {
            if (!_isOpen) return;
            if (_app == PhoneApp.Contacts) RebuildContacts();
            if (_app == PhoneApp.Messages) RebuildMessageList();
        }

        void HandleThreadChanged(SerializableGuid contactId)
        {
            if (_isOpen && _app == PhoneApp.Thread && _activeContact != null && _activeContact.Id == contactId)
            {
                RebuildThread();
            }
            if (_isOpen && _app == PhoneApp.Messages)
            {
                RebuildMessageList();
            }
        }

        void RebuildContacts()
        {
            if (_contactsScroll == null || controller == null) return;
            _contactsScroll.Clear();
            foreach (var contact in controller.GetUnlockedContacts())
            {
                var row = CreateContactRow(contact, openThread: false);
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _activeContact = contact;
                    ShowApp(PhoneApp.Thread);
                });
                _contactsScroll.Add(row);
            }
        }

        void RebuildMessageList()
        {
            if (_messagesScroll == null || controller == null) return;
            _messagesScroll.Clear();
            foreach (var contact in controller.GetUnlockedContacts())
            {
                var row = CreateContactRow(contact, openThread: true);
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _activeContact = contact;
                    ShowApp(PhoneApp.Thread);
                });
                _messagesScroll.Add(row);
            }
        }

        VisualElement CreateContactRow(ContactSO contact, bool openThread)
        {
            var row = new VisualElement();
            row.AddToClassList("contact-row");

            var avatar = new VisualElement();
            avatar.AddToClassList("contact-avatar");
            if (contact.avatar != null)
            {
                avatar.style.backgroundImage = new StyleBackground(contact.avatar);
            }

            var textCol = new VisualElement();
            textCol.AddToClassList("contact-text");

            var name = new Label(contact.displayName);
            name.AddToClassList("contact-name");

            var tag = new Label(contact.ArchetypeTag);
            tag.AddToClassList("contact-tag");

            var state = controller.GetState(contact.Id);
            if (state != null && state.invitedForNextParty)
            {
                tag.text = $"{contact.ArchetypeTag} · invited";
            }

            textCol.Add(name);
            textCol.Add(tag);
            row.Add(avatar);
            row.Add(textCol);

            if (openThread && controller.Messages.HasMessages(contact.Id))
            {
                var preview = controller.Messages.GetThread(contact.Id);
                if (preview.Count > 0)
                {
                    var last = preview[preview.Count - 1];
                    var previewLabel = new Label(last.body);
                    previewLabel.AddToClassList("contact-preview");
                    textCol.Add(previewLabel);
                }
            }

            return row;
        }

        void RebuildThread()
        {
            if (_threadScroll == null || controller == null || _activeContact == null) return;

            if (_threadTitle != null)
            {
                _threadTitle.text = _activeContact.displayName;
            }

            _threadScroll.Clear();
            foreach (var msg in controller.Messages.GetThread(_activeContact.Id))
            {
                var bubble = new Label(msg.body);
                bubble.AddToClassList("msg-bubble");
                bubble.AddToClassList(msg.fromPlayer ? "msg-bubble-out" : "msg-bubble-in");
                _threadScroll.Add(bubble);
            }

            RebuildTemplates();
        }

        void RebuildTemplates()
        {
            if (_templateRow == null || _activeContact == null) return;
            _templateRow.Clear();

            foreach (var template in PhoneContactDatabase.Templates)
            {
                var captured = template;
                var btn = new Button(() => OnSendTemplate(captured))
                {
                    text = template.kind.ToString()
                };
                btn.AddToClassList("template-btn");
                if (template.appliesInvite)
                {
                    btn.AddToClassList("template-btn-invite");
                }
                _templateRow.Add(btn);
            }
        }

        void OnSendTemplate(MessageTemplateSO template)
        {
            if (_activeContact == null || controller == null) return;
            controller.SendTemplate(_activeContact, template);
            RebuildThread();
        }

        static void SetVisible(VisualElement el, bool visible)
        {
            if (el != null)
            {
                el.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        static void ApplyCursor(bool phoneOpen)
        {
            FindLocalPlayerInputs()?.SetInventoryUIMode(phoneOpen);
        }

        static StarterAssetsInputs FindLocalPlayerInputs()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                var player = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (player != null)
                {
                    var inp = player.GetComponent<StarterAssetsInputs>();
                    if (inp != null) return inp;
                }
            }
            return FindFirstObjectByType<StarterAssetsInputs>();
        }
    }
}
