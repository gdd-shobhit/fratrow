using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.FPSSample_2.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class RespawnScreen : MonoBehaviour
    {
        public Camera RespawnCamera;
        private VisualElement m_RespawnScreen;
        private Label m_RespawnTimerLabel;

        private World m_ClientWorld;
        private EntityManager m_EntityManager;
        private EntityQuery m_LocalPlayerQuery;

        private float m_RespawnCountdown;
        private const float RESPAWN_DURATION = 5.0f;

        private void Awake()
        {
            RespawnCamera.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            m_RespawnScreen = GetComponent<UIDocument>().rootVisualElement;
            m_RespawnTimerLabel = m_RespawnScreen.Q<Label>("RespawnMessage");
        }

        private void InitializeEcs()
        {
            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    m_ClientWorld = world;
                    m_EntityManager = world.EntityManager;
                    break;
                }
            }

            if (m_EntityManager != null)
            {
                m_LocalPlayerQuery = m_EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>()
                );
            }
        }

        void LateUpdate()
        {
            if (GameSettings.Instance.GameState != GlobalGameState.InGame)
            {
                m_RespawnScreen.style.display = DisplayStyle.None;
                return;
            }

            if (m_ClientWorld == null || !m_ClientWorld.IsCreated)
            {
                InitializeEcs();
                if (m_ClientWorld == null) return;
            }

            bool isPlayerAlive = m_LocalPlayerQuery.HasSingleton<PredictedPlayerGhost>();

            if (isPlayerAlive)
            {
                RespawnCamera.gameObject.SetActive(false);
                // Player is alive, hide the respawn screen
                if (m_RespawnScreen.style.display == DisplayStyle.Flex)
                {
                    m_RespawnScreen.style.display = DisplayStyle.None;
                }
            }
            else
            {
                RespawnCamera.gameObject.SetActive(true);
                // Player is dead, show the respawn screen and update the timer
                if (m_RespawnScreen.style.display == DisplayStyle.None)
                {
                    // This is the first frame death is detected, start the countdown
                    m_RespawnCountdown = RESPAWN_DURATION;
                    m_RespawnScreen.style.display = DisplayStyle.Flex;
                }

                m_RespawnCountdown -= Time.deltaTime;
                if (m_RespawnCountdown < 0)
                {
                    m_RespawnCountdown = 0;
                }

                m_RespawnTimerLabel.text = $"RESPAWNING IN {Mathf.CeilToInt(m_RespawnCountdown).ToString()}";
            }
        }
    }
}