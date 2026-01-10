using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class GhostPrefabsAuthoring : MonoBehaviour
{
    [field:SerializeField] public List<AssetReferenceGameObject> GhostPrefabs { get; private set; }
}

public class GhostPrefabsBaker : Baker<GhostPrefabsAuthoring>
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        s_BakedPrefabs.Clear();
        s_BakedPrefabs = null;
    }
    
    private static HashSet<int> s_BakedPrefabs = new();

    public override void Bake(GhostPrefabsAuthoring authoring)
    {
        var ghosts = authoring.GhostPrefabs;

        s_BakedPrefabs.Clear();
#if UNITY_EDITOR
        for (int i = 0; i < ghosts.Count; i++)
        {
            var prefab = ghosts[i].editorAsset;
            if (prefab != null)
            {
                int hash = prefab.gameObject.GetInstanceID();

                if (!s_BakedPrefabs.Contains(hash))
                {
                    // required to get prefab to be baked
                    GetEntity(prefab.gameObject, TransformUsageFlags.None);

                    s_BakedPrefabs.Add(hash);
                }
                else
                {
                    Debug.LogError($"[GHOSTPREFABSAUTHORING] Duplicate entity prefab detected `{prefab.gameObject.name}` index {i}");
                }
            }
            else
            {
                Debug.LogError($"[GHOSTPREFABSAUTHORING] Invalid entry in GhostPrefabs list at index {i}");
            }
        }
#endif
    }
}
