using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Manages audio mixer groups and volume levels for different sound categories.
/// Converts amplitude values to decibel values for proper audio mixer integration.
/// </summary>
public class SoundMixer
{   
    [ConfigVar(Name = "sound.mute", DefaultValue = "-1", Description = "Is audio enabled. -1 causes default behavior (on when window has focus)", Flags = ConfigVar.Flags.None)]
    public static ConfigVar soundMute;

    // Debugging only
    [ConfigVar(Name = "sound.mastervol", DefaultValue = "1", Description = "Master volume", Flags = ConfigVar.Flags.None)]
    public static ConfigVar soundMasterVol;

    // Exposed in options menu
    [ConfigVar(Name = "sound.menuvol", DefaultValue = "1", Description = "Menu volume", Flags = ConfigVar.Flags.None)]
    public static ConfigVar soundMenuVol;
    [ConfigVar(Name = "sound.sfxvol", DefaultValue = "1", Description = "SFX volume", Flags = ConfigVar.Flags.Save)]
    public static ConfigVar soundSFXVol;
    [ConfigVar(Name = "sound.musicvol", DefaultValue = "1", Description = "Music volume", Flags = ConfigVar.Flags.Save)]
    public static ConfigVar soundMusicVol;

    /// <summary>
    /// Defines the different audio mixer groups available for sound categorization
    /// </summary> 
    public enum SoundMixerGroup
    {
        Music,
        SFX,
        Footsteps,
        Menu
    }

    public AudioMixerGroup[] MixerGroups;

    private const float m_SoundVolumeCutoff = -60f;
    private float m_SoundAmplitudeCutoff;
    private AudioMixer m_AudioMixer;

    /// <summary>
    /// Initializes a new SoundMixer with the specified audio mixer.
    /// </summary>
    public SoundMixer(AudioMixer audioMixer)
    {
        Init(audioMixer);
    }

    /// <summary>
    /// Initializes the sound mixer by setting up mixer groups and volume cutoff values.
    /// Maps SoundMixerGroup enum values to actual AudioMixerGroup references.
    /// </summary>
    /// <param name="audioMixer">The Unity AudioMixer to initialize with</param>
    public void Init(AudioMixer audioMixer)
    {
        m_SoundAmplitudeCutoff = Mathf.Pow(2.0f, m_SoundVolumeCutoff / 6.0f);
        m_AudioMixer = audioMixer;

        // Set up mixer groups
        string[] subBussNames = SoundMixerGroup.GetNames(typeof(SoundMixerGroup));
        MixerGroups = new AudioMixerGroup[subBussNames.Length];
        for (int i=0;i<subBussNames.Length;i++)
        {
            if (TryFindMatchingMixerGroup(audioMixer, subBussNames[i], out MixerGroups[i]))
            {
                Debug.Log($"Found MixerGroup: {subBussNames[i]}");
            }
            else
            {
                Debug.Log($"Missing MixerGroup: {subBussNames[i]} Setting to Master output");
            }
        }
    }

    /// <summary>
    /// Updates the audio mixer with current volume levels converted to decibel values.
    /// Should be called every frame to maintain proper volume control.
    /// </summary>
    /// <param name="masterVolume">Master volume multiplier (0-1)</param>
    public void Update(float masterVolume)
    {
        m_AudioMixer.SetFloat("MasterVolume", DecibelFromAmplitude(Mathf.Clamp(soundMasterVol.FloatValue, 0.0f, 1.0f) * masterVolume));
        m_AudioMixer.SetFloat("MusicVolume", DecibelFromAmplitude(Mathf.Clamp(soundMusicVol.FloatValue, 0.0f, 1.0f)));
        m_AudioMixer.SetFloat("SFXVolume", DecibelFromAmplitude(Mathf.Clamp(soundSFXVol.FloatValue, 0.0f, 1.0f)));
        m_AudioMixer.SetFloat("MenuVolume", DecibelFromAmplitude(Mathf.Clamp(soundMenuVol.FloatValue, 0.0f, 1.0f)));
    }

    private bool TryFindMatchingMixerGroup(AudioMixer audioMixer, string groupName, out  AudioMixerGroup firstGroup)
    {
        AudioMixerGroup[] groups =audioMixer.FindMatchingGroups(groupName);
        if (groups.Length==0)
        {
            firstGroup = null;  // Mixer definition is not found within the AudioMixerGroup. Set to default Master output
            return false;
        }
        firstGroup = groups[0];
        return true;
    }

    private float DecibelFromAmplitude(float amplitude)
    {
        if (amplitude < m_SoundAmplitudeCutoff)
        {
            return -60.0f;
        }
        return 6.0f * Mathf.Log(amplitude) / Mathf.Log(2.0f);
    }
}
