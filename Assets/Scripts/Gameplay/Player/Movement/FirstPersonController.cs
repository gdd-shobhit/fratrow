//#define DEBUG_RENDER_MOVEMENT

using System;
using System.Runtime.CompilerServices;
using Unity.FPSSample_2;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public SoundDef PlayerHitSFX;

    private static class AnimationParameters
    {
        //Speed, Strafe, Turn: float values
        //Verbs: triggers
        //Is-X: boolean values
        public static readonly int IsMoving = Animator.StringToHash("IsMoving"); //1P
        public static readonly int Speed = Animator.StringToHash("Speed"); //3P
        public static readonly int StrafeSpeed = Animator.StringToHash("SpeedStrafe"); //3P
        public static readonly int TurnSpeed = Animator.StringToHash("Turn"); //3P
        public static readonly int Shoot = Animator.StringToHash("Shoot");
        public static readonly int IsShooting = Animator.StringToHash("IsShooting");
        public static readonly int Reload = Animator.StringToHash("Reload");
        public static readonly int Jump = Animator.StringToHash("Jump");
        public static readonly int Fall = Animator.StringToHash("Fall");
        public static readonly int IsInAir = Animator.StringToHash("IsInAir");
        public static readonly int Land = Animator.StringToHash("Land");
        public static readonly int IsHit = Animator.StringToHash("IsHit");
        public static readonly int IsDead = Animator.StringToHash("IsDead"); //3P
    }

    private const float k_ResetMovementAdjustEpsilon = 1e-06f;

    public enum MovementType
    {
        Standing = 0,
        Jumping,
        Falling,
    }

    public struct ControllerState
    {
        public enum StateFlag
        {
            Jump = 1 << 0,
            Fall = 1 << 1,
            Land = 1 << 2,
            Shoot = 1 << 3,
            IsReloading = 1 << 4,
            IsHit = 1 << 5,
            JumpTrigger = 1 << 6,
            LandTrigger = 1 << 7
        }

        //WARNING WARNING: Adding more members to this struct might break network serialisation speak to Claire/Andy B

        // booleans
        public uint StateFlags;

        [GhostField(SendData = false)]
        public bool Jump
        {
            get => (StateFlags & (uint)StateFlag.Jump) != 0;
            set => SetFlag(StateFlag.Jump, value);
        }

        [GhostField(SendData = false)]
        public bool Fall
        {
            get => (StateFlags & (uint)StateFlag.Fall) != 0;
            set => SetFlag(StateFlag.Fall, value);
        }

        [GhostField(SendData = false)]
        public bool Land
        {
            get => (StateFlags & (uint)StateFlag.Land) != 0;
            set => SetFlag(StateFlag.Land, value);
        }

        [GhostField(SendData = false)]
        public bool Shoot
        {
            get => (StateFlags & (uint)StateFlag.Shoot) != 0;
            set => SetFlag(StateFlag.Shoot, value);
        }

        [GhostField(SendData = false)]
        public bool IsReloadingState
        {
            get => (StateFlags & (uint)StateFlag.IsReloading) != 0;
            set => SetFlag(StateFlag.IsReloading, value);
        }

        [GhostField(SendData = false)]
        public bool IsHit
        {
            get => (StateFlags & (uint)StateFlag.IsHit) != 0;
            set => SetFlag(StateFlag.IsHit, value);
        }

        [GhostField(SendData = false)]
        public bool JumpTriggered
        {
            get => (StateFlags & (uint)StateFlag.JumpTrigger) != 0;
            set => SetFlag(StateFlag.JumpTrigger, value);
        }

        [GhostField(SendData = false)]
        public bool LandTriggered
        {
            get => (StateFlags & (uint)StateFlag.LandTrigger) != 0;
            set => SetFlag(StateFlag.LandTrigger, value);
        }

        public quaternion CurrentRotation;
        public float3 CurrentPosition;
        public float3 GroundNormal;

        public float3 MovementRequest;
        public MovementType MovementType;
        public MovementType PreviousMovementType;
        public float TimeInState;

        public float YawDegrees;
        public float PitchDegrees;
        public float MovementSpeed;
        public float JumpFallSpeed;
        public float AnimatorTargetSpeed; // _animIDSpeed
        public float AnimatorTargetSpeedChangeRate;
        public float JumpTimeoutDelta;
        public float FallTimeoutDelta;
        public float FallHeight;

        public float RotationVelocity;

        public float3 AnimatorMotion;
        public float AnimatorMotionChangeRate;
        public float AnimatorSmoothedMotionX;
        public float AnimatorMotionSpeed; // _animIDMotionSpeed

        public float TeleportFreeze;

        //WARNING WARNING: Adding more members to this struct might break network serialisation speak to Claire/Andy B

        private void SetFlag(StateFlag flag, bool set)
        {
            if (set)
            {
                StateFlags |= (uint)flag;
            }
            else
            {
                StateFlags &= ~(uint)flag;
            }
        }

        public void Init(in float3 worldPosition, in quaternion worldRotation)
        {
            CurrentPosition = worldPosition;
            CurrentRotation = worldRotation;

            Quaternion rot = worldRotation;
            PitchDegrees = rot.eulerAngles.y;
        }
    }

    public struct ControllerConsts
    {
        public struct StateConsts
        {
            public float Speed;
            public float SpeedChangeRate;
            public float RotationSmoothTime;
            public float LandingSpeedMult;
            public float AnimationMotionScale;
        }

        public StateConsts Walk;
        public StateConsts Sprint;

        public float JumpHeight;
        public float Gravity;
        public float StandingFallSpeed;
        public float JumpTimeout;
        public float FallTimeout;
        public float LandingTimeout;
        public float LandingAnimTimeout;
        public float StateChangeSafetyTimeout;
        public float GroundedOffset;
        public LayerMask GroundLayers;
        public float TerminalVelocity;
    }

    [field: Header("Cinemachine")]
    [field: SerializeField,
            Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
    public GameObject CinemachineCameraTarget { get; private set; }

    [SerializeField, Tooltip("A small offset for the grounded spherecast. Should be a small positive value.")]
    private float GroundedOffset = 0.2f;

    public PhysicsMaterial GroundPhysicsMaterial { get; private set; }

    [field: SerializeField] public Vector3 ControllerOffset { get; private set; } = new Vector3(0f, 0f, 0f);

    [SerializeField] private Animator m_Animator_1P;
    [SerializeField] private Animator m_Animator_3P;
    [SerializeField] private bool m_EnableAnimationLogging = false;
    private DamageVisualsController m_DamageVisualsController;
    private uint _lastProcessedHitTick = 0;
    private uint _lastAnimatedShotTick = 0;
    private uint _lastAnimatedJumpTick = 0;
    private uint _lastAnimatedLandTick = 0;
    private uint _lastAnimatedReloadTick = 0;

    private CharacterController m_Controller;
    public CharacterController CharacterController => m_Controller;

    private PlayerGhost m_PlayerGhost;
    private PlayerGhost PlayerGhost => m_PlayerGhost;

    private const int k_NumPhysicsResults = 8;
    private readonly RaycastHit[] m_GroundCheckRaycastResults = new RaycastHit[k_NumPhysicsResults];

    private const float k_DefaultSpeedChange = 20f;

#if DEBUG_RENDER_MOVEMENT || DEBUG_RENDER_CLIMBING_MOVEMENT
    private const float k_DebugRenderingTimeout = 5f;
#endif

    private static readonly float3 k_UpVector = math.up();
    private static readonly float3 k_ForwardVector = math.forward();

    public float CachedJumpFallSpeed { get; private set; }
    public float CachedFallHeight { get; private set; }

#if UNITY_EDITOR || DEBUG
    private MovementType m_PrevMovementType;
#endif

    private float footstepTriggerTimer = 0;
    private float footstepStartTimer = 0;

    private void Awake()
    {
        TryGetComponent(out m_Controller);
        Debug.Assert(m_Controller, "[THIRDPERSONCONTROLLER] Player has no CharacterController component");

        TryGetComponent(out m_PlayerGhost);
        Debug.Assert(m_PlayerGhost, "[THIRDPERSONCONTROLLER] Player has no PlayerGhost component");

        TryGetComponent(out m_DamageVisualsController);
        Debug.Assert(m_DamageVisualsController,
            "[FIRSTPERSONCONTROLLER] Player has no DamageVisualsController component");
    }

    public void SetExcludeLayers(LayerMask excludeLayers)
    {
        m_Controller.excludeLayers = excludeLayers;
    }

    public void ApplyMovementUpdate(ref ControllerState state, in ControllerConsts consts,
        in float3 accumulatedMovement, float deltaTime)
    {
        ApplyMove(ref state, consts, accumulatedMovement, deltaTime);
        GroundedCheck(ref state, consts);

        // cache latest values for access outside of entity data
        CachedJumpFallSpeed = state.JumpFallSpeed;
        CachedFallHeight = state.FallHeight;
    }

    private static void SetMovementType(ref ControllerState state, MovementType type)
    {
        if (state.MovementType != type)
        {
#if ENABLE_MOVEMENT_DIAGNOSTICS
            Debug.Log($"SetMovementType to {(int)type} (from {(int)state.MovementType})");
#endif

            bool wasUpdatingFallHeight = ShouldUpdateFallHeight(state.MovementType);

            state.PreviousMovementType = state.MovementType;
            state.MovementType = type;
            state.TimeInState = 0;

            if (!wasUpdatingFallHeight && ShouldUpdateFallHeight(type))
            {
                state.FallHeight = 0f;
                state.FallTimeoutDelta = float.MaxValue;
                state.Fall = true;
            }

#if DEBUG_RENDER_MOVEMENT
            Debug.DrawLine(state.CurrentPosition - new float3(0.2f, 0f, 0f), 
                    state.CurrentPosition + new float3(0.2f, 0f, 0f), 
                    GetDebugColour(state.MovementType), k_DebugRenderingTimeout);
            Debug.DrawLine(state.CurrentPosition - new float3(0f, 0.2f, 0f), 
                    state.CurrentPosition + new float3(0f, 0.2f, 0f), 
                    GetDebugColour(state.PreviousMovementType), k_DebugRenderingTimeout);
#endif
        }
    }

    private static bool ShouldUpdateFallHeight(MovementType movementType)
    {
        return movementType == MovementType.Falling;
    }

    private struct GroundCollisionVariables
    {
        private RaycastHit m_ClosestHit;
        private RaycastHit m_FlattestHit;

        private readonly float m_FlattestHitDot;

        public Vector3 FlattestHitPoint => m_FlattestHit.point;
        public Vector3 FlattestHitNormal => m_FlattestHit.normal;
        public PhysicsMaterial ClosestHitSurfaceType => m_ClosestHit.collider.sharedMaterial;

        public GroundCollisionVariables(RaycastHit closestHit, RaycastHit flattestHit, float flattestHitDot)
        {
            m_ClosestHit = closestHit;
            m_FlattestHit = flattestHit;
            m_FlattestHitDot = flattestHitDot;
        }
    }

    private bool ShouldUpdateGround(MovementType movementType)
    {
        return movementType != MovementType.Jumping;
    }

    private static Vector3 GetGroundRaycastOrigin(in ControllerState state, in CharacterController controller)
    {
        var origin = state.CurrentPosition;
        var delta = origin - (float3)controller.bounds.center;
        delta.y = 0f;

        origin -= delta;

        return origin;
    }

    private bool UpdateGround(in ControllerState state, in ControllerConsts consts,
        out GroundCollisionVariables groundCollision)
    {
        if (ShouldUpdateGround(state.MovementType))
        {
            var currentPos = GetGroundRaycastOrigin(state, m_Controller);
            // set sphere position, with offset
            var controllerCentre = transform.rotation * ControllerOffset;
            var testRadius = m_Controller.radius;
            var testStart = new Vector3(currentPos.x + controllerCentre.x, currentPos.y + +controllerCentre.y,
                currentPos.z + controllerCentre.z);
            var numHits = Physics.SphereCastNonAlloc(testStart, testRadius, Vector3.down,
                m_GroundCheckRaycastResults, GroundedOffset, consts.GroundLayers, QueryTriggerInteraction.Ignore);


            // Choose the best hit
            float largestDot = float.MinValue;
            float closestDistSq = float.MaxValue;
            int flattestHitIndex = -1;
            int closestHitIndex = -1;

            for (int i = 0; i < numHits; ++i)
            {
                var raycastResult = m_GroundCheckRaycastResults[i];

                if (GhostGameObject.TryFindGhostGameObject(raycastResult.collider.gameObject, out var ghost) &&
                    !GhostGameObject.BroadClientServerRolesMatch(ghost.Role, PlayerGhost.Role))
                {
                    // not a valid ground for this player (server hitting client object, or vice versa)
                    continue;
                }
#if DEBUG_RENDER_MOVEMENT
                    Debug.DrawLine(raycastResult.point, raycastResult.point + raycastResult.normal, Color.red, k_DebugRenderingTimeout);
#endif

                float dot = math.dot(raycastResult.normal, k_UpVector);

                if (dot > largestDot) //select the most upright normal to avoid issues with corner collisions looking like a slope
                {
                    largestDot = dot;
                    flattestHitIndex = i;
                }

                float distSq = raycastResult.point.sqrMagnitude > 0f
                    ? math.distancesq(raycastResult.point, currentPos)
                    : 0f; //point will be (0,0,0) if the spherecast starts inside the collider

                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closestHitIndex = i;
                }
            }

            // Did we discard all of the collisions?
            if (flattestHitIndex >= 0)
            {
                Debug.Assert(closestHitIndex >= 0,
                    "[THIRDPERSONCONTROLLER] flattest hit is valid but closest hit isn't, this shouldn't be possible!");

                RaycastHit flattest = m_GroundCheckRaycastResults[flattestHitIndex];
                RaycastHit closest = m_GroundCheckRaycastResults[closestHitIndex];

                groundCollision = new GroundCollisionVariables(closestHit: closest, flattestHit: flattest,
                    flattestHitDot: largestDot);
                return true;
            }
        }

        groundCollision = new GroundCollisionVariables();
        return false;
    }

    public void UpdateGround(ref ControllerState state, in ControllerConsts consts)
    {
        if (UpdateGround(state, consts, out var groundCollision))
        {
            GroundPhysicsMaterial = groundCollision.ClosestHitSurfaceType;
            state.GroundNormal = groundCollision.FlattestHitNormal;
        }
        else
        {
            GroundPhysicsMaterial = null;
            state.GroundNormal = k_UpVector;
        }
    }

    public void GroundedCheck(ref ControllerState state, in ControllerConsts consts)
    {
        bool isGrounded = UpdateGround(state, consts, out var groundCollision);

        if (isGrounded && state.MovementType == MovementType.Falling)
        {
            state.JumpTimeoutDelta = consts.LandingTimeout;
            state.Land = true;
            state.LandTriggered = true;
            state.Jump = false;
            SetMovementType(ref state, MovementType.Standing);

            bool isClientOwned = (m_PlayerGhost.Role == MultiplayerRole.ClientOwned);
            if (isClientOwned)
            {
                Unity.FPSSample_2.EventHandler eventHandler = m_Animator_1P.GetComponent<Unity.FPSSample_2.EventHandler>();
                eventHandler.onFootDown = true;
            }
        }
        else if (!isGrounded && state.MovementType == MovementType.Standing)
        {
            SetMovementType(ref state, MovementType.Falling);
        }
        else if (isGrounded && state.MovementType == MovementType.Standing)
        {
            state.Land = false;
        }

        if (isGrounded)
        {
            state.GroundNormal = groundCollision.FlattestHitNormal;
            GroundPhysicsMaterial = groundCollision.ClosestHitSurfaceType;
        }
        else
        {
            state.GroundNormal = k_UpVector;
            GroundPhysicsMaterial = null;
        }
    }

    public void ApplyInterpolatedClientState(ref ControllerState state,
        in ControllerConsts consts, in LocalTransform localTransform, float deltaTime, bool applyAnimation = false)
    {
        // apply smoothed rotation
        state.CurrentPosition = localTransform.Position;
        state.CurrentRotation = localTransform.Rotation;

        transform.SetPositionAndRotation(state.CurrentPosition, state.CurrentRotation);
    }

    public void ApplyAnimatorState(in ControllerState state, in ControllerConsts consts, float deltaTime)
    {
        if (m_PlayerGhost == null || m_PlayerGhost.GhostGameObject == null)
        {
            return; //Return if the ghost has not been linked
        }

        var predictedPlayerGhost = m_PlayerGhost.GhostGameObject.ReadGhostComponentData<PredictedPlayerGhost>();
        bool isClientOwned = (m_PlayerGhost.Role == MultiplayerRole.ClientOwned);
        var animator = isClientOwned ? m_Animator_1P : m_Animator_3P;

        if (animator == null)
            return;

        // Handle one-shot events first
        HandleAnimationEvents(animator, predictedPlayerGhost);

        // Then handle continuous state parameters
        if (isClientOwned)
        {
            ApplyFirstPersonMovementAnimation(animator, state);
        }
        else
        {
            ApplyThirdPersonMovementAnimation(animator, state, predictedPlayerGhost.CurrentHealth);
        }
    }

    private void HandleAnimationEvents(Animator animator, in PredictedPlayerGhost ghostState)
    {
        // Shooting
        if (ghostState.LastShotTick > _lastAnimatedShotTick)
        {
            if (!ghostState.ControllerState.IsReloadingState && !animator.GetBool(AnimationParameters.IsShooting))
            {
                if (m_EnableAnimationLogging)
                {
                    Debug.Log($"[ANIMATION] Firing SHOOT trigger at Tick: {ghostState.LastShotTick.ToString()}");
                }

                animator.SetTrigger(AnimationParameters.Shoot);
            }

            _lastAnimatedShotTick = ghostState.LastShotTick;
        }

        // Reloading
        if (ghostState.LastReloadTick > _lastAnimatedReloadTick)
        {
            if (m_EnableAnimationLogging)
            {
                Debug.Log($"[ANIMATION] Firing RELOAD trigger at Tick: {ghostState.LastReloadTick.ToString()}");
            }

            animator.SetTrigger(AnimationParameters.Reload);


            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghostState.EquippedWeaponID);
            if (weaponData != null && weaponData.WeaponReloadSfx != null)
            {
                Unity.FPSSample_2.EventHandler eventHandler = animator.GetComponent<Unity.FPSSample_2.EventHandler>();
                eventHandler.reloadSFX = weaponData.WeaponReloadSfx;
            }

            _lastAnimatedReloadTick = ghostState.LastReloadTick;
        }

        // Jumping
        if (ghostState.LastJumpTick > _lastAnimatedJumpTick)
        {
            if (m_EnableAnimationLogging)
            {
                Debug.Log($"[ANIMATION] Firing JUMP trigger at Tick: {ghostState.LastJumpTick.ToString()}");
            }

            animator.SetTrigger(AnimationParameters.Jump);
            _lastAnimatedJumpTick = ghostState.LastJumpTick;
        }

        // Landing
        if (ghostState.LastLandTick > _lastAnimatedLandTick)
        {
            if (m_EnableAnimationLogging)
            {
                Debug.Log($"[ANIMATION] Firing LAND trigger at Tick: {ghostState.LastLandTick.ToString()}");
            }

            animator.SetTrigger(AnimationParameters.Land);
            _lastAnimatedLandTick = ghostState.LastLandTick;
        }

        // Hit Reaction
        if (ghostState.LastHitTick > _lastProcessedHitTick)
        {
            if (m_PlayerGhost.Role == MultiplayerRole.ClientOwned)
            {
                if (m_EnableAnimationLogging)
                {
                    Debug.Log($"[ANIMATION] Firing 1P HIT trigger at Tick: {ghostState.LastHitTick.ToString()}");
                }

                // Handle the 1P visual-only damage vignette
                if (m_DamageVisualsController != null)
                {
                    m_DamageVisualsController.TriggerDamageEffect();
                }
            }
            else
            {
                if (m_EnableAnimationLogging)
                {
                    Debug.Log($"[ANIMATION] Firing 3P HIT trigger at Tick: {ghostState.LastHitTick.ToString()}");
                }

                // Handle the 3P hit animation
                animator.SetTrigger(AnimationParameters.IsHit);
            }

            if (PlayerHitSFX != null)
            {
                GameManager.Instance.SoundSystem.CreateEmitter(PlayerHitSFX, transform.position);
            }

            _lastProcessedHitTick = ghostState.LastHitTick;
        }
    }

    private void ApplyFirstPersonMovementAnimation(Animator animator, in ControllerState state)
    {
        float targetIsMovingValue = (state.AnimatorTargetSpeed != 0.0f) ? 1.0f : 0.0f;
        animator.SetFloat(AnimationParameters.IsMoving, targetIsMovingValue);
    }

    private void ApplyThirdPersonMovementAnimation(Animator animator, in ControllerState state, float currentHealth)
    {
        float3 relativeSpeed = math.mul(math.inverse(state.CurrentRotation), state.MovementRequest);
        animator.SetFloat(AnimationParameters.Speed, relativeSpeed.z); // Use raw value for blend trees
        animator.SetFloat(AnimationParameters.StrafeSpeed, relativeSpeed.x);

        float speedZ = (math.abs(relativeSpeed.z) < 0.01f) ? 0.0f : math.sign(relativeSpeed.z);
        float speedX = (math.abs(relativeSpeed.x) < 0.01f) ? 0.0f : math.sign(relativeSpeed.x);
        animator.SetFloat(AnimationParameters.Speed, speedZ);
        animator.SetFloat(AnimationParameters.StrafeSpeed, speedX);

        bool isDead = (currentHealth <= 0);
        animator.SetBool(AnimationParameters.IsDead, isDead);
    }

    private void ApplyPosImmediate(in ControllerState state)
    {
        if (m_Controller.enabled)
        {
            m_Controller.enabled = false;
            transform.position = state.CurrentPosition;
            m_Controller.enabled = true;

#if UNITY_EDITOR || DEBUG
            float deltaSqrd = math.distancesq(state.CurrentPosition, transform.position);
            Debug.Assert(deltaSqrd <= k_ResetMovementAdjustEpsilon,
                $"ApplyPosImmediate has failed to move the controller to the correct location (target {state.CurrentPosition} actual {(float3)transform.position} delta {math.sqrt(deltaSqrd)} {state.CurrentPosition - (float3)transform.position} state {state.MovementType} prev {m_PrevMovementType})");
#endif
        }
        else
        {
            // set directly, no physics required
            transform.position = state.CurrentPosition;
        }
    }

    public void ApplyPosRotImmediate(in ControllerState state)
    {
        ApplyPosImmediate(state);
        transform.rotation = state.CurrentRotation;
    }

    private static void GetStateConsts(out ControllerConsts.StateConsts stateConsts, ref ControllerState state,
        in PlayerInput input, in ControllerConsts consts, float deltaTime)
    {
        stateConsts.Speed = 0f;
        stateConsts.RotationSmoothTime = 0f;
        stateConsts.SpeedChangeRate = k_DefaultSpeedChange; //just a large number to make it snap to the 0 speed
        stateConsts.LandingSpeedMult = 1f;
        stateConsts.AnimationMotionScale = 0.4f;

        // Freeze after teleport for a second
        if (state.TeleportFreeze > 0f)
        {
            state.TeleportFreeze -= deltaTime;
        }
        else
        {
            switch (state.MovementType)
            {
                case MovementType.Standing:
                case MovementType.Jumping:
                case MovementType.Falling:
                    stateConsts = consts.Walk;
                    break;
                default:
                    Debug.LogError(
                        $"[THIRDPERSONCONTROLLER] GetStateConsts : Unhandled state {state.MovementType.ToString()}");
                    break;
            }
        }
    }

    private static float3 CalculateMovementFromInput(ref ControllerState state, in ControllerConsts consts,
        in ControllerConsts.StateConsts stateConsts, in PlayerInput input, bool updateRotation, float deltaTime)
    {
        if (updateRotation)
        {
            state.YawDegrees = input.LookYawPitchDegrees.x;
            state.PitchDegrees = input.LookYawPitchDegrees.y;
            state.CurrentRotation = quaternion.RotateY(math.radians(state.YawDegrees));
        }

        var rotQuat = state.CurrentRotation; // Use the rotation already calculated above
        var localMove = new float3(input.MoveInput.x, 0f, input.MoveInput.y);

        // Normalize it to get a pure direction vector with a length of 1.
        // This is the crucial step.
        var localDir = math.normalizesafe(localMove);

        // Rotate the pure direction by the character's facing rotation.
        var worldDir = math.mul(rotQuat, localDir);

        // Multiply the pure direction by the final speed calculated in AccumulateMovement.
        return worldDir * state.MovementSpeed;
    }

    private static void AddMovementFromJumpFall(ref float3 moveDelta, in ControllerState state)
    {
        moveDelta.y += state.JumpFallSpeed;
    }

    public static void ProcessInputs(ref ControllerState state, in PlayerInput input, float deltaTime)
    {
    }

    public static void AccumulateMovement(ref ControllerState state,
        ref float3 accumulatedMovement, in PlayerInput input, in ControllerConsts consts, float deltaTime)
    {
        state.TimeInState += deltaTime;

        AccumulateJumpAndGravity(ref state, input, consts, deltaTime);

        GetStateConsts(out var stateConsts, ref state, in input, in consts, deltaTime);

        var updateRotation = true;

        float combinedMoveSpeedModifier = 1f;

        float modifiedTargetMoveSpeed =
            stateConsts.Speed * combinedMoveSpeedModifier; //don't apply modifiers to the aiming speed

        float inputMagnitude = math.length(input.MoveInput);

        // apply analog deadzone
        inputMagnitude = inputMagnitude >= 0.4f ? 1f : 0f;

        float applyTargetSpeed = modifiedTargetMoveSpeed * inputMagnitude;
        float blendAlpha = deltaTime * stateConsts.SpeedChangeRate;

        state.MovementSpeed = applyTargetSpeed;
        state.AnimatorTargetSpeedChangeRate = stateConsts.SpeedChangeRate;
        state.AnimatorTargetSpeed = stateConsts.Speed * inputMagnitude;
        state.AnimatorMotionSpeed = inputMagnitude > 0f ? state.MovementSpeed : 1f; //play the idle at 1x

        state.AnimatorMotion = float3.zero;
        state.AnimatorMotionChangeRate = 0f;

        var moveDelta = float3.zero;
        updateRotation &= applyTargetSpeed > 0f;

        switch (state.MovementType)
        {
            case MovementType.Standing:
                {
                    moveDelta = CalculateMovementFromInput(ref state, consts, stateConsts, input, true, deltaTime);
                    AddMovementFromJumpFall(ref moveDelta, state);
                }
                break;

            case MovementType.Jumping:
            case MovementType.Falling:
                {
                    moveDelta = CalculateMovementFromInput(ref state, consts, stateConsts, input, true, deltaTime);
                    AddMovementFromJumpFall(ref moveDelta, state);
                }
                break;

            default:
                {
                    Debug.LogError(
                        $"[THIRDPERSONCONTROLLER] AccumulateMovement : Unhandled state {state.MovementType.ToString()}");
                }
                break;
        }

        accumulatedMovement += moveDelta * deltaTime;
        state.MovementRequest = accumulatedMovement;

#if DEBUG_RENDER_MOVEMENT
        Debug.DrawLine(state.CurrentPosition, state.CurrentPosition + accumulatedMovement, GetDebugColour(state.MovementType), k_DebugRenderingTimeout);
#endif
    }

    private void ApplyMove(ref ControllerState state, in ControllerConsts consts, in float3 accumulatedMovement,
        float deltaTime)
    {
        var posDelta = (Vector3)state.CurrentPosition - transform.position;
        if (posDelta.sqrMagnitude > 0f)
        {
            MovementLog($"{name} - Reset Teleport to {state.CurrentPosition} (from {(float3)transform.position})");
            ApplyPosImmediate(state);
        }

        bool allowMove = math.lengthsq(accumulatedMovement) > 0f;
        if (allowMove)
        {
            // apply movement
            var movementToApply = accumulatedMovement;

            // This can be called before the controller is enabled (e.g. on spawn)
            // prevents errors of moving when controller is disabled or GameObject inactive
            if (m_Controller != null && m_Controller.enabled && gameObject.activeInHierarchy)
            {
                // just apply our move as normal
                m_Controller.Move(movementToApply);
            }
        }

        // apply rotation
        transform.rotation = state.CurrentRotation;

        if (ShouldUpdateFallHeight(state.MovementType))
        {
            float fallDist = state.CurrentPosition.y - transform.position.y;
            state.FallHeight += fallDist;
        }

        HandleFirstPersonFootstepSFX(state);

        // store position
        state.CurrentPosition = transform.position;

#if UNITY_EDITOR || DEBUG
        m_PrevMovementType = state.MovementType;
#endif

#if ENABLE_MOVEMENT_DIAGNOSTICS
        if (ServerPlayerMovementSystem.PlayerMovementActive)
        {
            MovementLog($"{name} - server tick {ServerPlayerMovementSystem.PlayerMovementTick} with position {state.CurrentPosition}");
        }
        else
        {
            MovementLog($"{name} - prediction tick {PlayerPredictionSystem.PlayerMovementTick} with position {state.CurrentPosition}");
        }
#endif
    }

#if ENABLE_MOVEMENT_DIAGNOSTICS
    public void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Debug.Log($"[{UnityEngine.Time.frameCount}] OnControllerColliderHit hit {hit.collider.name} ghost = {(GhostGameObject.TryFindGhostGameObject(hit.gameObject, out var ghost) ? ghost.name : "null")}");
    }

#endif

    private void HandleFirstPersonFootstepSFX(ControllerState state)
    {
        if (state.MovementType == MovementType.Standing && m_Controller != null && m_Controller.enabled && gameObject.activeInHierarchy)
        {
            bool isClientOwned = (m_PlayerGhost.Role == MultiplayerRole.ClientOwned);
            if (isClientOwned)
            {
                if (transform.position.x == state.CurrentPosition.x && transform.position.z == state.CurrentPosition.z)
                {
                    footstepTriggerTimer = 0;
                }
                else
                {
                    if (footstepTriggerTimer == 0)
                    {
                        footstepTriggerTimer = Time.time;  // This is called multiple times per update. So I can't use Time.delta
                        footstepStartTimer = footstepTriggerTimer;
                    }
                    footstepTriggerTimer = Time.time;

                    float t = footstepTriggerTimer - footstepStartTimer;
                    if (t >= 0.5f)
                    {
                        footstepStartTimer += 0.5f;
                        Unity.FPSSample_2.EventHandler eventHandler = m_Animator_1P.GetComponent<Unity.FPSSample_2.EventHandler>();
                        eventHandler.onFootDown = true;
                    }
                }
            }
        }
    }



    private static void AccumulateJumpAndGravity(ref ControllerState state, in PlayerInput input,
        in ControllerConsts consts, float deltaTime)
    {
        switch (state.MovementType)
        {
            case MovementType.Standing:
                {
                    ClearFallingState(ref state);

                    if (AccumulateJump(ref state, in input, in consts, deltaTime) ||
                        state.TimeInState < consts.LandingTimeout)
                    {
                        //we've just started jumping or we're in the process of landing so apply gravity
                        AccumulateGravity(ref state, in consts, deltaTime);
                    }
                    else
                    {
                        //fully in standing state reset gravity to a minimal fall speed to keep player aligned on ground (necessary for uneven terrain)
                        state.JumpFallSpeed = consts.StandingFallSpeed;
                    }
                }
                break;

            case MovementType.Falling:
            case MovementType.Jumping:
                {
                    state.Jump = true;
                    state.JumpTimeoutDelta = math.max(consts.JumpTimeout, state.JumpTimeoutDelta);

                    // fall timeout
                    if (state.FallTimeoutDelta < consts.FallTimeout)
                    {
                        state.FallTimeoutDelta += deltaTime;
                    }
                    else
                    {
                        state.Fall = true;
                    }

                    AccumulateGravity(ref state, in consts, deltaTime);

                    var newMovementState = state.JumpFallSpeed >= 0f ? MovementType.Jumping : MovementType.Falling;
                    SetMovementType(ref state, newMovementState);
                }
                break;

            default:
                {
                    Debug.LogError(
                        $"[THIRDPERSONCONTROLLER] AccumulateJumpAndGravity : Unhandled state {state.MovementType.ToString()}");
                }
                break;
        }
    }

    private static void ClearFallingState(ref ControllerState state)
    {
        // reset the fall timeout timer
        state.FallTimeoutDelta = 0f;

        // update animator if using character
        state.Jump = false;
        state.Fall = false;
    }

    private static void AccumulateGravity(ref ControllerState state, in ControllerConsts consts, float deltaTime)
    {
        // apply gravity over time if we haven't reached terminal (multiply by delta time twice to linearly speed up over time)
        var terminalVelocity = consts.TerminalVelocity;

        if (state.JumpFallSpeed > terminalVelocity)
        {
            state.JumpFallSpeed += consts.Gravity * deltaTime;
        }

        // Handle the terminal velocity becoming much smaller due to umbrella opening
        else if (state.JumpFallSpeed <= terminalVelocity)
        {
            state.JumpFallSpeed = Mathf.Lerp(state.JumpFallSpeed, terminalVelocity, deltaTime);
        }
    }

    public async void SpawnPredictedProjectile(uint spawnTick, uint weaponId, Vector3 spawnPosition,
        Quaternion spawnRotation)
    {
        try
        {
            var alreadyExists = false;
            for (var i = 0; i < Projectile.PredictedProjectiles.Count; i++)
            {
                var proj = Projectile.PredictedProjectiles[i];
                if (proj.SpawnTick == spawnTick)
                {
                    alreadyExists = true;
                    break;
                }
            }

            if (alreadyExists)
            {
                return;
            }

            var playerGhost = GetComponent<PlayerGhost>();
            var projectilePrefabRef = playerGhost.ProjectilePrefabAR;

            if (!projectilePrefabRef.RuntimeKeyIsValid() || playerGhost.CameraTarget == null)
            {
                Debug.LogWarning(
                    "[CLIENT] Cannot spawn predicted projectile as prefab ref or camera target is null");
                return;
            }

            // var spawnPosition = playerGhost.CameraTarget.position;
            // var spawnRotation = playerGhost.CameraTarget.rotation;
            //
            var predictedProjectile = await projectilePrefabRef.InstantiateAsync(spawnPosition, spawnRotation).Task;
            if (predictedProjectile == null)
            {
                Debug.LogWarning("[CLIENT] Failed to instantiate predicted projectile");
                return;
            }

            predictedProjectile.transform.parent =
                GhostBridgeBootstrap.Instance.ClientGameObjectHierarchy.transform;

            var projectile = predictedProjectile.GetComponent<Projectile>();
            projectile.SetWeaponId(weaponId);

            var projectileInfo = new Projectile.PredictedProjectileInfo
            {
                Instance = predictedProjectile,
                SpawnTick = spawnTick
            };

            Projectile.PredictedProjectiles.Add(projectileInfo);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private static bool AccumulateJump(ref ControllerState state, in PlayerInput input, in ControllerConsts consts,
        float deltaTime)
    {
        bool jumped = false;

        if (input.Jump
            && state.JumpTimeoutDelta <= 0f)
        {
            // the square root of H * -2 * G = how much velocity needed to reach desired height
            state.JumpFallSpeed = math.sqrt(consts.JumpHeight * -2f * consts.Gravity); // 6
            SetMovementType(ref state, MovementType.Jumping);
            state.Jump = true;
            state.JumpTriggered = true;
            jumped = true;

#if DEBUG_RENDER_MOVEMENT
            Debug.DrawLine(state.CurrentPosition - new float3(0.2f, 0f, 0f), state.CurrentPosition + new float3(0.2f, 0f, 0f), Color.black, k_DebugRenderingTimeout);
            Debug.DrawLine(state.CurrentPosition - new float3(0f, 0f, 0.2f), state.CurrentPosition + new float3(0f, 0f, 0.2f), Color.black, k_DebugRenderingTimeout);
#endif
        }

        if (state.JumpTimeoutDelta > 0f)
        {
            state.JumpTimeoutDelta -= deltaTime;
        }

        return jumped;
    }

#if DEBUG_RENDER_MOVEMENT
    private static Color GetDebugColour(MovementType movement)
    {
        var colour = Color.white;

        switch (movement)
        {
            case MovementType.Standing:
                colour = Color.green;
                break;
            case MovementType.Jumping:
                colour = Color.cyan;
                break;
            case MovementType.Falling:
                colour = Color.blue;
                break;
            case MovementType.Sliding:
                colour = Color.yellow;
                break;
            case MovementType.Stationary:
                colour = Color.red;
                break;
            case MovementType.Swimming:
                colour = Color.magenta;
                break;
        }

        return colour;
    }

#endif

    // taken from DOTSSample MathHelper
    // Collection of converted classic Unity (Mathf, Vector3 etc.) + some homegrown math functions using Unity.Mathematics
    // These are made/converted for production and unlike a proper library they are lacking any tests, so use at your own peril!
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothDampAngle(
        float current,
        float target,
        ref float currentVelocity,
        float smoothTime,
        float maxSpeed,
        float deltaTime)
    {
        target = current + DeltaAngle(current, target);
        float result = SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        result = Repeat360(result);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothDamp(
        float current,
        float target,
        ref float currentVelocity,
        float smoothTime,
        float maxSpeed,
        float deltaTime)
    {
        // Based on Game Programming Gems 4 Chapter 1.10
        smoothTime = math.max(0.0001F, smoothTime);
        float omega = 2F / smoothTime;

        float x = omega * deltaTime;
        float exp = 1F / (1F + x + (0.48F * x * x) + (0.235F * x * x * x));
        float change = current - target;
        float originalTo = target;

        // Clamp maximum speed
        float maxChange = maxSpeed * smoothTime;
        change = math.clamp(change, -maxChange, maxChange);
        target = current - change;

        float temp = (currentVelocity + (omega * change)) * deltaTime;
        currentVelocity = (currentVelocity - (omega * temp)) * exp;
        float result = target + ((change + temp) * exp);

        // Prevent overshooting
        if ((originalTo - current > 0.0F) == (result > originalTo))
        {
            result = originalTo;
            currentVelocity = (result - originalTo) / deltaTime;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DeltaAngle(float current, float target)
    {
        float delta = Repeat360(target - current);

        if (delta > 180.0F)
        {
            delta -= 360.0F;
        }

        float result = delta;

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Repeat360(float t)
    {
        const float repeat_length = 360f;
        const float inverse_length = 1 / repeat_length;

        return math.clamp(t - (math.floor(t * inverse_length) * repeat_length), 0.0f, repeat_length);
    }

    [System.Diagnostics.Conditional("ENABLE_MOVEMENT_DIAGNOSTICS")]
    public static void MovementLog(string message)
    {
        Debug.Log($"[{UnityEngine.Time.frameCount}] {message}");
    }
}