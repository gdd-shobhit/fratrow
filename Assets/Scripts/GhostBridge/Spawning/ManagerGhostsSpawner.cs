using System.Collections.Generic;
using Unity.GhostBridge;
using Unity.NetCode;
using UnityEngine;

public interface IGhostManager
{
}

public class ManagerGhostsSpawner : Singleton<ManagerGhostsSpawner>
{
    [field: SerializeField] public List<GhostSpawner.GhostReference> ManagersToSpawn { get; private set; } = new();

    public override void Awake()
    {
        base.Awake();

        // Don't attempt to spawn ghosts if there is no server world.
        if (!ClientServerBootstrap.HasServerWorld)
        {
            gameObject.SetActive(false);
        }
    }

    public void LateUpdate()
    {
        if (GhostBridgeManager.Instance.IsServerListening())
        {
            if (GhostEntityPrefabSystem.ServerInstance.PrefabsLoaded)
            {
                foreach (var manager in ManagersToSpawn)
                {
                    var managerPrefab = GhostSpawner.FindGhostPrefab(manager);
                    if (managerPrefab != null)
                    {
                        Debug.Assert(managerPrefab.TryGetComponent<IGhostManager>(out _),
                            "Manager prefabs must have an IManager interface");
                    }

                    var netGuid = GhostGameObject.GenerateRandomHash();
                    if (!GhostSpawner.SpawnGhostPrefab(manager, Vector3.zero, Quaternion.identity, netGuid))
                    {
                        Debug.LogError($"[MANAGERGHOSTSPAWNER] Unable to spawn ghost manager {manager.GhostPrefab.AssetGUID}");
                    }
                }
                gameObject.SetActive(false);
            }
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
