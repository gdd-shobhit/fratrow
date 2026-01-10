using Unity.Entities;

public interface IPredictedState : IBufferElementData
{
    uint Tick { get; set; }
}

public static class SequenceHelpers
{
    public static bool IsNewer(uint current, uint old)
    {
        return !(old - current < 1u << 31);
    }
}

public static class PredictedStateUtility
{
    public const int maxPredictedTicks = 64;

    public static bool GetStateAtTick<T>(this DynamicBuffer<T> stateArray, uint targetTick, out T state)
        where T : unmanaged, IPredictedState
    {
        int beforeIdx = 0;
        uint beforeTick = 0;
        for (int i = 0; i < stateArray.Length; ++i)
        {
            uint tick = stateArray[i].Tick;
            if (!SequenceHelpers.IsNewer(tick, targetTick) &&
                (beforeTick == 0 || SequenceHelpers.IsNewer(tick, beforeTick)))
            {
                beforeIdx = i;
                beforeTick = tick;
            }
        }

        if (beforeTick == 0)
        {
            state = default;
            return false;
        }

        state = stateArray[beforeIdx];
        return true;
    }

    public static void AddState<T>(this DynamicBuffer<T> stateArray, T state)
        where T : unmanaged, IPredictedState
    {
        uint targetTick = state.Tick;
        int oldestIdx = 0;
        uint oldestTick = 0;
        for (int i = 0; i < stateArray.Length; ++i)
        {
            uint tick = stateArray[i].Tick;
            if (tick == targetTick)
            {
                // Already exists, replace it
                stateArray[i] = state;
                return;
            }

            if (oldestTick == 0 || SequenceHelpers.IsNewer(oldestTick, tick))
            {
                oldestIdx = i;
                oldestTick = tick;
            }
        }

        if (stateArray.Length < maxPredictedTicks)
        {
            stateArray.Add(state);
        }
        else
        {
            stateArray[oldestIdx] = state;
        }
    }

    public static uint GetNewestTick<T>(this DynamicBuffer<T> stateArray)
        where T : unmanaged, IPredictedState
    {
        uint newestTick = 0;
        for (int i = 0; i < stateArray.Length; ++i)
        {
            uint tick = stateArray[i].Tick;

            if (newestTick == 0 || SequenceHelpers.IsNewer(tick, newestTick))
            {
                newestTick = tick;
            }
        }

        return newestTick;
    }
}
