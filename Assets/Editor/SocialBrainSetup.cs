using System.IO;
using System.Linq;
using FratRow.Social;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class SocialBrainSetup
{
    const string LlamaServerKey = "FratRow.Social.LlamaServer";
    const string BuildMenu = "FratRow/Social Brains/llama.cpp build/";

    /// <summary>Turns the local model on for editor hosts. The llama.cpp build follows the "llama.cpp build" menu (Auto by default).</summary>
    [MenuItem("FratRow/Social Brains/Enable development experiment")]
    public static void Enable()
    {
        EditorPrefs.SetBool("FratRow.Social.Enabled", true);
        string model = Path.GetFullPath("Library/SocialModels/" + SocialModelCatalog.QwenFile);
        if (File.Exists(model)) EditorPrefs.SetString("FratRow.Social.Model", model);
        else Debug.LogWarning("[Social] Model missing at " + model + ". Run: pwsh -File tools/social/bootstrap.ps1 -Models");
        var runtime = NetworkedSocialConversation.ChooseRuntime();
        if (runtime == null) Debug.LogError("[Social] No llama.cpp build in " + NetworkedSocialConversation.NativeRoot() + ". Run: pwsh -File tools/social/bootstrap.ps1 -Models");
        else Debug.Log("[Social] Local model enabled: " + runtime.Name + " (" + runtime.ServerPath + "). Applies to the next host session.");
    }
    [MenuItem("FratRow/Social Brains/Disable development experiment")]
    public static void Disable() => EditorPrefs.SetBool("FratRow.Social.Enabled", false);

    [MenuItem(BuildMenu + "Auto (CUDA on NVIDIA, else Vulkan, else CPU)")]
    static void BuildAuto() { EditorPrefs.DeleteKey(LlamaServerKey); LogBuild(); }
    [MenuItem(BuildMenu + "Auto (CUDA on NVIDIA, else Vulkan, else CPU)", true)]
    static bool BuildAutoCheck() { Menu.SetChecked(BuildMenu + "Auto (CUDA on NVIDIA, else Vulkan, else CPU)", !EditorPrefs.HasKey(LlamaServerKey)); return true; }
    [MenuItem(BuildMenu + "CUDA (NVIDIA)")] static void BuildCuda() => Force(LlamaBackend.Cuda);
    [MenuItem(BuildMenu + "CUDA (NVIDIA)", true)] static bool BuildCudaCheck() => Check(LlamaBackend.Cuda, "CUDA (NVIDIA)");
    [MenuItem(BuildMenu + "Vulkan (any GPU)")] static void BuildVulkan() => Force(LlamaBackend.Vulkan);
    [MenuItem(BuildMenu + "Vulkan (any GPU)", true)] static bool BuildVulkanCheck() => Check(LlamaBackend.Vulkan, "Vulkan (any GPU)");
    [MenuItem(BuildMenu + "CPU (slow)")] static void BuildCpu() => Force(LlamaBackend.Cpu);
    [MenuItem(BuildMenu + "CPU (slow)", true)] static bool BuildCpuCheck() => Check(LlamaBackend.Cpu, "CPU (slow)");

    static string ServerFor(LlamaBackend backend) =>
        Path.Combine(NetworkedSocialConversation.NativeRoot(), LlamaRuntimes.Folder(backend), LlamaRuntimes.ServerExe);
    static void Force(LlamaBackend backend)
    {
        string server = ServerFor(backend);
        if (!File.Exists(server)) { Debug.LogError("[Social] " + backend + " build not installed at " + server + ". Run: pwsh -File tools/social/bootstrap.ps1 -Runtime " + backend.ToString().ToLowerInvariant()); return; }
        EditorPrefs.SetString(LlamaServerKey, server);
        LogBuild();
    }
    static bool Check(LlamaBackend backend, string item)
    {
        Menu.SetChecked(BuildMenu + item, EditorPrefs.GetString(LlamaServerKey, "") == ServerFor(backend));
        return File.Exists(ServerFor(backend));
    }
    static void LogBuild()
    {
        var runtime = NetworkedSocialConversation.ChooseRuntime();
        Debug.Log(runtime == null ? "[Social] No llama.cpp build installed; run tools/social/bootstrap.ps1." : "[Social] llama.cpp build: " + runtime.Name + ". Applies to the next host session.");
    }

    const string ReplyModeKey = "FratRow.Social.ReplyMode";
    [MenuItem("FratRow/Social Brains/Reply mode: persona (model speaks in character)")]
    public static void PersonaReplies() { EditorPrefs.SetString(ReplyModeKey, "persona"); Debug.Log("[Social] Persona replies: the model voices each NPC from their persona and your chat history. Applies to the next host session."); }
    [MenuItem("FratRow/Social Brains/Reply mode: persona (model speaks in character)", true)]
    static bool PersonaRepliesCheck() { Menu.SetChecked("FratRow/Social Brains/Reply mode: persona (model speaks in character)", NetworkedSocialConversation.ReplyMode() == "persona"); return true; }
    [MenuItem("FratRow/Social Brains/Reply mode: grounded (template lines)")]
    public static void GroundedReplies() { EditorPrefs.SetString(ReplyModeKey, "grounded"); Debug.Log("[Social] Grounded replies: the model picks social acts and the host composes template lines. Applies to the next host session."); }
    [MenuItem("FratRow/Social Brains/Reply mode: grounded (template lines)", true)]
    static bool GroundedRepliesCheck() { Menu.SetChecked("FratRow/Social Brains/Reply mode: grounded (template lines)", NetworkedSocialConversation.ReplyMode() == "grounded"); return true; }

    [MenuItem("FratRow/Social Brains/Bake and validate network components")]
    public static void Bake()
    {
        const string managerPath = "Assets/Prefabs/GameManager.prefab";
        var manager = PrefabUtility.LoadPrefabContents(managerPath);
        try
        {
            if (manager.GetComponent<NetworkedSocialConversation>() == null) manager.AddComponent<NetworkedSocialConversation>();
            if (manager.GetComponent<NetworkedCampaign>() == null) manager.AddComponent<NetworkedCampaign>();
            PrefabUtility.SaveAsPrefabAsset(manager, managerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(manager); }
        RemoveDuplicatePhoneInvites();
        foreach (string path in AssetDatabase.FindAssets("t:GuestSO").Select(AssetDatabase.GUIDToAssetPath))
        {
            var data = AssetDatabase.LoadAssetAtPath<GuestSO>(path);
            if (data == null || data.prefab == null) continue;
            if (data.prefab.GetComponent<Unity.Netcode.NetworkObject>() == null || data.prefab.GetComponent<NPCGuest>() == null || data.prefab.GetComponent<NetworkedNPCGuest>() == null)
                throw new System.InvalidOperationException("Guest prefab requires baked network and guest components: " + path);
        }
        AssetDatabase.SaveAssets(); Debug.Log("[Social] Network component bake validated.");
    }

    /// <summary>
    /// GameManager owns the one NetworkedPhoneInvites singleton; a second copy on PartyManager fights over Instance.
    /// Removes any copy from PartyManager.prefab (no-op when already clean).
    /// </summary>
    static void RemoveDuplicatePhoneInvites()
    {
        const string partyPath = "Assets/Prefabs/PartyManager.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(partyPath) == null) return;
        var party = PrefabUtility.LoadPrefabContents(partyPath);
        try
        {
            var duplicates = party.GetComponentsInChildren<Systems.Phone.NetworkedPhoneInvites>(true);
            if (duplicates.Length == 0) return;
            foreach (var duplicate in duplicates) Object.DestroyImmediate(duplicate);
            PrefabUtility.SaveAsPrefabAsset(party, partyPath);
            Debug.Log("[Social] Removed " + duplicates.Length + " duplicate NetworkedPhoneInvites from " + partyPath + ".");
        }
        finally { PrefabUtility.UnloadPrefabContents(party); }
    }

    /// <summary>
    /// Deletes the local host's campaign save (social journal + game-loop state) so the next host session starts a fresh
    /// campaign. Path: Application.persistentDataPath/SocialCampaigns/fratrow-main. Asks for confirmation; refuses in Play Mode.
    /// </summary>
    [MenuItem("FratRow/Campaign/Reset local campaign save")]
    public static void ResetLocalCampaignSave()
    {
        string directory = Path.Combine(Application.persistentDataPath, "SocialCampaigns", "fratrow-main");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Reset local campaign save", "Exit Play Mode first (the host keeps the save files open).", "OK");
            return;
        }
        if (!Directory.Exists(directory))
        {
            EditorUtility.DisplayDialog("Reset local campaign save", "No local campaign save found at:\n" + directory, "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Reset local campaign save",
            "Delete the local campaign save?\n\n" + directory + "\n\nThis cannot be undone.", "Delete", "Cancel")) return;
        try
        {
            Directory.Delete(directory, true);
            Debug.Log("[Campaign] Deleted local campaign save: " + directory);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Campaign] Could not delete " + directory + ": " + e.Message);
        }
    }
    public static void BuildDevelopmentPlayer()
    {
        Bake();
        string output = NetworkedSocialConversation.Option("--social-build-output", "BuildTest/Social/FratRow.exe");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded) throw new System.Exception("Social development player build failed");
    }
}
