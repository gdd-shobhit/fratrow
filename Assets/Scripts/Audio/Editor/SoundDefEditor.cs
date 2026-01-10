
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Custom editor for SoundDef ScriptableObjects that provides audio playback testing functionality.
/// Allows developers to preview and test audio settings directly in the Unity Inspector.
/// </summary>

[CustomEditor(typeof(SoundDef))]

public class SoundDefEditor : Editor
{
    private SoundGameObject.AudioComponentData[] m_AudioComponentData = null;
    private SoundDef m_SoundDef;
    private SoundEmitter m_SoundEmitter;
    private bool m_IsPlaying;
    private SoundMixer m_SoundMixer;
    private int m_RepeatCount;

    /// <summary>
    /// Creates the inspector GUI for the SoundDef editor.
    /// Initializes audio components for testing playback in the editor.
    /// </summary>
    /// <returns>Returns null to use the default IMGUI inspector</returns>
    public override VisualElement CreateInspectorGUI()
    {
        m_SoundDef = (SoundDef)target;
        InitAudio();
        return null;
    }

    /// <summary>
    /// Called when the editor is destroyed.
    /// Cleans up any active audio playback and test GameObjects to prevent memory leaks.
    /// </summary>
    public void OnDestroy()
    {
        if (m_SoundMixer != null)
        {
            if (m_SoundEmitter != null)
            {
                m_SoundEmitter.Kill();
                CleanupTestGameObjects();
                m_SoundEmitter = null;
            }
        }
        EditorApplication.update -= Update;
    }

    /// <summary>
    /// Draws the custom inspector GUI for the SoundDef.
    /// Provides Play/Stop buttons for audio testing and displays all serialized properties.
    /// </summary>
    public override void OnInspectorGUI()
    {
        m_SoundDef = (SoundDef)target;

        // Allow playing audio even when sounddef is readonly
        var oldEnabled = GUI.enabled;
        GUI.enabled = true;
        if (m_IsPlaying && GUILayout.Button("Stop [" + m_RepeatCount + "]"))
        {
            StopPlayback();
        }
        else if (!m_IsPlaying && GUILayout.Button("Play >"))
        {
            if (m_SoundDef.UnityAudioMixer != null)
            {
                m_SoundMixer = new SoundMixer(m_SoundDef.UnityAudioMixer);
                m_IsPlaying = true;
                m_SoundEmitter.SetSoundDef(m_SoundDef);
                m_SoundEmitter.Activate();
                m_RepeatCount = Random.Range(m_SoundDef.RepeatInfo.RepeatMin, m_SoundDef.RepeatInfo.RepeatMax);

                SoundEmitter.SoundDefOverrideData soundDefOverrideInfo = m_SoundEmitter.SoundDefOverrideInfo;

                soundDefOverrideInfo.VolumeScale = m_SoundDef.VolumeScale;
                soundDefOverrideInfo.BasePitchInCents = m_SoundDef.BasePitchInCents;
                soundDefOverrideInfo.BaseLowPassCutoff = m_SoundDef.BaseLowPassCutoff;

                foreach (var soundGameObject in m_SoundEmitter.ActiveSoundGameObjects)
                {
                    soundGameObject.EnableAudioFilterComponents(m_SoundDef);
                    soundGameObject.Enable(m_SoundEmitter);
                }

                EditorApplication.update += Update;
                m_SoundEmitter.Volume = m_SoundDef.EditorVolume;
                m_SoundEmitter.Play(null, m_SoundMixer.MixerGroups);    
                    // No transform required when playing in the inspector window as we don't care about listener position
            }
        }
        GUI.enabled = oldEnabled;

        DrawPropertiesExcluding(serializedObject, new string[] { "m_Script" });

        serializedObject.ApplyModifiedProperties();
    }

    void StopPlayback()
    {
        if (!m_IsPlaying)
        {
            return;
        }
        m_IsPlaying = false;
        m_SoundEmitter?.Stop();
        EditorApplication.update -= Update;
    }

    void CleanupTestGameObjects()
    {
        if (m_AudioComponentData == null)
        {
            return;
        }

        foreach (SoundGameObject.AudioComponentData audioData in m_AudioComponentData)
        {
            if (audioData.ASource != null && audioData.ASource.gameObject != null)
            {
                DestroyImmediate(audioData.ASource.gameObject);
            }
        }
        m_AudioComponentData = null;
    }

    private void InitAudio()
    {
        if (m_SoundEmitter == null)
        {
            m_AudioComponentData = new SoundGameObject.AudioComponentData[m_SoundDef.PlayCount];
            m_SoundEmitter = new SoundEmitter(null);

            for (int i = 0; i < m_SoundDef.PlayCount; i++)
            {
                var gameObject = new GameObject("testSource");
                gameObject.hideFlags = HideFlags.HideAndDontSave;
                m_AudioComponentData[i] = new SoundGameObject.AudioComponentData()
                {
                    ASource = gameObject.AddComponent<AudioSource>(),
                    LowPassFilter = gameObject.AddComponent<AudioLowPassFilter>(),
                    HighPassFilter = gameObject.AddComponent<AudioHighPassFilter>(),
                    DistortionFilter = gameObject.AddComponent<AudioDistortionFilter>()
                };
                SoundGameObject soundGameObject = new SoundGameObject(gameObject, m_AudioComponentData[i]);
                m_SoundEmitter.ActiveSoundGameObjects.Add(soundGameObject);
            }
            m_SoundEmitter.Reserved = SoundEmitter.ReservedInfo.ReservedEmitterAndAudioSources;
        }
    }

    /// <summary>
    /// Determines whether the inspector requires constant repainting.
    /// Returns true to ensure the UI updates properly during audio playback.
    /// </summary>
    /// <returns>Always returns true to maintain real-time updates</returns>
    public override bool RequiresConstantRepaint()
    {
        return true;
    }

    void Update()
    {
        if (m_SoundEmitter == null)
        {
            StopPlayback();
            return;
        }
        
        int playCount = 0;
        foreach (var soundGameObject in m_SoundEmitter.ActiveSoundGameObjects)
        {
            if (soundGameObject.IsPlaying())
            {
                playCount++;
                soundGameObject.CheckForAudioSourceLoop();
            }
        }

        if (playCount == 0)
        {
            Debug.Log("Repeat Count:" + m_SoundEmitter.GetRepeatCount());

            if (m_RepeatCount > 1)
            {
                m_RepeatCount--;
                m_SoundEmitter.Play(null, m_SoundMixer.MixerGroups);
            }
            else
            {
                StopPlayback();
                EditorUtility.SetDirty(EditorWindow.focusedWindow);
            }
        }
    }
}
