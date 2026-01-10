using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

/// <summary> 
/// Represents a single audio source GameObject with associated audio effects and spatial positioning.
/// Handles detailed audio parameter management including filters, spatial audio, and distance-based effects.
/// </summary>
public class SoundGameObject
{

    /// <summary>
    ///
    /// Defines how the SoundGameObject's position is determined and updated.
    /// </summary>
    public enum PositionType
    {
        Position,
        ParentTransform
    }

    /// <summary>
    /// Contains references to all audio components attached to the GameObject.
    /// </summary>
    public class AudioComponentData
    {
        public AudioSource ASource;
        public AudioLowPassFilter LowPassFilter;
        public AudioHighPassFilter HighPassFilter;
        public AudioDistortionFilter DistortionFilter;
    }

    public SoundGameObject NextAvailable; // Linked list. Points to next available SoundGameObject (null = none available)
    public AudioSource m_AudioSource;

    private const float SOUND_VOL_CUTOFF = -60.0f;
    private AudioLowPassFilter m_LowPassFilter;
    private AudioHighPassFilter m_HighPassFilter;
    private AudioDistortionFilter m_DistortionFilter;
    private SoundEmitter m_Emitter;    // Emitter that is controlling this SoundObject
    private bool m_Active;
    private int m_CurrentLoopCount;
    private int m_LastTimeSamples;
    private float m_Volume;
    private GameObject m_SoundGameObjectPool;
    private GameObject m_Parent;
    private PositionType m_PositionType;

    private SoundDef m_SoundDef;
    private Transform m_ListenerTransform;
    private Interpolator m_Curve;
    private bool m_CalculateDistance;
    private float m_LPFCurveNormalizedResult;
    private float m_HPFCurveNormalizedResult;
    private float m_SpatialCurveNormalizedResult;
    private float m_LPFRandomValue;
    private float m_HPFRandomValue;
    private float m_SpatialBlendValue;

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        s_Counter = 0;
    }
    
    static int s_Counter = 0;
    private string m_DebugName;
#endif

    /// <summary>
    /// Initializes a new SoundGameObject with the specified parent and audio components.
    /// </summary>
    /// <param name="parent">Parent GameObject that will contain this sound object</param>
    /// <param name="audioComponentData">Audio components to use for playback and effects</param>
    public SoundGameObject(GameObject parent, AudioComponentData audioComponentData)
    {
#if UNITY_EDITOR
        m_DebugName = audioComponentData.ASource.gameObject.name;
#endif
        InitAudioComponents(parent, audioComponentData);
    }

    private void InitAudioComponents(GameObject parent, AudioComponentData audioComponentData)
    {
        m_SoundGameObjectPool = parent;
        m_Parent = parent;
        m_AudioSource = audioComponentData.ASource;
        m_LowPassFilter = audioComponentData.LowPassFilter;
        m_HighPassFilter = audioComponentData.HighPassFilter;
        m_DistortionFilter = audioComponentData.DistortionFilter;
        m_AudioSource.playOnAwake = false;
        m_Curve = new Interpolator();
    }

    /// <summary>
    /// Enables audio filter components based on SoundDef settings to optimize CPU usage.
    /// Only enables filters that are actually needed by the sound definition.
    /// </summary>
    /// <param name="soundDef">Sound definition containing filter settings</param>
    /// <returns>True if distance calculations are needed for any enabled effects</returns>
    public bool EnableAudioFilterComponents(SoundDef soundDef)
    {
        bool calcDistance = false;

        if (m_LowPassFilter != null)
        {
            if (m_LowPassFilter.enabled = soundDef.LowPassFilter.EnableComponent ? true : false)
            {
                calcDistance = true;
            }
        }
        if (m_HighPassFilter != null)
        {
            if (m_HighPassFilter.enabled = soundDef.HighPassFilter.EnableComponent ? true : false)
            {
                calcDistance = true;
            }
        }
        if (m_DistortionFilter)
        {
            m_DistortionFilter.enabled = soundDef.DistortionFilter.EnableComponent;
        }
#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
        {
            return calcDistance;    // No spatial blending when playing from the Unity editors inspector window
        }
#endif
        if (soundDef.DistanceInfo.SpatialBlendCurveType != Interpolator.CurveType.None && 
            soundDef.DistanceInfo.SpatialBlend_MaxDistance != 0)
        {
            calcDistance = true;
        }
        return calcDistance;
    }

    /// <summary>
    /// Enables the SoundGameObject and associates it with the specified emitter.
    /// </summary>
    /// <param name="soundEmitter">The emitter that will control this sound object</param>
    public void Enable(SoundEmitter soundEmitter)
    {
        m_Active = true;
        m_Emitter = soundEmitter;
        m_AudioSource.transform.position = Vector3.zero;
    }

    /// <summary>
    /// Kills the SoundGameObject if it belongs to the specified emitter, stopping playback and cleanup.
    /// </summary>
    /// <param name="emitter">The emitter requesting the kill operation</param>
    public void Kill(SoundEmitter emitter)
    {
        if (m_Emitter == emitter)
        {
            StopAudioSource();
            if (m_PositionType == PositionType.ParentTransform)
            {
                m_AudioSource.transform.parent = m_SoundGameObjectPool.transform;
            }
            m_Active = false;
            m_Emitter = null;
        }
    }

    /// <summary>
    /// Stops the AudioSource component immediately.
    /// </summary>
    public void StopAudioSource()
    {
        if (m_AudioSource != null)
        {
            m_AudioSource.Stop();
        }
    }

    /// <summary>
    /// Checks if the AudioSource is currently playing audio.
    /// </summary>
    /// <returns>True if playing, false otherwise</returns>
    public bool IsPlaying()
    {
        if (m_AudioSource == null)
        {
            return false;
        }
        return m_AudioSource.isPlaying;
    }

    /// <summary>
    /// Sets the active state of this SoundGameObject.
    /// </summary>
    /// <param name="activeFlag">True to activate, false to deactivate</param>
    public void SetActive(bool activeFlag)
    {
        m_Active = activeFlag;
    }

    /// <summary>
    /// Updates the SoundGameObject, handling audio parameters, looping, and lifecycle management.
    /// </summary>
    /// <param name="soundEmitter">The controlling sound emitter</param>
    /// <param name="repeatCount">Current repeat count</param>
    /// <param name="count">Reference to active audio source counter</param>
    /// <param name="fatal">Reference to fatal error flag</param>
    /// <returns>True if still active and playing, false otherwise</returns>
    public bool Update(SoundEmitter soundEmitter, int repeatCount, ref int count, ref bool fatal)
    {
        fatal = false;
        if (m_Active == false)
        {
            return false;
        }

        if (m_AudioSource == null)    // GameObject has been destroyed.
        {
            fatal = true;
            return false;
        }

        if (m_AudioSource.isPlaying)  
            // AudioSource is still playing? Handle fade out if stopping and update looping audio clip counter.
        {
            UpdateAudioParameters(soundEmitter);
            CheckForAudioSourceLoop();
            count++;
            return true;
        }

        // Finished playing all AudioSources. We can free the SoundGameObject if it's not reserved and we've no more
        // SoundEmitter repeats to do
        if (soundEmitter.Reserved != SoundEmitter.ReservedInfo.ReservedEmitterAndAudioSources)
        {
            if (repeatCount < 2)
            {
                Kill(soundEmitter); // Not reserved. Allow another emitter to use this SoundGameObject
            }
        }
        return false;   // No longer playing
    }

    /// <summary>
    /// Checks if the audio has looped and updates loop counting accordingly.
    /// </summary>
    /// <returns>True if a loop was detected, false otherwise</returns>
    public bool CheckForAudioSourceLoop()
    {
        bool looped = false;
        if (m_CurrentLoopCount > 1 && m_AudioSource.timeSamples < m_LastTimeSamples)   
            // have we looped? If so, decrease loop counter
        {
            m_CurrentLoopCount--;
            looped = true;
            if (m_CurrentLoopCount == 1)
            {
                m_AudioSource.loop = false;
            }
        }
        m_LastTimeSamples = m_AudioSource.timeSamples;
        return looped;
    }

    /// <summary>
    /// Validates that the SoundGameObject's GameObject still exists and is accessible.
    /// </summary>
    /// <returns>True if valid, false if the GameObject has been destroyed</returns>
    public bool IsValid()
    {
        return (m_AudioSource != null ? true : false); 
            // SoundGameObject could have had its parent changed and its parent has since been destroyed.
    }

    /// <summary>
    /// Triggers the AudioSource to start playing with all configured parameters from the SoundDef.
    /// Sets up audio clips, pitch, volume, spatial settings, and all audio effects.
    /// </summary>
    /// <param name="listenerTransform">Transform of the audio listener</param>
    /// <param name="soundDef">Sound definition containing all audio settings</param>
    /// <param name="soundEmitter">The controlling sound emitter</param>
    /// <param name="spatialBlend">Spatial blend value (0=2D, 1=3D)</param>
    /// <param name="mixerGroups">Array of audio mixer groups for routing</param>
    public void TriggerAudioSource(Transform listenerTransform, 
        SoundDef soundDef, SoundEmitter soundEmitter, float spatialBlend, AudioMixerGroup[] mixerGroups)
    {
        m_SoundDef = soundDef;
        SoundEmitter.SoundDefOverrideData soundDefOverrideData = soundEmitter.SoundDefOverrideInfo;

        m_CalculateDistance = InitDistanceCurves(soundDef);
        if (m_CalculateDistance)
        {
            m_ListenerTransform = listenerTransform;
        }

        // Get clip from list based on index type (random, user, sequencial..)
        uint clipIndex = GetClipIndex(soundDef, soundEmitter);
        m_AudioSource.clip = soundDef.Clips[(int)clipIndex];

        // Map from cent (100 = 1 semitone) space to linear playback multiplier
        float cents = soundDefOverrideData.BasePitchInCents + 
                      (Random.Range(soundDef.PitchAndVolumeInfo.PitchMin, soundDef.PitchAndVolumeInfo.PitchMax));
        m_AudioSource.pitch = Mathf.Pow(2.0f, cents / 1200.0f);

        // Set distance min / max attenuation
        m_AudioSource.minDistance = soundDef.DistanceInfo.VolumeDistMin;
        m_AudioSource.maxDistance = soundDef.DistanceInfo.VolumeDistMax;
        m_AudioSource.rolloffMode = soundDef.DistanceInfo.VolumeRolloffMode;

#if UNITY_EDITOR
        m_AudioSource.gameObject.name = m_DebugName + " (" + soundDef.name + " " + s_Counter + ")";
        s_Counter++;
#endif

        // Set volume using SoundGameObject random value, and soundDef and SoundEmitter volume scale parameters
        float vMin = AmplitudeFromDecibel(soundDef.PitchAndVolumeInfo.VolumeMin);
        float vMax = AmplitudeFromDecibel(soundDef.PitchAndVolumeInfo.VolumeMax);
        m_Volume = Random.Range(vMin, vMax);


        // Set loop on / off (Update code will count down the number of loops required and stop when necessary)
        m_AudioSource.loop = soundDef.RepeatInfo.LoopCount != 1; // True if loopcount!=1
                                                                 // (0 = infinite. 1 = play once. 2 = play twice...)
        m_CurrentLoopCount = soundDef.RepeatInfo.LoopCount;
        m_LastTimeSamples = 0;

        // Set mixergroup output
        if (mixerGroups != null)
        {
            m_AudioSource.outputAudioMixerGroup = mixerGroups[(int)soundDef.MixerGroup];
        }

        // Set start sample offset position
        float startPercentOffset = Random.Range(soundDef.StartStopInfo.StartOffsetPercentMin, soundDef.StartStopInfo.StartOffsetPercentMax) / 100.0f;
        m_AudioSource.timeSamples = (int)((float)m_AudioSource.clip.samples * startPercentOffset);
        m_AudioSource.dopplerLevel = soundDef.DistanceInfo.DopplerScale;

        m_LPFRandomValue = soundDefOverrideData.BaseLowPassCutoff + Random.Range(soundDef.LowPassFilter.CutoffMin, soundDef.LowPassFilter.CutoffMax);
        m_HPFRandomValue = Random.Range(soundDef.HighPassFilter.CutoffMin, soundDef.HighPassFilter.CutoffMax);
        m_SpatialBlendValue = spatialBlend;

        if (m_DistortionFilter != null && m_DistortionFilter.enabled)
        {
            m_DistortionFilter.distortionLevel = Random.Range(soundDef.DistortionFilter.DistortionMin, soundDef.DistortionFilter.DistortionMax);
        }

        // Set stereo pan.
        // This pan is applied before 3D panning calculations are considered.
        // In other words, stereo panning affects the left right balance of the sound before it is spatialised in 3D.
        m_AudioSource.panStereo = Random.Range(soundDef.DistanceInfo.PanMin, soundDef.DistanceInfo.PanMax);

        UpdateAudioParameters(soundEmitter);

        // Start sample with delay if required
        // If we also have a stopDelay time (stop the audioSource after n seconds), use PlayScheduled and SetScheduledEndTime
        float delay = Random.Range(soundDef.StartStopInfo.DelayMin, soundDef.StartStopInfo.DelayMax);
        if (soundDef.StartStopInfo.StopDelay > 0)
        {
            double startTime = AudioSettings.dspTime;
            startTime += delay;
            double endTime = startTime + soundDef.StartStopInfo.StopDelay;
            m_AudioSource.PlayScheduled(startTime);
            m_AudioSource.SetScheduledEndTime(endTime);
        }
        else
        {
            if (delay > 0.0f)
                m_AudioSource.PlayDelayed(delay);
            else
                m_AudioSource.Play();
        }
    }

    /// <summary>
    /// Sets the world position of the SoundGameObject for spatial audio.
    /// </summary>
    /// <param name="position">World position coordinates</param>
    public void SetPosition(Vector3 position)
    {
        m_AudioSource.transform.position = position;
        m_PositionType = PositionType.Position;
        m_Parent = m_SoundGameObjectPool;
    }

    /// <summary>
    /// Attaches the SoundGameObject to a parent transform with a local position offset.
    /// </summary>
    /// <param name="parent">Parent transform to attach to</param>
    /// <param name="localPosition">Local position offset from the parent</param>
    public void SetParent(Transform parent, Vector3 localPosition)
    {
        m_AudioSource.transform.parent = parent;
        m_AudioSource.transform.localPosition = localPosition;
        m_Parent = parent.gameObject;
        m_PositionType = PositionType.ParentTransform;
    }

    private float AmplitudeFromDecibel(float decibel)
    {
        if (decibel <= SOUND_VOL_CUTOFF)
        {
            return 0;
        }
        return Mathf.Pow(2.0f, decibel / 6.0f);
    }

    private Vector3 GetPosition()
    {
        if (m_PositionType == PositionType.Position)
        {
            return m_AudioSource.transform.position;
        }
        else if (m_PositionType == PositionType.ParentTransform)
        {
            return m_AudioSource.transform.parent.transform.position;
        }
        return Vector3.zero;
    }

    private bool InitDistanceCurves(SoundDef soundDef)
    {
        bool calcDistance = false;
        m_LPFCurveNormalizedResult = 1;
        m_HPFCurveNormalizedResult = 1;
        m_SpatialCurveNormalizedResult = 1;

#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
        {
            return false;    // No listener distance calculation required when testing in inspector window
        }
#endif
        calcDistance = EnableAudioFilterComponents(soundDef);
        return calcDistance;
    }

    private void UpdateDistanceCurves()
    {
        if (!m_CalculateDistance || m_ListenerTransform==null)
        {
            return;
        }

        Vector3 position = GetPosition();
        Vector3 listenerPosition = m_ListenerTransform.position;
        float dist = Vector3.Distance(position, listenerPosition);

        SoundDef.Distance distanceInfo = m_SoundDef.DistanceInfo;

        if (m_LowPassFilter != null && m_LowPassFilter.enabled)
        {
            float v = Mathf.Clamp(dist, 0, distanceInfo.LPF_MaxDistance);
            m_LPFCurveNormalizedResult = 1.0f - m_Curve.GetNormalizedCurveValue(distanceInfo.LPFRollOffCurveType, 
                                             v / distanceInfo.LPF_MaxDistance);
        }

        if (m_HighPassFilter != null && m_HighPassFilter.enabled)
        {
            float v = Mathf.Clamp(dist, 0, distanceInfo.HPF_MaxDistance);
            m_HPFCurveNormalizedResult = 1.0f - m_Curve.GetNormalizedCurveValue(distanceInfo.HPFRollOffCurveType, 
                v / distanceInfo.HPF_MaxDistance);
        }

        if (distanceInfo.SpatialBlend_MaxDistance > 0)
        {
            float v = Mathf.Clamp(dist, 0, distanceInfo.SpatialBlend_MaxDistance);
            m_SpatialCurveNormalizedResult = m_Curve.GetNormalizedCurveValue(distanceInfo.SpatialBlendCurveType, 
                v / distanceInfo.SpatialBlend_MaxDistance);
        }
    }

    private void SetDistanceCurveAudio()
    {
        if (m_LowPassFilter != null && m_LowPassFilter.enabled)
        {
            float cutoff = m_LPFRandomValue * m_LPFCurveNormalizedResult;
            if (m_SoundDef.DistanceInfo.LPFRollOffCurveType != Interpolator.CurveType.None && 
                m_SoundDef.DistanceInfo.LPF_MinCutoff > 0 && cutoff < m_SoundDef.DistanceInfo.LPF_MinCutoff)
            {
                cutoff = m_SoundDef.DistanceInfo.LPF_MinCutoff;
            }
            m_LowPassFilter.cutoffFrequency = cutoff;
        }
        if (m_HighPassFilter != null && m_HighPassFilter.enabled)
        {
            float cutoff = m_HPFRandomValue * m_HPFCurveNormalizedResult;
            if (m_SoundDef.DistanceInfo.HPFRollOffCurveType != Interpolator.CurveType.None && 
                m_SoundDef.DistanceInfo.HPF_MinCutoff > 0 && cutoff < m_SoundDef.DistanceInfo.HPF_MinCutoff)
            {
                cutoff = m_SoundDef.DistanceInfo.HPF_MinCutoff;
            }
            m_HighPassFilter.cutoffFrequency = cutoff;
        }
#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
        {
            m_AudioSource.spatialBlend = 0;     
            // Disable SpatialBlend if playing in the inspector window to ensure audio is playing from all speakers
        }
        else
#endif
        {
            m_AudioSource.spatialBlend = m_SpatialBlendValue * m_SpatialCurveNormalizedResult;
        }
    }

    private void UpdateAudioParameters(SoundEmitter soundEmitter)
    {
        if (m_CalculateDistance)
        {
            UpdateDistanceCurves();
        }
        SetDistanceCurveAudio();

        m_AudioSource.volume = m_Volume * soundEmitter.Volume * soundEmitter.SoundDefOverrideInfo.VolumeScale;
    }

    private uint GetClipIndex(SoundDef soundDef, SoundEmitter soundEmitter)
    {
        uint clipIndex = 0;
        if (soundDef.PlaybackType == SoundDef.PlaybackTypes.User)
        {
            clipIndex = soundEmitter.UserClipIndex;
            clipIndex %= (uint)soundDef.Clips.Count;
        }
        else if (soundDef.PlaybackType == SoundDef.PlaybackTypes.RandomNotLast)
        {
            if (soundDef.Clips.Count > 2)
            {
                clipIndex = GetRandomClipIndexButNotLast(soundEmitter, soundDef);  
                    // More than two clips, so choose one but not the last one
            }
            else
            {
                clipIndex = (uint)Random.Range(0, soundDef.Clips.Count);   
                    // If we've only 2 to choose from, just do a normal random lookup.
                    // Otherwise it would just toggle between the two.
            }
        }
        else if (soundDef.PlaybackType == SoundDef.PlaybackTypes.Random)
        {
            clipIndex = (uint)Random.Range(0, soundDef.Clips.Count);
        }
        else if (soundDef.PlaybackType == SoundDef.PlaybackTypes.Sequential)
        {
            soundEmitter.SequentialClipIndex++;
            soundEmitter.SequentialClipIndex %= (uint)soundDef.Clips.Count;    
                // ensure it's in range (if swapping between user <> sequential)
            clipIndex = soundEmitter.SequentialClipIndex;
        }

        return clipIndex;
    }

    private uint GetRandomClipIndexButNotLast(SoundEmitter soundEmitter, SoundDef soundDef)
    {
        uint randomOffset = (uint)Random.Range(1, soundDef.Clips.Count - 1);  
            // Select next random footstep AudioClip. But don't use the same one twice in a row
        soundEmitter.RandomClipIndex += randomOffset;
        soundEmitter.RandomClipIndex %= (uint)soundDef.Clips.Count;
        return soundEmitter.RandomClipIndex;
    }

}
