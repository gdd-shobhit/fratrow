#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Unity.FPSSample_2;

public static class LightingProfileExporter
{
    [MenuItem("Lighting/Export Current Settings to Profile")]
    public static void ExportProfile()
    {
        string path = EditorUtility.SaveFilePanelInProject("Save Lighting Profile", "LightingProfile", "asset", "Save lighting settings");
        if (string.IsNullOrEmpty(path)) return;

        var profile = ScriptableObject.CreateInstance<LightingProfile>();

        profile.skybox = RenderSettings.skybox;
        profile.sun = RenderSettings.sun;
        profile.ambientMode = RenderSettings.ambientMode;
        profile.ambientLight = RenderSettings.ambientLight;
        profile.ambientIntensity = RenderSettings.ambientIntensity;

        profile.fog = RenderSettings.fog;
        profile.fogMode = RenderSettings.fogMode;
        profile.fogColor = RenderSettings.fogColor;
        profile.fogDensity = RenderSettings.fogDensity;
        profile.fogStartDistance = RenderSettings.fogStartDistance;
        profile.fogEndDistance = RenderSettings.fogEndDistance;

        AssetDatabase.CreateAsset(profile, path);
        AssetDatabase.SaveAssets();
    }
}
#endif