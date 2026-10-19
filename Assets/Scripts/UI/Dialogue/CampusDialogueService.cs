using System;
using Systems.Campus;
using Systems.Phone;
using UnityEngine;
using Yarn.Unity;

namespace Systems.Dialogue
{
    /// <summary>
    /// Hosts Yarn DialogueRunner + UI Toolkit presenter. Compiles Resources/Dialogue yarn/text at runtime.
    /// </summary>
    public class CampusDialogueService : MonoBehaviour
    {
        public static CampusDialogueService Instance { get; private set; }

        [SerializeField] DialogueRunner dialogueRunner;
        [SerializeField] UITKDialoguePresenter presenter;

        NamedNpcSO _activeNpc;
        Action _onComplete;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureSetup();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void EnsureSetup()
        {
            if (dialogueRunner == null)
            {
                dialogueRunner = GetComponent<DialogueRunner>();
                if (dialogueRunner == null)
                {
                    dialogueRunner = gameObject.AddComponent<DialogueRunner>();
                }
            }

            if (presenter == null)
            {
                presenter = GetComponent<UITKDialoguePresenter>();
                if (presenter == null)
                {
                    presenter = gameObject.AddComponent<UITKDialoguePresenter>();
                }
            }

            var yarnAssets = Resources.LoadAll<TextAsset>("Dialogue");
            var filtered = new System.Collections.Generic.List<TextAsset>();
            if (yarnAssets != null)
            {
                foreach (var asset in yarnAssets)
                {
                    if (asset == null)
                    {
                        continue;
                    }

                    // Prefer CampusNPCs / dialogue scripts; skip empty
                    if (asset.text.Contains("title:"))
                    {
                        filtered.Add(asset);
                    }
                }
            }

            if (filtered.Count == 0)
            {
                Debug.LogWarning("[CampusDialogue] No yarn TextAssets in Resources/Dialogue");
            }
            else
            {
                var project = RuntimeYarnProjectFactory.CreateFromTextAssets(filtered);
                dialogueRunner.SetProject(project);
            }

            dialogueRunner.DialoguePresenters = new DialoguePresenterBase[] { presenter };
            dialogueRunner.AddCommandHandler<string>("unlock_contact", OnUnlockContact);
            dialogueRunner.AddCommandHandler<string>("recruit", OnRecruit);
            dialogueRunner.onDialogueComplete?.AddListener(HandleDialogueComplete);
        }

        public bool IsRunning => dialogueRunner != null && dialogueRunner.IsDialogueRunning;

        public void StartNamedNpcDialogue(NamedNpcSO npc, bool canUnlock, Action onComplete = null)
        {
            if (npc == null || dialogueRunner == null)
            {
                return;
            }

            if (IsRunning)
            {
                Debug.LogWarning("[CampusDialogue] Already running.");
                return;
            }

            _activeNpc = npc;
            _onComplete = onComplete;

            var storage = dialogueRunner.VariableStorage;
            storage.SetValue("$can_unlock", canUnlock);
            storage.SetValue("$npc_id", npc.npcId);
            storage.SetValue("$affinity", GetAffinity(npc));

            dialogueRunner.StartDialogue(npc.YarnNode);
        }

        float GetAffinity(NamedNpcSO npc)
        {
            if (NetworkedContactRoster.Instance != null)
            {
                return NetworkedContactRoster.Instance.GetAffinity(npc.npcId);
            }

            var contact = npc.ResolveContact();
            if (contact == null)
            {
                return 0f;
            }

            var phone = FindLocalPhoneController();
            return phone?.GetState(contact.Id)?.affinity ?? 0f;
        }

        void OnUnlockContact(string npcId)
        {
            var id = string.IsNullOrEmpty(npcId) ? _activeNpc?.npcId : npcId;
            NetworkedContactRoster.Instance?.RequestUnlock(id);
        }

        void OnRecruit(string npcId)
        {
            var id = string.IsNullOrEmpty(npcId) ? _activeNpc?.npcId : npcId;
            NetworkedContactRoster.Instance?.RequestRecruit(id);
        }

        void HandleDialogueComplete()
        {
            _activeNpc = null;
            var cb = _onComplete;
            _onComplete = null;
            cb?.Invoke();
        }

        public static PhoneController FindLocalPhoneController()
        {
            foreach (var phone in FindObjectsByType<PhoneController>(FindObjectsSortMode.None))
            {
                var net = phone.GetComponentInParent<Unity.Netcode.NetworkObject>();
                if (net == null || net.IsOwner)
                {
                    return phone;
                }
            }

            return FindFirstObjectByType<PhoneController>();
        }
    }
}
