using System.Threading;
using StarterAssets;
using UnityEngine;
using UnityEngine.UIElements;
using Yarn;
using Yarn.Unity;

namespace Systems.Dialogue
{
    /// <summary>
    /// UI Toolkit Yarn presenter: lines + options, look/cursor lock like shop/phone.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UITKDialoguePresenter : DialoguePresenterBase
    {
        [SerializeField] UIDocument document;

        VisualElement _root;
        Label _speaker;
        Label _body;
        Button _continue;
        VisualElement _options;

        bool _advanceRequested;
        DialogueOption _selectedOption;
        bool _awaitingOption;

        public bool IsShowing => _root != null && _root.style.display == DisplayStyle.Flex;

        void Awake()
        {
            if (document == null)
            {
                document = GetComponent<UIDocument>();
            }
        }

        void Bind()
        {
            if (document == null || document.rootVisualElement == null)
            {
                return;
            }

            var rootVe = document.rootVisualElement;
            _root = rootVe.Q<VisualElement>("dialogue-root");
            _speaker = rootVe.Q<Label>("dialogue-speaker");
            _body = rootVe.Q<Label>("dialogue-body");
            _continue = rootVe.Q<Button>("dialogue-continue");
            _options = rootVe.Q<VisualElement>("dialogue-options");

            if (_continue != null)
            {
                _continue.clicked -= OnContinueClicked;
                _continue.clicked += OnContinueClicked;
            }
        }

        void OnContinueClicked()
        {
            _advanceRequested = true;
        }

        public override YarnTask OnDialogueStartedAsync()
        {
            Bind();
            if (_root != null)
            {
                _root.style.display = DisplayStyle.Flex;
            }

            ApplyCursor(true);
            return YarnTask.CompletedTask;
        }

        public override YarnTask OnDialogueCompleteAsync()
        {
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }

            if (_options != null)
            {
                _options.Clear();
            }

            ApplyCursor(false);
            return YarnTask.CompletedTask;
        }

        public override async YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
        {
            Bind();
            if (_root == null)
            {
                return;
            }

            _root.style.display = DisplayStyle.Flex;
            if (_options != null)
            {
                _options.Clear();
            }

            if (_continue != null)
            {
                _continue.style.display = DisplayStyle.Flex;
            }

            string speaker = line.CharacterName ?? string.Empty;
            string body = line.TextWithoutCharacterName.Text ?? line.RawText ?? string.Empty;
            if (_speaker != null)
            {
                _speaker.text = speaker;
            }

            if (_body != null)
            {
                _body.text = body;
            }

            _advanceRequested = false;
            while (!_advanceRequested && !token.IsNextLineRequested && !token.IsHurryUpRequested)
            {
                if (token.NextLineToken.IsCancellationRequested)
                {
                    break;
                }

                await YarnTask.Yield();
            }
        }

        public override async YarnTask<DialogueOption?> RunOptionsAsync(DialogueOption[] dialogueOptions, CancellationToken cancellationToken)
        {
            Bind();
            if (_root == null || _options == null)
            {
                return await DialogueRunner.NoOptionSelected;
            }

            _root.style.display = DisplayStyle.Flex;
            if (_continue != null)
            {
                _continue.style.display = DisplayStyle.None;
            }

            _options.Clear();
            _selectedOption = null;
            _awaitingOption = true;

            for (int i = 0; i < dialogueOptions.Length; i++)
            {
                var option = dialogueOptions[i];
                if (!option.IsAvailable)
                {
                    continue;
                }

                var btn = new Button(() =>
                {
                    _selectedOption = option;
                    _awaitingOption = false;
                })
                {
                    text = option.Line.TextWithoutCharacterName.Text ?? option.Line.RawText ?? $"Option {i}",
                    name = $"option-{i}"
                };
                btn.AddToClassList("dialogue-option");
                _options.Add(btn);
            }

            while (_awaitingOption && !cancellationToken.IsCancellationRequested)
            {
                await YarnTask.Yield();
            }

            _options.Clear();
            return _selectedOption;
        }

        static void ApplyCursor(bool dialogueOpen)
        {
            if (dialogueOpen)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
                var inputs = Object.FindFirstObjectByType<StarterAssetsInputs>();
                if (inputs != null)
                {
                    inputs.cursorInputForLook = false;
                }
            }
            else
            {
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
                var inputs = Object.FindFirstObjectByType<StarterAssetsInputs>();
                if (inputs != null)
                {
                    inputs.cursorInputForLook = true;
                }
            }
        }
    }
}
