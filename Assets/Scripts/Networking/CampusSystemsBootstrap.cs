using Systems.Dialogue;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Campus
{
    /// <summary>
    /// Local dialogue host + campus locations. Named NPCs come from NetworkedCampusNpcDirector.
    /// </summary>
    public class CampusSystemsBootstrap : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            EnsureLocalDialogue();
            EnsureLocations();

            if (IsServer)
            {
                NetworkedCampusCalendar.Instance?.BeginWeekdayServer();
            }

            CampusPersistence.ApplyToLocalPhones();
        }

        static void EnsureLocations()
        {
            if (FindFirstObjectByType<CampusLocationRegistry>() != null)
            {
                return;
            }

            var regGo = new GameObject("CampusLocationRegistry");
            regGo.AddComponent<CampusLocationRegistry>();
            CreateLoc(regGo, "PartyHouse", "Party House", Vector3.zero);
            CreateLoc(regGo, "Shop", "Shop", new Vector3(12f, 0f, 0f));
            CreateLoc(regGo, "Quad", "Quad", new Vector3(-12f, 0f, 8f));
            CreateLoc(regGo, "Gym", "Gym", new Vector3(8f, 0f, -10f));
            CreateLoc(regGo, "Library", "Library", new Vector3(-8f, 0f, -10f));
            CreateLoc(regGo, "Dorm", "Dorm", new Vector3(0f, 0f, 14f));
            FindFirstObjectByType<CampusLocationRegistry>()?.Rebuild();
        }

        static void CreateLoc(GameObject parent, string id, string display, Vector3 pos)
        {
            var go = new GameObject($"Loc_{id}");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = pos;
            go.AddComponent<CampusLocation>().Configure(id, display);
        }

        static void EnsureLocalDialogue()
        {
            if (CampusDialogueService.Instance != null)
            {
                return;
            }

            var go = new GameObject("CampusDialogueService");
            DontDestroyOnLoad(go);
            var doc = go.AddComponent<UIDocument>();

            var panels = Resources.FindObjectsOfTypeAll<PanelSettings>();
            if (panels != null && panels.Length > 0)
            {
                doc.panelSettings = panels[0];
            }

            var uxml = Resources.Load<VisualTreeAsset>("Dialogue/Dialogue");
            if (uxml != null)
            {
                doc.visualTreeAsset = uxml;
            }
            else
            {
                Debug.LogWarning("[CampusBootstrap] Resources/Dialogue/Dialogue.uxml missing.");
            }

            go.AddComponent<UITKDialoguePresenter>();
            go.AddComponent<CampusDialogueService>();
        }
    }
}
