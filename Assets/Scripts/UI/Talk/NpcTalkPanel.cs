using System.Collections.Generic;
using FratRow.Game;
using FratRow.Social;
using StarterAssets;
using Systems.Campus;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Systems.Talk
{
    /// <summary>
    /// In-person conversation with a campus NPC (replaces the old Yarn dialogue). Every choice goes to the host's
    /// social backend through <see cref="NetworkedCampaign"/>, so what the NPC says and decides comes from their own
    /// persona and memory (design/gdd/core-game-loop.md §3.4). Display only: the panel never changes game state itself.
    /// Opened by <see cref="NamedNpcVisual.Interact"/>; created at runtime by <c>CampusSystemsBootstrap</c>.
    /// Example: <c>NpcTalkPanel.Ensure().Open(NamedNpcDatabase.GetById("Ashley"));</c>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class NpcTalkPanel : MonoBehaviour
    {
        /// <summary>User-facing strings in one place until the project has a localization system.</summary>
        static class Text
        {
            public const string Approach = "You walk up to {0}.";
            public const string AskNumber = "Can I get your number?";
            public const string Invite = "We're throwing a party Saturday. You should come.";
            public const string Rush = "Have you thought about rushing our house?";
            public const string SayPlaceholder = "Say something...";
            public const string Send = "Say it";
            public const string Leave = "See you around.";
            public const string Waiting = "...";
            public const string NoCampaign = "(The campus isn't running yet.)";
            public const string Coming = "(Coming Saturday)";
            public const string Conditional = "(Will come if you meet them at the door in the first minute)";
            public const string NotComing = "(Not coming)";
            public const string Undecided = "(Hasn't decided)";
            public const string Busy = "{0} is busy talking to someone else. Try again in a moment.";
            public const string Slurring = "(You're slurring.)";
            public const string Wasted = "(You're wasted.)";
        }

        public static NpcTalkPanel Instance { get; private set; }

        UIDocument document;
        VisualElement root;
        Label speaker;
        Label body;
        VisualElement options;
        Button continueButton;
        TextField input;
        NamedNpcSO npc;
        bool waiting;
        /// <summary>What the player actually said this turn (the host's rewrite when drunk), kept above the NPC's reply.</summary>
        string spokenLine;
        NetworkedCampaign subscribed;
        readonly List<PlayerInput> pausedInputs = new List<PlayerInput>();

        /// <summary>True while a conversation is on screen.</summary>
        public bool IsOpen => npc != null;

        /// <summary>Returns the panel, creating it (UIDocument + Resources/Dialogue/Dialogue.uxml) on first use.</summary>
        public static NpcTalkPanel Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("NpcTalkPanel");
            DontDestroyOnLoad(go);
            var doc = go.AddComponent<UIDocument>();
            var panels = Resources.FindObjectsOfTypeAll<PanelSettings>();
            if (panels != null && panels.Length > 0) doc.panelSettings = panels[0];
            var uxml = Resources.Load<VisualTreeAsset>("Dialogue/Dialogue");
            if (uxml != null) doc.visualTreeAsset = uxml;
            else Debug.LogWarning("[NpcTalkPanel] Resources/Dialogue/Dialogue.uxml missing.");
            return go.AddComponent<NpcTalkPanel>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            document = GetComponent<UIDocument>();
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (IsOpen) Close();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var campaign = NetworkedCampaign.Instance;
            if (campaign != subscribed) { Unsubscribe(); Subscribe(campaign); }
            if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        }

        bool Bind()
        {
            if (document == null || document.rootVisualElement == null) return false;
            var r = document.rootVisualElement;
            root = r.Q<VisualElement>("dialogue-root");
            speaker = r.Q<Label>("dialogue-speaker");
            body = r.Q<Label>("dialogue-body");
            options = r.Q<VisualElement>("dialogue-options");
            continueButton = r.Q<Button>("dialogue-continue");
            if (continueButton != null) continueButton.style.display = DisplayStyle.None;
            return root != null && body != null && options != null;
        }

        /// <summary>Starts an in-person conversation with a campus NPC.</summary>
        public void Open(NamedNpcSO target)
        {
            if (target == null || !Bind()) return;
            npc = target;
            waiting = false;
            spokenLine = null;
            if (speaker != null) speaker.text = target.displayName;
            body.text = string.Format(Text.Approach, target.displayName);
            BuildOptions();
            root.style.display = DisplayStyle.Flex;
            PausePlayerInput(true);
        }

        /// <summary>Ends the conversation and hands control back to the player.</summary>
        public void Close()
        {
            npc = null;
            waiting = false;
            if (root != null) root.style.display = DisplayStyle.None;
            options?.Clear();
            PausePlayerInput(false);
        }

        void BuildOptions()
        {
            options.Clear();
            var campaign = NetworkedCampaign.Instance;
            bool live = campaign != null && campaign.IsCampaignActive;
            bool unlocked = NetworkedContactRoster.Instance != null && NetworkedContactRoster.Instance.IsUnlocked(npc.npcId);
            if (!unlocked) AddOption(Text.AskNumber, () => Send(ActionIds.AskNumber));
            if (live)
            {
                AddOption(Text.Invite, () => Send(ActionIds.Invite));
                if (npc.isRecruitable) AddOption(Text.Rush, () => Send(ActionIds.RushTalk));
                var row = new VisualElement();
                row.AddToClassList("dialogue-input-row");
                input = new TextField { maxLength = SocialRules.InputCharacters };
                input.AddToClassList("dialogue-input");
                input.textEdition.placeholder = Text.SayPlaceholder;
                input.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) SendText(); });
                var send = new Button(SendText) { text = Text.Send };
                send.AddToClassList("dialogue-option");
                send.AddToClassList("dialogue-send");
                row.Add(input);
                row.Add(send);
                options.Add(row);
            }
            else if (body != null && unlocked) body.text = Text.NoCampaign;
            AddOption(Text.Leave, Close);
        }

        void AddOption(string label, System.Action onClick)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("dialogue-option");
            options.Add(button);
        }

        void Send(string action)
        {
            if (!IsOpen || waiting) return;
            var campaign = NetworkedCampaign.Instance;
            if (campaign != null && campaign.IsCampaignActive) campaign.RequestAction(action, npc.npcId);
            else if (action == ActionIds.AskNumber) { NetworkedContactRoster.Instance?.RequestUnlock(npc.npcId); BuildOptions(); return; }
            else return;
            waiting = true;
            spokenLine = null;
            body.text = Text.Waiting;
        }

        void SendText()
        {
            if (!IsOpen || waiting || input == null) return;
            string text = (input.value ?? "").Trim();
            if (text.Length == 0) return;
            var campaign = NetworkedCampaign.Instance;
            if (campaign == null || !campaign.IsCampaignActive) return;
            campaign.RequestTalk(npc.npcId, text);
            input.value = "";
            waiting = true;
            spokenLine = null;
            body.text = "“" + text + "”\n" + Text.Waiting;
        }

        void Subscribe(NetworkedCampaign campaign)
        {
            subscribed = campaign;
            if (campaign == null) return;
            campaign.OnActionResult += HandleActionResult;
            campaign.OnConversation += HandleConversation;
            campaign.OnSpeech += HandleSpeech;
        }

        void Unsubscribe()
        {
            if (subscribed == null) return;
            subscribed.OnActionResult -= HandleActionResult;
            subscribed.OnConversation -= HandleConversation;
            subscribed.OnSpeech -= HandleSpeech;
            subscribed = null;
        }

        void HandleActionResult(ActionResultNet result)
        {
            if (!IsOpen) return;
            string action = result.Action.ToString();
            bool talkLike = action == ActionIds.Invite || action == ActionIds.RushTalk || action == ActionIds.Talk;
            if (action != ActionIds.AskNumber && !talkLike) return;
            if (!result.Ok || action == ActionIds.AskNumber)
            {
                // Talk-type successes wait for the NPC's reply; everything else is final here.
                waiting = false;
                body.text = result.Message.ToString();
                if (action == ActionIds.AskNumber) BuildOptions();
            }
        }

        /// <summary>
        /// Replaces what the player typed with what actually came out (the host rewrites drunk speech before the NPC hears it,
        /// design/gdd/drunk-mechanic.md). Sent before the NPC's reply on the same reliable channel.
        /// </summary>
        void HandleSpeech(string npcId, string spoken, string condition)
        {
            if (!IsOpen || !waiting || npcId != npc.npcId || string.IsNullOrEmpty(spoken)) return;
            string tag = condition == SpeakerConditions.Wasted ? Text.Wasted : condition == SpeakerConditions.Drunk ? Text.Slurring : "";
            spokenLine = "“" + spoken + "”" + (tag.Length > 0 ? "\n" + tag : "");
            body.text = spokenLine + "\n" + Text.Waiting;
        }

        void HandleConversation(string npcId, ConversationResult result)
        {
            if (!IsOpen || npcId != npc.npcId) return;
            waiting = false;
            if (result.status == "committed")
            {
                string tag = result.commitment == "accepted" ? Text.Coming : result.commitment == "conditional" ? Text.Conditional :
                    result.commitment == "declined" ? Text.NotComing : result.commitment == "deferred" ? Text.Undecided : "";
                body.text = (spokenLine != null ? spokenLine + "\n\n" : "") + result.reply + (tag.Length > 0 ? "\n" + tag : "");
            }
            else body.text = result.status == "busy" ? string.Format(Text.Busy, npc.displayName) : (result.error ?? result.status);
        }

        /// <summary>Frees the cursor and stops the local player moving/looking (or typing into movement) while talking.</summary>
        void PausePlayerInput(bool pause)
        {
            if (pause)
            {
                pausedInputs.Clear();
                foreach (var playerInput in PlayerInput.all)
                {
                    if (playerInput == null || !playerInput.inputIsActive) continue;
                    var inputs = playerInput.GetComponent<StarterAssetsInputs>();
                    if (inputs != null)
                    {
                        inputs.move = Vector2.zero;
                        inputs.look = Vector2.zero;
                        inputs.jump = false;
                        inputs.sprint = false;
                        inputs.interact = false;
                        inputs.cursorInputForLook = false;
                    }
                    playerInput.DeactivateInput();
                    pausedInputs.Add(playerInput);
                }
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
            else
            {
                foreach (var playerInput in pausedInputs)
                {
                    if (playerInput == null) continue;
                    playerInput.ActivateInput();
                    var inputs = playerInput.GetComponent<StarterAssetsInputs>();
                    if (inputs != null) inputs.cursorInputForLook = true;
                }
                pausedInputs.Clear();
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
            }
        }
    }
}
