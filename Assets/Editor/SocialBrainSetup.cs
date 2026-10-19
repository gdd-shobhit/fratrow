using System.IO;
using System.Linq;
using FratRow.Social;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class SocialBrainSetup
{
    [MenuItem("FratRow/Social Brains/Enable development experiment")]
    public static void Enable()
    {
        EditorPrefs.DeleteKey("FratRow.Social.LlamaServer");
        EnableScenario();
        Debug.Log("RunAnywhere CPU selected. Use backend commands; CPU comparisons may need --social-timeout 90. Default deadline remains 15 seconds.");
    }
    [MenuItem("FratRow/Social Brains/Enable development experiment (CUDA fallback)")]
    public static void EnableCuda()
    {
        string server = Path.GetFullPath("Library/SocialNative/llama-cuda/llama-server.exe");
        if (!File.Exists(server)) throw new System.IO.FileNotFoundException("Run bootstrap.ps1 -CudaFallback first", server);
        EditorPrefs.SetString("FratRow.Social.LlamaServer", server);
        EnableScenario();
        Debug.Log("Direct llama.cpp CUDA fallback selected for editor hosts. This is the runtime measured near two seconds; RunAnywhere remains available through the CPU enable menu.");
    }
    static void EnableScenario()
    {
        EditorPrefs.SetBool("FratRow.Social.Enabled", true);
        string path = Path.GetFullPath("Library/SocialModels/" + SocialModelCatalog.QwenFile);
        if (File.Exists(path)) EditorPrefs.SetString("FratRow.Social.Model", path);
    }
    [MenuItem("FratRow/Social Brains/Disable development experiment")]
    public static void Disable() => EditorPrefs.SetBool("FratRow.Social.Enabled", false);

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
