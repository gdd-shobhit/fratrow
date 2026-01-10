using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public class PredictedPlayerControllerConstsAuthoring : MonoBehaviour
{
    [field: Header("Player Movement Speeds")]
    [field: SerializeField, Tooltip("Walk speed of the character in m/s")]
    public float WalkSpeed { get; private set; } = 2.35f;

    [field: SerializeField, Tooltip("Sprint speed of the character in m/s")]
    public float SprintSpeed { get; private set; } = 4.7f;

    [field: Header("Player Rotation Smoothing Times")]
    [field: SerializeField, Tooltip("How fast the character turns to face movement direction while walking")]
    public float WalkRotationSmoothTime { get; private set; } = 0.2f;

    [field: SerializeField] public float WalkAnimationMotionScale { get; private set; } = 0.4f;

    [field: SerializeField, Tooltip("How fast the character turns to face movement direction while sprinting")]
    public float SprintRotationSmoothTime { get; private set; } = 0f;

    [field: Header("Player Speed Change Rates")]
    [field: SerializeField, Tooltip("Acceleration and deceleration while walking")]
    public float WalkSpeedChangeRate { get; private set; } = 10.0f;

    [field: SerializeField, Tooltip("Acceleration and deceleration while sprinting")]
    public float SprintSpeedChangeRate { get; private set; } = 10f;

    [field: SerializeField, Tooltip("Multiplier to player target speed during the landing animation timeout when sprinting")]
    public float SprintLandingSpeedMultiplier { get; private set; } = 0.6f;

    [field: SerializeField] public float SprintAnimationMotionScale { get; private set; } = 0.1f;

    [field: Space(10)]
    [field: Header("Player Jumping and Gravity")]
    [field: SerializeField, Tooltip("The height the player can jump")]
    public float JumpHeight { get; private set; } = 1.2f;

    [field: SerializeField, Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
    public float Gravity { get; private set; } = -15.0f;

    [field: SerializeField]
    public float TerminalVelocity { get; private set; } = -53.0f;

    [field: SerializeField, Tooltip("The fall speed applied to the player when in the standing state, to ensure player aligns with the ground on uneven terrain")]
    public float StandingFallSpeed { get; private set; } = -1.25f;

    [field: Space(10)]
    [field: Header("Player State Timeouts")]
    [field: SerializeField, Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
    public float JumpTimeout { get; private set; } = 0.50f;

    [field: SerializeField, Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
    public float FallTimeout { get; private set; } = 0.15f;

    [field: SerializeField, Tooltip("Buffer time to continue applying gravity while in standing state, ensures player fully settles on the ground")]
    public float LandingTimeout { get; private set; } = 0.15f;

    [field: SerializeField, Tooltip("Length of land animation for applying the landing speed multiplier and blocking turns")]
    public float LandingAnimTimeout { get; private set; } = 0.4f;

    [field: SerializeField, Tooltip("Time required to pass between movement state changes before batching is re-enabled")]
    public float StateChangeSafetyTimeout { get; private set; } = 0.3f;

    [field: Space(10)]
    [field: Header("Player Grounded")]
    [field: SerializeField, Tooltip("Additional offset for the grounded spherecast")]
    public float GroundedOffset { get; private set; } = -0.14f;

    [field: SerializeField, Tooltip("What layers the character uses as ground")]
    public LayerMask GroundLayers { get; private set; }
}

public class PredictedPlayerControllerConstsBaker : Baker<PredictedPlayerControllerConstsAuthoring>
{
    public override void Bake(PredictedPlayerControllerConstsAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new PredictedPlayerControllerConsts
        {
            ControllerConsts = new FirstPersonController.ControllerConsts
            {
                JumpHeight = authoring.JumpHeight,
                Gravity = authoring.Gravity,
                StandingFallSpeed = authoring.StandingFallSpeed,
                JumpTimeout = math.max(authoring.JumpTimeout, math.EPSILON), //set a minimum delay to force the jump timeout to wait 1 frame
                FallTimeout = authoring.FallTimeout,
                LandingTimeout = authoring.LandingTimeout,
                LandingAnimTimeout = authoring.LandingAnimTimeout,
                StateChangeSafetyTimeout = authoring.StateChangeSafetyTimeout,
                GroundedOffset = authoring.GroundedOffset,
                GroundLayers = authoring.GroundLayers,
                TerminalVelocity = authoring.TerminalVelocity,

                Walk = new FirstPersonController.ControllerConsts.StateConsts
                {
                    Speed = authoring.WalkSpeed,
                    RotationSmoothTime = authoring.WalkRotationSmoothTime,
                    SpeedChangeRate = authoring.WalkSpeedChangeRate,
                    LandingSpeedMult = 1f,
                    AnimationMotionScale = authoring.WalkAnimationMotionScale,
                },

                Sprint = new FirstPersonController.ControllerConsts.StateConsts
                {
                    Speed = authoring.SprintSpeed,
                    RotationSmoothTime = authoring.SprintRotationSmoothTime,
                    SpeedChangeRate = authoring.SprintSpeedChangeRate,
                    LandingSpeedMult = authoring.SprintLandingSpeedMultiplier,
                    AnimationMotionScale = authoring.SprintAnimationMotionScale,
                },
            }
        });
    }
}
