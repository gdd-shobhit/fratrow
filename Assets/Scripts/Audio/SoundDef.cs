
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "Sound", menuName = "SoundDef/Create", order = 10000)]
public class SoundDef : ScriptableObject
{
    public enum PlaybackTypes
    {
        Random,
        RandomNotLast,
        Sequential,
        User
    }
    [Serializable]
    public class Filter
    {
        public bool EnableComponent;
        [Range(0, 22000)]
        public float CutoffMin = 22000.0f;
        [Range(0, 22000)]
        public float CutoffMax = 22000.0f;
    }

    [Serializable]
    public class Distortion
    {
        public bool EnableComponent;
        [Range(0.0f, 1.0f)]
        public float DistortionMin = 0f;
        [Range(0.0f, 1.0f)]
        public float DistortionMax = 0f;
    }

    [Serializable]
    public class StartStop
    {
        [Range(0, 10.0f)]
        public float DelayMin = 0.0f;
        [Range(0, 10.0f)]
        public float DelayMax = 0.0f;
        [Space(5)]
        [Range(0, 100)]
        public int StartOffsetPercentMin = 0;
        [Range(0, 100)]
        public int StartOffsetPercentMax = 0;
        [Space(5)]
        [Range(0, 60)]
        public float StopDelay = 0.0f;
    }

    [Serializable]
    public class PitchAndVolume
    {
        [Range(-60.0f, 0.0f)]
        public float VolumeMin = -6.0f;
        [Range(-60.0f, 0.0f)]
        public float VolumeMax = -6.0f;
        [Space(5)]
        [Range(-8000, 8000.0f)]
        public float PitchMin = 0.0f;
        [Range(-8000, 8000.0f)]
        public float PitchMax = 0.0f;
    }

    [Serializable]
    public class Repeat
    {
        [Range(0, 10)]
        public int LoopCount = 1;
        [Space(5)]
        [Range(1, 20)]
        public int RepeatMin = 1;
        [Range(1, 20)]
        public int RepeatMax = 1;
    }

    [Serializable]
    public class Distance
    {
        [Range(0.1f, 100.0f)]
        public float VolumeDistMin = 1.5f;
        [Range(0.1f, 100.0f)]
        public float VolumeDistMax = 30.0f;
        [Space(5)]
        [Range(0.0f, 1.0f)]
        public float SpatialBlend = 1.0f;
        [Space(5)]
        [Range(0.0f, 1.0f)]
        public float DopplerScale = 0.0f;   // 0 = no doppler effect applied to pitch. 1 = full doppler effect applied to pitch
        [Space(5)]
        public AudioRolloffMode VolumeRolloffMode = AudioRolloffMode.Linear;
        [Space(5)]
        public Interpolator.CurveType LPFRollOffCurveType;
        [Range(0, 22000)]
        public float LPF_MinCutoff;
        [Range(0f, 100.0f)]
        public float LPF_MaxDistance;

        public Interpolator.CurveType HPFRollOffCurveType;
        [Space(5)]
        [Range(0, 22000)]
        public float HPF_MinCutoff;
        [Range(0f, 100.0f)]
        public float HPF_MaxDistance;
        [Space(5)]
        public Interpolator.CurveType SpatialBlendCurveType;
        [Range(0f, 100.0f)]
        public float SpatialBlend_MaxDistance;
        [Space(5)]
        [Range(-1.0f, 1.0f)]
        public float PanMin = 0.0f;
        [Range(-1.0f, 1.0f)]
        public float PanMax = 0.0f;
    }

#if UNITY_EDITOR
    [Header("Editor Only")]
    public float EditorVolume = 1;
    public AudioMixer UnityAudioMixer;

#endif
    [Header("Sound Definition")]
    public SoundMixer.SoundMixerGroup MixerGroup;
    [Space(5)]
    public float BasePitchInCents = 0;
    public float VolumeScale = 1;
    public float BaseLowPassCutoff = 0;
    [Space(10)]
    public PlaybackTypes PlaybackType;
    public int PlayCount = 1;
    [Space(10)]
    public List<AudioClip> Clips;
    [Space(10)]

    public Repeat RepeatInfo;
    [Space(5)]
    public StartStop StartStopInfo;
    [Space(5)]
    public PitchAndVolume PitchAndVolumeInfo;
    [Space(5)]
    public Distance DistanceInfo;
    [Space(5)]
    public Filter LowPassFilter;
    public Filter HighPassFilter;
    [Space(5)]
    public Distortion DistortionFilter;



    public void OnValidate()
    {
        PitchAndVolumeInfo.VolumeMin = PitchAndVolumeInfo.VolumeMin > PitchAndVolumeInfo.VolumeMax ? PitchAndVolumeInfo.VolumeMax : PitchAndVolumeInfo.VolumeMin;
        DistanceInfo.VolumeDistMin = DistanceInfo.VolumeDistMin > DistanceInfo.VolumeDistMax ? DistanceInfo.VolumeDistMax : DistanceInfo.VolumeDistMin;
        PitchAndVolumeInfo.PitchMin = PitchAndVolumeInfo.PitchMin > PitchAndVolumeInfo.PitchMax ? PitchAndVolumeInfo.PitchMax : PitchAndVolumeInfo.PitchMin;
        StartStopInfo.DelayMin = StartStopInfo.DelayMin > StartStopInfo.DelayMax ? StartStopInfo.DelayMax : StartStopInfo.DelayMin;
        RepeatInfo.RepeatMin = RepeatInfo.RepeatMin > RepeatInfo.RepeatMax ? RepeatInfo.RepeatMax : RepeatInfo.RepeatMin;
        DistanceInfo.PanMin = DistanceInfo.PanMin > DistanceInfo.PanMax ? DistanceInfo.PanMax : DistanceInfo.PanMin;
        LowPassFilter.CutoffMin = LowPassFilter.CutoffMin > LowPassFilter.CutoffMax ? LowPassFilter.CutoffMax : LowPassFilter.CutoffMin;
        HighPassFilter.CutoffMin = HighPassFilter.CutoffMin > HighPassFilter.CutoffMax ? HighPassFilter.CutoffMax : HighPassFilter.CutoffMin;
        DistortionFilter.DistortionMin = DistortionFilter.DistortionMin > DistortionFilter.DistortionMax ? DistortionFilter.DistortionMax : DistortionFilter.DistortionMin;
        StartStopInfo.StartOffsetPercentMin = StartStopInfo.StartOffsetPercentMin > StartStopInfo.StartOffsetPercentMax ? StartStopInfo.StartOffsetPercentMax : StartStopInfo.StartOffsetPercentMin;
    }
}
