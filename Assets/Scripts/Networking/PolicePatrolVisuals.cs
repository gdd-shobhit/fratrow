using System.Collections.Generic;
using Systems.Campus;
using UnityEngine;

/// <summary>
/// Local (non-networked) police visuals driven by <see cref="NetworkedCampaign.Patrols"/> (design §3.6 police patrols).
/// Each unit is drawn between its from/to campus locations; progress is extrapolated between 4 Hz updates.
/// Assign <see cref="officerPrefab"/> for real art; the fallback is a blue capsule. Added at runtime by NetworkedCampaign.
/// </summary>
public sealed class PolicePatrolVisuals : MonoBehaviour
{
    [SerializeField] GameObject officerPrefab;
    [SerializeField] float sideOffset = 2.5f;

    sealed class Unit
    {
        public GameObject go;
        public float progress;
        public float travelSeconds;
        public string from, to;
        public byte mode;
    }

    readonly Dictionary<short, Unit> units = new Dictionary<short, Unit>();
    readonly List<short> stale = new List<short>();
    Material material;

    void Update()
    {
        var campaign = NetworkedCampaign.Instance;
        var registry = CampusLocationRegistry.Instance;
        if (campaign == null || campaign.Patrols == null || registry == null || !campaign.IsSpawned) return;
        var list = campaign.Patrols;
        stale.Clear();
        foreach (var key in units.Keys) stale.Add(key);
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            stale.Remove(p.Id);
            if (!units.TryGetValue(p.Id, out var unit))
            {
                unit = new Unit { go = CreateOfficer(p.Id) };
                units[p.Id] = unit;
            }
            string from = p.From.ToString(), to = p.To.ToString();
            // Accept the authoritative progress whenever the leg changes or we drift too far.
            if (unit.from != from || unit.to != to || Mathf.Abs(unit.progress - p.Progress) > 0.2f)
            {
                unit.from = from;
                unit.to = to;
                unit.progress = p.Progress;
            }
            unit.travelSeconds = p.TravelSeconds;
            unit.mode = p.Mode;
            if (unit.progress < 1f && unit.travelSeconds > 0f) unit.progress = Mathf.Min(1f, unit.progress + Time.deltaTime / unit.travelSeconds);
            if (!registry.TryGet(from, out var a) || !registry.TryGet(to, out var b)) { unit.go.SetActive(false); continue; }
            unit.go.SetActive(true);
            Vector3 pos = Vector3.Lerp(a.Position, b.Position, unit.progress) + Vector3.right * sideOffset;
            unit.go.transform.position = pos;
            Vector3 dir = b.Position - a.Position;
            if (dir.sqrMagnitude > 0.01f && unit.progress < 1f) unit.go.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z));
        }
        for (int i = 0; i < stale.Count; i++)
        {
            if (units.TryGetValue(stale[i], out var dead) && dead.go != null) Destroy(dead.go);
            units.Remove(stale[i]);
        }
    }

    GameObject CreateOfficer(short id)
    {
        GameObject go;
        if (officerPrefab != null) go = Instantiate(officerPrefab);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null) material = new Material(shader) { color = new Color(0.1f, 0.2f, 0.85f) };
            }
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
        }
        go.name = "CampusPolice_" + id;
        DontDestroyOnLoad(go);
        return go;
    }

    void OnDestroy()
    {
        foreach (var unit in units.Values) if (unit.go != null) Destroy(unit.go);
        units.Clear();
    }
}
