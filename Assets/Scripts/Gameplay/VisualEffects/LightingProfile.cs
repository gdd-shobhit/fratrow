using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.FPSSample_2
{
    [CreateAssetMenu(fileName = "LightingProfile", menuName = "Lighting/Lighting Profile")]
    public class LightingProfile : ScriptableObject
    {
        [Header("Skybox")] public Material skybox;

        [Header("Sun Light")] public Light sun;

        [Header("Ambient")] public AmbientMode ambientMode = AmbientMode.Skybox;
        public Color ambientLight = Color.gray;
        public float ambientIntensity = 1f;

        [Header("Fog")] public bool fog;
        public FogMode fogMode = FogMode.Exponential;
        public Color fogColor = Color.gray;
        public float fogDensity = 0.01f;
        public float fogStartDistance = 0f;
        public float fogEndDistance = 300f;
    }
}