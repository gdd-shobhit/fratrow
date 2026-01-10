using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class GhostSpawnerVisuals
{
    static GhostSpawnerVisuals()
    {
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        EditorSceneManager.sceneSaved -= OnSceneSaved;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        
        EditorSceneManager.sceneSaving += OnSceneSaving;
        EditorSceneManager.sceneSaved += OnSceneSaved;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RuntimeInit()
    {
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        EditorSceneManager.sceneSaved -= OnSceneSaved;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }
    
    private static void OnSceneSaving(Scene scene, string path)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var spawner in root.GetComponentsInChildren<GhostSpawner>(true))
            {
                spawner.StripEditorVisuals(false);
            }
        }
    }

    private static void OnSceneSaved(Scene scene)
    {
        if (!Application.isPlaying)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var spawner in root.GetComponentsInChildren<GhostSpawner>(true))
                {
                    //spawner.RestoreEditorVisuals();
                }
            }
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            foreach (var spawner in Object.FindObjectsByType<GhostSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                spawner.StripEditorVisuals(false);
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            foreach (var spawner in Object.FindObjectsByType<GhostSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                //spawner.RestoreEditorVisuals();
            }
        }
    }
}
