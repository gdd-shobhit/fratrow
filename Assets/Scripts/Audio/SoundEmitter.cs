using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

[Serializable]
public class SoundEmitter
{
    public enum ReservedInfo
    {
        FreeAfterPlaybackCompletes,
        ReservedEmitter,
        ReservedEmitterAndAudioSources
    };

    /// <summary>
    /// Contains override data that can modify SoundDef parameters at runtime.
    /// Allows per-instance customization of pitch, volume, and filter settings.
    /// </summary>
    public class SoundDefOverrideData
    {
        public float BasePitchInCents;  // Copied from SoundDef to allow user to override if required
        public float VolumeScale;       // Copied from SoundDef
        public float BaseLowPassCutoff; // Copied from SoundDef

        public SoundDefOverrideData()
        {
            BaseLowPassCutoff = 0;
            BasePitchInCents = 0;
            VolumeScale = 1;
        }

        public SoundDefOverrideData(SoundDef soundDef)
        {
            BasePitchInCents = soundDef.BasePitchInCents;
            VolumeScale = soundDef.VolumeScale;
            BaseLowPassCutoff = soundDef.BaseLowPassCutoff;
        }
    }

    public bool Allocated;          // True = Emitter has been allocated
    public ReservedInfo Reserved;   // Reserved type for this emitter. Reserve if you want to use RandomNotLast or Sequential clip selection.
    public List<SoundGameObject> ActiveSoundGameObjects;  // List of SoundGameObjects that are active for this SoundDef (using the SoundDef.PlayCount)
    public uint RandomClipIndex;    // Last Random clip index. Required to allow for "Play random, but don't play last"..
    public uint UserClipIndex;      // if SoundDef clip index = User, the user can set this value to use as the clip index
    public uint SequentialClipIndex; // This index is used if the SoundDef clip index = Sequential.
    public float Volume;            // Base emitter volume (final volume = Volume * SoundDefOverrideInfo.VolumeScale * RandomVol(min/max) * KeyOffFadeOutVol)
    public SoundDefOverrideData SoundDefOverrideInfo;
    public Interpolator FadeOutTime;  // Time (in seconds) to fade out the SoundGameObject when it is requested to stop
    public int SeqId;               // ID of this SoundEmitter. Used to validate user calls to play/stop etc.
    public bool Active;            // True = Active SoundGameObjects or SoundEmitter is reserved

    private SoundGameObjectPool m_SoundGameObjectPool;  // Pool of SoundGameObjects that can be allocated for this emitter
    private SoundDef m_SoundDef;    // SoundDef that is being processed for this Emitter
    private int m_RepeatCount;      // Current repeat count (1 > SoundDef RandomRepeat(min/max)) 0 will play once (as will 1)
    private Transform m_ListenerTransform;

    /// <summary>
    /// Copies override data from a SoundDef to allow runtime modifications.
    /// </summary>
    /// <param name="soundDef">The SoundDef to copy settings from</param>
    public void CopyOverrideDataFromSoundDef(SoundDef soundDef)
    {
        SoundDefOverrideInfo.BasePitchInCents = soundDef.BasePitchInCents;
        SoundDefOverrideInfo.VolumeScale = soundDef.VolumeScale;
        SoundDefOverrideInfo.BaseLowPassCutoff = soundDef.BaseLowPassCutoff;
    }

    /// <summary>
    /// Initializes a new SoundEmitter with the specified sound game object pool.
    /// </summary>
    /// <param name="soundGameObjectPool">Pool of sound game objects to use for audio playback</param>
    public SoundEmitter(SoundGameObjectPool soundGameObjectPool)
    {
        Init(soundGameObjectPool);
    }

    /// <summary>
    /// Initializes the sound emitter with default values and the specified pool.
    /// </summary>
    /// <param name="soundGameObjectPool">Pool of sound game objects to use</param>
    public void Init(SoundGameObjectPool soundGameObjectPool)
    {
        Allocated = false;
        Active = false;

        ActiveSoundGameObjects = new List<SoundGameObject>();
        Volume = 1.0f;
        SequentialClipIndex = 0;
        UserClipIndex = 0;
        m_RepeatCount = 0;
        Reserved = ReservedInfo.FreeAfterPlaybackCompletes;
        m_SoundGameObjectPool = soundGameObjectPool;
        SoundDefOverrideInfo = new SoundDefOverrideData();
    }

    /// <summary>
    /// Unreserves the emitter and kills it, making it available for reallocation.
    /// </summary>
    public void UnreserveAndKill()
    {
        Reserved = ReservedInfo.FreeAfterPlaybackCompletes;
        Kill(); // Stop all SoundGameObjects and kills emitter
    }

    /// <summary>
    /// Kills the sound emitter, stopping playback and potentially freeing it for reuse.
    /// </summary>
    public void Kill()
    {
        Stop(); // Stop emitter and potentially stop SoundGameObjects if not reserved
        if (Reserved == ReservedInfo.FreeAfterPlaybackCompletes)   // Free emitter unless reserved
        {
            Allocated = false;
        }
    }

    /// <summary>
    /// Stops the sound emitter, handling SoundGameObjects based on reservation status.
    /// </summary>
    public void Stop()
    {
        if (Reserved != ReservedInfo.ReservedEmitterAndAudioSources)
        {
            KillSoundGameObjects(); // Kill SoundGameObjects, allowing other emitters to use them
        }
        else
        {
            foreach (SoundGameObject soundGameObject in ActiveSoundGameObjects)   // SoundGameObjects are reserved for this Emitter, so just stop the AudioSource but keep
            {
                soundGameObject.StopAudioSource();
            }
        }
        Active = false;
    }

    /// <summary>
    /// Sets the volume level for this emitter.
    /// </summary>
    /// <param name="volume">Volume multiplier (typically 0-1)</param>
    public void SetVolume(float volume)
    {
        Volume = volume;
    }

    /// <summary>
    /// Plays the sound emitter using the current SoundDef and active SoundGameObjects.
    /// </summary>
    /// <param name="listenerTransform">Transform of the audio listener for spatial calculations</param>
    /// <param name="mixerGroups">Array of audio mixer groups for output routing</param>
    public void Play(Transform listenerTransform, AudioMixerGroup[] mixerGroups)
    {
        if (m_SoundDef == null || m_SoundDef.Clips == null || m_SoundDef.Clips.Count == 0)
        {
            return;
        }

        m_ListenerTransform = listenerTransform;


        float spatialBlend;
#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
        {
            spatialBlend = 0;    // Disable SpatialBlend if playing in the inspector window to ensure audio is playing from all speakers
        }
        else
#endif
        {
            spatialBlend = m_SoundDef.DistanceInfo.SpatialBlend;  // Playing in editor, so use the SoundDef.SpatialBlend
        }

        foreach (SoundGameObject soundGameObject in ActiveSoundGameObjects)
        {
            soundGameObject.TriggerAudioSource(m_ListenerTransform, m_SoundDef, this, spatialBlend, mixerGroups);
        }
    }

    /// <summary>
    /// Updates the sound emitter, handling fade-out, repeats, and SoundGameObject lifecycle.
    /// </summary>
    /// <param name="mixerGroups">Array of audio mixer groups</param>
    /// <returns>True if the emitter is still active, false if it should be removed</returns>
    public bool Update(AudioMixerGroup[] mixerGroups)
    {
        if (!Active)
        {
            return false;
        }

        if (FadeOutTime.GetValue() == 0.0f)
        {
            Kill();                // Emitter fadeout is complete. So stop and optionally kill the emitter (unless reserved) and optionally stop all audio sources
            return false;
        }

        int activeAudioSourceCounter = 0;
        foreach (SoundGameObject soundGameObject in ActiveSoundGameObjects)
        {
            bool fatal = false;
            soundGameObject.Update(this, m_RepeatCount, ref activeAudioSourceCounter, ref fatal);
            if (fatal == true)    // AudioSource's GameObject has been destroyed (likely attached to a parent GameObject that has been destroyed). Stop all sounds and recover
            {
                ValidateSoundGameObjects();
                UnreserveAndKill(); // Unreserve and kill this SoundEmitter
                return false;
            }
        }

        if (activeAudioSourceCounter > 0)   // An AudioSource is still playing?
        {
            return true;
        }

        if (m_RepeatCount > 1)      // All audio sources have finished playing for this emitter. Handle Repeat
        {
            m_RepeatCount--;
            Play(m_ListenerTransform, mixerGroups); // Play the emitter again
            return true;
        }

        Kill();  // SoundEmitter has finished. Kill SoundEmitter for reuse if not reserved.
        return false;
    }

    /// <summary>
    /// Initiates a fade-out effect over the specified duration.
    /// </summary>
    /// <param name="fadeOutTime">Time in seconds to fade out (0 for immediate)</param>
    public void FadeOut(float fadeOutTime)
    {
        if (fadeOutTime == 0.0f)
        {
            FadeOutTime.SetValue(0.0f);
        }
        else
        {
            FadeOutTime.SetValue(1.0f);
            FadeOutTime.MoveTo(0.0f, fadeOutTime);
        }
    }

    /// <summary>
    /// Sets the SoundDef that this emitter will use for playback.
    /// </summary>
    /// <param name="soundDef">The SoundDef containing audio clips and settings</param>
    /// <returns>True if the SoundDef was set successfully, false if null</returns>
    public bool SetSoundDef(SoundDef soundDef)
    {
        if (soundDef == null)
        {
            return false;
        }
        m_SoundDef = soundDef;
        return true;
    }

    /// <summary>
    /// Activates the emitter and sets up repeat count based on the SoundDef settings.
    /// </summary>
    public void Activate()
    {
        m_RepeatCount = Random.Range(m_SoundDef.RepeatInfo.RepeatMin, m_SoundDef.RepeatInfo.RepeatMax);
        Active = true;
    }

    /// <summary>
    /// Gets the current repeat count for this emitter.
    /// </summary>
    /// <returns>Number of repeats remaining</returns>
    public int GetRepeatCount()
    {
        return m_RepeatCount;
    }

    /// <summary>
    /// Prepares and allocates SoundGameObjects for playback at the specified position.
    /// Handles both transform-based and world position-based placement.
    /// </summary>
    /// <param name="parent">Parent transform to attach to (can be null)</param>
    /// <param name="position">Position or local offset depending on parent</param>
    /// <returns>True if SoundGameObjects were prepared successfully, false otherwise</returns>
    public bool PrepareSoundGameObjects(Transform parent, Vector3 position)
    {
        if (ActiveSoundGameObjects.Count == 0 || Reserved != ReservedInfo.ReservedEmitterAndAudioSources)   // No SoundGameObjects yet allocated.
        {
            KillSoundGameObjects();
            if (AllocateSoundGameObjects(m_SoundGameObjectPool, m_SoundDef.PlayCount) == false)    // create n SoundGameObjects (AudioSources)
            {
                return false;   // Failed to create n SoundGameObjects
            }
        }

        if (ValidateSoundGameObjects() == false)    // Fatal. SoundGameObject has been destroyed by a parent. Clean up and unreserve Emitter
        {
            KillSoundGameObjects();
            UnreserveAndKill();
            return false;
        }

        // ActiveSoundGameObjects may still have reserved SoundGameObjects, if Reserved = ReservedInfo.ReserveEmitterAndAudioSources            
        foreach (var soundGameObject in ActiveSoundGameObjects)
        {
            soundGameObject.Enable(this);
            if (parent != null)
            {
                soundGameObject.SetParent(parent, position); // Position is used as Transform.LocalPosition. So, an offset from the parent.
            }
            else
            {
                soundGameObject.SetPosition(position);
            }
        }
        return true;
    }

    /// <summary>
    /// Check if the SoundGameObjects still exist
    /// If they have been attached to another GameObject, and their Parent GameObject has been destroyed, we need to create another SoundGameObject
    ///</summary>
    private bool ValidateSoundGameObjects()
    {
        bool isValid = true;
        for (int i = 0; i < ActiveSoundGameObjects.Count; i++)
        {
            SoundGameObject soundGameObject = ActiveSoundGameObjects[i];
            if (!soundGameObject.IsValid())
            {
                SoundGameObject newSoundGameObject = m_SoundGameObjectPool.Create();  // Create another SoundGameObject so that there's the same number in the pool
                m_SoundGameObjectPool.Replace(soundGameObject, newSoundGameObject);     // Replace the old with the new in the pool
                ActiveSoundGameObjects[i] = newSoundGameObject;
                isValid = false;
            }
        }
        return isValid;
    }

    private void KillSoundGameObjects()
    {
        if (Reserved != ReservedInfo.ReservedEmitterAndAudioSources)
        {
            foreach (SoundGameObject soundGameObject in ActiveSoundGameObjects)
            {
                soundGameObject.Kill(this); // Killing a SoundGameObject moves it back under the SoundGameObjectPool parent GameObject
                UpdateSoundObjectPoolLinkedList(soundGameObject);   // Link soundGameObject back into the SoundObjectPool linked list (it becomes the next to be selected)
            }
            ActiveSoundGameObjects.Clear();
        }
    }

    private void UpdateSoundObjectPoolLinkedList(SoundGameObject newSoundGameObject)
    {
        SoundGameObject nextSoundGameObject = m_SoundGameObjectPool.NextSoundGameObject;
        m_SoundGameObjectPool.NextSoundGameObject = newSoundGameObject;     // Make this the next available SoundGameObject in the pool
        newSoundGameObject.NextAvailable = nextSoundGameObject;             // Link the previously next SoundGameObject to follow be selected after newSoundGameObject 
        m_SoundGameObjectPool.AvailableSoundObjectCount++;
    }

    private bool AllocateSoundGameObjects(SoundGameObjectPool soundGameObjectPool, int count)
    {
        if (soundGameObjectPool.AvailableSoundObjectCount < count)    // Are there enough free SoundGameObjects available to use for this emitter?
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            SoundGameObject soundGameObject = soundGameObjectPool.NextSoundGameObject;
            soundGameObjectPool.NextSoundGameObject = soundGameObject.NextAvailable;

            soundGameObject.SetActive(true);
            soundGameObjectPool.AvailableSoundObjectCount--;
            ActiveSoundGameObjects.Add(soundGameObject);
        }
        return true;
    }
}


