using System.Collections.Generic;
using UnityEngine;

namespace Systems.Campus
{
    /// <summary>
    /// Marker for a campus location spawn point. Register in scene under CampusLocationRegistry.
    /// </summary>
    public class CampusLocation : MonoBehaviour
    {
        [SerializeField] string locationId = "PartyHouse";
        [SerializeField] string displayName = "Party House";

        public string LocationId => locationId;
        public string DisplayName => displayName;
        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;

        public void Configure(string id, string display)
        {
            locationId = id;
            displayName = display;
        }

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(locationId))
            {
                locationId = gameObject.name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = locationId;
            }
        }
    }

    public class CampusLocationRegistry : MonoBehaviour
    {
        static CampusLocationRegistry _instance;
        public static CampusLocationRegistry Instance => _instance;

        [SerializeField] List<CampusLocation> locations = new List<CampusLocation>();
        readonly Dictionary<string, CampusLocation> _byId = new Dictionary<string, CampusLocation>();

        void Awake()
        {
            _instance = this;
            Rebuild();
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public void Rebuild()
        {
            _byId.Clear();
            if (locations == null || locations.Count == 0)
            {
                locations = new List<CampusLocation>(FindObjectsByType<CampusLocation>(FindObjectsSortMode.None));
            }

            foreach (var loc in locations)
            {
                if (loc == null || string.IsNullOrWhiteSpace(loc.LocationId))
                {
                    continue;
                }

                _byId[loc.LocationId] = loc;
            }
        }

        public bool TryGet(string locationId, out CampusLocation location)
        {
            if (_byId.Count == 0)
            {
                Rebuild();
            }

            return _byId.TryGetValue(locationId, out location);
        }

        public IEnumerable<CampusLocation> All
        {
            get
            {
                if (_byId.Count == 0)
                {
                    Rebuild();
                }

                return _byId.Values;
            }
        }
    }
}
