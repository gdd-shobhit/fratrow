using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Scripting;

using Unity.NetCode;

/// <summary>
/// The default serialization strategy for the <see cref="Unity.Transforms.LocalTransform"/> components provided by the NetCode package.
/// </summary>
[Preserve]
[GhostComponentVariation(typeof(LocalTransform), "Ghost Transform 3D")]
[GhostComponent(PrefabType=GhostPrefabType.All, SendTypeOptimization=GhostSendType.AllClients)]
public struct TransformDefaultVariant
{
    /// <summary>
    /// The position value is replicated with a default quantization unit of 1000 (so roughly 1mm precision per component).
    /// The replicated position value support both interpolation and extrapolation
    /// </summary>
    [GhostField (Quantization=1000, Smoothing=SmoothingAction.InterpolateAndExtrapolate, MaxSmoothingDistance = 2)]
    public float3 Position;

    /// <summary>
    /// The scale value is replicated with a default quantization unit of 1000.
    /// The replicated scale value support both interpolation and extrapolation
    /// </summary>
    [GhostField(Quantization=1000, Smoothing=SmoothingAction.InterpolateAndExtrapolate)]
    public float Scale;

    /// <summary>
    /// The rotation quaternion is replicated and the resulting floating point data use for replication the rotation is quantized with good precision (10 or more bits per component)
    /// </summary>
    [GhostField(Quantization=1000, Smoothing=SmoothingAction.InterpolateAndExtrapolate)]
    public quaternion Rotation;
}

/// <summary>Registers the default variants for all samples. Since multiple user-defined variants are present for the
/// Transform components, we must explicitly define a default, and how it applies to components on child entities.</summary>
[CreateBefore(typeof(TransformDefaultVariantSystem))]
sealed partial class DefaultGhostVariantSystem : DefaultVariantSystemBase
{
    protected override void RegisterDefaultVariants(Dictionary<ComponentType, Rule> defaultVariants)
    {
        defaultVariants.Add(typeof(LocalTransform), Rule.OnlyParents(typeof(TransformDefaultVariant)));
    }
}
