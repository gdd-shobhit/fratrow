#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class PlayModeSettingsHandler
{
    public const string k_SaveOpenScenesOnPlayKey = "PlayModeSettings.SaveOpenScenesOnPlay";
    public const string k_AutomaticallyPlayCurrentLevelKey = "PlayModeSettings.AutomaticallyPlayCurrentLevel";
    public const string k_BootDeveloperMenu = "PlayModeSettings.BootDeveloperMenu";
    public const string k_SkipUITransitions = "PlayModeSettings.SkipUITransitions";
    public const string k_SHowEditorVisuals = "PlayModeSettings.ShowEditorVisuals";

    public class PlayModeSettings
    {
        public bool SaveOpenScenesOnPlay;
        public bool AutomaticallyPlayCurrentLevel;
        public bool BootDeveloperMenu;
        public bool SkipUITransitions;
        public bool ShowEditorVisuals;
    }

    public static PlayModeSettings GetEditorSettings()
    {
        return new PlayModeSettings
        {
            SaveOpenScenesOnPlay = EditorPrefs.GetBool(k_SaveOpenScenesOnPlayKey, true),
            AutomaticallyPlayCurrentLevel = EditorPrefs.GetBool(k_AutomaticallyPlayCurrentLevelKey, false),
            BootDeveloperMenu = EditorPrefs.GetBool(k_BootDeveloperMenu, true),
            SkipUITransitions = EditorPrefs.GetBool(k_SkipUITransitions, true),
            ShowEditorVisuals = EditorPrefs.GetBool(k_SHowEditorVisuals, false)
        };
    }

    public static void SetEditorSettings(PlayModeSettings settings)
    {
        EditorPrefs.SetBool(k_SaveOpenScenesOnPlayKey, settings.SaveOpenScenesOnPlay);
        EditorPrefs.SetBool(k_AutomaticallyPlayCurrentLevelKey, settings.AutomaticallyPlayCurrentLevel);
        EditorPrefs.SetBool(k_BootDeveloperMenu, settings.BootDeveloperMenu);
        EditorPrefs.SetBool(k_SkipUITransitions, settings.SkipUITransitions);
        EditorPrefs.SetBool(k_SHowEditorVisuals, settings.ShowEditorVisuals);
    }
}

class PlayModeSettingsGUIContent
{
    private static readonly GUIContent m_SaveOpenScenesOnPlay = new GUIContent("Automatically save open scenes on play",
        "Will save any open scenes with any changes before entering play mode");

    private static readonly GUIContent m_AutomaticallyPlayCurrentLevel = new GUIContent(
        "Automatically host and play the current level", "Will host in IP mode with current playmode settings");

    private static readonly GUIContent m_BootDeveloperMenu =
        new GUIContent("Automatically boot into the Developer menu & skip the frontend",
            "Will be override by Automatically host and play");

    private static readonly GUIContent m_SkipUITransitions =
        new GUIContent("Skip UI transitions and fades for faster developer experience");

    private static readonly GUIContent m_ShowEditorVisuals = new GUIContent("Show [EDITORVISUALS] in the inspector");

    public static void DrawSettingsButtons(PlayModeSettingsHandler.PlayModeSettings settings)
    {
        EditorGUI.indentLevel += 1;

        settings.SaveOpenScenesOnPlay =
            EditorGUILayout.ToggleLeft(m_SaveOpenScenesOnPlay, settings.SaveOpenScenesOnPlay);
        settings.AutomaticallyPlayCurrentLevel = EditorGUILayout.ToggleLeft(m_AutomaticallyPlayCurrentLevel,
            settings.AutomaticallyPlayCurrentLevel);
        settings.BootDeveloperMenu = EditorGUILayout.ToggleLeft(m_BootDeveloperMenu, settings.BootDeveloperMenu);
        settings.SkipUITransitions = EditorGUILayout.ToggleLeft(m_SkipUITransitions, settings.SkipUITransitions);
        settings.ShowEditorVisuals = EditorGUILayout.ToggleLeft(m_ShowEditorVisuals, settings.ShowEditorVisuals);

        EditorGUI.indentLevel -= 1;
    }
}

static class PlayModeSettingsProvider
{
    [SettingsProvider]
    public static SettingsProvider CreateSettingsProvider()
    {
        var provider = new SettingsProvider("Preferences/PlayMode Settings", SettingsScope.User)
        {
            label = "PlayMode Settings",

            guiHandler = (searchContext) =>
            {
                PlayModeSettingsHandler.PlayModeSettings settings = PlayModeSettingsHandler.GetEditorSettings();
                EditorGUI.BeginChangeCheck();
                PlayModeSettingsGUIContent.DrawSettingsButtons(settings);
                if (EditorGUI.EndChangeCheck())
                {
                    PlayModeSettingsHandler.SetEditorSettings(settings);
                }
            },

            // Keywords for the search bar in the Unity Preferences menu
            keywords = new HashSet<string>(new[] { "PlayMode", "Settings" })
        };

        return provider;
    }
}
#endif