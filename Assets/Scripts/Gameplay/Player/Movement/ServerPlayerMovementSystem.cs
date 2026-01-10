using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.FPSSample_2
{
    public class PlayerControllerLink : IComponentData
    {
        public FirstPersonController Controller;
    }

    struct ProjectileSpawnData
    {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
        public int OwnerNetworkId;
        public uint SpawnTick;
        public uint WeaponId;
    }

    struct VfxSpawnData
    {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial class ServerPlayerMovementSystem : SingletonSystem<ServerPlayerMovementSystem>
    {
        private const int k_NumHistoryTicks = 20;
        private static readonly int s_ShootableLayerMask = LayerMask.GetMask("Ground", "Default");
        private static readonly int s_HitscanLayerMask = LayerMask.GetMask("ServerPlayer", "Default", "Ground");
        private NativeList<ClientCommandInput> m_ProcessedClientInputCommands;
        private ComponentLookup<PredictedClientInput> m_PredictedClientInputComponentLookup;
        private ComponentLookup<PredictedPlayerGhost> m_PlayerGhostLookup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            s_PlayerMovementActive = false;
            s_PlayerMovementTick = 0;
        }

        // logging and diagnostics
        private static bool s_PlayerMovementActive = false;
        private static uint s_PlayerMovementTick;
        public static bool PlayerMovementActive => s_PlayerMovementActive;
        public static uint PlayerMovementTick => s_PlayerMovementTick;

        private ComponentLookup<GhostOwner> ghostOwnerLookup;

        public PlayerMovementHistory MovementHistory { get; private set; } =
            new PlayerMovementHistory(k_NumHistoryTicks);

        protected override void OnCreate()
        {
            base.OnCreate();

            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<NetworkTime>();

            m_PredictedClientInputComponentLookup = GetComponentLookup<PredictedClientInput>(true);
            m_PlayerGhostLookup = GetComponentLookup<PredictedPlayerGhost>();

            const int maxLocalPlayers = 4; //PlayerGhostManager.k_MaxLocalPlayers
            m_ProcessedClientInputCommands =
                new NativeList<ClientCommandInput>(maxLocalPlayers * 32, Allocator.Persistent);

            ghostOwnerLookup = GetComponentLookup<GhostOwner>(true);
        }

        protected override void OnDestroy()
        {
            m_ProcessedClientInputCommands.Dispose();

            var query = GetEntityQuery(typeof(PlayerControllerLink));
            foreach (var entity in query.ToEntityArray(Allocator.Temp))
            {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                controllerLink.Controller = null;
                EntityManager.SetComponentData(entity, controllerLink);
            }

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            float deltaTime = World.Time.DeltaTime;

            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            if (!networkTime.ServerTick.IsValid)
            {
                return;
            }

            uint serverTick = networkTime.ServerTick.TickIndexForValidTick;

            s_PlayerMovementActive = true;
            s_PlayerMovementTick = serverTick;

            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (predictedPlayer, localTransform, entity) in
                     SystemAPI.Query<RefRW<PredictedPlayerGhost>, RefRO<LocalTransform>>()
                         .WithEntityAccess()
                         .WithAll<PlayerInputComponent, GhostGameObjectLink>()
                         .WithNone<PlayerControllerLink>())
            {
                var gameObjectLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                if (gameObjectLink.LinkedInstance != null)
                {
                    if (gameObjectLink.LinkedInstance.TryGetComponent<FirstPersonController>(out var controller))
                    {
                        ecb.AddComponent(entity, new PlayerControllerLink { Controller = controller });
                        predictedPlayer.ValueRW.ControllerState.Init(localTransform.ValueRO.Position,
                            localTransform.ValueRO.Rotation);
                    }
                }
            }

            // for each client
            // we process the input and create a list of commandInputs that need to be processed by the movement code
            // the movement code processes the inputs in order
            m_ProcessedClientInputCommands.Clear();
            var commands = m_ProcessedClientInputCommands;
            var unityFrameCount = UnityEngine.Time.frameCount;

            var logPredictionWarnings = PlayerPredictionSystem.CheckForPredictionErrors.IsEnabled;
            var clientCommandInputBufferLookup = SystemAPI.GetBufferLookup<ClientCommandInput>(true);

            foreach (var (predictedClient, entity)
                     in SystemAPI.Query<RefRW<PredictedClientInput>>()
                         .WithEntityAccess()
                         .WithAll<GhostOwner, Simulate>())
            {
                if (!clientCommandInputBufferLookup.TryGetBuffer(entity, out var buffer) || buffer.Length == 0)
                {
                    continue;
                }

                predictedClient.ValueRW.BeginInputIndex = commands.Length;

                if (predictedClient.ValueRO.LastProcessedServerTick == 0)
                {
                    // first frame, let's just get the latest data and set it
                    if (buffer.GetDataAtTick(new NetworkTick(serverTick), out var commandInput))
                    {
                        ClientCommandInput input = commandInput;
                        commands.Add(input);
                        predictedClient.ValueRW.LastProcessedServerTick = commandInput.Tick.TickIndexForValidTick;
                    }
                }
                else
                {
                    if (logPredictionWarnings && serverTick - predictedClient.ValueRO.LastProcessedServerTick > 1)
                    {
                        Debug.LogWarning($"[{unityFrameCount.ToString()}] " +
                                         $"[ServerPlayerMovementSystem] Server tick has skipped a frame of input. " +
                                         $"Current server tick {serverTick.ToString()}, last processed tick {predictedClient.ValueRO.LastProcessedServerTick.ToString()}");
                    }

                    // we need to check we aren't missing data
                    // so let's check for any data from the ticks in between
                    for (uint tick = predictedClient.ValueRO.LastProcessedServerTick + 1; tick <= serverTick; tick++)
                    {
                        if (buffer.GetDataAtTick(new NetworkTick(tick), out var commandInput)
                            && commandInput.Tick.TickIndexForValidTick !=
                            predictedClient.ValueRO.LastProcessedServerTick)
                        {
                            ClientCommandInput input = commandInput;
                            commands.Add(input);

                            if (commandInput.Tick.TickIndexForValidTick >
                                predictedClient.ValueRO.LastProcessedServerTick + 1)
                            {
                                // we are missing input from the client which means we might mispredict
                                for (uint i = predictedClient.ValueRO.LastProcessedServerTick + 1;
                                     i < commandInput.Tick.TickIndexForValidTick;
                                     i++)
                                {
                                    // if we have missing ticks here, it means the server did not (and will not) receive it.
                                    // So the best we can do is assume the input is the same and accumulate our movement based on that
                                    if (logPredictionWarnings)
                                    {
                                        Debug.LogWarning($"[{unityFrameCount.ToString()}] " +
                                                         $"[ServerPlayerMovementSystem] Missing client input for tick {i.ToString()} - " +
                                                         $"using the input from tick {commandInput.Tick.TickIndexForValidTick.ToString()}");
                                    }

                                    commands.Add(commandInput);
                                }
                            }

                            predictedClient.ValueRW.LastProcessedServerTick = commandInput.Tick.TickIndexForValidTick;
                        }
                    }
                }

                predictedClient.ValueRW.InputCount = commands.Length - predictedClient.ValueRO.BeginInputIndex;

                if (logPredictionWarnings)
                {
                    var unityFrameCountString = unityFrameCount.ToString();
                    var serverTickString = serverTick.ToString();
                    if (predictedClient.ValueRO.InputCount == 0)
                    {
                        Debug.LogWarning(
                            $"[{unityFrameCountString}] [ServerPlayerMovementSystem] No input to process for tick {serverTickString}.");
                    }
                    else if (predictedClient.ValueRO.LastProcessedServerTick != serverTick)
                    {
                        Debug.LogWarning(
                            $"[{unityFrameCountString}] [ServerPlayerMovementSystem] Last processed tick not set to current. Current server tick {serverTickString}, last processed tick {predictedClient.ValueRO.LastProcessedServerTick.ToString()}");
                    }
                }
            }

            ghostOwnerLookup.Update(this);
            m_PlayerGhostLookup.Update(this);
            m_PredictedClientInputComponentLookup.Update(this);

            var predictedClientInputComponentLookup = m_PredictedClientInputComponentLookup;
            var playerGhostLookup = m_PlayerGhostLookup;

            var projectileSpawnList = new NativeList<ProjectileSpawnData>(Allocator.Temp);
            var vfxSpawnList = new NativeList<VfxSpawnData>(Allocator.Temp);

            foreach (var predictedPlayer in SystemAPI.Query<RefRW<PredictedPlayerGhost>>()
                         .WithAll<Simulate>())
            {
                if (predictedPlayer.ValueRO.WeaponCooldown < float.MaxValue)
                {
                    predictedPlayer.ValueRW.WeaponCooldown += deltaTime;
                }

                if (predictedPlayer.ValueRO.ControllerState.IsReloadingState)
                {
                    predictedPlayer.ValueRW.ReloadTimer -= deltaTime;
                    if (predictedPlayer.ValueRO.ReloadTimer <= 0f)
                    {
                        predictedPlayer.ValueRW.ControllerState.IsReloadingState = false;
                        var weaponData =
                            WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                                predictedPlayer.ValueRO.EquippedWeaponID);
                        if (weaponData != null)
                        {
                            predictedPlayer.ValueRW.CurrentAmmo = weaponData.MagazineSize;
                        }
                    }
                }
            }

            foreach (var (predictedPlayer, inputLookup,
                         entity)
                     in SystemAPI.Query<RefRW<PredictedPlayerGhost>, RefRO<PlayerClientCommandInputLookup>>()
                         .WithEntityAccess()
                         .WithAll<Simulate, GhostGameObjectLink>())
            {
                var ghostLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                var playerGhost = ghostLink.LinkedInstance.GetComponent<PlayerGhost>();
                var predictedClient = predictedClientInputComponentLookup[inputLookup.ValueRO.ClientCommandInputEntity];
                if (predictedClient.InputCount > 0)
                {
                    // get movement index for the last frame we are going to process
                    var commandInput = commands[predictedClient.BeginInputIndex + predictedClient.InputCount - 1];
                    if (commandInput.TryGetPlayerMovementInput(predictedPlayer.ValueRO.InputIndex, out var input))
                    {
                        playerGhost.ServerMovementInput = input;
                    }

                    predictedPlayer.ValueRW.ControllerState.Shoot = false;

                    var weaponData =
                        WeaponManager.Instance.WeaponRegistry.GetWeaponData(predictedPlayer.ValueRO.EquippedWeaponID);
                    if (weaponData != null)
                    {
                        bool wantsToReload = commandInput.PlayerInput.Reload;
                        bool wantsToShoot = commandInput.PlayerInput.Shoot;
                        bool mustReload = wantsToShoot && predictedPlayer.ValueRO.CurrentAmmo <= 0;

                        if ((wantsToReload || mustReload) &&
                            !predictedPlayer.ValueRO.ControllerState.IsReloadingState &&
                            predictedPlayer.ValueRO.CurrentAmmo < weaponData.MagazineSize)
                        {
                            predictedPlayer.ValueRW.ControllerState.IsReloadingState = true;
                            predictedPlayer.ValueRW.ReloadTimer = weaponData.ReloadTime;
                            predictedPlayer.ValueRW.LastReloadTick = serverTick;
                        }

                        if (wantsToShoot &&
                            !predictedPlayer.ValueRO.ControllerState.IsReloadingState &&
                            predictedPlayer.ValueRO.CurrentAmmo > 0 &&
                            predictedPlayer.ValueRO.WeaponCooldown >= weaponData.CooldownInMs)
                        {
                            predictedPlayer.ValueRW.WeaponCooldown = 0f;
                            predictedPlayer.ValueRW.CurrentAmmo--;
                            predictedPlayer.ValueRW.LastShotTick = serverTick;

                            var shooterNetworkId = ghostOwnerLookup[entity].NetworkId;
                            var controllerState = predictedPlayer.ValueRO.ControllerState;
                            quaternion aimRotation = quaternion.Euler(
                                math.radians(controllerState.PitchDegrees),
                                math.radians(controllerState.YawDegrees),
                                0f);

                            float3 eyePosition = playerGhost.CameraTarget.position;
                            float3 aimDirection = math.mul(aimRotation, new float3(0, 0, 1));
                            float3 shotOriginPosition = eyePosition + aimDirection * 0.5f;

                            if (VisualEffectManager.ServerInstance != null)
                            {
                                VisualEffectManager.ServerInstance.Server_RequestVfx(shooterNetworkId,
                                    predictedPlayer.ValueRO.EquippedWeaponID);
                            }

                            switch (weaponData.Type)
                            {
                                case WeaponType.Hitscan:
                                {
                                    // Use the calculated eyePosition and aimDirection
                                    if (UnityEngine.Physics.Raycast(eyePosition, aimDirection, out var hit,
                                            weaponData.HitscanRange,
                                            s_HitscanLayerMask))
                                    {
                                        if (hit.collider.gameObject.layer == LayerMask.NameToLayer("ServerPlayer"))
                                        {
                                            if (GhostGameObject.TryFindGhostGameObject(hit.collider.gameObject,
                                                    out var hitGhostObject) &&
                                                playerGhostLookup.HasComponent(hitGhostObject.LinkedEntity))
                                            {
                                                var targetPredictedPlayer =
                                                    playerGhostLookup.GetRefRW(hitGhostObject.LinkedEntity);
                                                var targetNetworkId = ghostOwnerLookup[hitGhostObject.LinkedEntity]
                                                    .NetworkId;

                                                if (shooterNetworkId == targetNetworkId)
                                                {
                                                    //skip hitting self
                                                    continue;
                                                }

                                                var healthBeforeDamage = targetPredictedPlayer.ValueRO.CurrentHealth;
                                                targetPredictedPlayer.ValueRW.CurrentHealth -= weaponData.Damage;
                                                targetPredictedPlayer.ValueRW.ControllerState.IsHit = true;

                                                targetPredictedPlayer.ValueRW.LastDamageAmount = weaponData.Damage;
                                                targetPredictedPlayer.ValueRW.LastHitTick = serverTick;

                                                if (healthBeforeDamage > 0 &&
                                                    targetPredictedPlayer.ValueRO.CurrentHealth <= 0)
                                                {
                                                    if (LeaderboardManager.Instance != null)
                                                    {
                                                        LeaderboardManager.Instance.AddKill(shooterNetworkId,
                                                            targetNetworkId);
                                                        Debug.Log(
                                                            $"[Server] Player {shooterNetworkId.ToString()} killed player {targetNetworkId.ToString()}.");
                                                    }
                                                    else
                                                    {
                                                        Debug.LogWarning(
                                                            "[Server] LeaderboardManager instance not found. Cannot add kill.");
                                                    }
                                                }
                                            }
                                        }

                                        if (weaponData.ProjectileHitVfxPrefab != null)
                                        {
                                            vfxSpawnList.Add(new VfxSpawnData()
                                            {
                                                Position = hit.point,
                                                Rotation = Quaternion.LookRotation(hit.normal),
                                                Prefab = GhostSpawner.FindGhostPrefabEntity(weaponData
                                                    .ProjectileHitVfxPrefab.GhostGuid)
                                            });
                                        }
                                    }

                                    break;
                                }
                                case WeaponType.Projectile:
                                {
                                    var prefabEntity =
                                        GhostSpawner.FindGhostPrefabEntity(weaponData.ProjectileGhostPrefab.GhostGuid);
                                    if (prefabEntity != Entity.Null)
                                    {
                                        // Determine target point
                                        Vector3 targetPoint;
                                        // Use calculated eyePosition and aimDirection for the raycast
                                        if (UnityEngine.Physics.Raycast(eyePosition, aimDirection,
                                                out RaycastHit aimHit, 1000f,
                                                s_ShootableLayerMask))
                                        {
                                            targetPoint = aimHit.point;
                                        }
                                        else
                                        {
                                            targetPoint = eyePosition + aimDirection * 1000f;
                                        }

                                        // Use calculated shotOriginPosition for direction calculation
                                        Vector3 directionToTarget =
                                            (targetPoint - (Vector3)shotOriginPosition).normalized;
                                        Quaternion spawnRotation = Quaternion.LookRotation(directionToTarget);

                                        projectileSpawnList.Add(new ProjectileSpawnData
                                        {
                                            Prefab = prefabEntity,
                                            Position = shotOriginPosition,
                                            Rotation = spawnRotation,
                                            OwnerNetworkId = ghostOwnerLookup[entity].NetworkId,
                                            SpawnTick = commandInput.Tick.TickIndexForValidTick,
                                            WeaponId = predictedPlayer.ValueRO.EquippedWeaponID
                                        });
                                    }

                                    break;
                                }
                            }
                        }
                    }
                }
            }

            foreach (var spawnData in projectileSpawnList)
            {
                GhostSpawner.SpawnGhostPrefab(
                    spawnData.Prefab,
                    spawnData.Position,
                    spawnData.Rotation,
                    GhostGameObject.GenerateRandomHash(),
                    1.0f,
                    (spawnedEntity, ecb2) =>
                    {
                        ecb2.SetComponent(spawnedEntity, new Projectile.ProjectileData
                        {
                            OwnerNetworkId = spawnData.OwnerNetworkId,
                            SpawnTick = spawnData.SpawnTick,
                            WeaponID = spawnData.WeaponId
                        });
                    });
            }

            foreach (var vfxData in vfxSpawnList)
            {
                GhostSpawner.SpawnGhostPrefab(
                    vfxData.Prefab,
                    vfxData.Position,
                    vfxData.Rotation,
                    GhostGameObject.GenerateRandomHash());
            }

            predictedClientInputComponentLookup.Update(this);

            foreach (var (predictedPlayer, inputLookup, controllerConsts, entity)
                     in SystemAPI
                         .Query<RefRW<PredictedPlayerGhost>, RefRO<PlayerClientCommandInputLookup>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                var predictedClient = predictedClientInputComponentLookup[inputLookup.ValueRO.ClientCommandInputEntity];
                if (predictedClient.InputCount > 0)
                {
                    float movementDt = deltaTime / predictedClient.InputCount;
                    for (int i = 0; i < predictedClient.InputCount; i++)
                    {
                        var commandInput = commands[i + predictedClient.BeginInputIndex];
                        if (commandInput.TryGetPlayerMovementInput(predictedPlayer.ValueRO.InputIndex, out var input))
                        {
                            FirstPersonController.AccumulateMovement(ref predictedPlayer.ValueRW.ControllerState,
                                ref predictedPlayer.ValueRW.AccumulatedMovement,
                                input,
                                controllerConsts.ValueRO.ControllerConsts,
                                movementDt);
                        }
                    }

                    if (predictedPlayer.ValueRW.ControllerState.JumpTriggered)
                    {
                        predictedPlayer.ValueRW.LastJumpTick = serverTick;
                        predictedPlayer.ValueRW.ControllerState.JumpTriggered = false;
                    }

                    if (predictedPlayer.ValueRW.ControllerState.LandTriggered)
                    {
                        predictedPlayer.ValueRW.LastLandTick = serverTick;
                        predictedPlayer.ValueRW.ControllerState.LandTriggered = false;
                    }
                }
            }

            foreach (var (transform,
                         predictedPlayer,
                         controllerConsts, entity)
                     in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRW<PredictedPlayerGhost>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate, PlayerControllerLink, GhostGameObjectLink>()
                         .WithNone<GhostGameObjectDeferredActivation>()
                         .WithEntityAccess())
            {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                if (math.lengthsq(predictedPlayer.ValueRO.AccumulatedMovement) > 0f)
                {
                    controllerLink.Controller.ApplyMovementUpdate(ref predictedPlayer.ValueRW.ControllerState,
                        controllerConsts.ValueRO.ControllerConsts,
                        predictedPlayer.ValueRO.AccumulatedMovement,
                        deltaTime);
                }

                predictedPlayer.ValueRW.AccumulatedMovement = float3.zero;
            }

            bool checkForPredictionErrors = PlayerPredictionSystem.CheckForPredictionErrors.IsEnabled;

            foreach (var (transform, predictedPlayer, controllerConsts, entity)
                     in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRW<PredictedPlayerGhost>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate, PlayerControllerLink, GhostGameObjectLink>()
                         .WithEntityAccess())
            {
                transform.ValueRW.Position = predictedPlayer.ValueRO.ControllerState.CurrentPosition;
                transform.ValueRW.Rotation = predictedPlayer.ValueRO.ControllerState.CurrentRotation;

                if (checkForPredictionErrors)
                {
                    if (MovementHistory.TryGetTick(serverTick, out var pos, out var rot))
                    {
                        // compare our calculation with the recorded
                        Debug.Assert(PlayerMovementHistory.HistoryMatches(pos,
                                rot,
                                predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                                predictedPlayer.ValueRO.ControllerState.CurrentRotation),
                            $"[ServerPlayerMovementSystem] Mispredict detected at tick {serverTick} predicted pos {predictedPlayer.ValueRO.ControllerState.CurrentPosition} recorded {(float3)pos}");
                    }
                    else
                    {
                        // this is the first time we've processed this tick
                        // let's add our result
                        MovementHistory.Add(serverTick, predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                            predictedPlayer.ValueRO.ControllerState.CurrentRotation);
                    }
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();

            s_PlayerMovementActive = false;
        }
    }
}