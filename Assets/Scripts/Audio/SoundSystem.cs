using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Initializes the null sound system with the specified parameters.
/// This implementation performs no operations.
/// </summary>
public class SoundSystemNull : ISoundSystem
{
    public void Init(Transform listenerTransform, int maxSoundEmitters, SoundGameObjectPool soundGameObjectPool, AudioMixer mixer) { }
    public SoundSystem.SoundInfo CreateEmitter(SoundDef soundDef, Transform transform, float volume = 1)
    {
        return default(SoundSystem.SoundInfo);
    }
    public SoundSystem.SoundInfo CreateEmitter(SoundDef soundDef, Vector3 position, float volume = 1)
    {
        return default(SoundSystem.SoundInfo);
    }
    public SoundSystem.SoundInfo CreateEmitter(SoundDef soundDef, Transform transform, Vector3 localPosition, float volume = 1)
    {
        return default(SoundSystem.SoundInfo);
    }
    public bool PlayEmitter(SoundSystem.SoundInfo soundInfo, float volume = 1)
    {
        return true;
    }
    public void UpdateSoundSystem(bool muteSound = false) { }
    public bool Stop(SoundSystem.SoundInfo soundInfo, float fadeOutTime = 0.0f)
    {
        return true;
    }
    public void KillAll() { }
    public bool Kill(SoundSystem.SoundInfo soundInfo)
    {
        return true;
    }
    public bool SetListenerTransform(Transform listenerTransform)
    {
        return true;
    }
}

/// <summary>
/// Main sound system implementation that manages audio playback via SoundDef ScriptableObjects through sound emitters and sound game objects.
/// Handles pooling of audio sources, spatial audio, and advanced audio effects.
/// A SoundDef requires a SoundEmitter
/// A SoundEmitter controls a number of SoundGameObjects
/// A SoundGameObject processes the SoundDef information. It also contains the AudioSource where the desired AudioClips are finally played
/// </summary>
public class SoundSystem : ISoundSystem
{
    public class SoundInfo
    {
        public SoundDef SoundDefinition;
        public Transform SoundTransform;
        public SoundEmitter.SoundDefOverrideData SoundDefOverrideInfo;  // Information is optionally copied from the SoundDef, allowing the user to tweak the values prior to playing
        public SoundEmitter.ReservedInfo Reserved;
        public Vector3 Position;
        public SoundHandle Handle;
        public SoundInfo()
        {
            SoundDefinition = null;
            SoundTransform = null;
            SoundDefOverrideInfo = null;
            Reserved = SoundEmitter.ReservedInfo.FreeAfterPlaybackCompletes;
            Position = Vector3.zero;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        s_SoundMute = false;
    }
    
    public static bool s_SoundMute;
    public SoundGameObjectPool SoundGameObjects;

    public List<SoundEmitter> m_SoundEmitterPool;          // Contains the sound emitters that can be (or are) allocated
    public List<SoundEmitter> m_SoundEmitterActiveList;   // Contains the sound emitters that are actually active (playing or reserved)

    private int m_SequenceId;
    private Interpolator m_MasterVolume = new Interpolator(1.0f, Interpolator.CurveType.SmoothStep);
    private SoundMixer m_SoundMixer;
    private Transform m_ListenerTransform;


    /// <summary>
    /// Initializes the sound system with a pool of emitters and sound game objects.
    /// Creates the specified number of sound emitters and sets up the audio mixer.
    /// </summary>
    /// <param name="listenerTransform">Transform representing the audio listener</param>
    /// <param name="maxSoundEmitters">Maximum number of sound emitters to create</param>
    /// <param name="soundGameObjectPool">Pool of sound game objects containing audio sources</param>
    /// <param name="mixer">Audio mixer for managing audio groups</param>
    public void Init(Transform listenerTransform, int maxSoundEmitters, SoundGameObjectPool soundGameObjectPool, AudioMixer mixer)
    {
        SoundGameObjects = soundGameObjectPool; // 

        m_ListenerTransform = listenerTransform;
        m_SoundMixer = new SoundMixer(mixer);   // Initialise mixer groups
        m_SequenceId = 0;

        m_SoundEmitterPool = new List<SoundEmitter>();
        for (var i = 0; i < maxSoundEmitters; i++)
        {
            var emitter = CreateSoundEmitterForPool(SoundGameObjects);
            m_SoundEmitterPool.Add(emitter);
        }
        m_SoundEmitterActiveList = new List<SoundEmitter>();   // This holds the list of currently active emitters
    }


    /// <summary>
    /// Sets the listener transform for spatial audio calculations.
    /// In most cases, this would be the Transform of the GameObject that contains the AudioListener component
    /// </summary>
    /// <param name="listenerTransform"> Transform of the listener</param>
    /// <returns>true = Listener Transform set. False = Listener Transform not set</returns>
    public bool SetListenerTransform(Transform listenerTransform)
    {
        if (listenerTransform == null)
        {
            return false;
        }
        m_ListenerTransform = listenerTransform;
        return true;
    }


    /// <summary>
    /// Stops the sound emitter associated with the given sound info, optionally fading out over time.
    /// If fadeOutTime is 0, the emitter may be killed and freed for reuse unless it's reserved.
    /// </summary>
    /// <param name="soundInfo">Sound information containing the emitter to stop</param>
    /// <param name="fadeOutTime">Time in seconds to fade out (default 0 for immediate stop)</param>
    /// <returns>True if the sound was stopped successfully, false if invalid</returns>se = Stop failed</returns>
    public bool Stop(SoundInfo soundInfo, float fadeOutTime = 0.0f)
    {
        if (soundInfo == null || soundInfo.Handle == null || !soundInfo.Handle.IsValid())
        {
            return false;
        }
        SoundHandle soundHandle = soundInfo.Handle;
        if (fadeOutTime == 0)
        {
            soundHandle.Emitter.Kill();
        }
        else
        {
            soundHandle.Emitter.FadeOut(fadeOutTime);
        }
        return true;
    }

    /// <summary>
    /// Immediately stops and kills all active sound emitters and their sound game objects.
    /// All emitters are returned to the allocation pool for reuse.
    /// </summary>
    public void KillAll()
    {
        foreach (var soundEmitter in m_SoundEmitterActiveList)
        {
            soundEmitter.UnreserveAndKill();    // Kill all emitters and SoundGameObjects
        }
        m_SoundEmitterActiveList.Clear();
    }

    /// <summary>
    /// Immediately kills the specified sound emitter and removes it from the active list.
    /// </summary>
    /// <param name="soundInfo">Sound information containing the emitter to kill</param>
    /// <returns>True if the sound was killed successfully, false if invalid</returns>
    public bool Kill(SoundInfo soundInfo)
    {
        if (soundInfo == null || soundInfo.Handle == null || !soundInfo.Handle.IsValid())
        {
            return false;
        }

        SoundHandle soundHandle = soundInfo.Handle;
        soundHandle.Emitter.UnreserveAndKill();
        for (int i = 0; i < m_SoundEmitterActiveList.Count; i++)
        {
            if (m_SoundEmitterActiveList[i] == soundHandle.Emitter)
            {
                m_SoundEmitterActiveList.RemoveAt(i);
                break;
            }
        }
        return true;
    }

    /// <summary>
    /// Updates all active sound emitters and handles master volume and muting.
    /// Should be called every frame to maintain proper audio playback.
    /// </summary>
    /// <param name="muteSound">Whether to mute all sounds</param>
    public void UpdateSoundSystem(bool muteSound = false)
    {
        if (m_SoundEmitterActiveList.Count == 0)
        {
            return; // Nothing playing
        }

        float masterVolume = (muteSound == true ? 0 : m_MasterVolume.GetValue());
        m_SoundMixer.Update(masterVolume);
        UpdateActiveSoundEmitters();
    }

    /// <summary>
    /// Creates and immediately plays a sound emitter with the specified parameters.
    /// This is a "fire and forget" method suitable for one-off sound effects.
    /// </summary>
    /// <param name="soundDef">Sound definition containing audio clips and settings</param>
    /// <param name="transform">Transform to attach the emitter to</param>
    /// <param name="localPosition">Local position offset from the transform</param>
    /// <param name="volume">Volume multiplier (default 1)</param>
    /// <returns>SoundInfo that can be used to control the sound</returns>  
    public SoundInfo CreateEmitter(SoundDef soundDef, Transform transform, Vector3 localPosition, float volume = 1)
    {
        SoundInfo soundInfo = new SoundInfo()
        {
            SoundDefinition = soundDef,
            SoundTransform = transform,
            Position = localPosition,
        };
        return AllocateAndPlayEmitter(soundInfo, volume);
    }

    /// <summary>
    /// Creates and immediately plays a sound emitter attached to the specified transform.
    /// This is a "fire and forget" method suitable for one-off sound effects.
    /// </summary>
    /// <param name="soundDef">Sound definition containing audio clips and settings</param>
    /// <param name="transform">Transform to attach the emitter to</param>
    /// <param name="volume">Volume multiplier (default 1)</param>
    /// <returns>SoundInfo that can be used to control the sound</returns>
    public SoundInfo CreateEmitter(SoundDef soundDef, Transform transform, float volume = 1)
    {
        return CreateEmitter(soundDef, transform, Vector3.zero, volume);
    }

    /// <summary>
    /// Creates and immediately plays a sound emitter at the specified world position.
    /// This is a "fire and forget" method suitable for one-off sound effects.
    /// </summary>
    /// <param name="soundDef">Sound definition containing audio clips and settings</param>
    /// <param name="position">World position for the sound</param>
    /// <param name="volume">Volume multiplier (default 1)</param>
    /// <returns>SoundInfo that can be used to control the sound</returns>
    public SoundInfo CreateEmitter(SoundDef soundDef, Vector3 position, float volume = 1)
    {
        SoundInfo soundInfo = new SoundInfo()
        {
            SoundDefinition = soundDef,
            Position = position
        };
        return AllocateAndPlayEmitter(soundInfo, volume);
    }

    /// <summary>
    /// Plays a sound emitter using the information provided in the SoundInfo.
    /// If the SoundInfo already has a valid handle, it will reuse the same emitter.
    /// This allows for tracking of clip selection modes like Random_But_Not_Last and Sequential.
    /// </summary>
    /// <param name="soundInfo">Sound information containing playback parameters</param>
    /// <param name="volume">Volume multiplier (default 1)</param>
    /// <returns>True if the emitter was played successfully, false otherwise</returns>
    public bool PlayEmitter(SoundInfo soundInfo, float volume = 1)
    {
        if (AllocateAndPlayEmitter(soundInfo, volume) == null)
        {
            return false;
        }
        return true;
    }
    private SoundEmitter CreateSoundEmitterForPool(SoundGameObjectPool soundGameObjectPool)
    {
        var emitter = new SoundEmitter(soundGameObjectPool);
        emitter.FadeOutTime = new Interpolator(1.0f, Interpolator.CurveType.Linear);
        return emitter;
    }

    private SoundEmitter GetSoundEmitterFromPool(SoundGameObjectPool soundGameObjectPool)
    {
        foreach (var emitter in m_SoundEmitterPool)
        {
            if (!emitter.Allocated)
            {
                emitter.SeqId = m_SequenceId++;
                emitter.Init(soundGameObjectPool);
                emitter.Allocated = true;
                return emitter;
            }
        }
        SoundEmitter soundEmitter = CreateSoundEmitterForPool(soundGameObjectPool);     // No available SoundEmitters. Create another and add it to the pool
        soundEmitter.Allocated = true;
        m_SoundEmitterPool.Add(soundEmitter);
        return soundEmitter;
    }

    private SoundInfo AllocateAndPlayEmitter(SoundInfo soundInfo, float volume)
    {
        if (soundInfo == null || soundInfo.SoundDefinition == null)
        {
            return null;
        }

        if (soundInfo.Handle == null || soundInfo.Handle.Emitter.Allocated == false)
        {
            soundInfo.Handle = AllocateSoundEmitter(soundInfo);    // Allocates a SoundEmitter and set the soundDef and Reserve information
        }

        SoundHandle soundHandle = soundInfo.Handle;
        if (!soundHandle.IsValid())
        {
            Debug.Log("Invalid Handle");
            return null;
        }

        SoundEmitter soundEmitter = soundHandle.Emitter;

        if (soundInfo.SoundDefOverrideInfo != null)
        {
            soundEmitter.SoundDefOverrideInfo = soundInfo.SoundDefOverrideInfo;
        }
        else
        {
            soundEmitter.CopyOverrideDataFromSoundDef(soundInfo.SoundDefinition);
        }

        if (Play(soundEmitter, soundInfo.Position, soundInfo.SoundTransform) == true)
        {
            soundEmitter.SetVolume(volume);
        }
        return soundInfo;
    }

    private SoundHandle AllocateSoundEmitter(SoundInfo soundInfo)
    {
        SoundEmitter newEmitter = GetSoundEmitterFromPool(SoundGameObjects); // Get an unused emitter
        newEmitter.Reserved = soundInfo.Reserved;   // Reserved info is only stored on allocation, to ensure that reserved Emitters always use the same info
        newEmitter.SetSoundDef(soundInfo.SoundDefinition);   // SoundDef info is only stored on allocation to ensure that reserved Emitters always use the same info
        return new SoundHandle(newEmitter);
    }

    private bool Play(SoundEmitter soundEmitter, Vector3 position, Transform parent = null)
    {
        if (soundEmitter == null || soundEmitter.Allocated == false || soundEmitter.Active == true && soundEmitter.Reserved == SoundEmitter.ReservedInfo.FreeAfterPlaybackCompletes)
        {
            return false;
        }

        soundEmitter.Activate();
        if (soundEmitter.PrepareSoundGameObjects(parent, position) == false)  // Allocate SoundGameObjects if not reserved and set their position or transform
        {
            return false;   // Failed to obtain SoundGameObjects.
        }

        m_SoundEmitterActiveList.Add(soundEmitter);
        soundEmitter.Play(m_ListenerTransform, m_SoundMixer.MixerGroups);
        return true;
    }

    private void UpdateActiveSoundEmitters()
    {
        for (int i = 0; i < m_SoundEmitterActiveList.Count; i++)
        {
            if (m_SoundEmitterActiveList[i].Update(m_SoundMixer.MixerGroups) == false)
            {
                m_SoundEmitterActiveList.RemoveAt(i);  // Emitter is no longer active. Remove it from active list.
                i--;
            }
        }
    }
}

