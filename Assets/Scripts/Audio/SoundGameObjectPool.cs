using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a pool of reusable SoundGameObjects that contain AudioSource components.
/// Provides efficient allocation and deallocation of audio sources for the sound system.
/// </summary> 

public class SoundGameObjectPool
{
    public List<SoundGameObject> SoundGameObjectList;
    public SoundGameObject NextSoundGameObject;
    public int AvailableSoundObjectCount;
    private GameObject m_SourceHolder;

#if UNITY_EDITOR
    private int m_SoundObjectCounter;
#endif

    /// <summary>
    /// Initializes a new sound game object pool with the specified capacity.
    /// Creates all sound game objects upfront and organizes them in a linked list for efficient allocation.
    /// </summary>
    /// <param name="parentGameObjectName">Name for the parent GameObject that will hold all sound objects</param>
    /// <param name="maxSoundGameObjects">Maximum number of sound game objects to create</param>
    public SoundGameObjectPool(string parentGameObjectName, int maxSoundGameObjects)
    {
        if (maxSoundGameObjects <= 0)
        {
            return;
        }
        SoundGameObjectList = new List<SoundGameObject>();

        m_SourceHolder = new GameObject(parentGameObjectName);  // All SoundGameObjects are instantiated under this parent.
        GameObject.DontDestroyOnLoad(m_SourceHolder);

        AvailableSoundObjectCount = maxSoundGameObjects;
        SoundGameObject lastAllocated = null;

        for (int i = 0; i < maxSoundGameObjects; i++)
        {
            SoundGameObject soundGameObject = Create();
            if (lastAllocated != null)
            {
                lastAllocated.NextAvailable = soundGameObject;
            }
            lastAllocated = soundGameObject;
            SoundGameObjectList.Add(soundGameObject);
        }
        SoundGameObjectList[maxSoundGameObjects - 1].NextAvailable = SoundGameObjectList[0];
        NextSoundGameObject = SoundGameObjectList[0];
    }

    /// <summary>
    /// Creates a new SoundGameObject with all required audio components.
    /// Adds AudioSource, AudioDistortionFilter, AudioLowPassFilter, and AudioHighPassFilter components.
    /// </summary>
    /// <returns>A new SoundGameObject ready for use</returns>
    public SoundGameObject Create()
    {
        GameObject go;
#if  UNITY_EDITOR
        go = new GameObject("SGO" + " " + (m_SoundObjectCounter++)); 
            // Add name and counter value to the GameObject name to make it easier to find in the Hierarchy window
#else
        go = new GameObject("SGO");
#endif
        go.transform.parent = m_SourceHolder.transform;

        SoundGameObject.AudioComponentData audioComponentData = new SoundGameObject.AudioComponentData()
        {
            ASource = go.AddComponent<AudioSource>(),
            DistortionFilter = go.AddComponent<AudioDistortionFilter>(),
            LowPassFilter = go.AddComponent<AudioLowPassFilter>(),
            HighPassFilter = go.AddComponent<AudioHighPassFilter>()
        };

        SoundGameObject audioSourceObject = new SoundGameObject(m_SourceHolder, audioComponentData);
        return audioSourceObject;
    }


    /// <summary>
    /// Replaces a broken SoundGameObject with a new one and maintains the linked list integrity.
    /// This is called when a SoundGameObject's parent has been destroyed and needs to be replaced.
    /// </summary>
    /// <param name="oldSGO">The broken SoundGameObject to replace</param>
    /// <param name="newSGO">The new SoundGameObject to use as replacement</param>
    /// <returns>True if replacement was successful, false if the old object wasn't found</returns>
    public bool Replace(SoundGameObject oldSGO, SoundGameObject newSGO)
    {
        for (int i = 0; i < SoundGameObjectList.Count; i++)
        {
            if (SoundGameObjectList[i] == oldSGO)
            {
                SoundGameObjectList[i] = newSGO;  // Replace SoundGameObject with new
                                                  // (required if parent destroys the SoundGameObject)

                for (int j = 0; j < SoundGameObjectList.Count; j++)
                {
                    if (SoundGameObjectList[j].NextAvailable != null && SoundGameObjectList[j].NextAvailable == oldSGO)
                    {
                        SoundGameObjectList[j].NextAvailable = newSGO;  
                            // This would only be found if the SoundGameObject had been destroyed whilst in its
                            // SoundGameObjectPool. Unlikely
                    }
                }
                return true;
            }
        }
        return false;   // Failed to patch up linked list
    }
}
