using UnityEngine;
using System;

namespace Unity.FPSSample_2
{
    public class PlayerMovementHistory
    {
        private readonly struct PlayerTickData
        {
            public uint Tick { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }

            public PlayerTickData(uint tick, Vector3 position, Quaternion rotation)
            {
                Tick = tick;
                Position = position;
                Rotation = rotation;
            }
        }

        private readonly PlayerTickData[] m_Buffer;
        private readonly int m_Capacity;
        private int m_WriteIndex; // Index where the next element will be written.
        private int m_CurrentSize; // Number of valid elements currently in the buffer.

        public PlayerMovementHistory(int capacity = 20)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            }

            m_Capacity = capacity;
            m_Buffer = new PlayerTickData[m_Capacity];
            m_WriteIndex = 0;
            m_CurrentSize = 0;
        }

        public void Add(uint tick, Vector3 pos, Quaternion rot)
        {
            // Store the new data at the current write index
            m_Buffer[m_WriteIndex] = new PlayerTickData(tick, pos, rot);

            // Move the write index to the next slot, wrapping around if necessary
            m_WriteIndex = (m_WriteIndex + 1) % m_Capacity;

            // Increment current size if the buffer isn't full yet
            if (m_CurrentSize < m_Capacity)
            {
                m_CurrentSize++;
            }
        }

        public bool TryGetTick(uint tick, out Vector3 pos, out Quaternion rot)
        {
            // Search backwards from the most recently added item to the oldest valid item.
            // This is important because m_WriteIndex points to the *next empty slot* (or the oldest item if full),
            // so the last written item is at (m_WriteIndex - 1).
            for (int i = 0; i < m_CurrentSize; i++)
            {
                // Calculate the actual index in the circular buffer.
                // i = 0 corresponds to the newest item, i = m_CurrentSize - 1 corresponds to the oldest.
                // The expression `m_WriteIndex - 1 - i` might be negative. Adding `m_Capacity`
                // before the modulo ensures the result is always a valid positive index.
                int bufferIndex = (m_WriteIndex - 1 - i + m_Capacity) % m_Capacity;

                // Check if the tick at this buffer slot matches the requested tick.
                if (m_Buffer[bufferIndex].Tick == tick)
                {
                    pos = m_Buffer[bufferIndex].Position;
                    rot = m_Buffer[bufferIndex].Rotation;
                    return true;
                }
            }

            // Tick not found
            pos = default; // e.g., Vector3.zero or new Vector3()
            rot = default; // e.g., Quaternion.identity or new Quaternion()
            return false;
        }

        private const float k_MatchPositionToleranceSquared = 0.0001f * 0.0001f;
        private const float k_MatchQuaternionEpsilon = 1.5e-6f;

        public static bool HistoryMatches(
            Vector3 historyPos, Quaternion historyRot,
            Vector3 currentPos, Quaternion currentRot)
        {
            // --- Position Check ---
            // Calculate squared distance to avoid costly square root operation.
            // Assumes Vector3 supports subtraction and has a LengthSquared() method (or equivalent like sqrMagnitude).
            float positionDifferenceSquared = (historyPos - currentPos).sqrMagnitude;
            // For UnityEngine.Vector3, you might use: (historyPos - currentPos).sqrMagnitude;
            // For System.Numerics.Vector3, you can use: Vector3.DistanceSquared(historyPos, currentPos);
            // or (historyPos - currentPos).LengthSquared();

            if (positionDifferenceSquared > k_MatchPositionToleranceSquared)
            {
                return false; // Positions are too different
            }

            // --- Rotation Check ---
            // Quaternion.Dot gives cos(angle/2) between the rotations.
            // For identical rotations (or q and -q), |DotProduct| is 1.
            // We check if 1 - |DotProduct| is less than our epsilon.
            float dotProduct = Quaternion.Dot(historyRot, currentRot);

            if ((1.0f - Math.Abs(dotProduct)) > k_MatchQuaternionEpsilon)
            {
                return false; // Rotations are too different
            }

            return true; // Both position and rotation match within tolerances
        }
    }
}