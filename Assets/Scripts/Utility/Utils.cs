using System.Net;
using System.Net.Sockets;
using Unity.CharacterController;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// This data structure wraps Unity.Mathematics.Random in a singleton,
    /// so that ECS can use it to generate random numbers
    /// </summary>
    public struct FixedRandom : IComponentData
    {
        public Random Random;
    }

    public static class Utils
    {
        public static void SetShadowModeInHierarchy(EntityManager entityManager, EntityCommandBuffer ecb,
            Entity onEntity, ref BufferLookup<Child> childBufferFromEntity, ShadowCastingMode mode)
        {
            if (childBufferFromEntity.HasBuffer(onEntity))
            {
                DynamicBuffer<Child> childBuffer = childBufferFromEntity[onEntity];
                for (var i = 0; i < childBuffer.Length; i++)
                {
                    SetShadowModeInHierarchy(entityManager, ecb, childBuffer[i].Value, ref childBufferFromEntity, mode);
                }
            }
        }

        public static void DisableRenderingInHierarchy(EntityCommandBuffer ecb, Entity onEntity,
            ref BufferLookup<Child> childBufferFromEntity)
        {
            if (childBufferFromEntity.HasBuffer(onEntity))
            {
                DynamicBuffer<Child> childBuffer = childBufferFromEntity[onEntity];
                for (var i = 0; i < childBuffer.Length; i++)
                {
                    DisableRenderingInHierarchy(ecb, childBuffer[i].Value, ref childBufferFromEntity);
                }
            }
        }

        public static void DisableRenderingInHierarchy2(EntityCommandBuffer ecb, Entity onEntity,
            ref BufferLookup<Child> childBufferFromEntity)
        {
            ecb.AddComponent<Disabled>(onEntity);

            if (childBufferFromEntity.HasBuffer(onEntity))
            {
                DynamicBuffer<Child> childBuffer = childBufferFromEntity[onEntity];
                for (var i = 0; i < childBuffer.Length; i++)
                {
                    DisableRenderingInHierarchy(ecb, childBuffer[i].Value, ref childBufferFromEntity);
                }
            }
        }

        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.Equals(IPAddress.Loopback))
                {
                    return ip.ToString();
                }
            }

            return IPAddress.Any.ToString();
        }
        
        public static float ComputeCharacterYAngleFromDirection(float3 forward)
        {
            float direction = math.dot(forward, math.right()) >= 0 ? 1 : -1;
            return math.degrees(math.acos(math.dot(forward, math.forward()))) * direction;
        }
        
        public static void ComputeFinalRotationsFromRotationDelta(
            ref float viewPitchDegrees,
            ref float characterRotationYDegrees,
            float3 characterTransformUp,
            float2 yawPitchDeltaDegrees,
            float viewRollDegrees,
            float minPitchDegrees,
            float maxPitchDegrees,
            out quaternion characterRotation,
            out quaternion viewLocalRotation)
        {
            // Yaw
            characterRotationYDegrees += yawPitchDeltaDegrees.x;
            ComputeRotationFromYAngleAndUp(characterRotationYDegrees, characterTransformUp, out characterRotation);

            // Pitch
            viewPitchDegrees += yawPitchDeltaDegrees.y;
            viewPitchDegrees = math.clamp(viewPitchDegrees, minPitchDegrees, maxPitchDegrees);

            viewLocalRotation = CalculateLocalViewRotation(viewPitchDegrees, viewRollDegrees);
        }

        public static void ComputeRotationFromYAngleAndUp(
            float characterRotationYDegrees,
            float3 characterTransformUp,
            out quaternion characterRotation)
        {
            characterRotation = math.mul(
                MathUtilities.CreateRotationWithUpPriority(characterTransformUp, math.forward()), 
                quaternion.Euler(0f, math.radians(characterRotationYDegrees), 0f));
        }

        public static quaternion CalculateLocalViewRotation(float viewPitchDegrees, float viewRollDegrees)
        {
            // Pitch
            quaternion viewLocalRotation = quaternion.AxisAngle(-math.right(), math.radians(viewPitchDegrees));

            // Roll
            viewLocalRotation = math.mul(viewLocalRotation, quaternion.AxisAngle(math.forward(), math.radians(viewRollDegrees)));

            return viewLocalRotation;
        }

        public static void SetCursorVisible(bool isVisible)
        {
            Cursor.visible = isVisible;
            Cursor.lockState = Cursor.visible
                ? CursorLockMode.None
                : CursorLockMode.Locked;            
        }
    }
}