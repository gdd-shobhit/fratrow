using UnityEngine;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// This class allows the <see cref="MainCameraSystem"/> to sync its position to the current player character position in the Client World.
    /// </summary>
    [RequireComponent(typeof(Camera), typeof(AudioListener))]
    public class MainCameraSingleton : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            if (Instance != null)
            {
                Destroy(Instance);
                Instance = null;
            }
        }
        
        public static Camera Instance;
        public AudioListener AudioListener { get; private set; } = null;

        void Awake()
        {
            // We already have a main camera and don't need a new one.
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = GetComponent<Camera>();
            AudioListener = GetComponent<AudioListener>();
        }

        void OnDestroy()
        {
            if (Instance == GetComponent<Camera>())
            {
                Instance = null;
            }
        }
    }
}