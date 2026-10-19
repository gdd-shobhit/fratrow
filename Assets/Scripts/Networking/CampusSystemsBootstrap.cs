using System.Collections.Generic;
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

        static void EnsureLocations() => CampusLocationFallbacks.Ensure();

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

    /// <summary>
    /// Runtime fallback campus locations shared by <see cref="CampusSystemsBootstrap"/> and <see cref="NetworkedCampusNpcDirector"/>.
    /// Guarantees every location id the game loop needs (players' house, campus spots, the three rival houses) exists in the
    /// <see cref="CampusLocationRegistry"/>: creates the registry when absent, and adds only the missing markers under an
    /// existing one (scenes authored before rival houses existed keep working). Logs one warning listing what it created
    /// so a designer can place real markers. Idempotent — call it as often as needed.
    /// Example: <c>CampusLocationFallbacks.Ensure(); CampusLocationRegistry.Instance.TryGet("House_Kappa", out var kappa);</c>
    /// </summary>
    public static class CampusLocationFallbacks
    {
        struct Fallback
        {
            public string Id;
            public string Display;
            public Vector3 Position;

            public Fallback(string id, string display, Vector3 position)
            {
                Id = id;
                Display = display;
                Position = position;
            }
        }

        /// <summary>Required ids with placeholder positions (rival houses sit well outside the 14 m campus-location radius).</summary>
        static readonly Fallback[] Required =
        {
            new Fallback("PartyHouse", "Party House", Vector3.zero),
            new Fallback("Shop", "Shop", new Vector3(12f, 0f, 0f)),
            new Fallback("Quad", "Quad", new Vector3(-12f, 0f, 8f)),
            new Fallback("Gym", "Gym", new Vector3(8f, 0f, -10f)),
            new Fallback("Library", "Library", new Vector3(-8f, 0f, -10f)),
            new Fallback("Dorm", "Dorm", new Vector3(0f, 0f, 14f)),
            new Fallback("House_Kappa", "Kappa House", new Vector3(30f, 0f, 20f)),
            new Fallback("House_Theta", "Theta House", new Vector3(-30f, 0f, 20f)),
            new Fallback("House_Lambda", "Lambda House", new Vector3(0f, 0f, -30f)),
        };

        /// <summary>Ensures the registry and all required locations exist (single-threaded; main thread only).</summary>
        public static void Ensure()
        {
            var registry = CampusLocationRegistry.Instance != null
                ? CampusLocationRegistry.Instance
                : Object.FindFirstObjectByType<CampusLocationRegistry>();

            var created = new List<string>();
            if (registry == null)
            {
                // Build markers while the root is inactive so the registry's Awake scan (FindObjectsByType) sees them,
                // together with any designer-placed CampusLocation markers already in the scene.
                var root = new GameObject("CampusLocationRegistry");
                root.SetActive(false);
                var present = new HashSet<string>();
                foreach (var loc in Object.FindObjectsByType<CampusLocation>(FindObjectsSortMode.None))
                {
                    if (loc != null && !string.IsNullOrWhiteSpace(loc.LocationId)) present.Add(loc.LocationId);
                }

                foreach (var f in Required)
                {
                    if (present.Contains(f.Id)) continue;
                    CreateLoc(root, f);
                    created.Add(f.Id);
                }

                registry = root.AddComponent<CampusLocationRegistry>();
                root.SetActive(true);
                registry.Rebuild();
                RegisterIfUnresolved(registry, root, created);
                if (created.Count == Required.Length)
                {
                    Debug.Log("[CampusLocations] No campus markers in scene; using placeholder positions for all " + created.Count + " locations.");
                }
                else if (created.Count > 0)
                {
                    Debug.LogWarning("[CampusLocations] Created fallback locations (place real CampusLocation markers): " + string.Join(", ", created));
                }

                return;
            }

            foreach (var f in Required)
            {
                if (registry.TryGet(f.Id, out _)) continue;
                CreateLoc(registry.gameObject, f);
                created.Add(f.Id);
            }

            if (created.Count == 0) return;

            registry.Rebuild();
            RegisterIfUnresolved(registry, registry.gameObject, created);
            Debug.LogWarning("[CampusLocations] Registry was missing " + created.Count + " location(s); created fallbacks (place real CampusLocation markers): " +
                string.Join(", ", created));
        }

        static void CreateLoc(GameObject parent, Fallback f)
        {
            var go = new GameObject("Loc_" + f.Id);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = f.Position;
            go.AddComponent<CampusLocation>().Configure(f.Id, f.Display);
        }

        /// <summary>
        /// <see cref="CampusLocationRegistry.Rebuild"/> only rescans the scene when its serialized list is empty, so markers
        /// added to a registry with a pre-populated list are appended to that list directly. TODO: replace with a public
        /// <c>CampusLocationRegistry.Register(CampusLocation)</c> API.
        /// </summary>
        static void RegisterIfUnresolved(CampusLocationRegistry registry, GameObject parent, List<string> ids)
        {
            List<CampusLocation> missing = null;
            foreach (var id in ids)
            {
                if (registry.TryGet(id, out _)) continue;
                var t = parent.transform.Find("Loc_" + id);
                var loc = t != null ? t.GetComponent<CampusLocation>() : null;
                if (loc == null) continue;
                missing ??= new List<CampusLocation>();
                missing.Add(loc);
            }

            if (missing == null) return;

            foreach (var loc in missing) registry.Register(loc);
        }
    }
}
