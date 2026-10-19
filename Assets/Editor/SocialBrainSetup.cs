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

    [MenuItem("FratRow/Social Brains/Bake and validate network components")]
    public static void Bake()
    {
        const string managerPath = "Assets/Prefabs/GameManager.prefab";
        var manager = PrefabUtility.LoadPrefabContents(managerPath);
        try
        {
            if (manager.GetComponent<NetworkedSocialConversation>() == null) manager.AddComponent<NetworkedSocialConversation>();
            PrefabUtility.SaveAsPrefabAsset(manager, managerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(manager); }
        foreach (string path in AssetDatabase.FindAssets("t:GuestSO").Select(AssetDatabase.GUIDToAssetPath))
        {
            var data = AssetDatabase.LoadAssetAtPath<GuestSO>(path);
            if (data == null || data.prefab == null) continue;
            if (data.prefab.GetComponent<Unity.Netcode.NetworkObject>() == null || data.prefab.GetComponent<NPCGuest>() == null || data.prefab.GetComponent<NetworkedNPCGuest>() == null)
                throw new System.InvalidOperationException("Guest prefab requires baked network and guest components: " + path);
        }
        AssetDatabase.SaveAssets(); Debug.Log("[Social] Network component bake validated.");
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
