using System.Collections.Generic;
using System;
using Gameplay.Leaderboard;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.FPSSample_2
{
    public class Projectile : GhostMonoBehaviour, IUpdateServer, IUpdateClient
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            PredictedProjectiles = new List<PredictedProjectileInfo>();
        }

        public static List<PredictedProjectileInfo> PredictedProjectiles = new List<PredictedProjectileInfo>();
        private int _hitLayerMask;

        // Holds the projectile's networked state
        public struct ProjectileData : IComponentData
        {
            [GhostField] public int OwnerNetworkId;
            [GhostField] public uint SpawnTick;
            [GhostField] public uint WeaponID;
        }

        public class PredictedProjectileInfo
        {
            public GameObject Instance;
            public uint SpawnTick;
        }

        private void Awake()
        {
            _hitLayerMask = LayerMask.GetMask("ServerPlayer", "Ground", "Default");
        }

        private void Update()
        {
            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked())
            {
                var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(_weaponId);
                Move(Time.deltaTime, weaponData.ProjectileSpeed);
            }
        }

        public void SetWeaponId(uint weaponId)
        {
            _weaponId = weaponId;
        }

        private float _localTime;
        private uint _weaponId;

        public override void OnGhostLinked()
        {
            var projectileData = GhostGameObject.ReadGhostComponentData<ProjectileData>();
            _weaponId = projectileData.WeaponID;
        }

        public void UpdateServer(float deltaTime)
        {
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(_weaponId);
            Move(deltaTime, weaponData.ProjectileSpeed);

            _localTime += deltaTime;
            if (_localTime > 5f)
            {
                GhostGameObject.DestroyEntity();
                return;
            }

            CheckForCollision(weaponData, deltaTime);
        }

        private void Move(float deltaTime, float speed)
        {
            transform.position += transform.forward * (speed * deltaTime);
        }

        public void UpdateClient(float deltaTime)
        {
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(_weaponId);
            Move(deltaTime, weaponData.ProjectileSpeed);
        }

        // In Projectile.cs
        private void CheckForCollision(WeaponData weaponData, float deltaTime)
        {
            float projectileRadius = 0.2f;
            float distanceThisFrame = weaponData.ProjectileSpeed * deltaTime;

            // Combine layer masks for a single, efficient cast
            if (UnityEngine.Physics.SphereCast(transform.position, projectileRadius, transform.forward,
                    out RaycastHit hitInfo, distanceThisFrame, _hitLayerMask, QueryTriggerInteraction.Ignore))
            {
                // We hit something! The precise impact point is in hitInfo.point
                GameObject hitObject = hitInfo.collider.gameObject;

                var projectileData = GhostGameObject.ReadGhostComponentData<ProjectileData>();

                // Check if the hit object is a player
                if (hitObject.layer == LayerMask.NameToLayer("ServerPlayer"))
                {
                    var world = GhostGameObject.World;

                    var playerGhostLookup = world.GetExistingSystemManaged<ServerPlayerMovementSystem>()
                        .GetComponentLookup<PredictedPlayerGhost>();
                    var ghostOwnerLookup = world.GetExistingSystemManaged<ServerPlayerMovementSystem>()
                        .GetComponentLookup<GhostOwner>();


                    if (GhostGameObject.TryFindGhostGameObject(hitObject, out var hitGhostObject) &&
                        playerGhostLookup.HasComponent(hitGhostObject.LinkedEntity))
                    {
                        var hitPlayerOwner = ghostOwnerLookup[hitGhostObject.LinkedEntity];
                        if (hitPlayerOwner.NetworkId == projectileData.OwnerNetworkId)
                        {
                            // It's the owner, so we ignore this hit and do nothing.
                            // The projectile continues its path.
                            return;
                        }
                    }

                    // It's another player, so handle the impact using the precise hit point
                    HandlePlayerImpact(hitObject, hitInfo.point, world, projectileData, playerGhostLookup,
                        ghostOwnerLookup);
                }
                else
                {
                    // It's geometry (Ground, Default, etc.), handle the impact
                    HandleGeometryImpact(hitInfo.point);
                }

                if (weaponData.ProjectileHitVfxPrefab != null)
                {
                    GhostSpawner.SpawnGhostPrefab(weaponData.ProjectileHitVfxPrefab, hitInfo.point,
                        Quaternion.LookRotation(hitInfo.normal), GhostGameObject.GenerateRandomHash());
                }
            }
        }

        private void HandleGeometryImpact(Vector3 impactPosition)
        {
            DrawGizmoAtPosition(impactPosition);
            GhostGameObject.DestroyEntity();
        }

        private void HandlePlayerImpact(GameObject hitObject, Vector3 impactPosition, World world,
            ProjectileData projectileData,
            ComponentLookup<PredictedPlayerGhost> playerGhostLookup, ComponentLookup<GhostOwner> ghostOwnerLookup)
        {
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(projectileData.WeaponID);
            int shooterNetworkId = projectileData.OwnerNetworkId;

            var serverCurrentTick = GhostGameObject.GetCurrentTick();

            // Check the behavior type to decide the damage logic.
            if (weaponData.Behavior == ProjectileBehavior.AreaOfEffect)
            {
                // ROCKET LOGIC 
                var playersInRadius = UnityEngine.Physics.OverlapSphere(impactPosition, weaponData.AoeRadius,
                    LayerMask.GetMask("ServerPlayer"));

                foreach (var playerCollider in playersInRadius)
                {
                    if (GhostGameObject.TryFindGhostGameObject(playerCollider.gameObject, out var hitGhostObject) &&
                        playerGhostLookup.HasComponent(hitGhostObject.LinkedEntity))
                    {
                        // Get the owner of the hit player and compare it to the projectile's owner.
                        var hitPlayerOwner = ghostOwnerLookup[hitGhostObject.LinkedEntity];
                        int targetNetworkId = hitPlayerOwner.NetworkId;
                        if (targetNetworkId == shooterNetworkId)
                        {
                            continue; // Skip self-damage
                        }

                        var targetPredictedPlayer = playerGhostLookup.GetRefRW(hitGhostObject.LinkedEntity);

                        var healthBeforeDamage = targetPredictedPlayer.ValueRO.CurrentHealth;
                        targetPredictedPlayer.ValueRW.CurrentHealth -= weaponData.Damage;

                        // Set the simple flag for animations
                        targetPredictedPlayer.ValueRW.ControllerState.IsHit = true;

                        // Set the detailed data for the 1P visual effect
                        targetPredictedPlayer.ValueRW.LastDamageAmount = weaponData.Damage;
                        targetPredictedPlayer.ValueRW.LastHitTick = serverCurrentTick;

                        if (healthBeforeDamage > 0 && targetPredictedPlayer.ValueRO.CurrentHealth <= 0)
                        {
                            if (LeaderboardManager.Instance != null)
                            {
                                LeaderboardManager.Instance.AddKill(shooterNetworkId, targetNetworkId);
                                Debug.Log(
                                    $"[Server] Player {shooterNetworkId.ToString()} killed player {targetNetworkId.ToString()} (AOE).");
                            }
                            else
                            {
                                Debug.LogWarning("[Server] LeaderboardManager instance not found. Cannot add kill.");
                            }
                        }

                        float gizmoDuration = 4.0f; // How long the gizmo will be visible in seconds
                        float gizmoSize = 0.25f; // The length of the lines for the cross marker

                        Debug.DrawRay(impactPosition - Vector3.up * gizmoSize, Vector3.up * gizmoSize * 2, Color.yellow,
                            gizmoDuration);
                        Debug.DrawRay(impactPosition - Vector3.right * gizmoSize, Vector3.right * gizmoSize * 2,
                            Color.yellow, gizmoDuration);
                        Debug.DrawRay(impactPosition - Vector3.forward * gizmoSize, Vector3.forward * gizmoSize * 2,
                            Color.yellow, gizmoDuration);
                        // After the impact is handled, the projectile must be destroyed.
                        GhostGameObject.DestroyEntity();
                    }
                }
            }
            else // DirectDamage
            {
                if (GhostGameObject.TryFindGhostGameObject(hitObject, out var hitGhostObject) &&
                    playerGhostLookup.HasComponent(hitGhostObject.LinkedEntity))
                {
                    // Get the owner of the hit player and compare it to the projectile's owner.
                    var hitPlayerOwner = ghostOwnerLookup[hitGhostObject.LinkedEntity];
                    int targetNetworkId = hitPlayerOwner.NetworkId;

                    if (targetNetworkId != shooterNetworkId)
                    {
                        Debug.Log(
                            $"2 hitPlayerOwner.NetworkId: {hitPlayerOwner.NetworkId.ToString()}, projectileData.OwnerNetworkId: {projectileData.OwnerNetworkId.ToString()}");
                        var targetPredictedPlayer = playerGhostLookup.GetRefRW(hitGhostObject.LinkedEntity);
                        var healthBeforeDamage = targetPredictedPlayer.ValueRO.CurrentHealth;
                        targetPredictedPlayer.ValueRW.CurrentHealth -= weaponData.Damage;
                        targetPredictedPlayer.ValueRW.ControllerState.IsHit = true;
                        targetPredictedPlayer.ValueRW.LastDamageAmount = weaponData.Damage;
                        targetPredictedPlayer.ValueRW.LastHitTick = serverCurrentTick;

                        if (healthBeforeDamage > 0 && targetPredictedPlayer.ValueRO.CurrentHealth <= 0)
                        {
                            if (LeaderboardManager.Instance != null)
                            {
                                LeaderboardManager.Instance.AddKill(shooterNetworkId, targetNetworkId);
                                Debug.Log(
                                    $"[Server] Player {shooterNetworkId.ToString()} killed player {targetNetworkId.ToString()} (Direct).");
                            }
                            else
                            {
                                Debug.LogWarning("[Server] LeaderboardManager instance not found. Cannot add kill.");
                            }
                        }


                        DrawGizmoAtPosition(impactPosition);
                        GhostGameObject.DestroyEntity();
                    }
                }
            }
        }

        private static void DrawGizmoAtPosition(Vector3 position)
        {
            var color = Color.yellow;
            const float duration = 4.0f; // How long the gizmo will be visible in seconds
            const float size = 0.25f; // The length of the lines for the cross marker

            Debug.DrawRay(position - Vector3.up * size, Vector3.up * size * 2, color, duration);
            Debug.DrawRay(position - Vector3.right * size, Vector3.right * size * 2, color, duration);
            Debug.DrawRay(position - Vector3.forward * size, Vector3.forward * size * 2, color, duration);
        }
    }
}