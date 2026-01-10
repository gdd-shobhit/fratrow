using UnityEngine;

namespace Unity.FPSSample_2
{
    public class LightingProfileApplier : MonoBehaviour
    {
        [SerializeField] private LightingProfile profile;

        private void OnEnable()
        {
            if (profile == null)
            {
                Debug.LogWarning("LightingProfileApplier has no assigned profile.");
                return;
            }

            ApplyLighting(profile);
        }

        private void ApplyLighting(LightingProfile p)
        {
            RenderSettings.skybox = p.skybox;

            RenderSettings.sun = p.sun;
            RenderSettings.ambientMode = p.ambientMode;
            RenderSettings.ambientLight = p.ambientLight;
            RenderSettings.ambientIntensity = p.ambientIntensity;

            RenderSettings.fog = p.fog;
            RenderSettings.fogMode = p.fogMode;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;
            RenderSettings.fogStartDistance = p.fogStartDistance;
            RenderSettings.fogEndDistance = p.fogEndDistance;

            DynamicGI.UpdateEnvironment();
        }
    }
}