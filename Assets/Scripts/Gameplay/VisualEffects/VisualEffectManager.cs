using UnityEngine;
using System.Collections.Generic;
using System;
using Unity.NetCode;

namespace Unity.FPSSample_2
{
    public struct ClientSpawnVfxRpc : IRpcCommand
    {
        public int OwnerNetworkId;
        public uint WeaponId;
    }

    public class VisualEffectManager : GhostSingleton<VisualEffectManager>, IUpdateServer, IUpdateClient, IGhostManager
    {
        private Queue<ClientSpawnVfxRpc> _vfxQueue = new();

        public void Server_RequestVfx(int ownerNetworkId, uint weaponId)
        {
            _vfxQueue.Enqueue(new ClientSpawnVfxRpc { OwnerNetworkId = ownerNetworkId, WeaponId = weaponId });
        }

        public void UpdateServer(float deltaTime)
        {
            while (_vfxQueue.Count > 0)
            {
                var vfxRequest = _vfxQueue.Dequeue();
                GhostGameObject.BroadcastRPC(vfxRequest);
            }
        }

        public void UpdateClient(float deltaTime)
        {
            while (GhostGameObject.ConsumeRPC(out ClientSpawnVfxRpc rpc))
            {
                int localPlayerNetworkId = -1;

                // Find the local player's network ID
                if (PlayerGhostManager.TryGetInstanceByRole(MultiplayerRole.ClientOwned, out var playerManager) &&
                    playerManager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players) && players.Count > 0)
                {
                    var localPlayer = players[0];
                    if (localPlayer != null && localPlayer.GhostGameObject != null)
                    {
                        localPlayerNetworkId = localPlayer.GhostGameObject.Owner;
                    }
                }

                // If the RPC is for the local player, skip it.
                // The local player already spawned their own VFX in the prediction system.
                if (localPlayerNetworkId != -1 && localPlayerNetworkId == rpc.OwnerNetworkId)
                {
                    continue;
                }

                // This is for a remote player. Find them and spawn the effect.
                if (PlayerGhostManager.TryGetInstanceByRole(MultiplayerRole.ClientAll, out var allPlayersManager) &&
                    allPlayersManager.TryGetPlayersByRole(MultiplayerRole.ClientAll, out var allPlayers) && allPlayers.Count > 0)
                {
                    foreach (var player in allPlayers)
                    {
                        if (player.GhostGameObject.Owner == rpc.OwnerNetworkId)
                        {
                            // Found the remote player. Spawn the effect.
                            SpawnMuzzleFlash(player, rpc.WeaponId, false);
                            break;
                        }
                    }
                }
            }
        }

        public async void SpawnMuzzleFlash(PlayerGhost player, uint weaponId, bool isFirstPerson)
        {
            try
            {
                if (player == null)
                {
                    Debug.Log("Cannot spawn muzzle flash: player is null");
                    return;
                }

                var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId);
                if (weaponData == null)
                {
                    Debug.Log("Cannot spawn muzzle flash: weapon data is null");
                    return;
                }

                var spawnPoint = isFirstPerson ? player.VisualShotOrigin1P : player.VisualShotOrigin3P;
                if (spawnPoint == null)
                {
                    spawnPoint = player.transform; // Fallback
                }

                try
                {
                    var vfxInstance = await weaponData.MuzzleFlashVfxPrefab.GhostPrefab.InstantiateAsync(
                        spawnPoint.position, spawnPoint.rotation, spawnPoint).Task;
                    if (vfxInstance == null)
                    {
                        Debug.LogWarning("Cannot spawn muzzle flash: vfx instance is null");
                        return;
                    }

                    vfxInstance.AddComponent<DestroyAfterDelay>().Lifetime = 0.5f;

                    vfxInstance.SetActive(true);

                    GameManager.Instance.SoundSystem.CreateEmitter(weaponData.WeaponFireSfx, spawnPoint);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to spawn muzzle flash: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Error in SpawnMuzzleFlash: " + e.Message);
            }
        }

        private static void DrawGizmoAtPosition(Vector3 position)
        {
            var color = Color.red;
            const float duration = 4.0f; // How long the gizmo will be visible in seconds
            const float size = 0.5f; // The length of the lines for the cross marker

            Debug.DrawRay(position - Vector3.up * size, Vector3.up * size * 2, color, duration);
            Debug.DrawRay(position - Vector3.right * size, Vector3.right * size * 2, color, duration);
            Debug.DrawRay(position - Vector3.forward * size, Vector3.forward * size * 2, color, duration);
        }
    }
}